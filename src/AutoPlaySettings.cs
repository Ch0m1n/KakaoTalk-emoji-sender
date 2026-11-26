using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgitCon {
    internal static class AutoPlaySettings {
        internal static void AddTab(Form settings,Action open,bool enabled,Action<bool> save){
            var tabs=settings.Controls.OfType<TabControl>().Single();
            var page=new TabPage("움짤 자동재생"){BackColor=Ui.Paper,Padding=new Padding(22)};
            var text=Ui.Label("카톡 안의 GIF·움직이는 WebP를 반복재생해요.\n콘 전송과 별도로 시작·중지할 수 있어요.\n\n자동재생 창을 닫아도 재생은 유지돼요.\n아지트콘을 완전히 종료하면 함께 중지돼요.\n기존 자동재생 도우미는 먼저 종료해주세요.",10);
            text.Dock=DockStyle.Top;text.Height=170;
            var check=new CheckBox{Text="아지트콘 실행 시 자동재생 시작",Checked=enabled,Dock=DockStyle.Top,Height=42};
            check.CheckedChanged+=(s,e)=>save(check.Checked);
            var button=Ui.Button("자동재생 열기",open,true);button.Dock=DockStyle.Top;button.Height=44;
            page.Controls.Add(button);page.Controls.Add(check);page.Controls.Add(text);tabs.TabPages.Add(page);
        }
        internal static async Task<bool> WaitStopped(){
            var signal=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"codecs/autoplay/artifacts/kakao-internals/autoplay-status.json");
            for(int i=0;i<150;i++){
                if(!File.Exists(signal))return true;
                try{
                    var json=new System.Web.Script.Serialization.JavaScriptSerializer();
                    var state=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(signal));
                    object value;if(state.TryGetValue("phase",out value)&&Convert.ToString(value)=="stopped")return true;
                    if(state.TryGetValue("updated",out value)&&DateTimeOffset.UtcNow.ToUnixTimeSeconds()-Convert.ToDouble(value)>15)return true;
                }catch(IOException){}catch(ArgumentException){}
                await Task.Delay(100);
            }
            return false;
        }
        internal static void Test(){
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-work/autoplay-"+Guid.NewGuid().ToString("N"));
            try{
                using(var host=new Form())using(var tabs=new TabControl{Dock=DockStyle.Fill}){
                    host.Controls.Add(tabs);bool saved=false;int opened=0;
                    AddTab(host,()=>opened++,false,value=>saved=value);
                    var page=tabs.TabPages[0];page.Controls.OfType<CheckBox>().Single().Checked=true;
                    if(!saved)throw new Exception("Start preference callback");
                    using(var form=new AutoPlayForm(root)){
                        form.ConfigureIntegrated(()=>{},value=>{});
                        var owner=Path.Combine(root,"artifacts/kakao-internals/autoplay-owner.json");
                        if(!File.Exists(owner)||form.HiddenInTray)throw new Exception("Shared owner/tray");
                        form.ShutdownIntegrated();
                        // An unshown window is disposed by the owner. No service is started by this test.
                        if(!File.Exists(Path.Combine(root,"artifacts/kakao-internals/autoplay-stop.signal")))throw new Exception("Stop signal");
                    }
                }
                if(Updates.UpdateCore.AssetName!="AgitCon-win-"+(IntPtr.Size==8?"x64":"x86")+".zip")throw new Exception("Architecture update asset");
                if(Updates.UpdateCore.AllowedFile("autoplay/artifacts/private.json"))throw new Exception("State allowed in update");
                if(!Updates.UpdateCore.AllowedFile("autoplay/tools/kakao-auto-bridge.js"))throw new Exception("Engine excluded from update");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"autoplay-integration-test.txt"),"PASS: shared UI ownership, optional startup setting, stop lease, architecture-specific updates, state exclusion. No Kakao interaction.");
            }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"autoplay-integration-test.txt"),"FAIL: "+ex);Environment.ExitCode=1;}
        }
    }
}
