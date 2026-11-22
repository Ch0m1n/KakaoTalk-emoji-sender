using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace AgitCon {
    internal enum OcrState { Available, Missing, Unavailable }
    internal sealed class OcrAvailability {
        internal OcrState State; internal string Detail;
        internal static OcrAvailability Of(OcrState state,string detail=null){return new OcrAvailability{State=state,Detail=detail};}
        internal string Caption {get{return State==OcrState.Available?"한국어 OCR: 사용 가능":State==OcrState.Missing?"한국어 OCR: 설치 안 됨":"한국어 OCR: 확인하지 못함";}}
    }
    internal static class OcrSupport {
        static readonly object sync=new object();static Task<OcrAvailability> checking;
        internal static OcrAvailability Check(){try{var engine=OcrEngine.TryCreateFromLanguage(new Language("ko-KR"));return engine!=null&&(engine.RecognizerLanguage.LanguageTag.Equals("ko",StringComparison.OrdinalIgnoreCase)||engine.RecognizerLanguage.LanguageTag.StartsWith("ko-",StringComparison.OrdinalIgnoreCase))?OcrAvailability.Of(OcrState.Available):OcrAvailability.Of(OcrState.Missing);}catch(Exception ex){return OcrAvailability.Of(OcrState.Unavailable,"확인 오류 0x"+ex.HResult.ToString("X8"));}}
        internal static async Task<OcrAvailability> CheckAsync(){Task<OcrAvailability> task;lock(sync){if(checking==null||checking.IsCompleted)checking=Task.Run(()=>Check());task=checking;}await Task.WhenAny(task,Task.Delay(3000));return task.IsCompleted?await task:OcrAvailability.Of(OcrState.Unavailable,"확인 시간이 초과됐어요. 다시 확인해주세요.");}
        internal static bool ShouldPrompt(bool prompted,OcrAvailability result){return !prompted&&result.State!=OcrState.Available;}
    }
    internal sealed class OcrInstallResult {
        internal OcrAvailability Availability;internal string Message;internal bool PendingActivation;
    }
    internal sealed class OcrInstaller {
        static readonly System.Threading.SemaphoreSlim gate=new System.Threading.SemaphoreSlim(1);
        readonly Func<Task<OcrAvailability>> check;readonly Func<Task<int>> launch;
        internal bool Busy {get;private set;}
        internal OcrInstaller(Func<Task<OcrAvailability>> check=null,Func<Task<int>> launch=null){this.check=check??OcrSupport.CheckAsync;this.launch=launch??RunDism;}
        internal static ProcessStartInfo InstallCommand(){return new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"dism.exe"),"/Online /Add-Capability /CapabilityName:Language.OCR~~~ko-KR~0.0.1.0 /NoRestart"){UseShellExecute=true,Verb="runas",WorkingDirectory=Environment.SystemDirectory,WindowStyle=ProcessWindowStyle.Hidden};}
        static Task<int> RunDism(){return Task.Run(()=>{using(var process=Process.Start(InstallCommand())){if(process==null)throw new InvalidOperationException("Windows 설치 도구를 시작하지 못했어요.");process.WaitForExit();return process.ExitCode;}});}
        internal async Task<OcrInstallResult> Install(bool userRequested){
            if(!userRequested)return new OcrInstallResult{Message="설치는 설치 버튼을 눌렀을 때만 시작해요."};
            if(Busy||!gate.Wait(0))return new OcrInstallResult{Message="이미 설치 중이에요."};Busy=true;
            try{var before=await check();if(before.State!=OcrState.Missing)return new OcrInstallResult{Availability=before,Message=before.State==OcrState.Available?"이미 사용 가능해요. 설치하지 않았어요.":"설치 여부를 확인하지 못해 설치를 시작하지 않았어요."};
                int code=await launch();var after=await check();
                if(after.State==OcrState.Available)return new OcrInstallResult{Availability=after,Message="한국어 OCR을 사용할 수 있어요."};
                if(code==0||code==3010)return new OcrInstallResult{Availability=after,PendingActivation=true,Message=code==3010?"Windows에서 재시작이 필요하다고 알려왔어요. 작업을 저장하고 직접 재시작한 뒤 다시 확인해주세요. 자동 재시작은 하지 않아요.":"Windows 설치 명령은 완료됐지만 현재 앱에서는 아직 OCR을 사용할 수 없어요. 다시 확인하거나 아지트콘을 재시작해주세요."};
                return new OcrInstallResult{Availability=after,Message="설치하지 못했어요. Windows 오류 0x"+unchecked((uint)code).ToString("X8")+"\n인터넷 연결·Windows Update·조직 정책을 확인하거나 아래 Windows 설정에서 설치해주세요."};
            }catch(Win32Exception ex){return new OcrInstallResult{Message=ex.NativeErrorCode==1223?"Windows 권한 요청을 취소했어요. 필요할 때 다시 설치할 수 있어요.":"Windows 설치 도구를 시작하지 못했어요. 오류 0x"+ex.NativeErrorCode.ToString("X8")};}
            catch(Exception ex){return new OcrInstallResult{Message="설치를 완료하지 못했어요. 오류 0x"+ex.HResult.ToString("X8")+"\nWindows 설정에서 상태를 확인해주세요."};}
            finally{Busy=false;gate.Release();}
        }
    }
    internal sealed class OcrSetupForm:Form {
        readonly OcrInstaller installer;readonly Func<Task<OcrAvailability>> check;
        readonly Label state=Ui.Label("한국어 OCR 확인 중…",10);
        readonly ProgressBar progress=new ProgressBar{Dock=DockStyle.Top,Height=10};
        readonly UiButton install,recheck,later;bool busy,pending;OcrAvailability available;
        internal OcrSetupForm(OcrAvailability initial=null,Func<Task<OcrAvailability>> check=null,OcrInstaller installer=null){
            this.check=check??OcrSupport.CheckAsync;this.installer=installer??new OcrInstaller();available=initial;
            Text="아지트콘 · 한국어 OCR 준비";Icon=Ui.AppIcon();ClientSize=new Size(550,430);BackColor=Ui.Paper;Font=Ui.Regular(10);StartPosition=FormStartPosition.CenterParent;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=6};foreach(int height in new[]{44,102,114,20,44,58})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));Controls.Add(layout);
            var title=Ui.Label("움짤 첨부를 위한 준비",17,true);title.Dock=DockStyle.Fill;layout.Controls.Add(title);
            var description=Ui.Label("한국어 OCR은 카톡의 전송 전 확인 설정을 읽을 때 사용해요.\n없으면 Windows 기능을 설치할 수 있어요.\n설치 버튼을 누르면 Windows 관리자 권한을 요청하며,\n필요한 경우 Windows에서 기능 파일을 받아요.",10);description.Dock=DockStyle.Fill;layout.Controls.Add(description);state.Dock=DockStyle.Fill;layout.Controls.Add(state);layout.Controls.Add(progress);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};install=Ui.Button("한국어 OCR 설치",async()=>await Install(),true);install.Width=170;recheck=Ui.Button("다시 확인",async()=>await RefreshStatus());recheck.Width=100;later=Ui.Button("나중에",()=>Close());later.Width=90;buttons.Controls.AddRange(new Control[]{install,recheck,later});layout.Controls.Add(buttons);
            var link=new LinkLabel{Text="Windows 언어 설정 열기",AutoSize=true};link.LinkClicked+=(s,e)=>Process.Start(new ProcessStartInfo("ms-settings:regionlanguage"){UseShellExecute=true});layout.Controls.Add(link);
            Shown+=async(s,e)=>{if(available==null)await RefreshStatus();else Apply(available);};FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};Apply(initial);
        }
        void SetBusy(bool value){busy=value;install.Enabled=!value&&!pending&&available!=null&&available.State==OcrState.Missing;recheck.Enabled=later.Enabled=!value;progress.Style=value?ProgressBarStyle.Marquee:ProgressBarStyle.Blocks;progress.MarqueeAnimationSpeed=value?25:0;}
        void Apply(OcrAvailability value){available=value;state.Text=value==null?"한국어 OCR 확인 중…":value.Caption+"\n"+(value.Detail??(value.State==OcrState.Missing?"설치하지 않아도 일반 검색과 정지 콘 첨부는 쓸 수 있어요.":value.State==OcrState.Available?"추가 설치가 필요 없어요.":"다시 확인하거나 Windows 언어 설정을 확인해주세요."));later.Text=value!=null&&value.State==OcrState.Available?"닫기":"나중에";SetBusy(busy);}
        async Task RefreshStatus(){SetBusy(true);try{var result=await check();if(result.State==OcrState.Available)pending=false;Apply(result);}finally{SetBusy(false);}}
        async Task Install(){SetBusy(true);state.Text="Windows 한국어 OCR 설치 중…\n권한 요청 창이 보이면 내용을 확인해주세요.\n설치에는 몇 분이 걸릴 수 있어요. 자동 재시작은 하지 않아요.";try{var result=await installer.Install(true);pending=result.PendingActivation;if(result.Availability!=null)Apply(result.Availability);state.Text=(available==null?"":available.Caption+"\n")+result.Message;}finally{SetBusy(false);}}
        internal static void Probe(){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ocr-check.txt"),OcrSupport.Check().Caption+"\r\nRead only: no installation, elevation, images or Kakao interaction.");}
        internal static void Test(){string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ocr-setup-test.txt");try{Task.Run(async()=>{int checks=0;Action<bool> assert=value=>{if(!value)throw new Exception("OCR setup check "+checks);checks++;};foreach(OcrState state in Enum.GetValues(typeof(OcrState)))foreach(bool prompted in new[]{false,true})assert(OcrSupport.ShouldPrompt(prompted,OcrAvailability.Of(state))==(!prompted&&state!=OcrState.Available));var command=OcrInstaller.InstallCommand();assert(command.FileName==Path.Combine(Environment.SystemDirectory,"dism.exe")&&command.UseShellExecute&&command.Verb=="runas");assert(command.Arguments=="/Online /Add-Capability /CapabilityName:Language.OCR~~~ko-KR~0.0.1.0 /NoRestart");
                int launches=0;var missing=OcrAvailability.Of(OcrState.Missing);var ready=OcrAvailability.Of(OcrState.Available);var unknown=OcrAvailability.Of(OcrState.Unavailable);Func<Task<int>> runner=()=>{launches++;return Task.FromResult(0);};
                var service=new OcrInstaller(()=>Task.FromResult(missing),runner);await service.Install(false);assert(launches==0&&!service.Busy);
                service=new OcrInstaller(()=>Task.FromResult(ready),runner);assert((await service.Install(true)).Availability.State==OcrState.Available&&launches==0);
                service=new OcrInstaller(()=>Task.FromResult(unknown),runner);await service.Install(true);assert(launches==0);
                int calls=0;service=new OcrInstaller(()=>Task.FromResult(++calls==1?missing:ready),runner);assert((await service.Install(true)).Availability.State==OcrState.Available&&launches==1&&calls==2);
                service=new OcrInstaller(()=>Task.FromResult(missing),()=>Task.FromResult(3010));assert((await service.Install(true)).PendingActivation);
                service=new OcrInstaller(()=>Task.FromResult(missing),()=>Task.FromResult(0));assert((await service.Install(true)).PendingActivation);
                service=new OcrInstaller(()=>Task.FromResult(missing),()=>Task.FromResult(unchecked((int)0x800f0954)));var failure=await service.Install(true);assert(!failure.PendingActivation&&failure.Message.Contains("800F0954"));
                service=new OcrInstaller(()=>Task.FromResult(missing),()=>{throw new Win32Exception(1223);});assert((await service.Install(true)).Message.Contains("취소")&&!service.Busy);
                var done=new TaskCompletionSource<int>();service=new OcrInstaller(()=>Task.FromResult(missing),()=>done.Task);var first=service.Install(true);assert(service.Busy);var duplicate=await service.Install(true);assert(duplicate.Message.Contains("이미"));var other=new OcrInstaller(()=>Task.FromResult(missing),()=>{throw new Exception("Duplicate installer launched");});assert((await other.Install(true)).Message.Contains("이미")&&!other.Busy);done.SetResult(0);await first;assert(!service.Busy);
                File.WriteAllText(report,"PASS: "+checks+" first-run/skip/ready/missing/unknown, explicit consent, fixed elevated Windows command with NoRestart, post-install API recheck, pending activation/restart, failure, canceled UAC and duplicate guard cases. Fake installer only; no DISM/elevation/Windows changes/network/Kakao.");}).GetAwaiter().GetResult();
            using(var form=new OcrSetupForm(OcrAvailability.Of(OcrState.Missing),()=>Task.FromResult(OcrAvailability.Of(OcrState.Missing)),new OcrInstaller(()=>Task.FromResult(OcrAvailability.Of(OcrState.Missing)),()=>Task.FromResult(0))))using(var surface=new UserControl{Size=form.ClientSize,BackColor=Ui.Paper}){var layout=form.Controls[0];form.Controls.Remove(layout);surface.Controls.Add(layout);surface.CreateControl();surface.PerformLayout();using(var bitmap=new Bitmap(surface.Width,surface.Height)){surface.DrawToBitmap(bitmap,surface.ClientRectangle);bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ocr-setup-preview.png"));}}
            }catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}}
    }
}
