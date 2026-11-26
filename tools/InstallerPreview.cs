// Capture only an off-screen, test-only installer spawned by this process.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
class InstallerPreview {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Entry {public uint Size,Usage,Id;public UIntPtr Heap;public uint Module,Threads,Parent;public int Priority;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;}
    [StructLayout(LayoutKind.Sequential)]struct Rect {public int L,T,R,B;}
    delegate bool Visit(IntPtr h,IntPtr p);
    [DllImport("kernel32.dll")]static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32FirstW(IntPtr snapshot,ref Entry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32NextW(IntPtr snapshot,ref Entry entry);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")]static extern bool EnumWindows(Visit visit,IntPtr p);
    [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr root,Visit visit,IntPtr p);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder text,int length);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out Rect rect);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder value,int length);
    [DllImport("user32.dll")]static extern IntPtr SendMessageTimeout(IntPtr h,uint message,UIntPtr w,IntPtr l,uint flags,uint timeout,out UIntPtr result);
    [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
    static string Text(IntPtr h){var b=new StringBuilder(512);GetWindowText(h,b,b.Capacity);return b.ToString();}
    static HashSet<uint> Descendants(int root){var all=new List<Entry>();IntPtr h=CreateToolhelp32Snapshot(2,0);try{var e=new Entry{Size=(uint)Marshal.SizeOf(typeof(Entry))};if(Process32FirstW(h,ref e))do{all.Add(e);}while(Process32NextW(h,ref e));}finally{CloseHandle(h);}var own=new HashSet<uint>{(uint)root};for(int n=0;n<8;n++)foreach(var e in all)if(own.Contains(e.Parent))own.Add(e.Id);return own;}
    static IntPtr Window(HashSet<uint> owned){IntPtr result=IntPtr.Zero;EnumWindows((h,p)=>{uint pid;GetWindowThreadProcessId(h,out pid);if(owned.Contains(pid)&&Text(h)=="아지트콘 설치"&&IsWindowVisible(h))result=h;return true;},IntPtr.Zero);return result;}
    static IntPtr Button(IntPtr root,string caption){IntPtr result=IntPtr.Zero;EnumChildWindows(root,(h,p)=>{if(IsWindowVisible(h)&&Text(h)==caption)result=h;return true;},IntPtr.Zero);return result;}
    static void Capture(IntPtr h,string path){Rect r;if(!GetWindowRect(h,out r)||r.L>-10000||r.R-r.L<400)throw new Exception("Preview must be an owned off-screen installer");using(var bitmap=new Bitmap(r.R-r.L,r.B-r.T))using(var graphics=Graphics.FromImage(bitmap)){IntPtr dc=graphics.GetHdc();try{if(!PrintWindow(h,dc,0))throw new Exception("PrintWindow failed");}finally{graphics.ReleaseHdc(dc);}bitmap.Save(path,ImageFormat.Png);}}
    static void Advance(IntPtr h,string caption,bool keyboard){IntPtr button=Button(h,caption);if(button==IntPtr.Zero)throw new Exception("Missing accessible primary: "+caption);if(keyboard){PostMessage(h,0x100,new IntPtr(13),new IntPtr(0x1c0001));PostMessage(h,0x101,new IntPtr(13),new IntPtr(unchecked((int)0xc01c0001)));}else PostMessage(button,0xf5,IntPtr.Zero,IntPtr.Zero);Thread.Sleep(400);}
    static int Main(string[] args){Process process=null;HashSet<uint> owned=null;try{
        string exe=Path.GetFullPath(args[0]),folder=Path.GetFullPath(args[1]);
        if(exe.IndexOf("artifacts\\tests\\installer-build\\",StringComparison.OrdinalIgnoreCase)<0)throw new Exception("Test installer required");Directory.CreateDirectory(folder);
        process=Process.Start(new ProcessStartInfo(exe,"/LANG=korean /SP- /NORESTART"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
        IntPtr window=IntPtr.Zero;for(int i=0;i<200&&window==IntPtr.Zero;i++){owned=Descendants(process.Id);window=Window(owned);Thread.Sleep(100);}if(window==IntPtr.Zero)throw new Exception("Owned preview not ready");Thread.Sleep(300);
        IntPtr clip=IntPtr.Zero;EnumChildWindows(window,(h,p)=>{var name=new StringBuilder(128);GetClassName(h,name,name.Capacity);if(IsWindowVisible(h)&&name.ToString()=="SysAnimate32")clip=h;return true;},IntPtr.Zero);
        UIntPtr playing;if(clip==IntPtr.Zero||SendMessageTimeout(clip,0x468,UIntPtr.Zero,IntPtr.Zero,2,500,out playing)==IntPtr.Zero||playing==UIntPtr.Zero)throw new Exception("Original AVI is not playing in native animation control");
        string first=Path.Combine(folder,"01-welcome.png"),second=Path.Combine(folder,"01-animation-frame2.png");
        Capture(window,first);
        // Occluded/off-screen common controls may suppress timed painting. Seek a
        // different original frame to verify decode/colour; verify PLAY separately.
        UIntPtr sought;if(SendMessageTimeout(clip,0x465,new UIntPtr(1),new IntPtr(0x00050005),2,500,out sought)==IntPtr.Zero||sought==UIntPtr.Zero)throw new Exception("Original AVI frame seek failed");
        Thread.Sleep(100);Capture(window,second);
        Rect rootRect,clipRect;GetWindowRect(window,out rootRect);GetWindowRect(clip,out clipRect);
        using(var a=new Bitmap(first))using(var b=new Bitmap(second)){
            int x=clipRect.L-rootRect.L,y=clipRect.T-rootRect.T,width=clipRect.R-clipRect.L,height=clipRect.B-clipRect.T,changed=0;
            if(a.GetPixel(x,y).ToArgb()!=Color.FromArgb(192,192,192).ToArgb()||a.GetPixel(x+width-1,y).ToArgb()!=Color.FromArgb(192,192,192).ToArgb())throw new Exception("Animation background differs from classic dialog");
            for(int yy=y;yy<y+height;yy++)for(int xx=x;xx<x+width;xx++)if(a.GetPixel(xx,yy)!=b.GetPixel(xx,yy))changed++;
            if(changed<10)throw new Exception("Original AVI frame 5 matched initial frame unexpectedly");
        }
        Advance(window,"다음(&N) >",true);
        Capture(window,Path.Combine(folder,"02-location.png"));Advance(window,"다음(&N) >",false);
        Capture(window,Path.Combine(folder,"03-data.png"));Advance(window,"다음(&N) >",false);
        Capture(window,Path.Combine(folder,"04-options.png"));Advance(window,"다음(&N) >",false);
        Capture(window,Path.Combine(folder,"05-ready.png"));
        if(Button(window,"설치(&I)")==IntPtr.Zero)throw new Exception("Keyboard/click navigation did not reach Ready");
        File.WriteAllText(Path.Combine(folder,"result.txt"),"PASS: own off-screen installer only; unmodified original AVI in SysAnimate32, playing state, distinct original frame rendering and matching grey background; accessible button captions; Enter and click navigation; five page captures; no installation or Kakao interaction.");Console.WriteLine(folder);return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{if(process!=null){owned=Descendants(process.Id);foreach(uint id in owned)try{using(var child=Process.GetProcessById((int)id)){if(child.StartTime>=process.StartTime)child.Kill();}}catch{}}}}
}
