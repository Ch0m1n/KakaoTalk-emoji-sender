using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal class SoftPanel : Panel
{
    public Color Fill = Color.White;
    public SoftPanel() { DoubleBuffered=true; BackColor=Color.Transparent; }
    internal static GraphicsPath Rounded(Rectangle r, int radius) {
        var p=new GraphicsPath(); int d=radius*2;
        p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=Rounded(new Rectangle(0,0,Width-1,Height-1),18))
        using(var brush=new SolidBrush(Fill)) e.Graphics.FillPath(brush,p);
        base.OnPaint(e);
    }
}

internal class SoftButton : Button
{
    public bool Primary;
    bool hover;
    public SoftButton() { FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); }
    protected override void OnMouseEnter(EventArgs e) {hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e) {hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var surface=Parent as SoftPanel;
        e.Graphics.Clear(surface!=null?surface.Fill:(Parent!=null?Parent.BackColor:Color.White));
        var color=Primary?(hover?Color.FromArgb(246,208,48):Color.FromArgb(255,223,76)):
            (hover?Color.FromArgb(226,231,234):Color.FromArgb(240,243,245));
        if(!Enabled)color=Color.FromArgb(233,236,238);
        using(var p=SoftPanel.Rounded(new Rectangle(0,0,Width-1,Height-1),10))
        using(var b=new SolidBrush(color))e.Graphics.FillPath(b,p);
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?Color.FromArgb(30,40,47):Color.Gray,
            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(5,5,Width-10,Height-10));
    }
}

internal class AutoPlayForm : Form
{
    readonly string root,state;
    readonly int ownerPid;
    readonly long ownerCreated;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    readonly Color ink=Color.FromArgb(29,43,51),muted=Color.FromArgb(98,112,120);
    readonly Label phase,detail,playing,downloads,notice;
    readonly SoftButton start,stop,startup,diagnostics,logs,update;
    readonly Timer timer=new Timer();
    readonly NotifyIcon tray=new NotifyIcon();
    readonly ContextMenuStrip trayMenu=new ContextMenuStrip();
    readonly string settingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KakaoAutoPlay","settings.json");
    bool integrated; Action<bool> saveEnabled;
    public void ConfigureIntegrated(Action checkUpdate,Action<bool> save) {integrated=true;saveEnabled=save;tray.Visible=false;Text="아지트콘 · 움짤 자동재생";Icon=AgitCon.Ui.AppIcon();update.Click+=delegate{checkUpdate();};}
    public Task ResumeIntegrated(){return Action("start",null);}
    public void ShutdownIntegrated(){exitRequested=true;StopOwnedPlayback();Close();}
    bool busy,updateAvailable,lastActionOk,exitRequested,closeToTray=true;
    public bool HiddenInTray { get { return !Visible && tray.Visible; } }
    public void RestoreWindow(){Show();WindowState=FormWindowState.Normal;Activate();}
    void ExitApplication(){if(busy){RestoreWindow();notice.Text="작업을 마친 뒤 완전 종료를 다시 눌러 주세요.";return;}exitRequested=true;Close();}
    void ShowOptions(){
        using(var dialog=new Form {Text="Kakao AutoPlay 옵션",ClientSize=new Size(390,352),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,Font=Font}) {
            var label=new Label {Text=integrated?"X 버튼은 창만 숨겨요. 종료는 아지트콘 트레이에서 해주세요.":"창의 X 버튼을 누르면",Left=20,Top=20,AutoSize=true};
            var choices=new ComboBox {Left=20,Top=50,Width=350,DropDownStyle=ComboBoxStyle.DropDownList};
            choices.Enabled=!integrated;
            choices.Items.AddRange(new object[]{"트레이로 숨기고 계속 재생 (기본)","프로그램과 재생 작업 종료"});choices.SelectedIndex=closeToTray?0:1;
            var filterLabel=new Label {Text="사진과 칸을 함께 작게 표시 (최대 64픽셀)",Left=20,Top=100,AutoSize=true};
            var roomLabel=new Label {Text="채팅방 이름",Left=20,Top=140,AutoSize=true};
            var senderLabel=new Label {Text="보낸 사람",Left=20,Top=180,AutoSize=true};
            var room=new TextBox {Left=130,Top=136,Width=240,MaxLength=255,AccessibleName=roomLabel.Text};
            var sender=new TextBox {Left=130,Top=176,Width=240,MaxLength=255,AccessibleName=senderLabel.Text};
            var hide=new CheckBox {Text="마우스를 올릴 때만 사진 보이기",Left=20,Top=214,Width=350};
            var help=new Label {Text="여러 명은 쉼표로 구분. 끄려면 두 칸을 비우세요.",Left=20,Top=254,AutoSize=true};
            try {
                var values=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(settingsPath));
                room.Text=Value(values,"photoFilterRoom","");
                sender.Text=Value(values,"photoFilterSender","");
                hide.Checked=Value(values,"photoFilterHide","False")=="True";
            }catch{}
            var save=new Button {Text="저장",Left=280,Top=306,Width=90};
            save.Click+=delegate {
                try {
                    string filterRoom=room.Text.Trim(),filterSender=sender.Text.Trim();
                    if((filterRoom.Length==0)!=(filterSender.Length==0)) {
                        MessageBox.Show(dialog,"채팅방 이름과 보낸 사람을 모두 입력해 주세요.");
                        return;
                    }
                    var values=new Dictionary<string,object>();
                    if(File.Exists(settingsPath))values=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(settingsPath))??values;
                    values["closeToTray"]=choices.SelectedIndex==0;
                    values["photoFilterRoom"]=filterRoom;
                    values["photoFilterSender"]=filterSender;
                    values["photoFilterHide"]=hide.Checked;
                    Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                    string temp=settingsPath+"."+Guid.NewGuid().ToString("N")+".tmp";
                    try{File.WriteAllText(temp,json.Serialize(values),Encoding.UTF8);if(File.Exists(settingsPath))File.Replace(temp,settingsPath,null);else File.Move(temp,settingsPath);}finally{if(File.Exists(temp))File.Delete(temp);}
                    closeToTray=choices.SelectedIndex==0;dialog.Close();
                }catch(Exception error){MessageBox.Show(dialog,"설정을 저장하지 못했어요: "+error.Message);}
            };
            dialog.Controls.AddRange(new Control[]{label,choices,filterLabel,roomLabel,room,senderLabel,sender,hide,help,save});dialog.AcceptButton=save;dialog.ShowDialog(this);
        }
    }
    public AutoPlayForm(string bundle,string previousBundle=null,bool resume=false,bool startInTray=false) {
        root=bundle;state=Path.Combine(root,"artifacts","kakao-internals");
        using(var self=Process.GetCurrentProcess()){ownerPid=self.Id;ownerCreated=self.StartTime.ToUniversalTime().ToFileTimeUtc();}
        BindOwner();
        try{var settings=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(settingsPath));object value;if(settings.TryGetValue("closeToTray",out value)&&value is bool)closeToTray=(bool)value;}catch{}
        Text="Kakao AutoPlay";ClientSize=new Size(584,758);MinimumSize=Size;MaximumSize=Size;
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(245,247,248);Font=new Font("맑은 고딕",10);ForeColor=ink;DoubleBuffered=true;
        AddLabel(this,"AUTOPLAY / PERSONAL",28,24,350,22,9,muted,true);
        var options=Button(this,"옵션",456,18,100,34,false);options.Click+=delegate {ShowOptions();};
        AddLabel(this,"움짤은, 움직이게.",28,53,510,45,25,ink,true);
        AddLabel(this,"GIF와 WebP를 채팅방에서 자동으로, 계속.",30,105,520,26,11,muted,false);
        var card=new SoftPanel {Left=28,Top=153,Width=528,Height=240};Controls.Add(card);
        phase=AddLabel(card,"시작할 준비가 됐어요",22,20,480,29,15,ink,true);
        detail=AddLabel(card,"시작을 누르고 카톡 채팅방을 열어 주세요.",22,57,480,26,10,muted,false);
        playing=AddLabel(card,"—",22,101,220,35,22,ink,true);
        downloads=AddLabel(card,"—",278,101,220,35,22,ink,true);
        playing.AutoSize=true;downloads.AutoSize=true;playing.Top=88;downloads.Top=88;
        AddLabel(card,"재생 중인 원본",24,141,220,22,9,muted,false);
        AddLabel(card,"원본 다운로드 완료",280,141,220,22,9,muted,false);
        start=Button(card,"자동재생 시작",22,183,298,42,true);
        stop=Button(card,"중지",332,183,174,42,false);
        start.Click+=async delegate {await Action("start",null);};
        stop.Click+=async delegate {await Action("stop",null);};
        AddLabel(this,"아지트콘을 켤 때도 자동으로",30,415,260,24,11,ink,true);
        AddLabel(this,"아지트콘 시작프로그램 등록과 함께 사용해요.",30,445,315,23,9,muted,false);
        startup=Button(this,"앱 시작 시 재생",367,418,189,42,false);
        startup.Click+=async delegate {await Action("startup",null);};
        AddLabel(this,"문제가 생겼나요?",30,497,280,25,11,ink,true);
        AddLabel(this,"실행·오류 기록을 진단 ZIP으로 저장해 주세요.",30,528,520,23,9,muted,false);
        diagnostics=Button(this,"진단 ZIP 저장",28,565,259,42,false);
        logs=Button(this,"로그 폴더 열기",299,565,257,42,false);
        diagnostics.Click+=async delegate {await Action("diagnostics",null);};
        logs.Click+=delegate {Directory.CreateDirectory(state);Process.Start("explorer.exe","\""+state+"\"");};
        string version="알 수 없음";
        try{version=Convert.ToString(json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"version.json")))["version"]);}catch{}
        AddLabel(this,"현재 버전  "+version,30,632,310,24,11,ink,true);
        AddLabel(this,"GitHub Releases에서 새 버전을 확인해요.",30,661,320,23,9,muted,false);
        update=Button(this,"업데이트 확인",367,635,189,42,false);
        update.Click+=async delegate {
            if(integrated)return;
            if(updateAvailable&&MessageBox.Show(this,"새 버전을 다운로드하고 실행할까요?\n재생 중이면 안전하게 중지한 뒤 다시 이어가요.","Kakao AutoPlay 업데이트",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            await Action(updateAvailable?"update-install":"update-check",null);
        };
        notice=AddLabel(this,"X는 트레이로 숨겨요. 완전 종료는 트레이 메뉴에서 할 수 있어요.",30,698,520,48,9,muted,false);
        timer.Interval=2000;timer.Tick+=delegate {RefreshStatus();};timer.Start();
        tray.Text="Kakao AutoPlay";tray.Icon=SystemIcons.Application;tray.ContextMenuStrip=trayMenu;
        trayMenu.Items.Add("열기",null,delegate {RestoreWindow();});
        trayMenu.Items.Add("옵션",null,delegate {RestoreWindow();ShowOptions();});
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("완전 종료",null,delegate {ExitApplication();});
        tray.DoubleClick+=delegate {RestoreWindow();};tray.Visible=true;
        FormClosing+=delegate(object sender,FormClosingEventArgs e){
            if(e.CloseReason==CloseReason.UserClosing&&!exitRequested){
                if(integrated||closeToTray){e.Cancel=true;Hide();}
                else if(busy){e.Cancel=true;notice.Text="작업을 마친 뒤 종료해 주세요.";}
            }
        };
        FormClosed+=delegate {timer.Stop();timer.Dispose();tray.Visible=false;tray.Dispose();trayMenu.Dispose();StopOwnedPlayback();};RefreshStatus();
        Shown+=async delegate {
            if(integrated)return;
            if(startInTray)Hide();
            await Action(previousBundle!=null?"update-adopt":"startup-migrate",previousBundle);
            if(resume)await Action("start",null);
        };
    }
    void BindOwner(){
        Directory.CreateDirectory(state);string path=Path.Combine(state,"autoplay-owner.json"),temporary=Path.Combine(state,"owner-"+Guid.NewGuid().ToString("N")+".tmp");
        try{File.WriteAllText(temporary,json.Serialize(new Dictionary<string,object>{{"pid",ownerPid},{"created",ownerCreated}}),Encoding.UTF8);
            for(int i=0;;i++){try{if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);break;}catch(IOException){if(i>=3)throw;System.Threading.Thread.Sleep(20);}}
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    void StopOwnedPlayback(){try{var lease=Read("autoplay-owner.json");if(Convert.ToInt64(lease["pid"])==ownerPid&&Convert.ToInt64(lease["created"])==ownerCreated)File.WriteAllText(Path.Combine(state,"autoplay-stop.signal"),"owner-closed",Encoding.ASCII);}catch{}}
    Label AddLabel(Control parent,string text,int x,int y,int w,int h,float size,Color color,bool bold) {
        var surface=parent as SoftPanel;
        var l=new Label {Text=text,Left=x,Top=y,Width=w,Height=h,BackColor=surface!=null?surface.Fill:parent.BackColor,ForeColor=color,
            Font=new Font("맑은 고딕",size,bold?FontStyle.Bold:FontStyle.Regular),AutoEllipsis=true};parent.Controls.Add(l);return l;
    }
    SoftButton Button(Control parent,string text,int x,int y,int w,int h,bool primary) {
        var b=new SoftButton {Text=text,AccessibleName=text,Left=x,Top=y,Width=w,Height=h,Primary=primary,
            Font=new Font("맑은 고딕",10,FontStyle.Bold)};parent.Controls.Add(b);return b;
    }
    Dictionary<string,object> Read(string name) {
        try {return json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(state,name)));}
        catch {return new Dictionary<string,object>();}
    }
    static string Value(Dictionary<string,object> obj,string key,string fallback) {return obj.ContainsKey(key)?Convert.ToString(obj[key]):fallback;}
    void RefreshStatus() {
        if(busy)return;
        var s=Read("autoplay-status.json");double updated;
        bool fresh=double.TryParse(Value(s,"updated","0"),out updated)&&DateTimeOffset.UtcNow.ToUnixTimeSeconds()-updated<10;
        string status=fresh?Value(s,"phase","waiting"):"idle";
        bool running=status=="running";
        phase.Text=running?"자동재생이 켜져 있어요":status=="waiting"||status=="waiting-for-chat"?"채팅방을 기다리고 있어요":
            status=="starting"||status=="reconnecting"?"카톡에 연결하고 있어요":status=="stopping"?"안전하게 종료하고 있어요":"시작할 준비가 됐어요";
        phase.ForeColor=running?Color.FromArgb(29,115,83):ink;
        detail.Text=running?"새 GIF · WebP를 자동으로 받고 반복재생해요.":"시작을 누르고 GIF가 있는 채팅방을 열어 주세요.";
        string lastExit=Value(s,"lastWorkerExit","");
        if(fresh&&!running&&lastExit!=""&&lastExit!="0"&&status!="stopped"&&status!="stopping") {
            phase.Text="재생 작업이 종료돼 다시 연결 중이에요";phase.ForeColor=Color.FromArgb(175,62,44);
            detail.Text="진단 ZIP에 종료 이유를 기록해요. 진단 저장을 눌러 주세요.";
        }
        var p=Read("bitmap-bridge-progress.json");
        var d=p.ContainsKey("originalDownloads")?p["originalDownloads"] as Dictionary<string,object>:null;
        if(running&&d!=null&&Value(d,"available","")=="False")detail.Text="저장된 움짤 재생 중 · 원본 자동 받기는 중단됐어요.";
        if(running&&Value(p,"architecture","")=="x64")detail.Text="64비트 시험 연결 · 저장된 움짤 재생만 지원해요.";
        if(running&&Value(p,"engine","")=="qt6-live")detail.Text="Qt 카톡 연결 · 사진 원본을 캐시에 받아 반복재생해요.";
        var filter=p.ContainsKey("photoFilter")?p["photoFilter"] as Dictionary<string,object>:null;
        if(running&&filter!=null&&Value(filter,"enabled","False")=="True")
            detail.Text="사진 칸 축소 적용 중 · "+Value(filter,"compacted","0")+"개";
        playing.Text=running?Value(p,"active","0")+"개":"—";
        downloads.Text=running&&d!=null?Value(d,"saved","0")+"개":"—";
        start.Enabled=!fresh||status=="stopped";stop.Enabled=fresh&&status!="stopped";
        if(Value(s,"connectionError","")!="") {
            phase.Text="카톡 연결을 확인해주세요";phase.ForeColor=Color.FromArgb(175,62,44);
            detail.Text=Value(s,"connectionError","");
        }
    }
    async Task Action(string action,string output) {
        if(busy||IsDisposed)return;
        if(integrated&&(action=="startup"||action=="stop")){saveEnabled(action=="startup");if(action=="startup")action="start";}
        busy=true;
        lastActionOk=false;
        foreach(var b in new[]{start,stop,startup,diagnostics,logs,update})b.Enabled=false;
        notice.ForeColor=muted;notice.Text=action=="diagnostics"?"진단 기록을 모으고 있어요…":"요청을 처리하고 있어요…";
        try {
            if(action=="start"||action=="startup")BindOwner();
            string result=await Task.Run(()=>{
                var info=new ProcessStartInfo(Path.Combine(root,"..","..","runtime","python","python.exe"),"-X utf8 \""+Path.Combine(root,"tools","kakao-portable-launcher.py")+"\" "+action+" --json"+(output==null?"":" --output \""+output+"\""));
                info.UseShellExecute=false;info.CreateNoWindow=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;
                info.EnvironmentVariables["KAKAO_AUTOPLAY_EXE"]=Application.ExecutablePath;info.EnvironmentVariables["AGITCON_INTEGRATED"]="1";
                info.StandardOutputEncoding=Encoding.UTF8;info.StandardErrorEncoding=Encoding.UTF8;info.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";
                using(var process=Process.Start(info)) {
                    var stderr=process.StandardError.ReadToEndAsync();string stdout=process.StandardOutput.ReadToEnd();process.WaitForExit();
                    if(stdout.Trim().Length==0)throw new Exception(stderr.Result.Length>0?stderr.Result:"실행 결과를 받지 못했어요. 로그 폴더를 확인해 주세요.");
                    return stdout.Trim();
                }
            });
            if(IsDisposed)return;
            var response=json.Deserialize<Dictionary<string,object>>(result);
            bool ok=Value(response,"ok","False")=="True";
            lastActionOk=ok;
            notice.ForeColor=ok?Color.FromArgb(29,115,83):Color.FromArgb(175,62,44);
            notice.Text=ok&&action=="diagnostics"?"진단 ZIP을 저장했어요. ‘로그 폴더 열기’에서 찾을 수 있어요.":Value(response,"message","처리를 마쳤어요.");
            if(ok&&(action=="update-check"||action=="update-install")) {
                var info=response["message"] as Dictionary<string,object>;
                updateAvailable=Value(info,"available","False")=="True";
                update.Text=updateAvailable?"v"+Value(info,"latest","")+" 업데이트":"업데이트 확인";
                notice.Text=updateAvailable?"새 버전이 있어요. 업데이트 버튼을 눌러 설치할 수 있어요.":Value(info,"notPublished","False")=="True"?"아직 공개된 배포 버전이 없어요.":"현재 최신 버전을 사용하고 있어요.";
                if(action=="update-install"&&info.ContainsKey("path")) {
                    string arguments="--upgrade-from \""+root+"\""+(Value(info,"resume","False")=="True"?" --resume":"");
                    Process.Start(new ProcessStartInfo(Convert.ToString(info["path"]),arguments){UseShellExecute=false});
                    exitRequested=true;Close();
                }
            }
        } catch(Exception error) {notice.ForeColor=Color.FromArgb(175,62,44);notice.Text=error.Message;}
        finally {busy=false;if(!IsDisposed){foreach(var b in new[]{startup,diagnostics,logs,update})b.Enabled=true;RefreshStatus();}}
    }
}
