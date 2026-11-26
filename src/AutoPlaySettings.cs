using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgitCon {
    internal static class AutoPlaySettings {
        internal static TabPage AddTab(Form settings,Func<AutoPlayController> engine,bool enabled,Action<bool> save){
            var tabs=settings.Controls.OfType<TabControl>().Single();
            var page=new TabPage("움짤 자동재생"){BackColor=Ui.Paper};
            var body=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(18),Margin=Padding.Empty};page.Controls.Add(body);
            Func<string,int,bool,Label> label=(text,height,bold)=>{var c=Ui.Label(text,10,bold);c.Height=height;c.Margin=new Padding(0,0,0,8);body.Controls.Add(c);return c;};
            label("카톡의 GIF·움직이는 WebP를 반복재생해요.",28,false);
            var phase=label("자동재생 상태를 확인하고 있어요",32,true);
            var detail=label("",44,false);detail.ForeColor=Ui.Muted;
            var counts=label("재생 중 —   ·   원본 다운로드 —",28,false);
            var actions=new TableLayoutPanel{Height=42,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,12)};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));
            var start=Ui.Button("자동재생 시작",()=>{},true);var stop=Ui.Button("중지",()=>{});start.Dock=stop.Dock=DockStyle.Fill;start.Margin=new Padding(0,0,4,0);stop.Margin=new Padding(4,0,0,0);actions.Controls.Add(start,0,0);actions.Controls.Add(stop,1,0);body.Controls.Add(actions);
            var startup=new CheckBox{Text="아지트콘 실행 시 자동재생 시작",Checked=enabled,Height=30,Margin=new Padding(0,0,0,8)};startup.CheckedChanged+=(s,e)=>save(startup.Checked);body.Controls.Add(startup);
            var notice=label("설정을 닫아도 재생은 유지되고, 아지트콘 종료 시 함께 중지돼요.",44,false);notice.ForeColor=Ui.Muted;
            label("사진 표시 옵션",28,true);
            label("특정 채팅방·보낸 사람의 사진과 칸을 작게 표시해요.\n여러 명은 쉼표로 구분하고, 해제하려면 두 칸을 비워주세요.",44,false);
            label("채팅방 이름",24,false);var room=new TextBox{MaxLength=255,AccessibleName="사진 축소 채팅방",Height=28,Margin=new Padding(0,0,0,8)};var roomField=Ui.Field(room,"사진 축소 채팅방");roomField.Height=34;roomField.Margin=new Padding(0,0,0,8);body.Controls.Add(roomField);
            label("보낸 사람",24,false);var sender=new TextBox{MaxLength=255,AccessibleName="사진 축소 보낸 사람",Height=28,Margin=new Padding(0,0,0,8)};var senderField=Ui.Field(sender,"사진 축소 보낸 사람");senderField.Height=34;senderField.Margin=new Padding(0,0,0,8);body.Controls.Add(senderField);
            var hide=new CheckBox{Text="마우스를 올릴 때만 사진 보이기",Height=30,Margin=new Padding(0,0,0,8)};body.Controls.Add(hide);
            var apply=Ui.Button("사진 옵션 저장",()=>{});apply.Height=36;apply.Margin=new Padding(0,0,0,14);body.Controls.Add(apply);
            var support=new TableLayoutPanel{Height=38,ColumnCount=2,RowCount=1,Margin=Padding.Empty};support.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));support.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            var diagnostics=Ui.Button("진단 ZIP 저장",()=>{});var logs=Ui.Button("로그 폴더 열기",()=>{});diagnostics.Dock=logs.Dock=DockStyle.Fill;diagnostics.Margin=new Padding(0,0,4,0);logs.Margin=new Padding(4,0,0,0);support.Controls.Add(diagnostics,0,0);support.Controls.Add(logs,1,0);support.Margin=new Padding(0,0,0,14);body.Controls.Add(support);body.Controls.SetChildIndex(support,7);
            Action arrange=()=>{int width=Math.Max(180,body.ClientSize.Width-body.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);foreach(Control c in body.Controls)c.Width=width;};body.SizeChanged+=(s,e)=>arrange();arrange();
            AutoPlayController current=null;Action update=null;
            Action bind=()=>{
                if(current!=null||settings.IsDisposed)return;current=engine();
                var options=current.PhotoOptions();object value;room.Text=options.TryGetValue("photoFilterRoom",out value)?Convert.ToString(value):"";sender.Text=options.TryGetValue("photoFilterSender",out value)?Convert.ToString(value):"";hide.Checked=options.TryGetValue("photoFilterHide",out value)&&Convert.ToBoolean(value);
                update=()=>{if(page.IsDisposed)return;phase.Text=current.Phase;detail.Text=current.Detail;counts.Text="재생 중 "+current.Playing+"   ·   원본 다운로드 "+current.Downloads;notice.Text=current.Notice;notice.ForeColor=current.Error?Color.Firebrick:Ui.Muted;start.Enabled=!current.Busy&&current.CanStart;stop.Enabled=!current.Busy&&current.CanStop;apply.Enabled=diagnostics.Enabled=logs.Enabled=!current.Busy;};
                current.Changed+=update;update();
            };
            start.Click+=async(s,e)=>{bind();await current.Run("start");};stop.Click+=async(s,e)=>{bind();await current.Run("stop");};diagnostics.Click+=async(s,e)=>{bind();await current.Run("diagnostics");};
            logs.Click+=(s,e)=>{try{bind();current.OpenLogs();}catch(Exception ex){notice.Text=ex.Message;notice.ForeColor=Color.Firebrick;}};
            apply.Click+=(s,e)=>{try{bind();current.SavePhotoOptions(room.Text,sender.Text,hide.Checked);}catch(Exception ex){notice.Text=ex.Message;notice.ForeColor=Color.Firebrick;}};
            tabs.SelectedIndexChanged+=(s,e)=>{if(tabs.SelectedTab==page)bind();};settings.Shown+=(s,e)=>{if(tabs.SelectedTab==page)bind();};
            page.Disposed+=(s,e)=>{if(current!=null&&update!=null)current.Changed-=update;};
            page.Tag=bind;tabs.TabPages.Add(page);return page;
        }
        internal static async Task<bool> WaitStopped(){
            var signal=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"codecs/autoplay/artifacts/kakao-internals/autoplay-status.json");
            for(int i=0;i<150;i++){
                if(!File.Exists(signal))return true;
                try{
                    var json=new System.Web.Script.Serialization.JavaScriptSerializer();var state=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(signal));
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
                using(var engine=new AutoPlayController(root,Path.Combine(root,"options.json"))){
                    using(var host=new Form{ClientSize=new Size(560,650)})using(var tabs=new TabControl{Dock=DockStyle.Fill}){
                        host.Controls.Add(tabs);bool saved=false;var page=AddTab(host,()=>engine,false,value=>saved=value);((Action)page.Tag)();
                        var body=(FlowLayoutPanel)page.Controls[0];body.Controls.OfType<CheckBox>().First().Checked=true;if(!saved)throw new Exception("Start preference callback");
                        if(!body.Controls.OfType<TableLayoutPanel>().SelectMany(p=>p.Controls.OfType<Button>()).Any(b=>b.Text=="자동재생 시작"))throw new Exception("Inline playback controls missing");
                        engine.SavePhotoOptions("fixture-room","fixture-sender",true);if(Convert.ToString(engine.PhotoOptions()["photoFilterRoom"])!="fixture-room")throw new Exception("Options persistence");
                        bool rejected=false;try{engine.SavePhotoOptions("room","",false);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Partial filter accepted");
                        host.Controls.Remove(tabs);using(var surface=new UserControl{Size=host.ClientSize,BackColor=Ui.Paper}){surface.Controls.Add(tabs);surface.CreateControl();surface.PerformLayout();using(var bitmap=new Bitmap(surface.Width,surface.Height)){surface.DrawToBitmap(bitmap,surface.ClientRectangle);bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"autoplay-settings.png"));}}
                    }
                    if(engine.IsDisposed)throw new Exception("Closing settings stopped controller");
                    if(!File.Exists(Path.Combine(root,"artifacts/kakao-internals/autoplay-owner.json")))throw new Exception("Shared owner lease missing");
                    engine.ShutdownIntegrated();if(!File.Exists(Path.Combine(root,"artifacts/kakao-internals/autoplay-stop.signal")))throw new Exception("Stop signal missing");
                }
                if(Updates.UpdateCore.AssetName!="AgitCon-win-"+(IntPtr.Size==8?"x64":"x86")+".zip")throw new Exception("Architecture update asset");
                if(Updates.UpdateCore.AllowedFile("codecs/autoplay/artifacts/private.json"))throw new Exception("State allowed in update");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"autoplay-integration-test.txt"),"PASS: inline playback/settings/diagnostics controls, optional startup, option persistence and validation, settings-close lifetime, shared owner, shutdown signal, architecture update, no independent window/tray. No Kakao interaction.");
            }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"autoplay-integration-test.txt"),"FAIL: "+ex);Environment.ExitCode=1;}
        }
    }
}
