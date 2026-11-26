using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

namespace AgitCon {
    [DataContract] public sealed class FloatingOffset {
        [DataMember] public int X;
        [DataMember] public int Y;
        public static FloatingOffset From(Rectangle input,Point position){return new FloatingOffset{X=position.X-input.Left,Y=position.Y-input.Bottom};}
        public Rectangle Desired(Rectangle input,Size size){return new Rectangle(input.Left+X,input.Bottom+Y,size.Width,size.Height);}
    }
    public sealed class ChatAnchor {
        public IntPtr Window,Editor;public uint ProcessId;public string Title;
        public Rectangle ChatRect,InputRect;
        public string Key {get{return ProcessId+":"+Window.ToInt64()+":"+Editor.ToInt64()+":"+Title;}}
    }
    public static class ChatFloating {
        sealed class FloatingFixture:Form {protected override bool ShowWithoutActivation {get{return true;}}}
        delegate bool ChildWindow(IntPtr h,IntPtr param);
        [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr parent,ChildWindow callback,IntPtr param);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder text,int count);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr h,int index);
        internal static bool ChatTitle(string title){return !String.IsNullOrWhiteSpace(title)&&Array.IndexOf(new[]{"카카오톡","KakaoTalk","열기","Open","파일 전송","설정","Settings","환경설정","클립보드 이미지 전송","이미지 전송","사진 전송"},title)<0;}
        public static Rectangle Rect(IntPtr h){Native.Rect r;return Native.GetWindowRect(h,out r)?Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom):Rectangle.Empty;}
        public static ChatAnchor Find(IntPtr window,bool requireKakao=true){if(window==IntPtr.Zero||!Native.IsWindow(window)||Native.IsIconic(window)||!IsWindowVisible(window)||!IsWindowEnabled(window)||(requireKakao&&!Native.IsKakao(window)))return null;
            string title=Native.Title(window);if(String.IsNullOrWhiteSpace(title)||title=="카카오톡")return null;Rectangle chat=Rect(window);if(chat.Width<180||chat.Height<180)return null;
            var editors=new List<IntPtr>();ChildWindow find=(h,p)=>{var name=new StringBuilder(128);GetClassName(h,name,name.Capacity);string cls=name.ToString();bool rich=cls.StartsWith("RichEdit",StringComparison.OrdinalIgnoreCase)||(!requireKakao&&cls.StartsWith("WindowsForms10.RichEdit",StringComparison.OrdinalIgnoreCase));if(!rich||!IsWindowVisible(h)||!IsWindowEnabled(h)||(GetWindowLong(h,-16)&0x800)!=0)return true;
                Rectangle input=Rect(h);if(input.Width>=chat.Width/2&&input.Height>=24&&input.Bottom>=chat.Bottom-Math.Max(120,chat.Height/4)&&input.Bottom<=chat.Bottom&&input.Top>chat.Top+24&&Native.GetAncestor(h,2)==window)editors.Add(h);return true;};EnumChildWindows(window,find,IntPtr.Zero);if(editors.Count!=1)return null;uint pid;Native.GetWindowThreadProcessId(window,out pid);return new ChatAnchor{Window=window,Editor=editors[0],ProcessId=pid,Title=title,ChatRect=chat,InputRect=Rect(editors[0])};
        }
        // Position-only fallback for compositors without an identifiable native RichEdit.
        // Editor stays zero; a separate verified composer is required for attachment.
        public static ChatAnchor FindPalette(IntPtr window,bool requireKakao=true){
            var editor=Find(window,requireKakao);if(editor!=null)return editor;
            if(window==IntPtr.Zero||!Native.IsWindow(window)||Native.IsIconic(window)||!IsWindowVisible(window)||!IsWindowEnabled(window)||(requireKakao&&!Native.IsKakao(window))||Native.GetAncestor(window,2)!=window)return null;
            string title=Native.Title(window);var cls=new StringBuilder(128);GetClassName(window,cls,cls.Capacity);
            if(cls.ToString()=="#32770"||String.IsNullOrWhiteSpace(title)||title=="카카오톡"||title=="KakaoTalk"||title=="열기"||title=="파일 전송"||title=="설정"||title=="환경설정"||title=="클립보드 이미지 전송")return null;
            var chat=Rect(window);if(chat.Width<180||chat.Height<180)return null;
            uint pid;Native.GetWindowThreadProcessId(window,out pid);
            return new ChatAnchor{Window=window,Editor=IntPtr.Zero,ProcessId=pid,Title=title,ChatRect=chat,InputRect=new Rectangle(chat.Left,chat.Bottom-1,chat.Width,1)};
        }
        // Disabled by an attachment dialog is still displayed; minimized/hidden is not.
        public static bool DisplayAvailable(ChatAnchor anchor){if(anchor==null||!Native.IsWindow(anchor.Window)||Native.IsIconic(anchor.Window)||!IsWindowVisible(anchor.Window))return false;uint pid;Native.GetWindowThreadProcessId(anchor.Window,out pid);return pid==anchor.ProcessId&&Native.Title(anchor.Window)==anchor.Title;}
        public static bool Current(ChatAnchor anchor,bool requireKakao=true){if(!DisplayAvailable(anchor)||!IsWindowEnabled(anchor.Window)||(requireKakao&&!Native.IsKakao(anchor.Window)))return false;return anchor.Editor==IntPtr.Zero?FindPalette(anchor.Window,requireKakao)!=null:Native.IsWindow(anchor.Editor)&&Native.GetAncestor(anchor.Editor,2)==anchor.Window;}
        public static Rectangle Place(Rectangle chat,Rectangle input,Size palette,Rectangle work){int width=Math.Min(palette.Width,work.Width),height=Math.Min(palette.Height,work.Height);int x=chat.Right+8;if(x+width>work.Right)x=chat.Left-width-8;if(x<work.Left)x=Math.Max(work.Left,Math.Min(chat.Right+8,work.Right-width));int y=input.Bottom-height;return new Rectangle(Math.Max(work.Left,Math.Min(x,work.Right-width)),Math.Max(work.Top,Math.Min(y,work.Bottom-height)),width,height);}
        public static Rectangle PlaceCustom(Rectangle input,Size palette,FloatingOffset offset,Rectangle work){var desired=offset.Desired(input,palette);int width=Math.Min(palette.Width,work.Width),height=Math.Min(palette.Height,work.Height);return new Rectangle(Math.Max(work.Left,Math.Min(desired.Left,work.Right-width)),Math.Max(work.Top,Math.Min(desired.Top,work.Bottom-height)),width,height);}
        // Pure geometry/settings checks: never create a window or inspect the desktop.
        public static void TestPosition(){string report=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"floating-position-test.txt");try{int count=0;Action<bool> check=value=>{if(!value)throw new Exception("Custom floating position check "+count);count++;};var input=new Rectangle(110,600,380,100);var size=new Size(340,480);var work=new Rectangle(0,0,1920,1040);var offset=FloatingOffset.From(input,new Point(550,220));check(offset.X==440&&offset.Y==-480);check(PlaceCustom(input,size,offset,work)==new Rectangle(550,220,340,480));var moved=input;moved.Offset(200,100);check(PlaceCustom(moved,size,offset,work)==new Rectangle(750,320,340,480));var resized=input;resized.Height+=60;check(PlaceCustom(resized,size,offset,work).Top==280);var negative=FloatingOffset.From(input,new Point(-800,120));check(PlaceCustom(input,size,negative,new Rectangle(-1920,0,1920,1040)).Left==-800);var outside=new FloatingOffset{X=3000,Y=-3000};check(PlaceCustom(input,size,outside,work)==new Rectangle(1580,0,340,480));check(outside.X==3000&&outside.Y==-3000);check(PlaceCustom(input,size,offset,new Rectangle(0,0,300,300))==new Rectangle(0,0,300,300));using(var stream=new System.IO.MemoryStream()){var serializer=new DataContractJsonSerializer(typeof(Settings));serializer.WriteObject(stream,new Settings{FloatingPosition=offset});stream.Position=0;var saved=(Settings)serializer.ReadObject(stream);check(saved.FloatingPosition.X==440&&saved.FloatingPosition.Y==-480);}using(var stream=new System.IO.MemoryStream(Encoding.UTF8.GetBytes("{}"))){var old=(Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);check(old.FloatingPosition==null);}System.IO.File.WriteAllText(report,"PASS: "+count+" custom position/persistence checks. No windows, desktop inspection, clipboard, or key injection.\r\n");}catch(Exception ex){System.IO.File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}}
        public static void Test(){string report=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"floating-test.txt");try{int count=0;Action<bool> check=value=>{if(!value)throw new Exception("Floating placement or editor identity check "+count);count++;};var work=new Rectangle(0,0,1920,1040);var chat=new Rectangle(100,100,400,650);var input=new Rectangle(110,600,380,100);var a=Place(chat,input,new Size(340,480),work);check(a.Left==508&&a.Bottom==input.Bottom);var right=Place(new Rectangle(1600,100,320,650),new Rectangle(1610,600,300,100),new Size(340,480),work);check(right.Right==1592);var negative=Place(new Rectangle(-1800,100,400,650),new Rectangle(-1790,600,380,100),new Size(340,480),new Rectangle(-1920,0,1920,1040));check(negative.Left==-1392);var tiny=Place(chat,input,new Size(340,480),new Rectangle(0,0,300,300));check(tiny==new Rectangle(0,0,300,300));check(a.Top>=work.Top&&a.Bottom<=work.Bottom);
                using(var form=new FloatingFixture{Text="플로팅 입력창 검사",ClientSize=new Size(380,600),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}){var history=new RichTextBox{ReadOnly=true,Bounds=new Rectangle(10,10,360,420)};var editor=new RichTextBox{Bounds=new Rectangle(10,450,360,90)};form.Controls.Add(history);form.Controls.Add(editor);form.Show();Application.DoEvents();var found=Find(form.Handle,false);check(found!=null&&found.Editor==editor.Handle);check(found.Key.Contains(editor.Handle.ToInt64().ToString()));check(DisplayAvailable(found));form.Hide();Application.DoEvents();check(!DisplayAvailable(found));form.Show();Application.DoEvents();form.Enabled=false;check(DisplayAvailable(found)&&!Current(found,false));form.Enabled=true;check(DisplayAvailable(found));editor.ReadOnly=true;check(Find(form.Handle,false)==null);var display=FindPalette(form.Handle,false);check(display!=null&&display.Editor==IntPtr.Zero&&Current(display,false));string caption=form.Text;form.Text="설정";check(FindPalette(form.Handle,false)==null&&!Current(display,false));form.Text=caption;editor.ReadOnly=false;editor.Bounds=new Rectangle(10,120,360,420);check(Find(form.Handle,false)!=null&&Find(form.Handle,false).Editor==editor.Handle);editor.Bounds=new Rectangle(10,450,360,90);using(var duplicate=new RichTextBox{Bounds=new Rectangle(10,450,360,90)}){form.Controls.Add(duplicate);duplicate.BringToFront();Application.DoEvents();check(Find(form.Handle,false)==null);}}
                System.IO.File.WriteAllText(report,"PASS: "+count+" floating placement/editor checks. No real KakaoTalk or input injection.\r\n");}catch(Exception ex){System.IO.File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}}
    }
}
