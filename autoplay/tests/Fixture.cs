
using System;using System.IO;using System.Drawing;using System.Diagnostics;using System.Runtime.InteropServices;using System.Windows.Forms;
class Fixture {
[DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr LoadLibrary(string name);
[DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateWindowEx(int e,string c,string n,int s,int x,int y,int w,int h,IntPtr p,IntPtr m,IntPtr i,IntPtr q);
[STAThread]static void Main(string[] args){using(var image=new Bitmap(24,24))using(var graphics=Graphics.FromImage(image))graphics.Clear(Color.White);LoadLibrary("msimg32.dll");var hwnd=CreateWindowEx(0,"STATIC","AgitCon offline agent fixture",0,0,0,100,100,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);File.WriteAllText(args[0]+".tmp","{\"pid\":"+Process.GetCurrentProcess().Id+",\"hwnd\":"+hwnd.ToInt64()+"}");File.Move(args[0]+".tmp",args[0]);var timer=new Timer{Interval=60000};timer.Tick+=(s,e)=>Application.Exit();timer.Start();Application.Run();}
}