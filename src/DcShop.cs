using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgitCon {
    [DataContract] public class PackRecord {
        [DataMember] public string Id;
        [DataMember] public string Title;
        [DataMember] public string ThumbnailUrl;
        [DataMember] public string Seller;
        [DataMember] public string ThumbnailRemoteId;
        public override string ToString(){return Title+"  (#"+Id+")";}
    }
    public class PackResult : PackRecord {public override string ToString(){return Title+"  /  "+Seller+"  (#"+Id+")";}}
    [DataContract] class DcInfo {[DataMember] public string package_idx; [DataMember] public string title;}
    [DataContract] class DcItem {[DataMember] public string idx; [DataMember] public string title; [DataMember] public string ext; [DataMember] public string path;}
    [DataContract] class DcDetail {[DataMember] public DcInfo info; [DataMember] public List<DcItem> detail;}
    public class ImportedPack {public PackRecord Pack; public List<Entry> Entries;}
    public static class DcShop {
        const int Limit=10*1024*1024;
        public static string CacheRoot=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","images");
        static readonly ConcurrentDictionary<string,SemaphoreSlim> imageLocks=new ConcurrentDictionary<string,SemaphoreSlim>();
        static readonly SemaphoreSlim imageSlots=new SemaphoreSlim(2);
        internal static Func<Uri,string,CancellationToken,int,bool,byte[]> TestTransport;
        static bool ImageHost(Uri u) {return u.Scheme=="https"&&u.IsDefaultPort&&u.UserInfo==""&&
            ((u.Host=="arca.live"&&Regex.IsMatch(u.AbsolutePath,@"^/api/emoticon/\d{1,12}/thumb$"))||(Regex.IsMatch(u.Host,@"^dcimg\d*\.dcinside\.com$")&&u.AbsolutePath=="/dccon.php")||u.Host=="ac.arca.live"||Regex.IsMatch(u.Host,@"^ac(?:-[opq])?\.namu\.la$"));}
        public static Uri PersonalUrl(string url) {
            Uri u;if(!Uri.TryCreate(url.Trim(),UriKind.Absolute,out u)||!ImageHost(u))throw new Exception("디시콘 또는 아카콘의 HTTPS 이미지 링크를 넣어주세요.");
            if(u.Host.EndsWith(".dcinside.com")&&!Regex.IsMatch(u.Query,@"(?:^\?|&)no=[a-fA-F0-9]+(?:&|$)"))throw new Exception("디시콘 이미지 주소의 no 값이 없어요.");
            return u;
        }
        internal static byte[] Request(Uri uri,string body,CancellationToken cancel,int limit,bool imageOnly) {
            cancel.ThrowIfCancellationRequested();
            if(TestTransport!=null)return TestTransport(uri,body,cancel,limit,imageOnly);
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            for(int redirects=0;redirects<4;redirects++) {
                cancel.ThrowIfCancellationRequested();
                if(imageOnly&&!ImageHost(uri))throw new Exception("허용된 이미지 서버 밖으로 이동하는 링크예요.");
                var req=(HttpWebRequest)WebRequest.Create(uri);req.AllowAutoRedirect=false;
                req.UserAgent="AgitCon/0.2 (personal desktop sticker picker)";
                req.Referer=uri.Host=="arca.live"||uri.Host=="ac.arca.live"||uri.Host.EndsWith(".namu.la")?"https://arca.live/":"https://dccon.dcinside.com/";
                req.Timeout=15000;req.ReadWriteTimeout=15000;req.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
                if(body!=null) {req.Method="POST";req.ContentType="application/x-www-form-urlencoded; charset=UTF-8";req.Headers["X-Requested-With"]="XMLHttpRequest";byte[] bytes=Encoding.UTF8.GetBytes(body);req.ContentLength=bytes.Length;using(var output=req.GetRequestStream())output.Write(bytes,0,bytes.Length);}
                using(cancel.Register(()=>req.Abort())) using(var response=(HttpWebResponse)req.GetResponse()) {
                    if((int)response.StatusCode>=300&&(int)response.StatusCode<400) {
                        if(!imageOnly){if(uri.Host=="arca.live")throw new ArcaSearchBlockedException(null);throw new Exception("DC search redirect requires an updated endpoint.");}
                        uri=PersonalUrl(new Uri(uri,response.Headers["Location"]).AbsoluteUri);continue;
                    }
                    if(response.ContentLength>limit)throw new Exception("응답 파일이 용량 제한을 넘었어요.");
                    using(var input=response.GetResponseStream())using(var result=new MemoryStream()) {
                        byte[] buffer=new byte[8192];int read;
                        while((read=input.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();if(result.Length+read>limit)throw new Exception("파일이 너무 커요.");result.Write(buffer,0,read);}
                        return result.ToArray();
                    }
                }
            }
            throw new Exception("리디렉션이 너무 많아요.");
        }
        static string Plain(string html){return WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]*>","")).Trim();}
        public static List<PackResult> ParseSearch(string html) {
            var result=new List<PackResult>();
            foreach(Match m in Regex.Matches(html,@"<li\b[^>]*\bpackage_idx\s*=\s*[""'](?<id>\d+)[""'][^>]*>(?<body>.*?)</li>",RegexOptions.Singleline|RegexOptions.IgnoreCase)) {
                var title=Regex.Match(m.Groups["body"].Value,@"<strong\b[^>]*class=[""']dcon_name[""'][^>]*>(.*?)</strong>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
                var seller=Regex.Match(m.Groups["body"].Value,@"<span\b[^>]*class=[""']dcon_seller[""'][^>]*>(.*?)</span>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
                var thumb=Regex.Match(m.Groups["body"].Value,@"<img\b[^>]*src=[""']([^""']+)[""']",RegexOptions.IgnoreCase);
                string thumbUrl=thumb.Success?WebUtility.HtmlDecode(thumb.Groups[1].Value):null;
                var imageId=Regex.Match(thumbUrl??"",@"[?&]no=([a-fA-F0-9]+)");
                if(imageId.Success)thumbUrl="https://dcimg5.dcinside.com/dccon.php?no="+imageId.Groups[1].Value;
                if(title.Success)result.Add(new PackResult{Id=m.Groups["id"].Value,Title=Plain(title.Groups[1].Value),Seller=Plain(seller.Groups[1].Value),ThumbnailUrl=thumbUrl});
            }
            if(result.Count==0&&!html.Contains("dccon_search_none")&&!html.Contains("검색된 디시콘이 없습니다"))throw new Exception("디시콘 검색 페이지를 해석하지 못했어요. 사이트 변경 또는 접속 차단일 수 있어요.");
            return result.GroupBy(p=>p.Id).Select(g=>g.First()).ToList();
        }
        public static List<PackResult> Search(string term,int page,CancellationToken cancel) {
            if(String.IsNullOrWhiteSpace(term))throw new Exception("팩 이름을 입력해주세요.");
            string url="https://dccon.dcinside.com/new/"+page+"/title/"+Uri.EscapeDataString(term.Trim());
            return ParseSearch(Encoding.UTF8.GetString(Request(new Uri(url),null,cancel,2*1024*1024,false)));
        }
        static string Extension(byte[] bytes) {
            if(bytes.Length>=8&&bytes.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return ".png";
            if(bytes.Length>=6&&(Encoding.ASCII.GetString(bytes,0,6)=="GIF89a"||Encoding.ASCII.GetString(bytes,0,6)=="GIF87a"))return ".gif";
            if(bytes.Length>=3&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255)return ".jpg";
            throw new Exception("지원하는 이미지 형식이 아니거나 파일이 손상됐어요.");
        }
        static string Store(byte[] bytes,string subfolder,CancellationToken cancel,Entry entry) {
            if(AssetCodec.Encoded(bytes)){string originalExt=Encoding.ASCII.GetString(bytes,0,4)=="RIFF"?".webp":Encoding.ASCII.GetString(bytes,4,4)=="ftyp"?".mp4":".webm";string originalHash;using(var sha=SHA256.Create())originalHash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();string rawDir=Path.Combine(CacheRoot,subfolder);Directory.CreateDirectory(rawDir);string raw=Path.Combine(rawDir,originalHash+originalExt);if(!File.Exists(raw))File.WriteAllBytes(raw,bytes);entry.OriginalPath=raw;bytes=AssetCodec.Convert(bytes,cancel);}
            string ext=Extension(bytes);
            using(var ms=new MemoryStream(bytes))using(var image=Image.FromStream(ms)) {
                if(image.Width>2048||image.Height>2048)throw new Exception("콘 이미지 해상도가 너무 커요.");
            }
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            string dir=Path.Combine(CacheRoot,subfolder);Directory.CreateDirectory(dir);
            string path=Path.Combine(dir,hash+ext);
            if(!File.Exists(path)) {string temp=Path.Combine(dir,Guid.NewGuid().ToString("N")+".tmp");File.WriteAllBytes(temp,bytes);try{File.Move(temp,path);}catch(IOException){if(!File.Exists(path))throw;}finally{if(File.Exists(temp))File.Delete(temp);}}
            return path;
        }
        public static ImportedPack ParsePack(string id,byte[] bytes) {
            if(!Regex.IsMatch(id??"",@"^\d{1,12}$"))throw new Exception("팩 번호가 올바르지 않아요.");
            DcDetail detail;using(var stream=new MemoryStream(bytes))detail=(DcDetail)new DataContractJsonSerializer(typeof(DcDetail)).ReadObject(stream);
            if(detail==null||detail.info==null||detail.detail==null||detail.detail.Count==0||detail.detail.Count>300)throw new Exception("팩 정보를 읽지 못했어요.");
            var result=new ImportedPack{Pack=new PackRecord{Id=id,Title=detail.info.title},Entries=new List<Entry>()};
            foreach(var item in detail.detail) {
                if(!Regex.IsMatch(item.path??"",@"^[a-fA-F0-9]+$"))throw new Exception("잘못된 이미지 경로예요.");
                string url="https://dcimg5.dcinside.com/dccon.php?no="+item.path;
                result.Entries.Add(new Entry{DisplayName=item.title,Aliases=item.title,PackId=id,SourceUrl=url,Format=item.ext});
            }
            return result;
        }
        public static ImportedPack Import(string id,CancellationToken cancel,Action<string> progress) {
            if(!Regex.IsMatch(id??"",@"^\d{1,12}$"))throw new Exception("팩 번호가 올바르지 않아요.");
            progress("콘 목록을 가져오는 중");
            byte[] bytes=Request(new Uri("https://dccon.dcinside.com/index/package_detail"),"package_idx="+id,cancel,2*1024*1024,false);
            cancel.ThrowIfCancellationRequested();return ParsePack(id,bytes);
        }
        // Resolve cache only when the image is visible or explicitly selected.
        // URL references allow cache reuse even if a previous view was cancelled.
        public static string EnsureImage(Entry entry,CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested();
            if(!String.IsNullOrEmpty(entry.Path)&&File.Exists(entry.Path))return entry.Path;
            if(String.IsNullOrEmpty(entry.SourceUrl))throw new FileNotFoundException("원본 파일이 없어요.");
            Uri uri=PersonalUrl(entry.SourceUrl);
            string identity=!String.IsNullOrEmpty(entry.RemoteId)&&(entry.PackId??"").StartsWith("arca:")?Catalog.Key(entry):uri.AbsoluteUri;
            string key;using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","").ToLowerInvariant();
            var gate=imageLocks.GetOrAdd(key,k=>new SemaphoreSlim(1));gate.Wait(cancel);
            try {
                string refs=Path.Combine(CacheRoot,"refs"),reference=Path.Combine(refs,key+".txt");
                if(File.Exists(reference)) {
                    string relative=File.ReadAllText(reference);
                    if(Regex.IsMatch(relative,@"^[0-9a-f]{64}\.(?:png|gif|jpg)$")) {
                        string originalRef=reference+".original";if(File.Exists(originalRef)){string originalName=File.ReadAllText(originalRef);if(Regex.IsMatch(originalName,@"^[0-9a-f]{64}\.(?:webp|mp4|webm)$")){string original=Path.Combine(CacheRoot,"shared",originalName);if(File.Exists(original))entry.OriginalPath=original;}}
                        string cached=Path.Combine(CacheRoot,"shared",relative);if(File.Exists(cached))return cached;
                    }
                }
                imageSlots.Wait(cancel);
                string path;
                try {byte[] bytes;try{bytes=Request(uri,null,cancel,Limit,true);}catch(WebException ex){var response=ex.Response as HttpWebResponse;if((entry.PackId??"").StartsWith("arca:")&&!String.IsNullOrEmpty(entry.RemoteId)&&response!=null&&(response.StatusCode==HttpStatusCode.Forbidden||response.StatusCode==HttpStatusCode.NotFound)){response.Dispose();entry.SourceUrl=ArcaShop.RefreshUrl(entry,cancel);bytes=Request(PersonalUrl(entry.SourceUrl),null,cancel,Limit,true);}else throw;}path=Store(bytes,"shared",cancel,entry);} finally {imageSlots.Release();}
                Directory.CreateDirectory(refs);
                File.WriteAllText(reference,Path.GetFileName(path));
                if(!String.IsNullOrEmpty(entry.OriginalPath))File.WriteAllText(reference+".original",Path.GetFileName(entry.OriginalPath));
                cancel.ThrowIfCancellationRequested();return path;
            } finally {gate.Release();}
        }
        public static string EnsureAttachment(Entry entry,CancellationToken cancel,bool compatible=false){string preview=EnsureImage(entry,cancel);entry.Path=preview;if(!compatible&&!String.IsNullOrEmpty(entry.OriginalPath)&&Path.GetExtension(entry.OriginalPath)==".webp"&&File.Exists(entry.OriginalPath))return entry.OriginalPath;return preview;}
        public static Entry ImportPersonal(string url,string name,CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested();Uri uri=PersonalUrl(url);
            return new Entry{DisplayName=String.IsNullOrWhiteSpace(name)?"개별 콘":name.Trim(),Aliases=(name??"").Trim(),PackId="personal",SourceUrl=uri.AbsoluteUri};
        }
    }
}
