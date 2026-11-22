using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace AgitCon {
    // Synchronous scopes only: never carry a thread's DPI context across an await.
    internal static class DpiGeometry {
        static readonly uint ownProcess=(uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        internal static void OwnUi(Control control,Action action){if(!control.IsHandleCreated){action();return;}InWindowContext(control.Handle,false,()=>{action();return true;});}
        [DllImport("user32.dll")]static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
        [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("shcore.dll")]static extern int GetScaleFactorForMonitor(IntPtr monitor,out int scale);
        [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
        internal sealed class Scope:IDisposable {
            readonly IntPtr previous;readonly uint thread;bool disposed;
            internal Scope(IntPtr window,bool physical=false){thread=GetCurrentThreadId();var context=physical?new IntPtr(-4):GetWindowDpiAwarenessContext(window);if(context==IntPtr.Zero)throw new InvalidOperationException("Window DPI context unavailable");previous=SetThreadDpiAwarenessContext(context);if(previous==IntPtr.Zero&&physical)previous=SetThreadDpiAwarenessContext(new IntPtr(-3));if(previous==IntPtr.Zero)throw new InvalidOperationException("DPI scope unavailable");}
            public void Dispose(){if(disposed)return;if(thread!=GetCurrentThreadId())throw new InvalidOperationException("DPI scope crossed threads");SetThreadDpiAwarenessContext(previous);disposed=true;}
        }
        internal static T InWindowContext<T>(IntPtr window,bool physical,Func<T> action){
            Func<T> scoped=()=>{using(new Scope(window,physical))return action();};uint pid;Native.GetWindowThreadProcessId(window,out pid);
            // A foreign HWND's DPI context belongs on a worker, never the UI thread.
            // Keep own-window calls synchronous so PrintWindow cannot deadlock on
            // the caller's own window procedure while the caller waits for a worker.
            return pid!=ownProcess?Task.Run(scoped).GetAwaiter().GetResult():scoped();
        }
        internal static bool GetRect(IntPtr window,out Native.Rect rect){rect=new Native.Rect();try{var value=InWindowContext(window,false,()=>{Native.Rect found;bool ok=Native.GetWindowRect(window,out found);return Tuple.Create(ok,found);});rect=value.Item2;return value.Item1;}catch{return false;}}
        internal static bool GetPhysicalRect(IntPtr window,out Native.Rect rect){rect=new Native.Rect();try{var value=InWindowContext(window,true,()=>{Native.Rect found;bool ok=Native.GetWindowRect(window,out found);return Tuple.Create(ok,found);});rect=value.Item2;return value.Item1;}catch{return false;}}
        internal static string Stamp(IntPtr window){var monitor=MonitorFromWindow(window,2);int scale;try{if(GetScaleFactorForMonitor(monitor,out scale)!=0)scale=0;}catch{scale=0;}return monitor.ToInt64()+":"+scale+":"+ConfirmationNavigator.Dpi(window);}
        internal static string Describe(IntPtr window){Native.Rect native,physical;if(!GetRect(window,out native)||!GetPhysicalRect(window,out physical))return "window-geometry-unavailable";return "windowDpi="+ConfirmationNavigator.Dpi(window)+"; raster="+(native.Right-native.Left)+"x"+(native.Bottom-native.Top)+"; physical="+(physical.Right-physical.Left)+"x"+(physical.Bottom-physical.Top);}
        internal static bool MapPoint(Size raster,Rectangle physical,Point relative,out Point result){result=Point.Empty;if(raster.Width<=0||raster.Height<=0||physical.Width<=0||physical.Height<=0||relative.X<0||relative.Y<0||relative.X>=raster.Width||relative.Y>=raster.Height)return false;result=new Point(physical.Left+Math.Min(physical.Width-1,(int)Math.Floor((relative.X+.5)*physical.Width/raster.Width)),physical.Top+Math.Min(physical.Height-1,(int)Math.Floor((relative.Y+.5)*physical.Height/raster.Height)));return physical.Contains(result);}
        [DllImport("user32.dll")]static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")]static extern bool AreDpiAwarenessContextsEqual(IntPtr a,IntPtr b);
        sealed class FixtureForm:Form {protected override bool ShowWithoutActivation {get{return true;}}}
        internal static void Test(){string report=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dpi-geometry-test.txt");int checks=0;Action<bool> assert=value=>{if(!value)throw new Exception("DPI geometry check "+checks);checks++;};try{
            foreach(var desktop in new[]{new Size(1280,720),new Size(1920,1080),new Size(2560,1440),new Size(3840,2160),new Size(7680,4320)})foreach(float monitor in new[]{1f,1.25f,1.5f,1.75f,2f,2.25f,2.5f,3f,3.5f,4f})foreach(float native in new[]{1f,1.25f,monitor})foreach(var origin in new[]{Point.Empty,new Point(-desktop.Width,0),new Point(0,-desktop.Height),new Point(desktop.Width,0)}){
                var raster=new Size((int)(434*native),(int)(707*native));var physical=new Rectangle(origin,new Size((int)(434*monitor),(int)(707*monitor)));foreach(var relative in new[]{Point.Empty,new Point(raster.Width-1,raster.Height-1),new Point((int)(32*native),(int)(569*native))}){Point mapped;assert(MapPoint(raster,physical,relative,out mapped)&&physical.Contains(mapped));if(relative.X==(int)(32*native))assert(Math.Abs(mapped.X-(origin.X+32*monitor))<=Math.Ceiling(monitor)&&Math.Abs(mapped.Y-(origin.Y+569*monitor))<=Math.Ceiling(monitor));}Point ignored;assert(!MapPoint(raster,physical,new Point(raster.Width,0),out ignored));var crop=SidebarCrop(raster,native);assert(crop.Left==0&&crop.Bottom==raster.Height&&crop.Width<=raster.Width&&crop.Height<=raster.Height&&crop.Contains(new Point((int)(32*native),(int)(569*native))));}
            foreach(long mode in new long[]{-1,-2,-3,-4,-5}){IntPtr previous=SetThreadDpiAwarenessContext(new IntPtr(mode));if(previous==IntPtr.Zero)throw new Exception("Fixture context unsupported");IntPtr window=IntPtr.Zero;FixtureForm fixture=null;try{fixture=new FixtureForm{Text="AgitCon DPI fixture",ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,FormBorderStyle=FormBorderStyle.None,AutoScaleMode=AutoScaleMode.None,BackColor=Color.Black,Location=new Point(-30000,-30000),Size=new Size(240,160)};fixture.Show();Application.DoEvents();window=fixture.Handle;}finally{SetThreadDpiAwarenessContext(previous);}try{assert(window!=IntPtr.Zero);IntPtr before=GetThreadDpiAwarenessContext();Native.Rect raster,physical;assert(GetRect(window,out raster)&&GetPhysicalRect(window,out physical));assert(AreDpiAwarenessContextsEqual(before,GetThreadDpiAwarenessContext()));using(var bitmap=ConfirmationNavigator.Capture(window,new Rectangle(0,0,raster.Right-raster.Left,raster.Bottom-raster.Top))){assert(bitmap.Width==raster.Right-raster.Left&&bitmap.Height==raster.Bottom-raster.Top);assert(bitmap.GetPixel(bitmap.Width/2,bitmap.Height/2).R<30);}assert(AreDpiAwarenessContextsEqual(before,GetThreadDpiAwarenessContext()));}finally{if(fixture!=null)fixture.Dispose();}}
            System.IO.File.WriteAllText(report,"PASS: "+checks+" geometry/native-context cases. 720p/1080p/1440p/4K/8K, 100-400%, unaware/system/per-monitor raster mappings, negative/secondary monitor origins, bounds and native PrintWindow fixture contexts with scope restoration. Actual OS DPI/resolution were not changed; no Kakao, cursor, clipboard or Send interaction.");
            }catch(Exception ex){System.IO.File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}}
        internal static Rectangle SidebarCrop(Size window,float scale){int width=Math.Min(window.Width,(int)Math.Ceiling(66*scale)),height=Math.Min(window.Height,(int)Math.Ceiling(400*scale));return new Rectangle(0,window.Height-height,width,height);}
    }
}
