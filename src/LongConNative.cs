using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace AgitCon {
    public sealed partial class PreviewPaste {
        [DllImport("user32.dll",EntryPoint="PostMessageW",SetLastError=true)]static extern bool PostLongKey(IntPtr window,uint message,IntPtr key,IntPtr data);
        internal sealed class LongProof {internal IntPtr Window;internal string Class;internal Func<bool> Structure;}
        static bool EmptyEdit(IntPtr window){UIntPtr value;return SendMessageTimeout(window,0x000E,IntPtr.Zero,IntPtr.Zero,0x22,150,out value)!=IntPtr.Zero&&value.ToUInt64()==0;}
        static bool LongHeader(string text){return text=="클립보드 이미지 전송";}
        string longProofProblem="not-inspected";
        bool LongStructure(IntPtr dialog){
            try{
                bool heading=LongHeader(Native.Title(dialog));int buttons=0,editors=0;bool empty=true;
                EnumWindow visit=(window,data)=>{string cls=Class(window);if(!IsWindowVisible(window))return true;
                    if(cls=="Static"||cls.StartsWith("WindowsForms10.STATIC",StringComparison.OrdinalIgnoreCase))heading|=LongHeader(Native.Title(window));
                    if((cls=="Button"||cls.StartsWith("WindowsForms10.BUTTON",StringComparison.OrdinalIgnoreCase))&&Native.Title(window)=="전송"&&IsWindowEnabled(window))buttons++;
                    if(cls=="Edit"||cls.StartsWith("RichEdit",StringComparison.OrdinalIgnoreCase)||cls.StartsWith("WindowsForms10.EDIT",StringComparison.OrdinalIgnoreCase)||cls.StartsWith("WindowsForms10.RICHEDIT",StringComparison.OrdinalIgnoreCase)){editors++;if(!EmptyEdit(window))empty=false;}
                    return true;};EnumChildWindows(dialog,visit,IntPtr.Zero);
                longProofProblem="native heading="+heading+",buttons="+buttons+",edits="+editors+",empty="+empty;
                if(buttons>1||!empty)return false;
                if(heading&&buttons==1&&editors>0){longProofProblem="native-confirmed";return true;}
                // Scoped only to the new owned preview, never the conversation history.
                var root=AutomationElement.FromHandle(dialog);var heads=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"클립보드 이미지 전송"));
                if(!heading&&heads.Count!=1)return false;
                var sends=root.FindAll(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.NameProperty,"전송")));
                if(sends.Count!=1||!sends[0].Current.IsEnabled||sends[0].Current.IsOffscreen)return false;
                if(editors>0)return empty;
                var edits=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit));
                if(edits.Count!=1)return false;object pattern;
                return edits[0].TryGetCurrentPattern(ValuePattern.Pattern,out pattern)&&String.IsNullOrEmpty(((ValuePattern)pattern).Current.Value);
            }catch(ElementNotAvailableException){return false;}catch(InvalidOperationException){return false;}
        }
        string longCurrentProblem="not-checked",longEnterProblem="not-attempted";
        bool LongProofCurrent(LongProof proof,PreviewChatTarget target){
            longCurrentProblem="missing-proof";if(proof==null)return false;
            longCurrentProblem="window-closed";if(proof.Window==target.Window||!Native.IsWindow(proof.Window)||!IsWindowVisible(proof.Window))return false;
            longCurrentProblem="window-disabled";if(!IsWindowEnabled(proof.Window))return false;
            longCurrentProblem="foreground-changed";if(Native.GetForegroundWindow()!=proof.Window)return false;
            longCurrentProblem="class-owner-room-changed";if(Class(proof.Window)!=proof.Class||!OwnedByRoom(proof.Window,target)||!SameRoom(target))return false;
            uint pid;longCurrentProblem="thread-process-changed";if(Native.GetWindowThreadProcessId(proof.Window,out pid)!=target.ThreadId||pid!=target.ProcessId)return false;
            longCurrentProblem="structure-changed";if(!proof.Structure())return false;longCurrentProblem="confirmed";return true;
        }
        internal static bool LongEnterToWindow(IntPtr window){
            // Targeted queued keyboard messages, never global SendInput Enter.
            // The composer is never a target of this method's guarded caller.
            bool down=PostLongKey(window,0x0100,new IntPtr(0x0d),new IntPtr(0x001c0001));
            bool up=PostLongKey(window,0x0101,new IntPtr(0x0d),new IntPtr(unchecked((int)0xc01c0001)));
            return down&&(up||!Native.IsWindow(window));
        }
        internal async Task<LongFlowResult> TryLongCon(PreviewChatTarget target,LongPrepared ready,IntPtr source,CancellationToken token,IProgress<string> progress,Func<bool> roomCurrent,Action<int,string> trace=null){
            await pasteGate.WaitAsync(token);try{
                if(target!=null&&target.AccessibleEditor!=null)return await TryQtLongCon(target,ready,source,token,progress,roomCurrent,trace);
                if(!Valid(target)||Native.GetForegroundWindow()!=source||!Own(source)||!roomCurrent())throw new InvalidOperationException("대상 채팅방이 바뀌었어요. 카톡 입력창을 누른 뒤 다시 확인해주세요.");
                UIntPtr draftLength;if(SendMessageTimeout(target.Editor,0x000E,IntPtr.Zero,IntPtr.Zero,0x22,150,out draftLength)==IntPtr.Zero)throw new InvalidOperationException("작성 문장 상태를 확인하지 못했어요.");
                long processStart;using(var process=Process.GetProcessById((int)target.ProcessId))processStart=process.StartTime.ToUniversalTime().Ticks;
                longEnterProblem="not-attempted";longCurrentProblem="not-checked";bool started=false;uint sequence=0;LongProof proof=null;HashSet<IntPtr> before=null;photoFocusLost=false;photoTarget=target;FileAttachmentInProgress=true;
                Func<bool> context=()=>{if(disposed||token.IsCancellationRequested||photoFocusLost||!roomCurrent()||!SameRoom(target))return false;try{using(var p=Process.GetProcessById((int)target.ProcessId))if(p.StartTime.ToUniversalTime().Ticks!=processStart)return false;}catch{return false;}
                    IntPtr foreground=Native.GetForegroundWindow();return (!started&&foreground==source)||(foreground==target.Window)||(before!=null&&!before.Contains(foreground)&&OwnedByRoom(foreground,target));};
                Func<bool> composer=()=>{UIntPtr length;string reason;return context()&&Native.GetForegroundWindow()==target.Window&&Focus(target.ThreadId)==target.Editor&&Composer(target.Window,target.Editor)&&NoSelection(target.Editor,out reason)&&SendMessageTimeout(target.Editor,0x000E,IntPtr.Zero,IntPtr.Zero,0x22,150,out length)!=IntPtr.Zero&&length==draftLength;};
                var checks=new LongFlowChecks{Current=context,Progress=n=>{if(trace!=null)trace(n,"enter-requested");progress.Report("전송 요청 "+n+" / "+ready.Parts.Count+"장");},
                    Paste=async(index,cancel)=>{
                        if(started&&!composer())throw new InvalidOperationException("다음 조각 전에 입력창 상태가 달라졌어요.");
                        before=OwnedWindows(target);proof=null;Clipboard.SetDataObject(ready.Parts[index].Data,true,5,40);sequence=GetClipboardSequenceNumber();
                        var paste=new PasteChecks{SourceCurrent=()=>context()&&Native.GetForegroundWindow()==(started?target.Window:source),TargetCurrent=()=>SameRoom(target)&&roomCurrent(),ClipboardCurrent=()=>GetClipboardSequenceNumber()==sequence,ModifiersReleased=()=>Control.ModifierKeys==Keys.None,TargetFocused=()=>composer(),SelectionProblem=()=>{string reason;return NoSelection(target.Editor,out reason)?null:reason;},Activate=()=>{transition=true;return SetForegroundWindow(target.Window);},Pause=t=>Task.Delay(10,t),Send=()=>{if(!composer()||GetClipboardSequenceNumber()!=sequence)return false;var keys=PasteKeys();if(SendInput((uint)keys.Length,keys,Marshal.SizeOf(typeof(Input)))==keys.Length)return true;var release=new[]{Key(0x11,true)};SendInput(1,release,Marshal.SizeOf(typeof(Input)));return false;}};
                        var outcome=await PasteFlow.Execute(paste,cancel);if(!outcome.Requested){if(trace!=null)trace(index,"paste-"+outcome.Stage+" / "+outcome.Block);throw new InvalidOperationException("콘을 붙여넣기 전에 대상·클립보드·포커스가 달라졌어요.");}started=true;
                    },
                    Preview=async cancel=>{
                        var clock=Stopwatch.StartNew();var attempts=new LongPreviewAttempts();longVisualProblem="not-inspected";while(clock.ElapsedMilliseconds<7000){cancel.ThrowIfCancellationRequested();if(!context()||GetClipboardSequenceNumber()!=sequence)return null;
                            IntPtr candidate=Native.GetForegroundWindow();var windows=OwnedWindows(target).Where(w=>!before.Contains(w)&&w==candidate&&w!=target.Window).ToList();
                            if(windows.Count==1){Func<bool> structure=null;if(LongStructure(candidate))structure=()=>LongStructure(candidate);else {if(attempts.Available(candidate)){structure=await LongVisualStructure(candidate,cancel);attempts.Finished(candidate,longVisionReadStarted);}}
                                if(structure!=null){proof=new LongProof{Window=candidate,Class=Class(candidate),Structure=structure};if(LongProofCurrent(proof,target))return proof;}}
                            await Task.Delay(40,cancel);
                        }return null;
                    },
                    Enter=value=>{var found=value as LongProof;
                        longEnterProblem="context-changed";if(!context())return false;
                        longEnterProblem="clipboard-changed";if(GetClipboardSequenceNumber()!=sequence)return false;
                        longEnterProblem="modifiers-held";if(Control.ModifierKeys!=Keys.None)return false;
                        longEnterProblem="proof-invalid";if(!LongProofCurrent(found,target))return false;
                        longEnterProblem="post-key-result-unknown";bool sent=LongEnterToWindow(found.Window);if(sent)longEnterProblem="requested";return sent;},
                    Dismissed=async(value,cancel)=>{var found=(LongProof)value;var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<3500){cancel.ThrowIfCancellationRequested();if(!context())return false;
                            if(!Native.IsWindow(found.Window)||!IsWindowVisible(found.Window)){if(composer())return true;if(Native.GetForegroundWindow()!=target.Window)return false;}
                            await Task.Delay(10,cancel);
                        }return false;}
                };
                var result=await LongFlow.Run(ready.Parts.Count,checks,token);if(trace!=null)trace(result.Requested,result.Stage+" / pasted="+result.Pasted+" / "+longProofProblem+" / "+longVisualProblem+" / enter="+longEnterProblem+" / current="+longCurrentProblem);return result;
            }finally{FileAttachmentInProgress=false;photoTarget=null;transition=false;Invalidate();pasteGate.Release();}
        }
    }
}
