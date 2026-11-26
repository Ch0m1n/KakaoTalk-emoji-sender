using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Automation;
using System.Collections.Generic;
using System.IO;

namespace AgitCon {
    public sealed class PreviewChatTarget {public IntPtr Window,Editor;public uint ProcessId,ThreadId;public string Title,EditorClass;public long Version;internal AutomationElement AccessibleEditor;internal string AccessibleId;}
    public sealed class PreviewPasteResult {public bool Requested;public string Reason,Stage,Block;}
    internal sealed class PasteChecks {
        internal Func<bool> SourceCurrent,TargetCurrent,ClipboardCurrent,ModifiersReleased,TargetFocused,Activate,Send;
        internal Func<string> SourceProblem=()=>"source-not-current",TargetProblem=()=>"target-not-current";internal Func<string> SelectionProblem;internal Func<CancellationToken,Task> Pause;internal Action End=()=>{};
    }
    internal static class PasteFlow {
        internal static async Task<PreviewPasteResult> Run(SemaphoreSlim gate,PasteChecks checks,CancellationToken token){await gate.WaitAsync(token);try{var result=await Execute(checks,token);if(!result.Requested&&result.Reason=="대상·포커스 또는 클립보드가 바뀌어 콘 복사만 했어요.")result.Reason=BlockedReason(result.Block);return result;}finally{checks.End();gate.Release();}}
        static string BlockedReason(string block){
            if(block=="target/not-captured")return "카톡 입력창을 확인하지 못해 콘만 복사했어요. 채팅방을 한 번 클릭한 뒤 다시 골라주세요.";
            if(block!=null&&block.StartsWith("target/"))return "첨부 대상이 바뀌었거나 입력창을 확인하지 못해 콘만 복사했어요. ["+block+"]";
            if(block!=null&&block.StartsWith("clipboard/"))return "다른 작업이 클립보드를 바꿔 첨부를 멈췄어요. 콘을 다시 골라주세요.";
            if(block!=null&&block.StartsWith("focus/"))return "카톡 입력창으로 포커스를 옮기지 못해 콘만 복사했어요.";
            if(block=="source/observation-hooks-missing")return "카톡 창 감시를 시작하지 못해 콘만 복사했어요. 앱을 다시 실행해주세요.";
            if(block!=null&&block.StartsWith("source/"))return "콘을 고른 창의 포커스가 바뀌어 첨부를 멈췄어요. 콘을 다시 골라주세요.";
            if(block!=null&&block.StartsWith("modifiers/"))return "Alt·Ctrl·Shift 키가 눌린 상태여서 첨부를 멈췄어요. 키를 놓고 다시 골라주세요.";
            return "첨부 조건을 확인하지 못해 콘만 복사했어요. ["+block+"]";
        }
        internal static async Task<PreviewPasteResult> Execute(PasteChecks c,CancellationToken token){
            token.ThrowIfCancellationRequested();var result=new PreviewPasteResult{Reason="대상·포커스 또는 클립보드가 바뀌어 콘 복사만 했어요."};
            Func<string,bool> sourceReady=stage=>{result.Stage=stage;if(!c.SourceCurrent()){result.Block="source/"+c.SourceProblem();return false;}if(!c.TargetCurrent()){result.Block="target/"+c.TargetProblem();return false;}if(!c.ClipboardCurrent()){result.Block="clipboard/changed";return false;}if(!c.ModifiersReleased()){result.Block="modifiers/held";return false;}return true;};
            Func<string,bool> targetReady=stage=>{result.Stage=stage;if(!c.TargetCurrent()){result.Block="target/"+c.TargetProblem();return false;}if(!c.ClipboardCurrent()){result.Block="clipboard/changed";return false;}if(!c.TargetFocused()){result.Block="focus/target-editor-not-focused";return false;}if(!c.ModifiersReleased()){result.Block="modifiers/held";return false;}return true;};
            // Source/target/clipboard must be valid before waiting for modifiers.
            result.Stage="initial";
            if(!c.SourceCurrent()){result.Block="source/"+c.SourceProblem();return result;}
            if(!c.TargetCurrent()){result.Block="target/"+c.TargetProblem();return result;}
            if(!c.ClipboardCurrent()){result.Block="clipboard/changed";return result;}
            for(int i=0;i<25&&!c.ModifiersReleased();i++)await c.Pause(token);
            token.ThrowIfCancellationRequested();if(!sourceReady("before-selection-probe"))return result;
            string problem=c.SelectionProblem();token.ThrowIfCancellationRequested();if(problem!=null){result.Stage="source-selection-probe";result.Block="selection/"+problem;result.Reason=problem+" · 콘 복사만 했어요.";return result;}
            if(!sourceReady("before-activation"))return result;
            if(!c.Activate()){result.Stage="activation";result.Block="activation/failed";result.Reason="카톡 입력창을 활성화하지 못해 콘 복사만 했어요.";return result;}
            for(int i=0;i<10;i++){await c.Pause(token);if(!c.TargetCurrent()||!c.ClipboardCurrent())break;if(c.TargetFocused())break;}
            token.ThrowIfCancellationRequested();if(!targetReady("after-activation"))return result;
            problem=c.SelectionProblem();token.ThrowIfCancellationRequested();if(problem!=null){result.Stage="target-selection-probe";result.Block="selection/"+problem;result.Reason=problem+" · 콘 복사만 했어요.";return result;}
            if(!targetReady("before-ctrl-v"))return result;
            result.Stage="send-ctrl-v";
            if(!c.Send()){result.Block="input/partial-send";result.Reason="붙여넣기 키 전달을 확인하지 못했어요. 자동 재시도는 하지 않아요.";return result;}
            result.Requested=true;result.Reason="카톡에 붙여넣기를 요청했어요. 첨부 미리보기를 확인하고 직접 전송해주세요.";return result;
        }
    }
    // Only normal Ctrl+V after validating the current composer. No WM_PASTE/OLE insertion.
    public sealed partial class PreviewPaste:IDisposable {
        [StructLayout(LayoutKind.Sequential)]struct Keyboard {public ushort Key,Scan;public uint Flags,Time;public UIntPtr Extra;}
        [StructLayout(LayoutKind.Sequential)]struct Mouse {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
        [StructLayout(LayoutKind.Explicit)]struct InputData {[FieldOffset(0)]public Keyboard Keyboard;[FieldOffset(0)]public Mouse Mouse;}
        [StructLayout(LayoutKind.Sequential)]struct Input {public uint Type;public InputData Data;}
        [StructLayout(LayoutKind.Sequential)]struct GuiInfo {public uint Size,Flags;public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;public Native.Rect CaretRect;}
        [DllImport("user32.dll")]public static extern uint GetClipboardSequenceNumber();
        delegate bool EnumWindow(IntPtr window,IntPtr data);
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow callback,IntPtr data);
        [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr parent,EnumWindow callback,IntPtr data);
        [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]static extern IntPtr GetDlgItem(IntPtr dialog,int id);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string name);
        [DllImport("user32.dll")]static extern int GetDlgCtrlID(IntPtr window);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateWindowEx(uint extended,string cls,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr id,IntPtr instance,IntPtr param);
        [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageTimeoutW",SetLastError=true)]static extern IntPtr SetTextTimeout(IntPtr window,uint message,IntPtr unused,string text,uint flags,uint timeout,out UIntPtr result);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageTimeoutW",SetLastError=true)]static extern IntPtr ReadTextTimeout(IntPtr window,uint message,IntPtr length,StringBuilder text,uint flags,uint timeout,out UIntPtr result);
        public bool FileAttachmentInProgress {get;private set;}bool photoFocusLost;PreviewChatTarget photoTarget;
        delegate void WinEvent(IntPtr hook,uint type,IntPtr window,int objectId,int childId,uint thread,uint time);
        [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint thread,ref GuiInfo info);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder name,int size);
        [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr h,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out UIntPtr result);
        [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
        [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr h,int index);
        [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] inputs,int size);
        readonly uint ownProcess=(uint)Process.GetCurrentProcess().Id;readonly WinEvent events;IntPtr foregroundHook,nameHook,focusHook;PreviewChatTarget current;long version;bool transition,disposed;readonly SemaphoreSlim pasteGate=new SemaphoreSlim(1);
        int environmentVersion,confirmedEnvironment;uint confirmedDpi;string confirmedDisplay;
        void EnvironmentChanged(object sender,Microsoft.Win32.UserPreferenceChangedEventArgs args){Interlocked.Increment(ref environmentVersion);}
        void DisplayChanged(object sender,EventArgs args){Interlocked.Increment(ref environmentVersion);}
        uint confirmedProcess;long confirmedStart;IntPtr confirmedMain;bool confirmedRevoked;
        bool verifyingSettings;IntPtr verificationMain,verificationSource;Action<uint,long> gifVerified;static Task<ConfirmationReading> navigationTask;
        bool persistentGif;Action gifSuspended;uint approvedProcess;long approvedStart;
        bool gifDirectSend;
        public void ConfigureGifDirectSend(bool enabled){gifDirectSend=enabled;}
        static HashSet<IntPtr> OwnedWindows(PreviewChatTarget target){var result=new HashSet<IntPtr>();EnumWindow callback=(window,data)=>{if(IsWindowVisible(window)&&OwnedByRoom(window,target))result.Add(window);return true;};EnumWindows(callback,IntPtr.Zero);return result;}
        internal static bool DirectSendCaption(string name){return String.Equals((name??"").Trim(),"1개 전송",StringComparison.Ordinal);}
        internal static bool GifOpenAuthorized(bool directRequested,bool directEnabled,bool previewEnabled,int state,bool session){return directRequested?directEnabled:previewEnabled&&PhotoConfirmationAllowed(state,session);}
        internal sealed class DirectChecks {
            internal Func<bool> Current,PickerClosed,SawConfirmation;
            internal Func<Tuple<bool,Func<bool>>> FindSend;
            internal Func<CancellationToken,Task> Pause;
        }
        internal static async Task<PreviewPasteResult> FinishDirect(DirectChecks checks,CancellationToken token){
            var stopped=new PreviewPasteResult{Stage="gif-direct-after-open",Reason="파일 열기 후 대상이 바뀌어 추가 동작을 멈췄어요. 전송 여부는 카톡에서 확인해주세요."};
            for(int i=0;i<50;i++){
                token.ThrowIfCancellationRequested();if(!checks.Current()){stopped.Block="direct/context-changed";return stopped;}
                var candidate=checks.FindSend();if(candidate.Item1){stopped.Block="direct/ambiguous-confirmation";return stopped;}
                if(candidate.Item2!=null){token.ThrowIfCancellationRequested();if(!checks.Current()){stopped.Block="direct/context-changed-before-send";return stopped;}bool invoked;try{invoked=candidate.Item2();}catch{invoked=false;}return new PreviewPasteResult{Requested=invoked,Stage="gif-direct-send-requested",Block=invoked?null:"direct/send-unconfirmed",Reason=invoked?"움짤 전송 버튼을 요청했어요. 카톡에서 결과를 확인해주세요.":"전송 요청 결과를 확인하지 못했어요. 자동 재시도는 하지 않아요."};}
                await checks.Pause(token);
            }
            token.ThrowIfCancellationRequested();if(!checks.Current()){stopped.Block="direct/context-changed";return stopped;}
            if(checks.SawConfirmation()){stopped.Block="direct/send-control-unavailable";stopped.Reason="파일 전송 확인창의 버튼을 확실히 찾지 못했어요. 확인창에서 직접 전송해주세요.";return stopped;}
            bool closed=checks.PickerClosed();return new PreviewPasteResult{Requested=closed,Stage="gif-direct-open-requested",Block=closed?null:"direct/picker-still-open",Reason=closed?"GIF 파일 열기를 요청했어요. 확인창이 나타나지 않아 전송 여부는 카톡에서 확인해주세요.":"파일 선택창의 열기 처리를 확인하지 못했어요. 자동 재시도는 하지 않아요."};
        }
        bool DirectContext(PreviewChatTarget target,CancellationToken token){if(token.IsCancellationRequested||disposed||!gifDirectSend||photoFocusLost||!SameRoom(target))return false;IntPtr foreground=Native.GetForegroundWindow();return foreground==target.Window||OwnedByRoom(foreground,target);}
        Tuple<bool,Func<bool>> DirectSendControl(PreviewChatTarget target,HashSet<IntPtr> before,CancellationToken token,ref bool saw){
            var dialogs=OwnedWindows(target).Where(w=>!before.Contains(w)&&w!=target.Window&&Native.GetForegroundWindow()==w).ToList();
            var sends=new List<Func<bool>>();
            foreach(var dialog in dialogs){uint process;uint thread=Native.GetWindowThreadProcessId(dialog,out process);if(process!=target.ProcessId||thread!=target.ThreadId||!IsWindowEnabled(dialog))continue;
                var buttons=new List<IntPtr>();bool heading=Native.Title(dialog)=="파일 전송";
                EnumWindow child=(window,data)=>{string text=Native.Title(window);if(text=="파일 전송")heading=true;if(Class(window)=="Button"&&IsWindowVisible(window)&&IsWindowEnabled(window)&&DirectSendCaption(text))buttons.Add(window);return true;};EnumChildWindows(dialog,child,IntPtr.Zero);
                Func<bool> valid=()=>DirectContext(target,token)&&Native.GetForegroundWindow()==dialog&&Native.IsWindow(dialog)&&OwnedByRoom(dialog,target)&&!before.Contains(dialog);
                if(heading&&buttons.Count>0){saw=true;foreach(var button in buttons){IntPtr selected=button;sends.Add(()=>{uint ownerProcess;Native.GetWindowThreadProcessId(selected,out ownerProcess);if(!valid()||ownerProcess!=target.ProcessId||Native.GetAncestor(selected,2)!=dialog||!IsWindowEnabled(selected)||!IsWindowVisible(selected)||!DirectSendCaption(Native.Title(selected)))return false;UIntPtr result;return SendMessageTimeout(selected,0x00F5,IntPtr.Zero,IntPtr.Zero,0x22,250,out result)!=IntPtr.Zero;});}continue;}
                try{var root=AutomationElement.FromHandle(dialog);var labels=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"파일 전송"));if(!heading&&labels.Count==0)continue;saw=true;var found=root.FindAll(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.NameProperty,"1개 전송")));
                    foreach(AutomationElement button in found){if(button.Current.ProcessId!=target.ProcessId||!button.Current.IsEnabled||button.Current.IsOffscreen)continue;object pattern;if(!button.TryGetCurrentPattern(InvokePattern.Pattern,out pattern))continue;var selected=button;var invoke=(InvokePattern)pattern;sends.Add(()=>{if(!valid()||selected.Current.ProcessId!=target.ProcessId||!selected.Current.IsEnabled||selected.Current.IsOffscreen||!DirectSendCaption(selected.Current.Name))return false;invoke.Invoke();return true;});}
                }catch(ElementNotAvailableException){}catch(InvalidOperationException){}
            }
            return Tuple.Create(sends.Count>1,sends.Count==1?sends[0]:null);
        }
        public void ConfigurePersistentGif(bool enabled,uint process,long started,Action suspended,Action<uint,long> verified=null){persistentGif=enabled;approvedProcess=0;approvedStart=0;confirmedProcess=0;confirmedStart=0;confirmedMain=IntPtr.Zero;gifSuspended=suspended;gifVerified=verified;confirmedRevoked=!enabled;}
        void BindPersistentGif(uint expected){if(!persistentGif||confirmedRevoked)return;var processes=Process.GetProcessesByName("KakaoTalk");try{if(processes.Length!=1||(expected!=0&&(uint)processes[0].Id!=expected))return;uint pid=(uint)processes[0].Id;long start=processes[0].StartTime.ToUniversalTime().Ticks;if(!ConfirmedSessionMatches(approvedProcess,approvedStart,pid,start,false))return;if(pid==confirmedProcess&&start==confirmedStart)return;confirmedProcess=pid;confirmedStart=start;confirmedMain=IntPtr.Zero;EnumWindow cb=(w,d)=>{uint id;Native.GetWindowThreadProcessId(w,out id);if(id==pid&&Native.Title(w)=="카카오톡")confirmedMain=w;return true;};EnumWindows(cb,IntPtr.Zero);}catch{confirmedMain=IntPtr.Zero;}finally{foreach(var p in processes)p.Dispose();}}
        void RevokeGif(){confirmedRevoked=true;if(persistentGif){persistentGif=false;try{if(gifSuspended!=null)gifSuspended();}catch{}}}
        public PreviewPaste(bool observe=true){events=OnEvent;if(!observe)return;Microsoft.Win32.SystemEvents.UserPreferenceChanged+=EnvironmentChanged;Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;foregroundHook=SetWinEventHook(3,3,IntPtr.Zero,events,0,0,0);nameHook=SetWinEventHook(0x800C,0x800C,IntPtr.Zero,events,0,0,0);focusHook=SetWinEventHook(0x8005,0x8005,IntPtr.Zero,events,0,0,0);}
        internal static bool ConfirmedSessionMatches(uint expectedProcess,long expectedStart,uint actualProcess,long actualStart,bool revoked){return !revoked&&expectedProcess!=0&&expectedStart>0&&expectedProcess==actualProcess&&expectedStart==actualStart;}
        internal static bool PhotoConfirmationAllowed(int observedState,bool confirmedSession){return observedState==1||(observedState==0&&confirmedSession);}
        internal static void TestDirectGif(){string original=Catalog.DataPath,report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gif-direct-test.txt");int checks=0;Action<bool> check=value=>{if(!value)throw new Exception("Direct GIF check "+checks);checks++;};try{
            Catalog.DataPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-work","direct-"+Guid.NewGuid().ToString("N"),"catalog.json");check(!new Settings().GifDirectSend);var config=new Settings{GifDirectSend=true};Catalog.Save(config);check(Catalog.Load().GifDirectSend);File.WriteAllText(Catalog.DataPath,"{\"Folders\":[],\"Entries\":[],\"Packs\":[]}");check(!Catalog.Load().GifDirectSend);
            using(var preview=new PreviewPaste(false)){preview.ConfigureGifDirectSend(true);check(preview.Status.Contains("바로 전송 켜짐"));preview.ConfigureGifDirectSend(false);check(!preview.Status.Contains("바로 전송 켜짐"));}
            check(DirectSendCaption("1개 전송"));foreach(string wrong in new[]{"전송","2개 전송","열기","1개 전송 취소","","1개 보내기"})check(!DirectSendCaption(wrong));
            foreach(int state in new[]{-1,0,1})foreach(bool session in new[]{false,true}){check(!GifOpenAuthorized(true,false,true,state,session));check(GifOpenAuthorized(true,true,false,state,session));check(GifOpenAuthorized(false,true,true,state,session)==PhotoConfirmationAllowed(state,session));check(!GifOpenAuthorized(false,true,false,state,session));}
            Task.Run(async()=>{
                int sent=0,found=0;Func<DirectChecks> fresh=()=>new DirectChecks{Current=()=>true,PickerClosed=()=>true,SawConfirmation=()=>false,FindSend=()=>Tuple.Create(false,(Func<bool>)null),Pause=token=>Task.Delay(0,token)};
                var c=fresh();c.FindSend=()=>{found++;return Tuple.Create(false,(Func<bool>)(()=>{sent++;return true;}));};var result=await FinishDirect(c,CancellationToken.None);check(result.Requested&&sent==1&&found==1);
                sent=0;c=fresh();c.FindSend=()=>Tuple.Create(true,(Func<bool>)(()=>{sent++;return true;}));result=await FinishDirect(c,CancellationToken.None);check(!result.Requested&&sent==0&&result.Block.Contains("ambiguous"));
                c=fresh();c.Current=()=>false;c.FindSend=()=>{throw new Exception("Must not probe changed context");};result=await FinishDirect(c,CancellationToken.None);check(!result.Requested&&result.Block=="direct/context-changed");
                int current=0;c=fresh();c.Current=()=>++current==1;c.FindSend=()=>Tuple.Create(false,(Func<bool>)(()=>{sent++;return true;}));result=await FinishDirect(c,CancellationToken.None);check(!result.Requested&&sent==0&&result.Block.Contains("before-send"));
                c=fresh();c.FindSend=()=>Tuple.Create(false,(Func<bool>)(()=>{sent++;throw new InvalidOperationException("uncertain result");}));result=await FinishDirect(c,CancellationToken.None);check(!result.Requested&&sent==1&&result.Block=="direct/send-unconfirmed");
                c=fresh();result=await FinishDirect(c,CancellationToken.None);check(result.Requested&&result.Stage=="gif-direct-open-requested");
                c=fresh();c.SawConfirmation=()=>true;result=await FinishDirect(c,CancellationToken.None);check(!result.Requested&&result.Block=="direct/send-control-unavailable");
                c=fresh();c.PickerClosed=()=>false;result=await FinishDirect(c,CancellationToken.None);check(!result.Requested&&result.Block=="direct/picker-still-open");
                using(var cancel=new CancellationTokenSource()){c=fresh();c.Pause=token=>{cancel.Cancel();return Task.Delay(0,token);};bool canceled=false;try{await FinishDirect(c,cancel.Token);}catch(OperationCanceledException){canceled=true;}check(canceled);}
            }).GetAwaiter().GetResult();
            using(var form=new SemanticSettingsForm(false,value=>{})){int changes=0;form.AddThumbnailSettings(false,value=>{},()=>{},()=>{},()=>"test",()=>0,()=>false);form.AddOcrSettings(false,value=>{changes++;});var tabs=form.Controls.OfType<TabControl>().Single();var panel=(TableLayoutPanel)tabs.TabPages[tabs.TabPages.Count-1].Controls[0];var direct=panel.Controls.OfType<CheckBox>().Single();check(!direct.Checked&&changes==0);direct.Checked=true;check(changes==1);direct.Checked=false;check(changes==2);}
            File.WriteAllText(report,"PASS: "+checks+" direct GIF opt-in/default/persistence, preview policy unchanged, mode badge/settings toggle, exact single-file caption, at-most-once fake send, stale room/focus before invocation, duplicate/unsupported controls, uncertain outcome no retry, no-dialog outcome and cancellation. Fake send delegates only; no real Send/Open, clipboard, Kakao, UIA or external input.");
        }catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}finally{Catalog.DataPath=original;}}
        bool ConfirmedGifSession(uint process){try{using(var p=Process.GetProcessById((int)process))return foregroundHook!=IntPtr.Zero&&nameHook!=IntPtr.Zero&&confirmedMain!=IntPtr.Zero&&confirmedEnvironment==environmentVersion&&confirmedDpi==ConfirmationNavigator.Dpi(confirmedMain)&&confirmedDisplay==DpiGeometry.Stamp(confirmedMain)&&ConfirmedSessionMatches(confirmedProcess,confirmedStart,process,p.StartTime.ToUniversalTime().Ticks,confirmedRevoked);}catch{return false;}}
        void ClearGifProof(){approvedProcess=confirmedProcess=0;approvedStart=confirmedStart=0;confirmedMain=IntPtr.Zero;try{if(gifVerified!=null)gifVerified(0,0);}catch{}}
        void ObserveConfirmationSettings(IntPtr window){if(confirmedProcess==0||confirmedRevoked||window==IntPtr.Zero)return;uint pid;Native.GetWindowThreadProcessId(window,out pid);if(pid!=confirmedProcess)return;if(Native.Title(window)=="설정"||Native.Title(window)=="Settings"){ClearGifProof();return;}if(window==confirmedMain)return;for(IntPtr owner=GetWindow(window,4);owner!=IntPtr.Zero;owner=GetWindow(owner,4))if(owner==confirmedMain){ClearGifProof();return;}}
        static Input Key(ushort key,bool up){return new Input{Type=1,Data=new InputData{Keyboard=new Keyboard{Key=key,Flags=up?2u:0u,Extra=new UIntPtr(0x4d495543)}}};}
        static Input[] PasteKeys(){return new[]{Key(0x11,false),Key(0x56,false),Key(0x56,true),Key(0x11,true)};}
        bool Own(IntPtr h){uint pid;Native.GetWindowThreadProcessId(h,out pid);return pid==ownProcess;}
        void OnEvent(IntPtr hook,uint type,IntPtr window,int objectId,int childId,uint thread,uint time){if(type==0x8005){if(!transition&&!FileAttachmentInProgress){IntPtr root=Native.GetAncestor(window,2);if(root==Native.GetForegroundWindow()&&Native.IsKakao(root)){qtObservation.FocusChanged();Observe();}}return;}if(verifyingSettings&&type==3){if(photoTarget==null||!ConfirmationNavigator.ExpectedForeground(window,photoTarget.ProcessId,verificationMain,verificationSource,photoTarget.Window))photoFocusLost=true;return;}if(type==3)ObserveConfirmationSettings(window);if(type==3&&FileAttachmentInProgress&&photoTarget!=null&&!Own(window)&&window!=photoTarget.Window&&!OwnedByRoom(window,photoTarget)&&!NewFileDialogTransition(window,photoTarget))photoFocusLost=true;if(type==3){if(!Own(window)&&(current==null||window!=current.Window))Invalidate();if(!transition&&window==Native.GetForegroundWindow()&&Native.IsKakao(window)){qtObservation.FocusChanged();Observe();}}else if(current!=null&&window==current.Window&&objectId==0&&Native.Title(window)!=current.Title)Invalidate();}
        void Invalidate(){version++;current=null;}
        static string Class(IntPtr h){var b=new StringBuilder(128);GetClassName(h,b,b.Capacity);return b.ToString();}
        static IntPtr Focus(uint thread){var info=new GuiInfo{Size=(uint)Marshal.SizeOf(typeof(GuiInfo))};return GetGUIThreadInfo(thread,ref info)?info.Focus:IntPtr.Zero;}
        static bool Composer(IntPtr root,IntPtr editor){if(editor==IntPtr.Zero||!Native.IsWindow(editor)||!IsWindowVisible(editor)||!IsWindowEnabled(editor)||Native.GetAncestor(editor,2)!=root||!Class(editor).StartsWith("RichEdit",StringComparison.OrdinalIgnoreCase)||(GetWindowLong(editor,-16)&0x800)!=0)return false;var candidate=ChatFloating.Find(root,false);return candidate!=null&&candidate.Editor==editor;}
        // Read selection coordinates only; never obtain draft text or chat history.
        static bool NoSelection(IntPtr editor,out string reason){reason="";IntPtr start=Marshal.AllocHGlobal(4),end=Marshal.AllocHGlobal(4);try{Marshal.WriteInt32(start,-1);Marshal.WriteInt32(end,-1);UIntPtr result;if(SendMessageTimeout(editor,0x00B0,start,end,0x22,200,out result)==IntPtr.Zero){reason="선택 상태를 확인하지 못했어요";return false;}int a=Marshal.ReadInt32(start),b=Marshal.ReadInt32(end);if(a<0||b<0){reason="선택 상태를 제공하지 않는 입력창이에요";return false;}if(a!=b){reason="작성 문장에 선택 영역이 있어요";return false;}return true;}finally{Marshal.FreeHGlobal(start);Marshal.FreeHGlobal(end);}}
        string captureHint="카톡 메시지 입력창을 한 번 눌러주세요";readonly QtObservationBudget qtObservation=new QtObservationBudget();
        public void Observe(){
            if(transition)return;IntPtr root=Native.GetForegroundWindow();if(Own(root))return;
            if(!Native.IsKakao(root)||Native.IsIconic(root)){if(current!=null)Invalidate();captureHint="카톡 채팅방을 한 번 클릭해주세요";return;}
            uint pid;uint thread=Native.GetWindowThreadProcessId(root,out pid);string title=Native.Title(root);
            if(!ChatFloating.ChatTitle(title)){Invalidate();return;}
            IntPtr editor=Focus(thread);AutomationElement accessible=null;string accessibleId=null;
            if(Class(root).StartsWith("Qt",StringComparison.OrdinalIgnoreCase)){
                bool searchTree;if(!qtObservation.Begin(pid+":"+root+":"+title,DateTime.UtcNow,out searchTree))return;
                accessible=QtComposer.Find(root,pid,searchTree);accessibleId=QtComposer.Identity(accessible);editor=root;
                if(accessible==null||accessibleId==null){if(!searchTree&&current!=null&&current.Window==root&&current.Title==title)return;if(current!=null)Invalidate();captureHint="신형 카톡 입력창의 접근성·선택 상태를 확인하지 못했어요 · 직접 붙여넣어주세요";return;}
            }else if(!Composer(root,editor)){
                var found=ChatFloating.Find(root);editor=found==null?IntPtr.Zero:found.Editor;
                if(!Composer(root,editor)){if(current!=null)Invalidate();captureHint="입력창을 확인하지 못했어요 · 채팅방 입력란을 클릭해주세요";return;}
            }
            if(Native.GetForegroundWindow()!=root){Invalidate();return;}
            if(current==null||current.Window!=root||current.Editor!=editor||current.Title!=title||current.AccessibleId!=accessibleId){version++;current=new PreviewChatTarget{Window=root,Editor=editor,Title=title,EditorClass=Class(editor),ProcessId=pid,ThreadId=thread,Version=version,AccessibleEditor=accessible,AccessibleId=accessibleId};}
        }
        public PreviewChatTarget Snapshot(){return current==null?null:new PreviewChatTarget{Window=current.Window,Editor=current.Editor,Title=current.Title,EditorClass=current.EditorClass,ProcessId=current.ProcessId,ThreadId=current.ThreadId,Version=current.Version,AccessibleEditor=current.AccessibleEditor,AccessibleId=current.AccessibleId};}
        public static bool IsFocused(PreviewChatTarget target){if(target==null||Native.GetForegroundWindow()!=target.Window||!Native.IsKakao(target.Window)||Native.IsIconic(target.Window)||Native.Title(target.Window)!=target.Title)return false;uint pid;uint thread=Native.GetWindowThreadProcessId(target.Window,out pid);return pid==target.ProcessId&&thread==target.ThreadId&&(target.AccessibleEditor!=null?QtComposer.Focused(target):Focus(thread)==target.Editor&&Composer(target.Window,target.Editor));}
        public string Status {get{return (gifDirectSend?"움짤 바로 전송 켜짐 · ":"")+(current==null?captureHint:"첨부 대상: "+current.Title);}}
        bool Valid(PreviewChatTarget target){if(target==null||current==null||target.Version!=version||target.Window!=current.Window||target.Editor!=current.Editor||!Native.IsWindow(target.Window)||!IsWindowEnabled(target.Window)||Native.IsIconic(target.Window)||Native.Title(target.Window)!=target.Title||!Native.IsKakao(target.Window))return false;uint pid;uint thread=Native.GetWindowThreadProcessId(target.Window,out pid);return pid==target.ProcessId&&thread==target.ThreadId&&Class(target.Editor)==target.EditorClass&&(target.AccessibleEditor!=null?current.AccessibleId==target.AccessibleId&&QtComposer.Current(target):Composer(target.Window,target.Editor));}
        string TargetProblem(PreviewChatTarget target){
            if(disposed)return "disposed";if(target==null)return "not-captured";if(current==null)return "snapshot-invalidated";if(target.Version!=version)return "version-changed";if(target.Window!=current.Window||target.Editor!=current.Editor)return "composer-changed";
            if(!Native.IsWindow(target.Window))return "window-closed";if(!IsWindowEnabled(target.Window))return "window-disabled";if(Native.IsIconic(target.Window))return "window-minimized";if(Native.Title(target.Window)!=target.Title)return "title-changed";if(!Native.IsKakao(target.Window))return "not-kakao";
            uint pid;uint thread=Native.GetWindowThreadProcessId(target.Window,out pid);if(pid!=target.ProcessId||thread!=target.ThreadId)return "process-or-thread-changed";if(Class(target.Editor)!=target.EditorClass)return "editor-class-changed";if(target.AccessibleEditor!=null?!QtComposer.Current(target):!Composer(target.Window,target.Editor))return "composer-check-failed";return "changed-during-check";
        }
        bool ActivateComposer(PreviewChatTarget target){
            if(!Valid(target))return false;
            if(!SetForegroundWindow(target.Window)&&Native.GetForegroundWindow()!=target.Window)return false;
            if(Native.GetForegroundWindow()!=target.Window||!Valid(target))return false;
            if(target.AccessibleEditor!=null)return QtComposer.Focus(target);
            if(Focus(target.ThreadId)!=target.Editor){
                try{AutomationElement.FromHandle(target.Editor).SetFocus();}catch{return false;}
            }
            return Native.GetForegroundWindow()==target.Window&&Valid(target);
        }
        public async Task<PreviewPasteResult> TryPaste(PreviewChatTarget target,CancellationToken cancel,uint clipboardSequence,IntPtr sourceWindow){
            var checks=new PasteChecks{
                SourceCurrent=()=>!disposed&&foregroundHook!=IntPtr.Zero&&nameHook!=IntPtr.Zero&&Native.GetForegroundWindow()==sourceWindow&&Own(sourceWindow),
                SourceProblem=()=>disposed?"disposed":foregroundHook==IntPtr.Zero||nameHook==IntPtr.Zero?"observation-hooks-missing":"palette-not-foreground",
                TargetProblem=()=>TargetProblem(target),TargetCurrent=()=>!disposed&&Valid(target),ClipboardCurrent=()=>GetClipboardSequenceNumber()==clipboardSequence,
                ModifiersReleased=()=>Control.ModifierKeys==Keys.None,
                TargetFocused=()=>IsFocused(target),
                SelectionProblem=()=>{string reason;return (target.AccessibleEditor!=null?QtComposer.NoSelection(target,out reason):NoSelection(target.Editor,out reason))?null:reason;},
                Activate=()=>{transition=true;return ActivateComposer(target);},
                Pause=token=>Task.Delay(20,token),End=()=>transition=false,
                Send=()=>{var keys=PasteKeys();uint sent=SendInput((uint)keys.Length,keys,Marshal.SizeOf(typeof(Input)));if(sent==keys.Length)return true;var release=new[]{Key(0x11,true)};SendInput(1,release,Marshal.SizeOf(typeof(Input)));Invalidate();return false;}
            };
            var result=await PasteFlow.Run(pasteGate,checks,cancel);if(result.Requested)Invalidate();else if(target==null)result.Reason=captureHint+" · 콘은 복사되어 있어요.";return result;
        }
        static bool SameRoom(PreviewChatTarget target){if(target==null||!Native.IsWindow(target.Window)||Native.IsIconic(target.Window)||Native.Title(target.Window)!=target.Title||!Native.IsKakao(target.Window))return false;uint pid;uint thread=Native.GetWindowThreadProcessId(target.Window,out pid);return pid==target.ProcessId&&thread==target.ThreadId&&Native.IsWindow(target.Editor)&&Native.GetAncestor(target.Editor,2)==target.Window;}
        static bool OwnedByRoom(IntPtr window,PreviewChatTarget target){if(target==null)return false;uint pid;Native.GetWindowThreadProcessId(window,out pid);if(pid!=target.ProcessId)return false;for(IntPtr owner=GetWindow(window,4);owner!=IntPtr.Zero;owner=GetWindow(owner,4))if(owner==target.Window)return true;return false;}
        List<IntPtr> PhotoDialogs(PreviewChatTarget target){var result=new List<IntPtr>();if(target==null)return result;EnumWindow callback=(window,data)=>{uint pid;Native.GetWindowThreadProcessId(window,out pid);if(pid!=target.ProcessId||!IsWindowVisible(window)||Class(window)!="#32770")return true;IntPtr owner=GetWindow(window,4);for(int i=0;i<8&&owner!=IntPtr.Zero;i++,owner=GetWindow(owner,4))if(owner==target.Window){result.Add(window);break;}if(!result.Contains(window)&&QtPickerOwner(window,target))result.Add(window);return true;};EnumWindows(callback,IntPtr.Zero);return result;}
        readonly HashSet<IntPtr> priorFileDialogs=new HashSet<IntPtr>();
        internal static bool DialogTransitionAllowed(bool sameProcess,bool sameThread,bool commonDialog,bool existed){return sameProcess&&sameThread&&commonDialog&&!existed;}
        bool NewFileDialogTransition(IntPtr window,PreviewChatTarget target){if(target==null)return false;uint pid;uint thread=Native.GetWindowThreadProcessId(window,out pid);return DialogTransitionAllowed(pid==target.ProcessId,thread==target.ThreadId,Class(window)=="#32770",priorFileDialogs.Contains(window))||QtPickerShim(window,target)||(pid==target.ProcessId&&Class(window)=="#32770"&&QtPickerOwner(window,target));}
        void RememberFileDialogs(PreviewChatTarget target){priorFileDialogs.Clear();EnumWindow callback=(window,data)=>{uint pid;Native.GetWindowThreadProcessId(window,out pid);if(pid==target.ProcessId&&Class(window)=="#32770")priorFileDialogs.Add(window);return true;};EnumWindows(callback,IntPtr.Zero);}
        static Input[] FileDialogKeys(){return new[]{Key(0x11,false),Key(0x54,false),Key(0x54,true),Key(0x11,true)};}
        internal static bool FileOpenButton(string id,string name){return id=="1"&&new[]{"열기","열기(O)","열기(&O)","Open","&Open"}.Contains(name??"");}
        internal static bool FilenameField(string id,ControlType type,bool enabled,bool password){return id=="1148"&&type==ControlType.Edit&&enabled&&!password;}
        internal static bool StandardFilename(string cls,int id,bool enabled,bool password){return enabled&&!password&&id==1148&&(cls=="Edit"||cls=="ComboBox"||cls=="ComboBoxEx32");}
        static IntPtr FilenameEdit(IntPtr dialog){
            if(Class(dialog)!="#32770")return IntPtr.Zero;
            IntPtr field=GetDlgItem(dialog,1148);if(field==IntPtr.Zero||!StandardFilename(Class(field),GetDlgCtrlID(field),IsWindowEnabled(field),(GetWindowLong(field,-16)&0x20)!=0))return IntPtr.Zero;
            if(Class(field)=="ComboBoxEx32")field=FindWindowEx(field,IntPtr.Zero,"ComboBox",null);
            if(Class(field)=="ComboBox")field=FindWindowEx(field,IntPtr.Zero,"Edit",null);
            if(field==IntPtr.Zero||Class(field)!="Edit"||!IsWindowEnabled(field)||(GetWindowLong(field,-16)&0x20)!=0)return IntPtr.Zero;
            for(IntPtr parent=Native.GetAncestor(field,1);parent!=IntPtr.Zero;parent=Native.GetAncestor(parent,1))if(parent==dialog)return field;
            return IntPtr.Zero;
        }
        static bool ReadFilename(IntPtr field,out string value){var text=new StringBuilder(32768);UIntPtr result;value=null;if(ReadTextTimeout(field,0x000D,new IntPtr(text.Capacity),text,0x22,200,out result)==IntPtr.Zero)return false;value=text.ToString();return true;}
        static bool SetFilename(IntPtr dialog,string path){IntPtr field=FilenameEdit(dialog);string before,after;if(field==IntPtr.Zero||!ReadFilename(field,out before)||!String.IsNullOrWhiteSpace(before))return false;UIntPtr result;if(SetTextTimeout(field,0x000C,IntPtr.Zero,path,0x22,200,out result)==IntPtr.Zero||result==UIntPtr.Zero)return false;return ReadFilename(field,out after)&&String.Equals(after,path,StringComparison.Ordinal);}
        static bool NativeOpen(IntPtr dialog){if(Class(dialog)!="#32770")return false;IntPtr button=GetDlgItem(dialog,1);if(button==IntPtr.Zero||Class(button)!="Button"||!IsWindowEnabled(button)||!FileOpenButton("1",Native.Title(button)))return false;UIntPtr result;return SendMessageTimeout(button,0x00F5,IntPtr.Zero,IntPtr.Zero,0x22,300,out result)!=IntPtr.Zero;}
        // 1=observed on, -1=observed off/indeterminate, 0=unavailable.
        static int ReadConfirmation(uint process){
            int state=0;EnumWindow callback=(window,data)=>{uint pid;Native.GetWindowThreadProcessId(window,out pid);if(pid!=process||!IsWindowVisible(window)||(!Class(window).StartsWith("Qt",StringComparison.OrdinalIgnoreCase)&&!(Native.Title(window)=="설정"||Native.Title(window)=="Settings")))return true;
                EnumWindow child=(control,unused)=>{if(Class(control)!="Button"||Native.Title(control)!="파일 전송 시 확인하고 보내기"||!IsWindowVisible(control)||!IsWindowEnabled(control))return true;UIntPtr checkedState;if(SendMessageTimeout(control,0x00F0,IntPtr.Zero,IntPtr.Zero,0x22,150,out checkedState)!=IntPtr.Zero)state=checkedState.ToUInt64()==1?1:-1;return state==0;};EnumChildWindows(window,child,IntPtr.Zero);if(state!=0)return false;
                try{var root=AutomationElement.FromHandle(window);var boxes=root.FindAll(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.CheckBox),new PropertyCondition(AutomationElement.NameProperty,"파일 전송 시 확인하고 보내기")));if(boxes.Count!=1)return true;object pattern;if(boxes[0].Current.IsEnabled&&boxes[0].TryGetCurrentPattern(TogglePattern.Pattern,out pattern))state=((TogglePattern)pattern).Current.ToggleState==ToggleState.On?1:-1;}catch(ElementNotAvailableException){}catch(InvalidOperationException){}return state==0;};EnumWindows(callback,IntPtr.Zero);return state;
        }
        static Task<int> confirmationTask;static uint confirmationProcess;
        static async Task<int> ConfirmationState(uint process,CancellationToken cancel){
            if(confirmationTask==null||confirmationTask.IsCompleted){confirmationProcess=process;confirmationTask=Task.Run(()=>{try{return ReadConfirmation(process);}catch{return 0;}});}
            if(confirmationProcess!=process)return 0;
            var task=confirmationTask;await Task.WhenAny(task,Task.Delay(350,cancel));cancel.ThrowIfCancellationRequested();return task.IsCompleted&&task.Status==TaskStatus.RanToCompletion?task.Result:0;
        }
        internal static bool MayOpenPhoto(bool confirmationProven,bool sameRoom,bool ownsDialog,bool focused,bool canceled){return confirmationProven&&sameRoom&&ownsDialog&&focused&&!canceled;}
        internal static void TestGifPersistence(){string old=Catalog.DataPath,report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gif-open-settings-test.txt");try{int checks=0;Action<bool> check=pass=>{if(!pass)throw new Exception("GIF preference check "+checks);checks++;};Catalog.DataPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-work","gif-preference-"+Guid.NewGuid().ToString("N"),"catalog.json");
            check(!Catalog.Load().GifAutoOpen);var settings=new Settings{GifAutoOpen=true,GifConfirmedProcess=123,GifConfirmedStart=456};Catalog.Save(settings);check(Catalog.Load().GifAutoOpen);settings=Catalog.Load();Catalog.Save(settings);check(Catalog.Load().GifAutoOpen);check(settings.GifConfirmedProcess==123&&settings.GifConfirmedStart==456);check(ConfirmedSessionMatches(settings.GifConfirmedProcess,settings.GifConfirmedStart,123,456,false));check(!ConfirmedSessionMatches(settings.GifConfirmedProcess,settings.GifConfirmedStart,123,457,false));
            using(var monitor=new PreviewPaste(false)){monitor.persistentGif=true;monitor.gifSuspended=()=>{settings.GifAutoOpen=false;Catalog.Save(settings);};monitor.RevokeGif();check(monitor.confirmedRevoked&&!monitor.persistentGif&&!Catalog.Load().GifAutoOpen);}
            settings=Catalog.Load();settings.GifAutoOpen=true;Catalog.Save(settings);check(Catalog.Load().GifAutoOpen);settings.GifAutoOpen=false;Catalog.Save(settings);check(!Catalog.Load().GifAutoOpen);
            Directory.CreateDirectory(Path.GetDirectoryName(Catalog.DataPath));File.WriteAllText(Catalog.DataPath,"{\"Folders\":[],\"Entries\":[],\"Packs\":[]}");check(!Catalog.Load().GifAutoOpen);
            check(ConfirmedSessionMatches(10,100,10,100,false));check(!ConfirmedSessionMatches(10,100,11,101,false));check(PhotoConfirmationAllowed(0,true)&&!PhotoConfirmationAllowed(-1,true));
            using(var monitor=new PreviewPaste(false)){monitor.ConfigurePersistentGif(true,123,456,null);check(monitor.persistentGif&&monitor.approvedProcess==0&&monitor.confirmedProcess==0);int notices=0;monitor.gifVerified=(pid,start)=>{check(pid==0&&start==0);notices++;};monitor.confirmedProcess=123;monitor.ClearGifProof();check(monitor.persistentGif&&monitor.confirmedProcess==0&&notices==1);int before=monitor.environmentVersion;monitor.DisplayChanged(null,EventArgs.Empty);check(monitor.environmentVersion==before+1);monitor.EnvironmentChanged(null,null);check(monitor.environmentVersion==before+2);}
            File.WriteAllText(report,"PASS: "+checks+" persisted opt-in/reload/revocation/re-enable/legacy defaults and current-process guard checks. Isolated catalog and unobserved monitor; no clipboard, input or KakaoTalk windows.\r\n");
        }catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}finally{Catalog.DataPath=old;}}
        internal static void TestPhotoNative(){string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"photo-native-test.txt");IntPtr dialog=IntPtr.Zero;try{
            int checks=0;Action<bool> check=pass=>{if(!pass)throw new Exception("Native photo check "+checks);checks++;};
            dialog=CreateWindowEx(0,"#32770","AgitCon hidden fixture",0,0,0,400,200,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);check(dialog!=IntPtr.Zero);
            IntPtr editor=CreateWindowEx(0,"Edit","",0x40000000,0,0,300,25,dialog,new IntPtr(1148),IntPtr.Zero,IntPtr.Zero);check(editor!=IntPtr.Zero);
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-work","한글 GIF sample.gif"),text;
            check(FilenameEdit(dialog)==editor);check(SetFilename(dialog,path));check(ReadFilename(editor,out text)&&text==path);check(!SetFilename(dialog,"must-not-overwrite.gif"));check(ReadFilename(editor,out text)&&text==path);
            IntPtr send=CreateWindowEx(0,"Button","전송",0x40000000,0,40,100,25,dialog,new IntPtr(1),IntPtr.Zero,IntPtr.Zero);check(send!=IntPtr.Zero);check(!NativeOpen(dialog));
            DestroyWindow(editor);editor=CreateWindowEx(0,"Edit","draft",0x40000000,0,0,300,25,dialog,new IntPtr(1006),IntPtr.Zero,IntPtr.Zero);check(FilenameEdit(dialog)==IntPtr.Zero);check(!SetFilename(dialog,path));check(ReadFilename(editor,out text)&&text=="draft");
            check(!StandardFilename("RichEdit50W",1148,true,false));check(!StandardFilename("Edit",1006,true,false));check(!StandardFilename("Edit",1148,false,false));check(!StandardFilename("Edit",1148,true,true));
            for(int mask=0;mask<32;mask++)check(MayOpenPhoto((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0,(mask&16)!=0)==(mask==15));
            for(int mask=0;mask<16;mask++)check(DialogTransitionAllowed((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0)==(mask==7));
            check(ConfirmedSessionMatches(123,456,123,456,false));check(!ConfirmedSessionMatches(123,456,124,456,false));check(!ConfirmedSessionMatches(123,456,123,457,false));check(!ConfirmedSessionMatches(123,456,123,456,true));check(!ConfirmedSessionMatches(0,456,0,456,false));check(!ConfirmedSessionMatches(123,0,123,0,false));
            foreach(int state in new[]{-1,0,1})foreach(bool session in new[]{false,true})check(PhotoConfirmationAllowed(state,session)==(state==1||(state==0&&session)));
            check(!FileOpenButton("1","열기 후 전송"));check(!FileOpenButton("1","1개 전송"));check(FileOpenButton("1","열기(&O)"));
            File.WriteAllText(report,"PASS: "+checks+" hidden native common-dialog filename/Unicode/non-overwrite/wrong-editor/Send-rejection and exhaustive safety-gate checks. Own HWNDs only; no clipboard, global input or KakaoTalk interaction.\r\n");
        }catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}finally{if(dialog!=IntPtr.Zero)DestroyWindow(dialog);}}
        internal static string NineFileNames(IList<string> paths){return AttachmentFileNames(paths,9);}
        internal static string AttachmentFileNames(IList<string> paths,int expected){
            if((expected!=2&&expected!=9)||paths.Count!=expected||paths.Any(p=>!File.Exists(p)||!new[]{".png",".jpg",".jpeg",".gif",".webp"}.Contains(Path.GetExtension(p).ToLowerInvariant())))throw new InvalidDataException("첨부할 이미지 파일 목록이 맞지 않아요.");var full=paths.Select(Path.GetFullPath).ToArray();
            if(full.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=expected||full.Any(p=>p.IndexOfAny(new[]{'"','\r','\n','\0'})>=0)||full.Any(p=>!String.Equals(Path.GetDirectoryName(p),Path.GetDirectoryName(full[0]),StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("분할 파일 경로가 올바르지 않아요.");
            string names=String.Join(" ",full.Select(p=>"\""+p+"\""));if(names.Length>30000)throw new InvalidDataException("분할 파일 경로가 너무 길어요.");return names;
        }
        public async Task<PreviewPasteResult> TryFileSelection(PreviewChatTarget target,CancellationToken cancel,IList<string> paths,IntPtr sourceWindow,int expected){
            string names=AttachmentFileNames(paths,expected);await pasteGate.WaitAsync(cancel);try{
                var result=new PreviewPasteResult{Stage="nine-file-prepare",Reason="첨부 대상을 확인하지 못했어요. 카톡 입력창을 누른 뒤 다시 시도해주세요."};
                if(!Valid(target)){result.Block="target/not-current";return result;}
                if(target.AccessibleEditor!=null)return await TryQtFileSelection(target,cancel,paths,sourceWindow,expected);
                if(PhotoDialogs(target).Count!=0){result.Block="dialog/already-open";result.Reason="기존 카톡 파일 선택창을 닫고 다시 시도해주세요.";return result;}
                RememberFileDialogs(target);photoFocusLost=false;photoTarget=target;FileAttachmentInProgress=true;
                var checks=new PasteChecks{SourceCurrent=()=>!disposed&&foregroundHook!=IntPtr.Zero&&nameHook!=IntPtr.Zero&&Native.GetForegroundWindow()==sourceWindow&&Own(sourceWindow),TargetCurrent=()=>Valid(target),ClipboardCurrent=()=>true,ModifiersReleased=()=>Control.ModifierKeys==Keys.None,TargetFocused=()=>IsFocused(target),SelectionProblem=()=>{string reason;return NoSelection(target.Editor,out reason)?null:reason;},Activate=()=>{transition=true;return ActivateComposer(target);},Pause=token=>Task.Delay(20,token),Send=()=>{var keys=FileDialogKeys();if(SendInput((uint)keys.Length,keys,Marshal.SizeOf(typeof(Input)))==keys.Length)return true;var release=new[]{Key(0x11,true)};SendInput(1,release,Marshal.SizeOf(typeof(Input)));return false;}};
                var opened=await PasteFlow.Execute(checks,cancel);if(!opened.Requested){opened.Reason="카톡 첨부창을 열지 못했어요. 입력창을 클릭한 뒤 다시 시도해주세요.";return opened;}
                IntPtr dialog=IntPtr.Zero;result.Stage="nine-file-dialog-wait";
                for(int i=0;i<50;i++){
                    await Task.Delay(40,cancel);if(photoFocusLost||!SameRoom(target)){result.Block="target/context-changed";return result;}
                    var dialogs=PhotoDialogs(target);if(dialogs.Count>1){result.Block="dialog/ambiguous";return result;}if(dialogs.Count==1){dialog=dialogs[0];break;}
                    IntPtr foreground=Native.GetForegroundWindow();if(foreground!=target.Window&&!NewFileDialogTransition(foreground,target)){result.Block="focus/foreign-window";return result;}
                }
                cancel.ThrowIfCancellationRequested();
                if(photoFocusLost||dialog==IntPtr.Zero||Native.GetForegroundWindow()!=dialog||!SameRoom(target)||!PhotoDialogs(target).Contains(dialog)){result.Block="dialog/context-changed";return result;}
                if(!SetFilename(dialog,names)){result.Stage="nine-file-name";result.Block="filename/nonempty-or-unavailable";result.Reason="카톡 파일 선택창을 열었지만 파일을 지정하지 못했어요. 원래 입력한 내용은 덮어쓰지 않았어요.";return result;}
                // This multi-photo route never calls NativeOpen, FinishDirect or Send.
                // User confirms Open and the actual photo grouping in Kakao.
                return new PreviewPasteResult{Requested=true,Stage="nine-files-selected-manual-open",Reason="이미지 "+expected+"개를 지정했어요. ‘열기’를 직접 누르고 묶음과 순서를 확인해주세요. 전송도 직접 눌러주세요."};
            }finally{qtPickerTransition=false;FileAttachmentInProgress=false;photoTarget=null;priorFileDialogs.Clear();transition=false;pasteGate.Release();}
        }
        public async Task<PreviewPasteResult> TryPhotoAttach(PreviewChatTarget target,CancellationToken cancel,string gif,IntPtr sourceWindow,bool verificationOnly=false){
            await pasteGate.WaitAsync(cancel);try{
                bool direct=gifDirectSend&&!verificationOnly;
                cancel.ThrowIfCancellationRequested();var blocked=new PreviewPasteResult{Stage="photo-prepare",Reason="첨부 대상이 불확실해 GIF 파일만 복사했어요. 카톡의 파일 첨부에서 직접 골라주세요."};
                if(target==null){blocked.Block="target/not-captured";return blocked;}
                if(target.AccessibleEditor!=null){var qt=await TryQtPhotoAttach(target,cancel,gif,sourceWindow,direct,verificationOnly);if(qt.Requested)Invalidate();return qt;}
                BindPersistentGif(target.ProcessId);
                if(!GifAttachment.AnimatedGif(gif)){blocked.Block="asset/not-animated-gif";return blocked;}
                if(PhotoDialogs(target).Count!=0){blocked.Block="dialog/already-open";return blocked;}
                RememberFileDialogs(target);
                var previousOwned=direct?OwnedWindows(target):null;
                photoFocusLost=false;photoTarget=target;FileAttachmentInProgress=true;
                if(persistentGif&&!direct&&!ConfirmedGifSession(target.ProcessId)){
                    blocked.Stage="photo-confirmation-verify";
                    if(navigationTask!=null&&!navigationTask.IsCompleted){blocked.Block="settings/previous-worker-busy";return blocked;}
                    if(!Valid(target)||Native.GetForegroundWindow()!=sourceWindow||!Own(sourceWindow)){blocked.Block="settings/source-or-room-changed";return blocked;}
                    for(int release=0;release<40&&Control.ModifierKeys!=Keys.None;release++)await Task.Delay(20,cancel);if(Control.ModifierKeys!=Keys.None){blocked.Block="settings/modifiers-held";return blocked;}
                    verificationMain=ConfirmationNavigator.FindMain(target.ProcessId);verificationSource=sourceWindow;verifyingSettings=true;transition=true;
                    using(var verificationCancel=CancellationTokenSource.CreateLinkedTokenSource(cancel)){
                        verificationCancel.CancelAfter(10000);var token=verificationCancel.Token;
                        Func<bool> safe=()=>!token.IsCancellationRequested&&!disposed&&!photoFocusLost&&SameRoom(target)&&ConfirmationNavigator.ExpectedForeground(Native.GetForegroundWindow(),target.ProcessId,verificationMain,sourceWindow,target.Window);
                        string displayBefore=DpiGeometry.Stamp(verificationMain);int environmentBefore=environmentVersion;uint dpiBefore=ConfirmationNavigator.Dpi(verificationMain);long start;try{using(var process=Process.GetProcessById((int)target.ProcessId))start=process.StartTime.ToUniversalTime().Ticks;}catch{blocked.Block="settings/process-unavailable";return blocked;}
                        navigationTask=Task.Run(()=>ConfirmationNavigator.Verify(target.ProcessId,verificationMain,sourceWindow,safe,token));
                        await Task.WhenAny(navigationTask,Task.Delay(10500));
                        ConfirmationReading reading=navigationTask.IsCompleted&&navigationTask.Status==TaskStatus.RanToCompletion?navigationTask.Result:ConfirmationReading.Unknown("timeout");
                        verificationCancel.Cancel();verifyingSettings=false;transition=false;
                        if(reading.State!=1||displayBefore!=DpiGeometry.Stamp(verificationMain)||environmentBefore!=environmentVersion||dpiBefore!=ConfirmationNavigator.Dpi(verificationMain)||photoFocusLost||cancel.IsCancellationRequested||!Valid(target)||Native.GetForegroundWindow()!=sourceWindow){ClearGifProof();blocked.Block="settings/"+reading.Stage+"/foreign="+photoFocusLost+"/valid="+Valid(target)+"/source="+(Native.GetForegroundWindow()==sourceWindow)+"/display="+(displayBefore==DpiGeometry.Stamp(verificationMain))+"/environment="+(environmentBefore==environmentVersion)+"/dpi="+(dpiBefore==ConfirmationNavigator.Dpi(verificationMain));blocked.Reason=reading.State<0?"카톡의 ‘파일 전송 시 확인하고 보내기’가 꺼져 있어 첨부를 멈췄어요. 설정을 켠 뒤 다시 골라주세요.":reading.Stage!=null&&reading.Stage.StartsWith("gear-unrecognized",StringComparison.Ordinal)?"카톡 메인창의 설정 아이콘을 찾지 못했어요. 메인창을 열어둔 뒤 다시 골라주세요. GIF 파일은 복사되어 있어요.":"카톡의 전송 전 확인 설정을 확실히 읽지 못해 첨부를 멈췄어요. GIF 파일은 복사되어 있어요.";return blocked;}
                        using(var process=Process.GetProcessById((int)target.ProcessId))if(process.StartTime.ToUniversalTime().Ticks!=start){blocked.Block="settings/process-restarted";return blocked;}
                        confirmedDisplay=DpiGeometry.Stamp(verificationMain);confirmedEnvironment=environmentVersion;confirmedDpi=ConfirmationNavigator.Dpi(verificationMain);approvedProcess=confirmedProcess=target.ProcessId;approvedStart=confirmedStart=start;confirmedMain=verificationMain;confirmedRevoked=false;if(gifVerified!=null)gifVerified(confirmedProcess,start);
                    }
                }
                if(verificationOnly)return new PreviewPasteResult{Stage="photo-confirmation-only",Reason=ConfirmedGifSession(target.ProcessId)?"verified":"not-verified",Block=ConfirmedGifSession(target.ProcessId)?null:"settings/no-proof"};
                var checks=new PasteChecks{SourceCurrent=()=>!disposed&&foregroundHook!=IntPtr.Zero&&nameHook!=IntPtr.Zero&&Native.GetForegroundWindow()==sourceWindow&&Own(sourceWindow),TargetCurrent=()=>Valid(target),ClipboardCurrent=()=>true,ModifiersReleased=()=>Control.ModifierKeys==Keys.None,TargetFocused=()=>IsFocused(target),SelectionProblem=()=>{string reason;return NoSelection(target.Editor,out reason)?null:reason;},Activate=()=>{transition=true;return ActivateComposer(target);},Pause=token=>Task.Delay(20,token),Send=()=>{var keys=FileDialogKeys();if(SendInput((uint)keys.Length,keys,Marshal.SizeOf(typeof(Input)))==keys.Length)return true;var release=new[]{Key(0x11,true)};SendInput(1,release,Marshal.SizeOf(typeof(Input)));return false;}};
                var opened=await PasteFlow.Execute(checks,cancel);if(!opened.Requested)return opened;
                IntPtr dialog=IntPtr.Zero;
                blocked.Stage="photo-dialog-wait";
                for(int i=0;i<50;i++){await Task.Delay(40,cancel);if(photoFocusLost||!SameRoom(target)){blocked.Block=photoFocusLost?"focus/foreign-window":"target/room-changed";return blocked;}var dialogs=PhotoDialogs(target);if(dialogs.Count>1){blocked.Block="dialog/ambiguous";return blocked;}if(dialogs.Count==1){dialog=dialogs[0];break;}IntPtr foreground=Native.GetForegroundWindow();if(foreground!=target.Window&&!NewFileDialogTransition(foreground,target)){blocked.Block="focus/not-room-or-new-dialog";return blocked;}}
                if(photoFocusLost||dialog==IntPtr.Zero||Native.GetForegroundWindow()!=dialog||!SameRoom(target)){blocked.Block=photoFocusLost?"focus/foreign-window":dialog==IntPtr.Zero?"dialog/not-found-or-owner-mismatch":"focus/dialog-not-foreground";return blocked;}
                // Standard owned #32770 filename Edit only. Never address the chat composer.
                cancel.ThrowIfCancellationRequested();if(photoFocusLost||Native.GetForegroundWindow()!=dialog||!SameRoom(target)||!PhotoDialogs(target).Contains(dialog)){blocked.Stage="photo-before-filename";blocked.Block="dialog/context-changed";return blocked;}
                string fullPath=Path.GetFullPath(gif);if(!SetFilename(dialog,fullPath)){blocked.Stage="photo-filename";blocked.Block=FilenameEdit(dialog)==IntPtr.Zero?"filename/native-control-not-found":"filename/nonempty-or-write-failed";blocked.Reason="파일 선택창은 열었지만 GIF 경로를 지정하지 못했어요. 파일 이름 칸에서 직접 골라주세요.";return blocked;}cancel.ThrowIfCancellationRequested();
                // Persistent user-authorized Open is rebound to the current Kakao process.
                // A readable OFF value wins; settings activation revokes session fallback.
                int confirmationState=direct?0:await ConfirmationState(target.ProcessId,cancel);
                if(confirmationState<0)ClearGifProof();
                bool confirmation=GifOpenAuthorized(direct,gifDirectSend,persistentGif,confirmationState,ConfirmedGifSession(target.ProcessId));
                string selectedPath;IntPtr filename=FilenameEdit(dialog);
                if(!photoFocusLost&&MayOpenPhoto(confirmation,SameRoom(target),PhotoDialogs(target).Contains(dialog),Native.GetForegroundWindow()==dialog,cancel.IsCancellationRequested)&&filename!=IntPtr.Zero&&ReadFilename(filename,out selectedPath)&&String.Equals(selectedPath,fullPath,StringComparison.Ordinal)){
                    if(NativeOpen(dialog)){
                        if(direct){bool saw=false;var directResult=await FinishDirect(new DirectChecks{Current=()=>DirectContext(target,cancel),PickerClosed=()=>!Native.IsWindow(dialog)||!IsWindowVisible(dialog),SawConfirmation=()=>saw,FindSend=()=>DirectSendControl(target,previousOwned,cancel,ref saw),Pause=token=>Task.Delay(40,token)},cancel);Invalidate();return directResult;}
                        Invalidate();return new PreviewPasteResult{Requested=true,Stage="photo-open-requested",Reason="GIF 파일 첨부를 요청했어요. 카톡의 파일 전송 확인창에서 직접 전송해주세요."};}
                    blocked.Stage="photo-open-unconfirmed";blocked.Reason="파일 열기 요청 결과를 확인하지 못했어요. 자동 재시도는 하지 않아요.";return blocked;
                }
                return new PreviewPasteResult{Requested=true,Stage="photo-file-selected-manual-open",Reason="GIF 파일을 지정했어요. 전송 전 확인 설정을 자동으로 읽지 못해 ‘열기’는 직접 눌러주세요. 전송 버튼은 누르지 않아요."};
            }finally{qtPickerTransition=false;verifyingSettings=false;FileAttachmentInProgress=false;photoTarget=null;priorFileDialogs.Clear();transition=false;pasteGate.Release();}
        }
        internal static void TestPhotoPolicy(){string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"photo-attachment-test.txt");try{var keys=FileDialogKeys();if(keys.Length!=4||keys.Any(k=>k.Data.Keyboard.Key==0x0d)||keys[0].Data.Keyboard.Key!=0x11||keys[1].Data.Keyboard.Key!=0x54)throw new Exception("Only Ctrl+T may open the picker");if(!FilenameField("1148",ControlType.Edit,true,false)||FilenameField("1006",ControlType.Edit,true,false)||FilenameField("1148",ControlType.Document,true,false)||FilenameField("1148",ControlType.Edit,false,false)||FilenameField("1148",ControlType.Edit,true,true))throw new Exception("File-name-only guard");if(!FileOpenButton("1","열기(O)")||!FileOpenButton("1","Open")||FileOpenButton("1","전송")||FileOpenButton("1","1개 전송")||FileOpenButton("2","열기"))throw new Exception("Never invoke Send as Open");if(!MayOpenPhoto(true,true,true,true,false)||MayOpenPhoto(false,true,true,true,false)||MayOpenPhoto(true,false,true,true,false)||MayOpenPhoto(true,true,false,true,false)||MayOpenPhoto(true,true,true,false,false)||MayOpenPhoto(true,true,true,true,true))throw new Exception("Open requires positive live confirmation and ownership proof");File.WriteAllText(report,"PASS: Ctrl+T only, no Enter; standard enabled non-password filename edit only; chat composer/document rejected. Open gated by positive live confirmation setting plus current owner/focus; OFF always blocks; unavailable requires explicit same-process session authorization; no Send invocation. Fixtures only; no desktop, clipboard or KakaoTalk interaction.\r\n");}catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}}
        public void Dispose(){Microsoft.Win32.SystemEvents.UserPreferenceChanged-=EnvironmentChanged;Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged;disposed=true;Invalidate();if(foregroundHook!=IntPtr.Zero)UnhookWinEvent(foregroundHook);if(nameHook!=IntPtr.Zero)UnhookWinEvent(nameHook);if(focusHook!=IntPtr.Zero)UnhookWinEvent(focusHook);foregroundHook=nameHook=focusHook=IntPtr.Zero;}
        internal static void TestFlow(){
            Application.EnableVisualStyles();SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());var context=new ApplicationContext();var timer=new System.Windows.Forms.Timer{Interval=1};timer.Tick+=async(s,e)=>{timer.Stop();int count=0;string report=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"attachment-flow-test.txt");Action<bool> check=b=>{if(!b)throw new Exception("Attachment flow check "+count);count++;};
                try{int activate=0,send=0,probe=0,pause=0;bool source=true,target=true,clip=true,focus=true,mods=true,sent=true;string selection=null;Action<int> atProbe=null,atPause=null;
                    Action reset=()=>{activate=send=probe=pause=0;source=target=clip=focus=mods=sent=true;selection=null;atProbe=atPause=null;};
                    Func<PasteChecks> create=()=>new PasteChecks{SourceCurrent=()=>source,TargetCurrent=()=>target,ClipboardCurrent=()=>clip,TargetFocused=()=>focus,ModifiersReleased=()=>mods,SelectionProblem=()=>{probe++;if(atProbe!=null)atProbe(probe);return selection;},Activate=()=>{activate++;return true;},Send=()=>{send++;return sent;},Pause=token=>{token.ThrowIfCancellationRequested();pause++;if(atPause!=null)atPause(pause);return Task.FromResult(0);}};
                    Func<Task<PreviewPasteResult>> run=()=>PasteFlow.Run(new SemaphoreSlim(1),create(),CancellationToken.None);
                    reset();check((await run()).Requested&&activate==1&&send==1);
                    reset();source=false;check(!(await run()).Requested&&activate==0&&send==0);
                    reset();target=false;var missingTarget=await run();check(!missingTarget.Requested&&activate==0&&missingTarget.Stage=="initial"&&missingTarget.Block.StartsWith("target/")&&missingTarget.Reason.Contains("입력창"));
                    reset();clip=false;var changedClip=await run();check(!changedClip.Requested&&activate==0&&changedClip.Reason.Contains("클립보드"));
                    reset();selection="selected draft";check(!(await run()).Requested&&activate==0);
                    reset();atProbe=n=>{if(n==1)source=false;};check(!(await run()).Requested&&activate==0);
                    reset();atProbe=n=>{if(n==1)clip=false;};check(!(await run()).Requested&&activate==0);
                    reset();atPause=n=>target=false;check(!(await run()).Requested&&send==0);
                    reset();atPause=n=>focus=false;check(!(await run()).Requested&&send==0);
                    reset();atProbe=n=>{if(n==2)focus=false;};check(!(await run()).Requested&&send==0);
                    reset();atProbe=n=>{if(n==2)clip=false;};check(!(await run()).Requested&&send==0);
                    reset();atProbe=n=>{if(n==2)mods=false;};check(!(await run()).Requested&&send==0);
                    reset();mods=false;check(!(await run()).Requested&&activate==0&&pause==25);
                    reset();sent=false;check(!(await run()).Requested&&send==1);
                    reset();using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool canceled=false;try{await PasteFlow.Run(new SemaphoreSlim(1),create(),cancel.Token);}catch(OperationCanceledException){canceled=true;}check(canceled&&activate==0&&send==0);}
                    reset();using(var cancel=new CancellationTokenSource()){atProbe=n=>cancel.Cancel();bool canceled=false;try{await PasteFlow.Run(new SemaphoreSlim(1),create(),cancel.Token);}catch(OperationCanceledException){canceled=true;}check(canceled&&activate==0&&send==0);}
                    reset();var gate=new SemaphoreSlim(1);int firstSend=0,secondSend=0,secondActivate=0,ends=0;using(var cancel=new CancellationTokenSource()){
                        var first=create();first.Pause=token=>Task.Delay(Timeout.Infinite,token);first.Send=()=>{firstSend++;return true;};first.End=()=>ends++;
                        var firstTask=PasteFlow.Run(gate,first,cancel.Token);var second=create();second.Activate=()=>{secondActivate++;return true;};second.Send=()=>{secondSend++;return true;};second.End=()=>ends++;
                        var secondTask=PasteFlow.Run(gate,second,CancellationToken.None);check(secondActivate==0&&secondSend==0);cancel.Cancel();try{await firstTask;}catch(OperationCanceledException){}check((await secondTask).Requested&&firstSend==0&&secondSend==1&&ends==2&&gate.CurrentCount==1);
                    }
                    System.IO.File.WriteAllText(report,"PASS: "+count+" deterministic attachment checks: stale target/source/clipboard, selected draft, focus changes during both probes, modifier timeout, cancellation, partial-send no retry, serialized old/new requests. Fake environment only; no windows, desktop observation, clipboard, key injection or KakaoTalk interaction.\r\n");
                }catch(Exception ex){System.IO.File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}finally{timer.Dispose();context.ExitThread();}
            };timer.Start();Application.Run(context);
        }
        public static void Test(){string report=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-paste-test.txt");try{var keys=PasteKeys();if(keys.Length!=4||keys.Any(k=>k.Data.Keyboard.Key==0x0d)||keys[0].Data.Keyboard.Key!=0x11||keys[1].Data.Keyboard.Key!=0x56||keys[2].Data.Keyboard.Flags!=2||keys[3].Data.Keyboard.Flags!=2)throw new Exception("Unexpected paste key sequence");if(Marshal.SizeOf(typeof(Input))!=(IntPtr.Size==8?40:28))throw new Exception("Native INPUT structure size");using(var box=new RichTextBox()){box.Text="유지할 문장";IntPtr handle=box.Handle;string reason;box.Select(0,2);if(NoSelection(handle,out reason))throw new Exception("Selected draft text must block automatic paste");box.Select(box.TextLength,0);if(!NoSelection(handle,out reason))throw new Exception("Caret selection must be recognized");if(box.Text!="유지할 문장")throw new Exception("Selection probe changed text");using(var monitor=new PreviewPaste(false)){var element=AutomationElement.FromHandle(handle);monitor.current=new PreviewChatTarget{Window=handle,Editor=handle,AccessibleEditor=element,AccessibleId="fixture-runtime"};var snapshot=monitor.Snapshot();if(snapshot.AccessibleEditor!=element||snapshot.AccessibleId!="fixture-runtime")throw new Exception("Qt accessibility identity lost by snapshot");}}int qtChecks=QtComposer.TestGeometry();System.IO.File.WriteAllText(report,"PASS: "+qtChecks+" Qt process/physical geometry cases, preserved accessibility snapshot identity; Ctrl+V-only sequence, no Enter, Win64 layout, selected text blocks, text preserved. No key injection, clipboard, or KakaoTalk interaction.\r\n");}catch(Exception ex){System.IO.File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}}
    }
}
