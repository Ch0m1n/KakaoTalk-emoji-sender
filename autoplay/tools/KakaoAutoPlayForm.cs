using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Integrated playback service owner. No standalone window, tray or updater.
internal sealed class AutoPlayController : IDisposable {
    readonly string root,state,settingsPath;
    readonly int ownerPid;readonly long ownerCreated;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    readonly Timer timer=new Timer();
    public event Action Changed;
    public bool IsDisposed {get;private set;}
    public bool Busy {get;private set;}
    public bool CanStart {get;private set;}
    public bool CanStop {get;private set;}
    public string Phase {get;private set;}
    public string Detail {get;private set;}
    public string Playing {get;private set;}
    public string Downloads {get;private set;}
    public string Notice {get;private set;}
    public bool Error {get;private set;}
    public AutoPlayController(string bundle,string optionsPath=null){
        root=bundle;state=Path.Combine(root,"artifacts","kakao-internals");
        settingsPath=optionsPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KakaoAutoPlay","settings.json");
        using(var self=Process.GetCurrentProcess()){ownerPid=self.Id;ownerCreated=self.StartTime.ToUniversalTime().ToFileTimeUtc();}
        BindOwner();Notice="아지트콘을 완전히 종료하면 자동재생도 함께 중지돼요.";
        timer.Interval=2000;timer.Tick+=(s,e)=>RefreshStatus();timer.Start();RefreshStatus();
    }
    static string Value(Dictionary<string,object> values,string key,string fallback){object value;return values!=null&&values.TryGetValue(key,out value)?Convert.ToString(value):fallback;}
    Dictionary<string,object> Read(string file){try{return json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file))??new Dictionary<string,object>();}catch{return new Dictionary<string,object>();}}
    void Notify(){if(!IsDisposed&&Changed!=null)Changed();}
    void BindOwner(){
        Directory.CreateDirectory(state);string path=Path.Combine(state,"autoplay-owner.json"),temp=Path.Combine(state,"owner-"+Guid.NewGuid().ToString("N")+".tmp");
        try{File.WriteAllText(temp,json.Serialize(new Dictionary<string,object>{{"pid",ownerPid},{"created",ownerCreated}}),Encoding.UTF8);
            for(int i=0;;i++){try{if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);break;}catch(IOException){if(i>=3)throw;System.Threading.Thread.Sleep(20);}}
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    void StopOwnedPlayback(){try{var lease=Read(Path.Combine(state,"autoplay-owner.json"));if(Convert.ToInt64(lease["pid"])==ownerPid&&Convert.ToInt64(lease["created"])==ownerCreated)File.WriteAllText(Path.Combine(state,"autoplay-stop.signal"),"owner-closed",Encoding.ASCII);}catch{}}
    public void RefreshStatus(){
        if(IsDisposed||Busy)return;
        var s=Read(Path.Combine(state,"autoplay-status.json"));double updated;
        bool fresh=double.TryParse(Value(s,"updated","0"),out updated)&&DateTimeOffset.UtcNow.ToUnixTimeSeconds()-updated<10;
        string status=fresh?Value(s,"phase","waiting"):"idle";bool running=status=="running";
        Phase=running?"자동재생 중":status=="waiting"||status=="waiting-for-chat"?"채팅방을 기다리고 있어요":status=="starting"||status=="reconnecting"?"카톡에 연결하고 있어요":status=="stopping"?"안전하게 중지하고 있어요":"자동재생이 꺼져 있어요";
        Detail=running?"GIF·움직이는 WebP를 받아 반복재생해요.":"시작을 누른 뒤 GIF가 있는 채팅방을 열어주세요.";
        string lastExit=Value(s,"lastWorkerExit","");
        if(fresh&&!running&&lastExit!=""&&lastExit!="0"&&status!="stopped"&&status!="stopping"){Phase="재생 작업에 다시 연결 중이에요";Detail="계속 반복되면 아래에서 진단 ZIP을 저장해주세요.";}
        var p=Read(Path.Combine(state,"bitmap-bridge-progress.json"));object data;
        var d=p.TryGetValue("originalDownloads",out data)?data as Dictionary<string,object>:null;
        if(running&&d!=null&&Value(d,"available","")=="False")Detail="저장된 움짤 재생 중 · 원본 자동 받기는 중단됐어요.";
        if(running&&Value(p,"architecture","")=="x64")Detail="64비트 시험 연결 · 저장된 움짤 재생만 지원해요.";
        if(running&&Value(p,"engine","")=="qt6-live")Detail="Qt 카톡 연결 · 사진 원본을 캐시에 받아 반복재생해요.";
        var filter=p.TryGetValue("photoFilter",out data)?data as Dictionary<string,object>:null;
        if(running&&filter!=null&&Value(filter,"enabled","False")=="True")Detail="사진 칸 축소 적용 중 · "+Value(filter,"compacted","0")+"개";
        Playing=running?Value(p,"active","0")+"개":"—";Downloads=running&&d!=null?Value(d,"saved","0")+"개":"—";
        CanStart=!fresh||status=="stopped";CanStop=fresh&&status!="stopped";
        if(Value(s,"connectionError","")!=""){Phase="카톡 연결을 확인해주세요";Detail=Value(s,"connectionError","");}
        Notify();
    }
    public Dictionary<string,object> PhotoOptions(){return Read(settingsPath);}
    public void SavePhotoOptions(string room,string sender,bool hide){
        room=room.Trim();sender=sender.Trim();if((room.Length==0)!=(sender.Length==0))throw new InvalidOperationException("채팅방 이름과 보낸 사람을 모두 입력해주세요.");
        var values=PhotoOptions();values["photoFilterRoom"]=room;values["photoFilterSender"]=sender;values["photoFilterHide"]=hide;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));string temp=settingsPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,json.Serialize(values),Encoding.UTF8);if(File.Exists(settingsPath))File.Replace(temp,settingsPath,null);else File.Move(temp,settingsPath);}finally{if(File.Exists(temp))File.Delete(temp);}
        Notice="사진 표시 옵션을 저장했어요.";Error=false;Notify();
    }
    public void OpenLogs(){Directory.CreateDirectory(state);Process.Start("explorer.exe","\""+state+"\"");}
    public Task ResumeIntegrated(){return Run("start");}
    public async Task Run(string action){
        if(IsDisposed||Busy)return;
        if(action!="start"&&action!="stop"&&action!="diagnostics")throw new ArgumentException("Unsupported integrated action");
        Busy=true;Error=false;Notice=action=="diagnostics"?"진단 기록을 모으고 있어요…":"요청을 처리하고 있어요…";Notify();
        try{
            if(action=="start")BindOwner();
            string responseText=await Task.Run(()=>{
                var info=new ProcessStartInfo(Path.Combine(root,"..","..","runtime","python","python.exe"),"-X utf8 \""+Path.Combine(root,"tools","kakao-portable-launcher.py")+"\" "+action+" --json"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                info.EnvironmentVariables["KAKAO_AUTOPLAY_EXE"]=Application.ExecutablePath;info.EnvironmentVariables["AGITCON_INTEGRATED"]="1";info.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";
                using(var child=Process.Start(info)){var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();if(!child.WaitForExit(120000)){try{child.Kill();}catch{}throw new IOException("응답 대기 시간이 길어졌어요. 진단 기록을 확인해주세요.");}string output=stdout.Result.Trim();if(output.Length==0)throw new IOException(stderr.Result.Length>0?stderr.Result:"실행 결과를 받지 못했어요.");return output;}
            });
            if(IsDisposed){StopOwnedPlayback();return;}
            var result=json.Deserialize<Dictionary<string,object>>(responseText);Error=Value(result,"ok","False")!="True";
            Notice=!Error&&action=="diagnostics"?"진단 ZIP을 저장했어요. 로그 폴더에서 찾을 수 있어요.":Value(result,"message","처리를 마쳤어요.");
        }catch(Exception ex){if(!IsDisposed){Error=true;Notice=ex.Message;}}
        finally{Busy=false;if(!IsDisposed)RefreshStatus();else StopOwnedPlayback();}
    }
    public void ShutdownIntegrated(){Dispose();}
    public void Dispose(){if(IsDisposed)return;IsDisposed=true;timer.Stop();timer.Dispose();StopOwnedPlayback();Changed=null;}
}
