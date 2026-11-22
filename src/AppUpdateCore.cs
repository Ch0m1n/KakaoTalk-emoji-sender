using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AgitCon.Updates {
    [DataContract] public sealed class ReleaseAsset {
        [DataMember(Name="name")]public string Name;[DataMember(Name="state")]public string State;[DataMember(Name="browser_download_url")]public string Url;[DataMember(Name="size")]public long Size;[DataMember(Name="digest")]public string Digest;
    }
    [DataContract] public sealed class ReleaseInfo {
        [DataMember(Name="tag_name")]public string Tag;[DataMember(Name="draft")]public bool Draft;[DataMember(Name="prerelease")]public bool Prerelease;[DataMember(Name="assets")]public List<ReleaseAsset> Assets;
    }
    [DataContract] public sealed class UpdatePlan {
        [DataMember]public string Root;[DataMember]public string Package;[DataMember]public string Sha256;[DataMember]public string Version;[DataMember]public int Process;[DataMember]public long Started;
    }
    public static class UpdateCore {
        public const string Repository="Ch0m1n/KakaoTalk-emoji-sender",AssetName="AgitCon-win-x64.zip";
        public const string Api="https://api.github.com/repos/"+Repository+"/releases/latest";
        public static readonly string[] RedirectHosts={"github.com","api.github.com","release-assets.githubusercontent.com","objects.githubusercontent.com"};
        public static Version ParseVersion(string text){if(text==null||!Regex.IsMatch(text,@"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$"))throw new InvalidDataException("릴리스 버전 형식이 올바르지 않아요.");return System.Version.Parse(text.TrimStart('v'));}
        public static T Read<T>(Stream input){return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(input);}
        public static void Write<T>(string file,T value){using(var stream=File.Create(file))new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);}
        public static string Hash(string file){using(var input=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
        public static bool UrlAllowed(Uri url){return url.Scheme=="https"&&url.IsDefaultPort&&String.IsNullOrEmpty(url.UserInfo)&&RedirectHosts.Contains(url.DnsSafeHost,StringComparer.OrdinalIgnoreCase);}
        public static ReleaseAsset Select(ReleaseInfo release,string current){
            var version=ParseVersion(release.Tag);if(release.Draft||release.Prerelease)throw new InvalidDataException("정식 릴리스만 업데이트할 수 있어요.");
            if(version<=ParseVersion(current))return null;
            var assets=(release.Assets??new List<ReleaseAsset>()).Where(a=>a.Name==AssetName&&a.State=="uploaded").ToList();
            if(assets.Count!=1)throw new InvalidDataException("새 버전의 배포 파일이 아직 준비되지 않았어요.");var asset=assets[0];
            if(asset.Url!="https://github.com/"+Repository+"/releases/download/"+release.Tag+"/"+AssetName||asset.Size<2||asset.Size>512L*1024*1024||!Regex.IsMatch(asset.Digest??"",@"^sha256:[0-9a-fA-F]{64}$"))throw new InvalidDataException("GitHub 배포 파일의 주소·크기·무결성 정보를 확인하지 못했어요.");return asset;
        }
        static async Task<HttpWebResponse> Open(Uri url,CancellationToken token){
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            if(!UrlAllowed(url))throw new InvalidDataException("허용되지 않은 업데이트 주소예요.");
            for(int redirect=0;redirect<6;redirect++){
                var request=(HttpWebRequest)WebRequest.Create(url);request.AllowAutoRedirect=false;request.UserAgent="KakaoTalk-emoji-sender-Updater";request.Accept="application/vnd.github+json";request.Timeout=20000;request.ReadWriteTimeout=20000;
                using(token.Register(()=>request.Abort())){
                    var response=(HttpWebResponse)await request.GetResponseAsync();int status=(int)response.StatusCode;
                    if(status>=300&&status<400){var next=new Uri(url,response.Headers["Location"]);response.Dispose();if(!UrlAllowed(next))throw new InvalidDataException("업데이트 다운로드가 다른 서버로 이동했어요.");url=next;continue;}
                    return response;
                }
            }throw new InvalidDataException("업데이트 주소 이동이 너무 많아요.");
        }
        public static async Task<ReleaseInfo> Check(CancellationToken token){
            try{using(var response=await Open(new Uri(Api),token))using(token.Register(()=>response.Close()))using(var input=response.GetResponseStream())using(var output=new MemoryStream()){
                var buffer=new byte[32768];int read;while((read=await input.ReadAsync(buffer,0,buffer.Length,token))>0){if(output.Length+read>1024*1024)throw new InvalidDataException("GitHub 응답이 너무 커요.");output.Write(buffer,0,read);}output.Position=0;return Read<ReleaseInfo>(output);
            }}catch(WebException ex){var response=ex.Response as HttpWebResponse;if(response!=null&&response.StatusCode==HttpStatusCode.NotFound)return null;if(response!=null&&((int)response.StatusCode==403||(int)response.StatusCode==429))throw new InvalidOperationException("GitHub 조회 한도에 도달했어요. 잠시 후 다시 확인해주세요.");token.ThrowIfCancellationRequested();throw new InvalidOperationException("GitHub에 연결하지 못했어요. 인터넷 연결을 확인해주세요.",ex);}
        }
        public static async Task<string> Download(ReleaseAsset asset,string folder,CancellationToken token,IProgress<string> progress){
            Directory.CreateDirectory(folder);string file=Path.Combine(folder,AssetName),part=file+".part";long total=0;var clock=System.Diagnostics.Stopwatch.StartNew();
            try{using(var response=await Open(new Uri(asset.Url),token))using(token.Register(()=>response.Close()))using(var input=response.GetResponseStream())using(var output=new FileStream(part,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                var buffer=new byte[262144];int read;while((read=await input.ReadAsync(buffer,0,buffer.Length,token))>0){total+=read;if(total>asset.Size||clock.Elapsed.TotalMinutes>10)throw new InvalidDataException("다운로드 크기 또는 시간 제한을 초과했어요.");await output.WriteAsync(buffer,0,read,token);if(progress!=null)progress.Report("다운로드 "+(total*100/asset.Size)+"% · "+(total/1048576.0).ToString("0.0")+" / "+(asset.Size/1048576.0).ToString("0.0")+" MB");}
            }token.ThrowIfCancellationRequested();if(total!=asset.Size||Hash(part)!=asset.Digest.Substring(7).ToLowerInvariant())throw new InvalidDataException("다운로드 무결성 검증에 실패했어요. 기존 앱은 유지해요.");File.Move(part,file);return file;}finally{if(File.Exists(part))File.Delete(part);}
        }
        public static bool AllowedFile(string relative){
            var parts=relative.Split('/');if(parts.Length==1)return new[]{"AgitCon.exe","AgitCon.Updater.exe","AgitCon.ico","Microsoft.Web.WebView2.Core.dll","WebView2Loader.dll","version.json","package-manifest.json","README.txt","수정 안내.txt"}.Contains(relative,StringComparer.OrdinalIgnoreCase);
            if(parts.Any(p=>new[]{"data","profile","profiles","models","model",".venv","user-data",".git"}.Contains(p,StringComparer.OrdinalIgnoreCase)))return false;
            return new[]{"runtime","codecs","semantic","licenses"}.Contains(parts[0],StringComparer.OrdinalIgnoreCase);
        }
        public static string SafeRelative(string name){
            if(String.IsNullOrEmpty(name)||name.Length>240||name.Contains('\\')||name.Contains(':')||name.StartsWith("/")||name.IndexOf('\0')>=0)throw new InvalidDataException("안전하지 않은 ZIP 경로예요.");
            string relative=name.TrimEnd('/');if(relative.Split('/').Any(p=>p=="."||p==".."||p.Length==0||p.EndsWith(".")||p.EndsWith(" ")||p.IndexOfAny(Path.GetInvalidFileNameChars())>=0))throw new InvalidDataException("안전하지 않은 ZIP 파일명이에요.");return relative;
        }
        public static string Inside(string root,string relative){string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;string path=Path.GetFullPath(Path.Combine(prefix,relative.Replace('/',Path.DirectorySeparatorChar)));if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("설치 경로 밖의 파일이에요.");return path;}
        public static void NoLinks(string root,string path){
            string fullRoot=Path.GetFullPath(root);for(string check=path;!String.IsNullOrEmpty(check);check=Path.GetDirectoryName(check)){
                if((Directory.Exists(check)||File.Exists(check))&&(File.GetAttributes(check)&FileAttributes.ReparsePoint)!=0)throw new IOException("연결된 경로에는 업데이트하지 않아요.");if(String.Equals(check,fullRoot,StringComparison.OrdinalIgnoreCase))return;
            }throw new IOException("설치 경로를 확인하지 못했어요.");
        }
        public static List<string> Extract(string zip,string destination,string expected){
            Directory.CreateDirectory(destination);var files=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long size=0;
            using(var archive=ZipFile.OpenRead(zip)){
                if(archive.Entries.Count>20000)throw new InvalidDataException("업데이트 파일 수가 너무 많아요.");
                foreach(var entry in archive.Entries){string relative=SafeRelative(entry.FullName);if(((entry.ExternalAttributes>>16)&0xf000)==0xa000||(entry.ExternalAttributes&0x400)!=0)throw new InvalidDataException("연결 파일이 포함된 업데이트예요.");
                    if(entry.FullName.EndsWith("/"))continue;if(!AllowedFile(relative)||!seen.Add(relative))throw new InvalidDataException("허용되지 않거나 중복된 업데이트 파일: "+relative);
                    size+=entry.Length;if(entry.Length>512L*1024*1024||size>1024L*1024*1024)throw new InvalidDataException("압축 해제 용량이 너무 커요.");string target=Inside(destination,relative);NoLinks(destination,target);Directory.CreateDirectory(Path.GetDirectoryName(target));entry.ExtractToFile(target,false);files.Add(relative);
                }
            }
            if(!files.Contains("AgitCon.exe")||!files.Contains("AgitCon.Updater.exe")||!files.Contains("version.json"))throw new InvalidDataException("필수 업데이트 파일이 없어요.");
            using(var stream=File.OpenRead(Path.Combine(destination,"version.json"))){var info=Read<PackageVersion>(stream);if(info.Repository!=Repository||ParseVersion(info.Version)!=ParseVersion(expected))throw new InvalidDataException("배포 파일 버전이 GitHub 릴리스와 달라요.");}
            using(var stream=File.OpenRead(Path.Combine(destination,"AgitCon.exe"))){if(stream.ReadByte()!=77||stream.ReadByte()!=90)throw new InvalidDataException("실행 파일 형식이 올바르지 않아요.");}return files;
        }
        [DataContract] public sealed class PackageVersion {[DataMember(Name="version")]public string Version;[DataMember(Name="repository")]public string Repository;}
        public static void Apply(UpdatePlan plan,string stage,Action<string> launch,Action<int> fault=null){
            if(Hash(plan.Package)!=plan.Sha256)throw new InvalidDataException("업데이트 파일이 바뀌었어요.");
            if(!File.Exists(Path.Combine(plan.Root,"AgitCon.exe")))throw new IOException("기존 아지트콘 설치 폴더가 아니에요.");
            var files=Extract(plan.Package,stage,plan.Version);string backup=Inside(plan.Root,".updates-backup/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));NoLinks(plan.Root,backup);Directory.CreateDirectory(backup);
            var journal=new List<Tuple<string,string,bool>>();
            try{foreach(string relative in files){string target=Inside(plan.Root,relative),source=Inside(stage,relative),old=Inside(backup,relative);NoLinks(plan.Root,target);Directory.CreateDirectory(Path.GetDirectoryName(target));bool existed=File.Exists(target);if(existed){Directory.CreateDirectory(Path.GetDirectoryName(old));File.Copy(target,old,false);}string temporary=target+".update-"+Guid.NewGuid().ToString("N");try{File.Copy(source,temporary,false);if(existed)File.Replace(temporary,target,null);else File.Move(temporary,target);journal.Add(Tuple.Create(target,old,existed));}finally{if(File.Exists(temporary))File.Delete(temporary);}if(fault!=null)fault(journal.Count);}
                launch(Path.Combine(plan.Root,"AgitCon.exe"));
            }catch(Exception failure){
                var errors=new List<Exception>();foreach(var entry in journal.AsEnumerable().Reverse())try{if(entry.Item3)File.Copy(entry.Item2,entry.Item1,true);else if(File.Exists(entry.Item1))File.Delete(entry.Item1);}catch(Exception ex){errors.Add(ex);}
                if(errors.Count>0)throw new IOException("일부 파일을 복구하지 못했어요. 백업: "+backup,new AggregateException(new[]{failure}.Concat(errors)));failure.Data["RollbackComplete"]=true;throw;
            }
        }
    }
}
