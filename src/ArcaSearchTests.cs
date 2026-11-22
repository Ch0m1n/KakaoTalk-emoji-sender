using System;using System.IO;using System.Linq;using System.Text;using System.Threading;
namespace AgitCon {
 internal static class ArcaSearchTests {
  internal static void Run(){int count=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Arca search check "+count);count++;};try{
   string url=ArcaShop.SearchUrl("벽돌이 적",2);check(url.Contains("keyword=%EB%B2%BD%EB%8F%8C%EC%9D%B4%20%EC%A0%81")&&url.EndsWith("p=2"));
   check(ArcaBrowserSearch.MatchesSearch(url,url));check(!ArcaBrowserSearch.MatchesSearch("https://arca.live/login",url));check(!ArcaBrowserSearch.MatchesSearch(url.Replace("p=2","p=3"),url));
   foreach(string bad in new[]{"http://arca.live/e/","https://arca.live.evil/e/","https://user@arca.live/e/","https://arca.live:444/e/"})check(!ArcaBrowserSearch.AllowedNavigation(new Uri(bad)));
   bool blocked=false;try{ArcaShop.ParseSearch("<title>Just a moment...</title><div class='cf-chl-container'></div>");}catch(ArcaSearchBlockedException){blocked=true;}check(blocked);
   string cards="<div class='emoticon-list'><div class='emoticon-col'><a href='/e/123'><img src='//ac.arca.live/t.png'><div class='title'>벽돌이 적 &amp; 친구</div><div class='maker'>제작자</div></a></div></div>";
   var parsed=ArcaShop.ParseSearch(cards);check(parsed.Count==1&&parsed[0].Title=="벽돌이 적 & 친구"&&parsed[0].Seller=="제작자");check(parsed[0].Id=="arca:123"&&parsed[0].ThumbnailUrl=="https://ac.arca.live/t.png");check(ArcaShop.ParseSearch("<div class='emoticon-list'></div>").Count==0);
   var noImage=ArcaShop.ParseSearch("<a href='/e/123'><div class='title'>팩</div></a>");check(noImage[0].ThumbnailUrl=="https://arca.live/api/emoticon/123/thumb");check(DcShop.PersonalUrl(noImage[0].ThumbnailUrl).Host=="arca.live");bool rejected=false;try{DcShop.PersonalUrl("https://arca.live/login");}catch{rejected=true;}check(rejected);
   int requests=0;DcShop.TestTransport=(uri,body,token,limit,image)=>{requests++;check(uri.AbsolutePath=="/e/");return Encoding.UTF8.GetBytes(cards);};check(ArcaShop.Search("벽돌이 적",1,CancellationToken.None).Count==1&&requests==1);
   DcShop.TestTransport=(uri,body,token,limit,image)=>Encoding.UTF8.GetBytes("<title>Just a moment...</title>");blocked=false;try{ArcaShop.Search("벽돌",1,CancellationToken.None);}catch(ArcaSearchBlockedException){blocked=true;}check(blocked);
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"arca-search-test.txt"),"PASS "+count+" search/parser/navigation checks. Interactive Cloudflare/login completion not verified.");
  }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"arca-search-test.txt"),"FAIL "+ex);Environment.ExitCode=1;}finally{DcShop.TestTransport=null;}}
 }
}
