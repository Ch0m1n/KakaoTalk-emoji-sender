using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgitCon {
    public sealed class ChatTarget {
        public IntPtr Window,Editor;public uint ProcessId,ThreadId;public string Title,EditorClass;public long Version;
    }
    public sealed class AttachmentResult {public bool Requested;public string Reason;}
    public sealed class KakaoAttachment:IDisposable {
        [StructLayout(LayoutKind.Sequential)]struct GuiInfo {public uint Size,Flags;public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;public Native.Rect CaretRect;}
        delegate void WinEvent(IntPtr hook,uint type,IntPtr window,int objectId,int childId,uint thread,uint time);
        [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint thread,ref GuiInfo info);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder name,int size);
        [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr h,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out UIntPtr result);
        [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
        [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr h,int index);
        readonly uint ownProcess=(uint)Process.GetCurrentProcess().Id;readonly WinEvent events;IntPtr foregroundHook,nameHook;ChatTarget current;long version;bool transition;
        public KakaoAttachment(){events=OnEvent;foregroundHook=SetWinEventHook(3,3,IntPtr.Zero,events,0,0,0);nameHook=SetWinEventHook(0x800C,0x800C,IntPtr.Zero,events,0,0,0);}
        bool Own(IntPtr h){uint pid;Native.GetWindowThreadProcessId(h,out pid);return pid==ownProcess;}
        void OnEvent(IntPtr hook,uint type,IntPtr window,int objectId,int childId,uint thread,uint time){
            if(type==3){if(!Own(window)&&(current==null||window!=current.Window))Invalidate();}
            else if(current!=null&&window==current.Window&&objectId==0&&Native.Title(window)!=current.Title)Invalidate();
        }
        void Invalidate(){version++;current=null;}
        static string Class(IntPtr h){var b=new StringBuilder(128);GetClassName(h,b,b.Capacity);return b.ToString();}
        static IntPtr Focus(uint thread){var info=new GuiInfo{Size=(uint)Marshal.SizeOf(typeof(GuiInfo))};return GetGUIThreadInfo(thread,ref info)?info.Focus:IntPtr.Zero;}
        static bool Composer(IntPtr root,IntPtr editor){if(editor==IntPtr.Zero||!Native.IsWindow(editor)||!IsWindowEnabled(editor)||Native.GetAncestor(editor,2)!=root)return false;string name=Class(editor);if(!name.StartsWith("RichEdit",StringComparison.OrdinalIgnoreCase))return false;if((GetWindowLong(editor,-16)&0x800)!=0)return false;
            Native.Rect chat,input;if(!Native.GetWindowRect(root,out chat)||!Native.GetWindowRect(editor,out input))return false;return input.Top>=chat.Top+(chat.Bottom-chat.Top)*0.5&&input.Right-input.Left>=(chat.Right-chat.Left)*0.5&&input.Bottom-input.Top>=24&&input.Bottom<=chat.Bottom;
        }
        internal static bool NoSelection(IntPtr editor,out string reason){reason="";IntPtr start=Marshal.AllocHGlobal(4),end=Marshal.AllocHGlobal(4);try{Marshal.WriteInt32(start,-1);Marshal.WriteInt32(end,-1);UIntPtr value;if(SendMessageTimeout(editor,0x00B0,start,end,0x22,200,out value)==IntPtr.Zero){reason="선택 상태 응답 없음 ("+Marshal.GetLastWin32Error()+")";return false;}int a=Marshal.ReadInt32(start),b=Marshal.ReadInt32(end);if(a<0||b<0){reason="선택 상태를 제공하지 않는 입력창";return false;}if(a!=b){reason="작성 문장에 선택 영역이 있어요";return false;}return true;}finally{Marshal.FreeHGlobal(start);Marshal.FreeHGlobal(end);}}
        public void Observe(){if(transition)return;IntPtr root=Native.GetForegroundWindow();if(Own(root))return;if(!Native.IsKakao(root)||Native.IsIconic(root)){if(current!=null)Invalidate();return;}uint pid;uint thread=Native.GetWindowThreadProcessId(root,out pid);IntPtr editor=Focus(thread);if(!Composer(root,editor)){if(current!=null)Invalidate();return;}string title=Native.Title(root);if(String.IsNullOrWhiteSpace(title)||title=="카카오톡"){Invalidate();return;}
            if(current==null||current.Window!=root||current.Editor!=editor||current.Title!=title){version++;current=new ChatTarget{Window=root,Editor=editor,Title=title,EditorClass=Class(editor),ProcessId=pid,ThreadId=thread,Version=version};}}
        public ChatTarget Snapshot(){return current==null?null:new ChatTarget{Window=current.Window,Editor=current.Editor,Title=current.Title,EditorClass=current.EditorClass,ProcessId=current.ProcessId,ThreadId=current.ThreadId,Version=current.Version};}
        public string Status {get{return current==null?"카톡의 메시지 입력창을 한 번 눌러주세요":"첨부 대상: "+current.Title;}}
        public bool Valid(ChatTarget target){if(target==null||current==null||target.Version!=version||target.Window!=current.Window||target.Editor!=current.Editor||!Native.IsWindow(target.Window)||Native.IsIconic(target.Window)||Native.Title(target.Window)!=target.Title||!Native.IsKakao(target.Window))return false;uint pid;uint thread=Native.GetWindowThreadProcessId(target.Window,out pid);return pid==target.ProcessId&&thread==target.ThreadId&&Class(target.Editor)==target.EditorClass&&Composer(target.Window,target.Editor);}
        public async Task<AttachmentResult> TryPaste(ChatTarget target,CancellationToken cancel){var outcome=new AttachmentResult{Reason="대상 입력창이 확실하지 않아 복사만 했어요."};
            if(foregroundHook==IntPtr.Zero||nameHook==IntPtr.Zero||!Own(Native.GetForegroundWindow())||!Valid(target))return outcome;
            string selection;if(!NoSelection(target.Editor,out selection)){outcome.Reason=selection+" · 복사만 했어요.";return outcome;}
            transition=true;try{
            if(!SetForegroundWindow(target.Window)){outcome.Reason="카톡을 활성화하지 못해 복사만 했어요.";return outcome;}
            for(int i=0;i<10;i++){await Task.Delay(20,cancel);if(!Valid(target))break;if(Native.GetForegroundWindow()==target.Window&&Focus(target.ThreadId)==target.Editor)break;}
            cancel.ThrowIfCancellationRequested();
            if(Native.GetForegroundWindow()!=target.Window){outcome.Reason="카톡이 활성화되지 않아 복사만 했어요.";return outcome;}
            if(!Valid(target)){outcome.Reason="대상 채팅방이 달라져 복사만 했어요.";return outcome;}
            if(Focus(target.ThreadId)!=target.Editor){outcome.Reason="메시지 입력창 포커스를 확인하지 못해 복사만 했어요.";return outcome;}
            if(!NoSelection(target.Editor,out selection)){outcome.Reason=selection+" · 복사만 했어요.";return outcome;}
            // Target this exact editor, never the system-wide active keyboard input.
            // WM_PASTE is the only write. Never inject Enter, Ctrl+A, or text.
            UIntPtr result;if(SendMessageTimeout(target.Editor,0x0302,IntPtr.Zero,IntPtr.Zero,0x22,500,out result)==IntPtr.Zero){outcome.Reason="카톡 응답을 확인하지 못했어요. 자동으로 재시도하지 않아요.";Invalidate();return outcome;}
            outcome.Requested=true;outcome.Reason="카톡에 첨부 요청을 보냈어요. 미리보기를 확인하고 직접 전송해주세요.";Invalidate();return outcome;
            }finally{transition=false;}
        }
        public void Dispose(){if(foregroundHook!=IntPtr.Zero)UnhookWinEvent(foregroundHook);if(nameHook!=IntPtr.Zero)UnhookWinEvent(nameHook);foregroundHook=nameHook=IntPtr.Zero;}
    }
}
