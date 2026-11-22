using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace AgitCon {
    internal sealed class ArcaSearchBlockedException:Exception {
        internal ArcaSearchBlockedException(Exception inner):base("아카라이브에서 브라우저 확인이 필요해요. 검색 확인창에서 확인·로그인해주세요.",inner){}
    }
    internal sealed class ArcaBrowserSearch:Form {
        readonly string address;readonly Panel surface=new Panel{Dock=DockStyle.Fill};readonly Label status=Ui.Label("아카라이브 확인 화면이 나오면 직접 완료해주세요. 로그인은 필요한 경우에만 해요.",10);
        CoreWebView2Controller controller;bool reading,closed;UiButton read;internal List<PackResult> Results;
        internal ArcaBrowserSearch(string term,int page){
            address=ArcaShop.SearchUrl(term,page);Text="아지트콘 · 아카콘 검색 확인";Icon=Ui.AppIcon();Font=Ui.Regular(10);BackColor=Ui.Paper;ClientSize=new Size(880,700);MinimumSize=new Size(620,480);StartPosition=FormStartPosition.CenterParent;
            var top=new Panel{Dock=DockStyle.Top,Height=80,Padding=new Padding(14)};status.Dock=DockStyle.Fill;top.Controls.Add(status);Controls.Add(surface);Controls.Add(top);
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=58,Padding=new Padding(12)};read=Ui.Button("검색 결과 가져오기",async()=>await ReadResults());read.Width=180;read.Enabled=false;bottom.Controls.Add(read);bottom.Controls.Add(Ui.Button("검색 페이지로",()=>{if(controller!=null)controller.CoreWebView2.Navigate(address);}));bottom.Controls.Add(Ui.Button("닫기",Close));Controls.Add(bottom);
            surface.Resize+=(s,e)=>{if(controller!=null)controller.Bounds=surface.ClientRectangle;};Shown+=async(s,e)=>await Start();FormClosed+=(s,e)=>{closed=true;if(controller!=null){controller.Close();controller=null;}};
        }
        async Task Start(){try{
            // Isolated local browser session. Never export cookies or inspect login fields.
            string profile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","arca-browser");
            var environment=await CoreWebView2Environment.CreateAsync(null,profile);if(closed)return;
            var created=await environment.CreateCoreWebView2ControllerAsync(surface.Handle);if(closed){created.Close();return;}controller=created;controller.Bounds=surface.ClientRectangle;controller.IsVisible=true;
            var web=controller.CoreWebView2;web.Settings.IsPasswordAutosaveEnabled=false;web.Settings.IsGeneralAutofillEnabled=false;web.Settings.AreDevToolsEnabled=false;web.Settings.IsWebMessageEnabled=false;
            web.NavigationStarting+=(s,e)=>{Uri uri;if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out uri)||!AllowedNavigation(uri))e.Cancel=true;};web.NewWindowRequested+=(s,e)=>e.Handled=true;web.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;
            web.NavigationCompleted+=async(s,e)=>{if(!closed){read.Enabled=true;await ReadResults();}};web.Navigate(address);
        }catch(Exception ex){if(!closed)status.Text="검색 브라우저를 열지 못했어요. WebView2 Runtime을 확인해주세요.\n"+ex.Message;}}
        internal static bool AllowedNavigation(Uri uri){return uri.Scheme=="https"&&uri.Host=="arca.live"&&uri.IsDefaultPort&&String.IsNullOrEmpty(uri.UserInfo);}
        internal static bool MatchesSearch(string actual,string expected){Uri a,b;return Uri.TryCreate(actual,UriKind.Absolute,out a)&&Uri.TryCreate(expected,UriKind.Absolute,out b)&&AllowedNavigation(a)&&a.AbsolutePath==b.AbsolutePath&&a.Query==b.Query;}
        async Task ReadResults(){if(reading||closed||controller==null)return;var web=controller.CoreWebView2;if(!MatchesSearch(web.Source,address)){status.Text="확인·로그인을 마친 뒤 ‘검색 페이지로’를 눌러주세요.";return;}reading=true;
            try{
                // Read only search cards, never the page's account details or form contents.
                string script=@"(()=>{const lists=[...document.querySelectorAll('.emoticon-list')];const list=lists.find(x=>x.querySelector('.sort'))||lists[lists.length-1];if(!list)return null;const cards=[...list.querySelectorAll('.emoticon-col a[href]')].filter(x=>/^\/e\/\d+(?:[/?#]|$)/.test(x.getAttribute('href')));return '<div class=""emoticon-list"">'+cards.map(x=>x.outerHTML).join('')+'</div>';})()";
                string json=await web.ExecuteScriptAsync(script);if(closed)return;if(!MatchesSearch(web.Source,address))return;if(json=="null"){status.Text="브라우저 확인·로그인을 마친 뒤 ‘검색 결과 가져오기’를 눌러주세요.";return;}
                string html=GemmaHost.Read<string>(json);if(html.Length>3*1024*1024)throw new IOException("검색 결과가 너무 커요.");Results=ArcaShop.ParseSearch(html);DialogResult=DialogResult.OK;Close();
            }catch(Exception ex){if(!closed)status.Text="결과를 가져오지 못했어요. 검색 페이지가 열린 뒤 다시 눌러주세요.\n"+ex.Message;}finally{reading=false;}
        }
    }
}
