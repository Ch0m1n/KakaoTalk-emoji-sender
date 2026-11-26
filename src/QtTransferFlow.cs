using System;using System.Collections.Generic;using System.Diagnostics;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;
namespace AgitCon {
 public sealed partial class PreviewPaste {
  bool qtPickerTransition;
  bool QtPickerShim(IntPtr window,PreviewChatTarget target){if(!qtPickerTransition||target==null||target.AccessibleEditor==null)return false;uint pid;uint thread=Native.GetWindowThreadProcessId(window,out pid);Native.Rect r;if(!Native.GetWindowRect(window,out r))return false;return QtTransferRules.Shim(Class(window).StartsWith("Qt",StringComparison.OrdinalIgnoreCase),pid==target.ProcessId,thread==target.ThreadId,Native.Title(window),r.Right-r.Left,r.Bottom-r.Top);}
  bool QtPickerOwner(IntPtr dialog,PreviewChatTarget target){return qtPickerTransition&&target.AccessibleEditor!=null&&!priorFileDialogs.Contains(dialog)&&QtPickerShim(GetWindow(dialog,4),target);}

  bool SendQtChord(Input[] keys){if(SendInput((uint)keys.Length,keys,System.Runtime.InteropServices.Marshal.SizeOf(typeof(Input)))==keys.Length)return true;var release=new[]{Key(0x11,true)};SendInput(1,release,System.Runtime.InteropServices.Marshal.SizeOf(typeof(Input)));Invalidate();return false;}
  static bool QtDraftLength(PreviewChatTarget target,out int length){length=-1;try{if(!QtComposer.Current(target))return false;object value;if(!target.AccessibleEditor.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern,out value))return false;var range=((System.Windows.Automation.TextPattern)value).DocumentRange.Clone();length=range.MoveEndpointByUnit(System.Windows.Automation.Text.TextPatternRangeEndpoint.Start,System.Windows.Automation.Text.TextUnit.Character,32769);return length>=0&&length<32769;}catch{return false;}}
  static bool ComposerSelection(PreviewChatTarget target,out string reason){return target.AccessibleEditor!=null?QtComposer.NoSelection(target,out reason):NoSelection(target.Editor,out reason);}
  bool FocusQtComposer(PreviewChatTarget target){if(!SameRoom(target)||!QtComposer.Current(target))return false;if(!SetForegroundWindow(target.Window)&&Native.GetForegroundWindow()!=target.Window)return false;return Native.GetForegroundWindow()==target.Window&&SameRoom(target)&&QtComposer.Focus(target);}
  async Task<LongFlowResult> TryQtLongCon(PreviewChatTarget target,LongPrepared ready,IntPtr source,CancellationToken token,IProgress<string> progress,Func<bool> roomCurrent,Action<int,string> trace){
   if(!Valid(target)||Native.GetForegroundWindow()!=source||!Own(source)||!roomCurrent())throw new InvalidOperationException("대상 채팅방이 바뀌었어요. 카톡 입력창을 누른 뒤 다시 확인해주세요.");
   int originalLength;string selection;if(!QtDraftLength(target,out originalLength)||!ComposerSelection(target,out selection))throw new InvalidOperationException("신형 입력창의 작성 상태·선택 영역을 확인하지 못했어요.");
   long processStart;using(var process=Process.GetProcessById((int)target.ProcessId))processStart=process.StartTime.ToUniversalTime().Ticks;
   bool started=false;uint sequence=0;QtTransferProof proof=null;HashSet<string> before=null;photoFocusLost=false;photoTarget=target;FileAttachmentInProgress=true;
   Func<bool> context=()=>{if(disposed||token.IsCancellationRequested||photoFocusLost||!roomCurrent()||!SameRoom(target))return false;try{using(var p=Process.GetProcessById((int)target.ProcessId))if(p.StartTime.ToUniversalTime().Ticks!=processStart)return false;}catch{return false;}IntPtr foreground=Native.GetForegroundWindow();return (!started&&foreground==source)||foreground==target.Window||OwnedByRoom(foreground,target);};
   Func<bool> composer=()=>{int length;string reason;return context()&&IsFocused(target)&&QtDraftLength(target,out length)&&length==originalLength&&ComposerSelection(target,out reason);};
   var checks=new LongFlowChecks{Current=context,Progress=n=>{if(trace!=null)trace(n,"qt-confirm-requested");progress.Report("전송 요청 "+n+" / "+ready.Parts.Count+"장");},
    Paste=async(index,cancel)=>{
     if(started&&!composer())throw new InvalidOperationException("다음 조각 전에 입력창 상태가 달라졌어요.");
     var old=QtPreviews(target,QtTransferKind.Image);if(old.Count!=0)throw new InvalidOperationException("기존 이미지 확인창을 먼저 닫아주세요.");before=new HashSet<string>(old.Select(x=>x.SurfaceId));proof=null;
     Clipboard.SetDataObject(ready.Parts[index].Data,true,5,40);sequence=GetClipboardSequenceNumber();
     var paste=new PasteChecks{SourceCurrent=()=>context()&&Native.GetForegroundWindow()==(started?target.Window:source),TargetCurrent=()=>SameRoom(target)&&roomCurrent()&&QtComposer.Current(target),ClipboardCurrent=()=>GetClipboardSequenceNumber()==sequence,ModifiersReleased=()=>Control.ModifierKeys==Keys.None,TargetFocused=composer,SelectionProblem=()=>{string reason;return ComposerSelection(target,out reason)?null:reason;},Activate=()=>{transition=true;return FocusQtComposer(target);},Pause=t=>Task.Delay(10,t),Send=()=>{if(!composer()||GetClipboardSequenceNumber()!=sequence)return false;var keys=PasteKeys();return SendQtChord(keys);}};
     var result=await PasteFlow.Execute(paste,cancel);if(!result.Requested){if(trace!=null)trace(index,"qt-paste-"+result.Stage+"/"+result.Block);throw new InvalidOperationException("Qt 입력창으로 붙여넣기를 요청하지 못했어요. ["+result.Block+"]");}started=true;
    },
    Preview=async cancel=>{var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<7000){cancel.ThrowIfCancellationRequested();if(!context()||GetClipboardSequenceNumber()!=sequence)return null;var found=QtPreviews(target,QtTransferKind.Image).Where(x=>!before.Contains(x.SurfaceId)).ToList();if(found.Count>1)return null;if(found.Count==1&&QtProofCurrent(found[0])){proof=found[0];return proof;}await Task.Delay(35,cancel);}return null;},
    Enter=value=>{var found=value as QtTransferProof;return found!=null&&ConfirmQtPreview(found,()=>context()&&Control.ModifierKeys==Keys.None&&GetClipboardSequenceNumber()==sequence);},
    Dismissed=async(value,cancel)=>{var found=(QtTransferProof)value;var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<3500){cancel.ThrowIfCancellationRequested();if(!context())return false;if(!found.Visible()){if(composer())return true;if(Native.GetForegroundWindow()==target.Window&&QtComposer.Current(target)&&QtComposer.Focus(target)&&composer())return true;}await Task.Delay(15,cancel);}return false;}
   };
   var outcome=await LongFlow.Run(ready.Parts.Count,checks,token);if(trace!=null)trace(outcome.Requested,"qt-"+outcome.Stage+"/pasted="+outcome.Pasted+"/ui="+qtTransferProblem);return outcome;
  }
  sealed class QtFileChoice {internal IntPtr Native;internal QtPicker Picker;internal PreviewPasteResult Result;}
  async Task<QtFileChoice> SelectQtFiles(PreviewChatTarget target,CancellationToken token,string names,IntPtr source){
   var result=new PreviewPasteResult{Stage="qt-file-select",Reason="신형 파일 선택창을 확인하지 못했어요."};
   if(!Valid(target)||Native.GetForegroundWindow()!=source||!Own(source)){result.Block="target/not-current";return new QtFileChoice{Result=result};}
   if(PhotoDialogs(target).Count!=0||QtPickers(target).Count!=0){result.Block="dialog/already-open";return new QtFileChoice{Result=result};}
   RememberFileDialogs(target);qtPickerTransition=true;photoFocusLost=false;photoTarget=target;FileAttachmentInProgress=true;
   Func<bool> context=()=>!disposed&&!token.IsCancellationRequested&&!photoFocusLost&&SameRoom(target)&&(Native.GetForegroundWindow()==source||Native.GetForegroundWindow()==target.Window||OwnedByRoom(Native.GetForegroundWindow(),target)||NewFileDialogTransition(Native.GetForegroundWindow(),target));
   var checks=new PasteChecks{SourceCurrent=()=>context()&&Native.GetForegroundWindow()==source,TargetCurrent=()=>Valid(target),ClipboardCurrent=()=>true,ModifiersReleased=()=>Control.ModifierKeys==Keys.None,TargetFocused=()=>IsFocused(target),SelectionProblem=()=>{string reason;return ComposerSelection(target,out reason)?null:reason;},Activate=()=>{transition=true;return ActivateComposer(target);},Pause=t=>Task.Delay(20,t),Send=()=>{var keys=FileDialogKeys();return SendQtChord(keys);}};
   var opened=await PasteFlow.Execute(checks,token);if(!opened.Requested)return new QtFileChoice{Result=opened};
   IntPtr native=IntPtr.Zero;QtPicker picker=null;var clock=Stopwatch.StartNew();
   while(clock.ElapsedMilliseconds<3000){token.ThrowIfCancellationRequested();if(!context()){result.Block="focus/foreign-window";return new QtFileChoice{Result=result};}var natives=PhotoDialogs(target);var quick=QtPickers(target);if(natives.Count+quick.Count>1){result.Block="dialog/ambiguous";return new QtFileChoice{Result=result};}if(natives.Count==1){native=natives[0];break;}if(quick.Count==1){picker=quick[0];break;}await Task.Delay(35,token);}
   if(native==IntPtr.Zero&&picker==null){result.Block="dialog/not-found";result.Reason="신형 카톡 파일 선택창을 찾지 못했어요. Ctrl+T로 직접 첨부해주세요.";return new QtFileChoice{Result=result};}
   if(!context()||(native!=IntPtr.Zero?Native.GetForegroundWindow()!=native||!SetFilename(native,names):!SetQtFilename(picker,target,names))){result.Block="filename/unavailable-or-nonempty";result.Reason="파일 선택창은 열었지만 이름 칸을 확인하지 못했어요. 기존 입력은 덮어쓰지 않았어요.";return new QtFileChoice{Result=result};}
   return new QtFileChoice{Native=native,Picker=picker};
  }
  async Task<PreviewPasteResult> TryQtFileSelection(PreviewChatTarget target,CancellationToken token,IList<string> paths,IntPtr source,int expected){
   string names=AttachmentFileNames(paths,expected);var selection=await SelectQtFiles(target,token,names,source);if(selection.Result!=null)return selection.Result;
   // Multi-file batching deliberately stops before Open and Send, just like the classic route.
   return new PreviewPasteResult{Requested=true,Stage="qt-files-selected-manual-open",Reason="이미지 "+expected+"개를 지정했어요. ‘열기’를 직접 누르고 묶음과 순서를 확인해주세요. 전송도 직접 눌러주세요."};
  }
  async Task<PreviewPasteResult> TryQtPhotoAttach(PreviewChatTarget target,CancellationToken token,string path,IntPtr source,bool direct,bool verificationOnly){
   var result=new PreviewPasteResult{Stage="qt-file-prepare",Reason="신형 카톡의 파일 첨부 대상을 확인하지 못했어요. GIF 파일은 복사되어 있어요."};
   if(!Valid(target)||Native.GetForegroundWindow()!=source||!Own(source)){result.Block="target/not-current";return result;}
   if(!GifAttachment.AnimatedGif(path)){result.Block="asset/not-gif";return result;}
   // A readable confirmation setting is required unless automatic sending is opted in.
   int confirmation=direct?0:await ConfirmationState(target.ProcessId,token);
   if(verificationOnly)return new PreviewPasteResult{Stage="qt-confirmation-only",Reason=confirmation==1?"verified":"not-verified",Block=confirmation==1?null:"settings/no-proof"};
   if(PhotoDialogs(target).Count!=0||QtPickers(target).Count!=0||QtPreviews(target,QtTransferKind.File,path).Count!=0){result.Block="dialog/already-open";return result;}
   string full=Path.GetFullPath(path);var choice=await SelectQtFiles(target,token,full,source);if(choice.Result!=null)return choice.Result;IntPtr native=choice.Native;QtPicker picker=choice.Picker;
   Func<bool> context=()=>!disposed&&!token.IsCancellationRequested&&!photoFocusLost&&SameRoom(target)&&(Native.GetForegroundWindow()==source||Native.GetForegroundWindow()==target.Window||OwnedByRoom(Native.GetForegroundWindow(),target)||NewFileDialogTransition(Native.GetForegroundWindow(),target));
   if(!direct&&confirmation!=1){return new PreviewPasteResult{Requested=true,Stage="qt-file-selected-manual-open",Reason="GIF 파일을 지정했어요. 전송 전 확인 설정을 읽지 못해 ‘열기’는 직접 눌러주세요. ‘전송’은 자동으로 누르지 않아요."};}
   for(int attempt=0;attempt<40;attempt++){token.ThrowIfCancellationRequested();if(!context())break;bool enabled=native!=IntPtr.Zero?IsWindowEnabled(GetDlgItem(native,1)):UiVisible(picker.Open);if(enabled)break;await Task.Delay(20,token);}
   bool requested=false;
   if(context()&&Control.ModifierKeys==Keys.None){if(native!=IntPtr.Zero){string selected;var field=FilenameEdit(native);if(field!=IntPtr.Zero&&ReadFilename(field,out selected)&&selected==full&&PhotoDialogs(target).Contains(native)&&Native.GetForegroundWindow()==native)requested=NativeOpen(native);}else requested=OpenQtPicker(picker,target,context);}
   if(!requested){result.Stage="qt-open-unconfirmed";result.Block="open/result-unknown";result.Reason="열기 요청 결과가 불확실해 멈췄어요. 자동 재시도는 하지 않아요.";return result;}
   bool saw=false;
   if(direct){return await FinishDirect(new DirectChecks{Current=()=>context()&&gifDirectSend,PickerClosed=()=>native!=IntPtr.Zero?!Native.IsWindow(native)||!IsWindowVisible(native):!UiVisible(picker.Surface),SawConfirmation=()=>saw,FindSend=()=>{var found=QtPreviews(target,QtTransferKind.File,path);if(found.Count>1)return Tuple.Create(true,(Func<bool>)null);if(found.Count==0)return Tuple.Create(false,(Func<bool>)null);saw=true;var proof=found[0];return Tuple.Create(false,(Func<bool>)(()=>ConfirmQtPreview(proof,()=>context()&&gifDirectSend&&Control.ModifierKeys==Keys.None)));},Pause=t=>Task.Delay(35,t)},token);}
   var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<2500){token.ThrowIfCancellationRequested();if(!context())break;var found=QtPreviews(target,QtTransferKind.File,path);if(found.Count>1)break;if(found.Count==1)return new PreviewPasteResult{Requested=true,Stage="qt-file-preview",Reason="GIF 원본을 파일 전송 확인창에 첨부했어요. 전송은 직접 눌러주세요."};await Task.Delay(35,token);}
   return new PreviewPasteResult{Stage="qt-open-result-unknown",Block="preview/not-proved/"+qtTransferProblem,Reason="GIF 열기를 요청했지만 파일 전송 확인창은 확인하지 못했어요. 카톡에서 결과를 확인해주세요. 자동 재시도는 하지 않아요."};
  }
 }
}
