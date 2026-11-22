using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AgitCon {
    [DataContract] class ArcaItem {[DataMember]public long id;[DataMember]public string imageUrl;}
    public static class ArcaShop {
        public static string PackId(string input) {
            input=(input??"").Trim();
            if(Regex.IsMatch(input??"",@"^\d{1,12}$"))return input;
            if((input??"").StartsWith("arca:")&&Regex.IsMatch(input.Substring(5),@"^\d{1,12}$"))return input.Substring(5);
            Uri uri;if(!Uri.TryCreate((input??"").Trim(),UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="arca.live"||!uri.IsDefaultPort||uri.UserInfo!="")throw new Exception("https://arca.live/e/번호 형태의 팩 링크를 넣어주세요.");
            var match=Regex.Match(uri.AbsolutePath,@"^/e/(\d{1,12})/?$");if(!match.Success)throw new Exception("아카콘 팩 번호를 확인해주세요.");return match.Groups[1].Value;
        }
        public static bool IsPackLink(string text){try{PackId(text);return true;}catch{return false;}}
        static string Attr(string html,string name){var m=Regex.Match(html,@"(?:^|\s)"+Regex.Escape(name)+@"\s*=\s*[""'](?<value>.*?)[""']",RegexOptions.Singleline|RegexOptions.IgnoreCase);return m.Success?WebUtility.HtmlDecode(m.Groups["value"].Value):null;}
        static string Plain(string text){return WebUtility.HtmlDecode(Regex.Replace(text??"","<[^>]+>","")).Trim();}
        static string Meta(string html,string property){foreach(Match match in Regex.Matches(html,@"<meta\b[^>]*>",RegexOptions.IgnoreCase)){if(String.Equals(Attr(match.Value,"property"),property,StringComparison.OrdinalIgnoreCase))return Attr(match.Value,"content");}return null;}
        public static string ImageUrl(string text){if(String.IsNullOrWhiteSpace(text))throw new Exception("아카콘 이미지 주소가 없어요.");if(text.StartsWith("//"))text="https:"+text;Uri uri=DcShop.PersonalUrl(WebUtility.HtmlDecode(text));var parts=uri.Query.TrimStart('?').Split('&').Where(p=>!p.StartsWith("type=",StringComparison.OrdinalIgnoreCase));var builder=new UriBuilder(uri){Query=String.Join("&",parts),Fragment=""};return builder.Uri.AbsoluteUri;}
        public static ImportedPack ParseApi(string id,byte[] json){id=PackId(id);List<ArcaItem> items;using(var stream=new MemoryStream(json))items=(List<ArcaItem>)new DataContractJsonSerializer(typeof(List<ArcaItem>)).ReadObject(stream);
            if(items==null||items.Count<1||items.Count>1000)throw new Exception("아카콘 목록을 읽지 못했어요.");
            var result=new ImportedPack{Pack=new PackRecord{Id="arca:"+id,Title="아카콘 #"+id,Seller="아카라이브"},Entries=new List<Entry>()};
            foreach(var item in items){if(item.id<=0)throw new Exception("아카콘 번호가 잘못됐어요.");string url=ImageUrl(item.imageUrl);result.Entries.Add(new Entry{PackId=result.Pack.Id,RemoteId=item.id.ToString(),SourceUrl=url,DisplayName="콘 "+(result.Entries.Count+1),Aliases="아카콘, "+id+", "+item.id,Format=Path.GetExtension(new Uri(url).AbsolutePath).TrimStart('.')});}
            if(result.Entries.Select(e=>e.RemoteId).Distinct().Count()!=result.Entries.Count)throw new Exception("중복된 아카콘 번호가 있어요.");result.Pack.ThumbnailUrl=result.Entries[0].SourceUrl;result.Pack.ThumbnailRemoteId=result.Entries[0].RemoteId;return result;
        }
        public static ImportedPack Import(string id,CancellationToken cancel){id=PackId(id);return ParseApi(id,DcShop.Request(new Uri("https://arca.live/api/emoticon/"+id),null,cancel,3*1024*1024,false));}
        public static string RefreshUrl(Entry entry,CancellationToken cancel){var pack=Import(entry.PackId,cancel);var found=pack.Entries.FirstOrDefault(e=>e.RemoteId==entry.RemoteId);if(found==null)throw new Exception("팩에서 이 콘을 찾지 못했어요. 팩을 다시 등록해주세요.");return found.SourceUrl;}
        public static List<PackResult> Search(string term,int page,CancellationToken cancel){if(IsPackLink(term)){var pack=Import(term,cancel);return new List<PackResult>{new PackResult{Id=pack.Pack.Id,Title=pack.Pack.Title,Seller=pack.Pack.Seller,ThumbnailUrl=pack.Pack.ThumbnailUrl,ThumbnailRemoteId=pack.Pack.ThumbnailRemoteId}};}
            try{string html=Encoding.UTF8.GetString(DcShop.Request(new Uri(SearchUrl(term,page)),null,cancel,3*1024*1024,false));return ParseSearch(html);}catch(WebException ex){var response=ex.Response as HttpWebResponse;if(response!=null&&response.StatusCode==HttpStatusCode.Forbidden)throw new ArcaSearchBlockedException(ex);throw;}
        }
        public static string SearchUrl(string term,int page){return IsPackLink(term)?"https://arca.live/e/"+PackId(term):"https://arca.live/e/?keyword="+Uri.EscapeDataString((term??"").Trim())+"&target=title&p="+Math.Max(1,page);}
        static void CheckPage(string html){if(html.Contains("cf-chl-")||Regex.IsMatch(html,@"<title>\s*Just a moment",RegexOptions.IgnoreCase))throw new ArcaSearchBlockedException(null);if(Regex.IsMatch(html,@"<title>\s*로그인",RegexOptions.IgnoreCase)||html.Contains("id=\"loginForm\""))throw new ArcaSearchBlockedException(null);}
        public static List<PackResult> ParseSearch(string html){CheckPage(html);var output=new List<PackResult>();foreach(Match match in Regex.Matches(html,@"<a\b(?<attrs>[^>]*\bhref\s*=\s*[""'](?:https://arca\.live)?/e/(?<id>\d+)(?:[/?#][^""']*)?[""'][^>]*)>(?<body>.*?)</a>",RegexOptions.Singleline|RegexOptions.IgnoreCase)){
                string body=match.Groups["body"].Value;var image=Regex.Match(body,@"<img\b[^>]*>",RegexOptions.IgnoreCase);var title=Regex.Match(body,@"<(?:div|span|strong|h\d)\b[^>]*class=[""'][^""']*(?:title|name)[^""']*[""'][^>]*>(.*?)</(?:div|span|strong|h\d)>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
                string name=Plain(title.Success?title.Groups[1].Value:Attr(match.Groups["attrs"].Value,"title")??Attr(image.Value,"alt"));if(String.IsNullOrWhiteSpace(name))continue;string thumbnail=Attr(image.Value,"data-src")??Attr(image.Value,"src");try{thumbnail=ImageUrl(thumbnail);}catch{thumbnail="https://arca.live/api/emoticon/"+match.Groups["id"].Value+"/thumb";}
                var seller=Regex.Match(body,@"<(?:span|div)\b[^>]*class=[""'][^""']*(?:nickname|author|seller|maker)[^""']*[""'][^>]*>(.*?)</(?:span|div)>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
                output.Add(new PackResult{Id="arca:"+match.Groups["id"].Value,Title=name,Seller=seller.Success?Plain(seller.Groups[1].Value):"아카라이브",ThumbnailUrl=thumbnail});}
            var unique=output.GroupBy(p=>p.Id).Select(g=>g.First()).ToList();if(unique.Count==0&&!html.Contains("검색 결과가 없")&&!html.Contains("검색결과가 없")&&!html.Contains("emoticon-list"))throw new Exception("상점 목록을 읽지 못했어요. 팩 링크로 검색해주세요.");return unique;
        }
        public static ImportedPack ParseSaved(string html){CheckPage(html);string link=Meta(html,"og:url");if(link==null){var canonical=Regex.Match(html,@"<link\b[^>]*rel=[""']canonical[""'][^>]*>",RegexOptions.IgnoreCase);link=Attr(canonical.Value,"href");}if(link==null){var saved=Regex.Match(html,@"saved from url=\([^)]*\)(https://arca\.live/e/\d+[^\s<>]*)",RegexOptions.IgnoreCase);if(saved.Success)link=saved.Groups[1].Value;}string id=PackId(link);
            string title=Meta(html,"og:title");if(!String.IsNullOrWhiteSpace(title))title=Regex.Replace(title,@"\s*[-–]\s*아카라이브\s*$","").Trim();if(String.IsNullOrWhiteSpace(title)){var name=Regex.Match(html,@"<(?:h1|h2|div|span)\b[^>]*class=[""']title[""'][^>]*>(.*?)</(?:h1|h2|div|span)>",RegexOptions.Singleline);title=name.Success?Plain(name.Groups[1].Value):"아카콘 #"+id;}
            var result=new ImportedPack{Pack=new PackRecord{Id="arca:"+id,Title=title,Seller="아카라이브"},Entries=new List<Entry>()};
            foreach(Match media in Regex.Matches(html,@"<video\b(?<attrs>[^>]*)>(?<body>.*?)</video>|<img\b(?<attrs>[^>]*)/?>",RegexOptions.Singleline|RegexOptions.IgnoreCase)){string attrs=media.Groups["attrs"].Value;string remote=Attr(attrs,"data-id");if(String.IsNullOrWhiteSpace(remote)||!Regex.IsMatch(remote,@"^\d+$"))continue;string source=Attr(attrs,"data-src")??Attr(attrs,"src");if(source==null)source=Attr(Regex.Match(media.Groups["body"].Value,@"<source\b[^>]*>",RegexOptions.IgnoreCase).Value,"src");string url;try{url=ImageUrl(source);}catch{continue;}if(result.Entries.Any(e=>e.RemoteId==remote))continue;
                string name=Attr(attrs,"alt")??Attr(attrs,"title");result.Entries.Add(new Entry{PackId=result.Pack.Id,RemoteId=remote,SourceUrl=url,DisplayName=String.IsNullOrWhiteSpace(name)?"콘 "+(result.Entries.Count+1):name,Aliases=name??remote,Format=Path.GetExtension(new Uri(url).AbsolutePath).TrimStart('.')});}
            if(result.Entries.Count==0)throw new Exception("이 파일에서 콘을 찾지 못했어요. 로그인 화면이 아닌 아카콘 상세 페이지를 저장해주세요.");result.Pack.ThumbnailUrl=result.Entries[0].SourceUrl;result.Pack.ThumbnailRemoteId=result.Entries[0].RemoteId;return result;
        }
    }
}
