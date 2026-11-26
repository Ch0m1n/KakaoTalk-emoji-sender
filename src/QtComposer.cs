using System;
using System.Linq;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace AgitCon {
    // Qt exposes editable accessibility elements instead of native RichEdit HWNDs.
    // Read metadata and selection endpoints only, never editor values or history text.
    internal static class QtComposer {
        internal static string Identity(AutomationElement element){try{var id=element.GetRuntimeId();return id==null||id.Length==0?null:String.Join(":",id.Select(x=>x.ToString()).ToArray());}catch{return null;}}
        static bool Belongs(AutomationElement element,IntPtr window,uint process){
            try{
                for(int depth=0;element!=null&&depth<24;depth++,element=TreeWalker.ControlViewWalker.GetParent(element)){
                    var info=element.Current;if(info.ProcessId!=(int)process)return false;
                    if(info.NativeWindowHandle!=0)return Native.GetAncestor(new IntPtr(info.NativeWindowHandle),2)==window;
                }
            }catch{}return false;
        }
        internal static bool Geometry(Native.Rect chat,System.Windows.Rect r){return !r.IsEmpty&&r.Width>=(chat.Right-chat.Left)*.5&&r.Height>=18&&r.Left>=chat.Left&&r.Right<=chat.Right+2&&r.Top>chat.Top+24&&r.Bottom<=chat.Bottom+2&&r.Bottom>=chat.Bottom-Math.Max(160,(chat.Bottom-chat.Top)/4);}
        internal static int TestGeometry(){
            int count=0;Action<bool> check=value=>{if(!value)throw new Exception("Qt composer geometry check "+count);count++;};
            foreach(double scale in new[]{1.0,1.5,2.0,3.0})foreach(int origin in new[]{0,-4000}){
                var chat=new Native.Rect{Left=origin,Top=100,Right=origin+(int)(400*scale),Bottom=100+(int)(700*scale)};
                check(Geometry(chat,new System.Windows.Rect(origin+10*scale,100+520*scale,380*scale,100*scale)));
                check(Geometry(chat,new System.Windows.Rect(origin+10*scale,100+150*scale,380*scale,470*scale)));
                check(!Geometry(chat,new System.Windows.Rect(origin+10*scale,100+40*scale,380*scale,30*scale)));
                check(!Geometry(chat,new System.Windows.Rect(origin+10*scale,100+520*scale,80*scale,100*scale)));
            }
            check(Native.KakaoProcessName("KakaoTalkUI"));check(Native.KakaoProcessName("KakaoTalk"));check(!Native.KakaoProcessName("KakaoTalkUI-helper"));check(!Native.KakaoProcessName("OtherApp"));return count;
        }
        static bool Candidate(AutomationElement element,IntPtr window,uint process){
            try{
                if(element==null)return false;var info=element.Current;
                if(info.ProcessId!=(int)process||info.ControlType!=ControlType.Edit||!info.IsEnabled||info.IsOffscreen||!info.IsKeyboardFocusable||info.IsPassword||!Belongs(element,window,process))return false;
                object value;if(element.TryGetCurrentPattern(ValuePattern.Pattern,out value)&&((ValuePattern)value).Current.IsReadOnly)return false;
                // A provider without a selection pattern cannot protect existing draft selections.
                if(!element.TryGetCurrentPattern(TextPattern.Pattern,out value))return false;
                Native.Rect chat;if(!DpiGeometry.GetPhysicalRect(window,out chat))return false;var r=info.BoundingRectangle;
                return Geometry(chat,r);
            }catch{return false;}
        }
        internal static AutomationElement Find(IntPtr window,uint process){
            try{
                var focused=AutomationElement.FocusedElement;if(Candidate(focused,window,process))return focused;
                var root=AutomationElement.FromHandle(window);
                var edits=root.FindAll(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit),new PropertyCondition(AutomationElement.IsEnabledProperty,true),new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty,true)));
                AutomationElement found=null;
                foreach(AutomationElement edit in edits){if(!Candidate(edit,window,process))continue;if(found!=null)return null;found=edit;}
                return found;
            }catch{return null;}
        }
        internal static bool Current(PreviewChatTarget target){return target!=null&&target.AccessibleEditor!=null&&target.AccessibleId!=null&&Identity(target.AccessibleEditor)==target.AccessibleId&&Candidate(target.AccessibleEditor,target.Window,target.ProcessId);}
        internal static bool Focused(PreviewChatTarget target){try{return Current(target)&&target.AccessibleEditor.Current.HasKeyboardFocus;}catch{return false;}}
        internal static bool Focus(PreviewChatTarget target){try{if(!Current(target))return false;target.AccessibleEditor.SetFocus();return true;}catch{return false;}}
        internal static bool NoSelection(PreviewChatTarget target,out string reason){
            reason="신형 카톡 입력창의 선택 상태를 확인하지 못했어요";
            try{
                if(!Current(target))return false;object value;if(!target.AccessibleEditor.TryGetCurrentPattern(TextPattern.Pattern,out value))return false;
                var ranges=((TextPattern)value).GetSelection();
                if(ranges.Length!=1)return false;
                if(ranges[0].CompareEndpoints(TextPatternRangeEndpoint.Start,ranges[0],TextPatternRangeEndpoint.End)!=0){reason="작성 문장에 선택 영역이 있어요";return false;}
                reason="";return true;
            }catch{return false;}
        }
    }
}
