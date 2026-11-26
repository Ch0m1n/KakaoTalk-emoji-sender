"""Real Win32 controls in an offscreen, nonactivating owned fixture only."""
import json,subprocess,sys,time,os
from pathlib import Path
ARCH='x64' if '--x64' in sys.argv else 'x86'
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'artifacts/kakao-internals/deps'))
import frida
work=ROOT/'artifacts/tests/refresh-native';work.mkdir(parents=True,exist_ok=True)
source=work/'Fixture.cs';exe=work/'KakaoTalk.exe';identity=work/'identity.json';identity.unlink(missing_ok=True)
source.write_text(r'''
using System;using System.IO;using System.Drawing;using System.Diagnostics;using System.Runtime.InteropServices;using System.Windows.Forms;
class Fixture {
[DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr LoadLibrary(string name);
[DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateWindowEx(int e,string c,string n,int s,int x,int y,int w,int h,IntPtr p,IntPtr m,IntPtr i,IntPtr q);
[STAThread]static void Main(string[] args){using(var image=new Bitmap(24,24))using(var graphics=Graphics.FromImage(image))graphics.Clear(Color.White);LoadLibrary("msimg32.dll");LoadLibrary("msftedit.dll");
var root=CreateWindowEx(0x08000000,"STATIC","AgitCon offscreen repaint fixture",0x12000000,-30000,-30000,420,600,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
var view=CreateWindowEx(0,"STATIC","",0x50000000,0,0,420,400,root,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
var editor=CreateWindowEx(0,"RICHEDIT50W","",0x50000000,0,400,420,160,root,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
var send=CreateWindowEx(0,"BUTTON","fixture",0x50000000,300,560,100,30,root,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
if(root==IntPtr.Zero||editor==IntPtr.Zero)throw new Exception("Fixture HWND");
File.WriteAllText(args[0],"{\"pid\":"+Process.GetCurrentProcess().Id+",\"hwnd\":"+root.ToInt64()+",\"editor\":"+editor.ToInt64()+",\"send\":"+send.ToInt64()+"}");
var timer=new Timer{Interval=60000};timer.Tick+=(s,e)=>Application.Exit();timer.Start();Application.Run();}
}
''',encoding='utf8')
subprocess.run([str(Path(os.environ['WINDIR'])/'Microsoft.NET/Framework/v4.0.30319/csc.exe'),'/nologo','/target:winexe','/platform:'+ARCH,'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/out:'+str(exe),str(source)],check=True)
process=subprocess.Popen([str(exe),str(identity)],creationflags=0x08000000);session=None
try:
    for _ in range(100):
        if identity.exists():break
        time.sleep(.05)
    info=json.loads(identity.read_text());assert info['pid']==process.pid and process.poll() is None
    session=frida.attach(process.pid)
    base=(ROOT/'tools/kakao-bitmap-bridge.js').read_text(encoding='utf-8-sig').split('const hwnd=')[0].replace('const redraw=fn','let redraw=fn')
    code='const BRIDGE_CONFIG='+json.dumps({'hwnd':info['hwnd'],'managed':True})+';\n'+base+(ROOT/'tools/kakao-auto-bridge.js').read_text(encoding='utf-8-sig')
    code+='''
const fixtureDraws=[],fixtureRedraw=redraw;
redraw=(window,rect,region,flags)=>{const mapped=Memory.alloc(16);mapped.writeByteArray(rect.readByteArray(16));refreshMap(window,hwnd,mapped,2);fixtureDraws.push({window:window.toUInt32(),flags,top:mapped.add(4).readS32(),bottom:mapped.add(12).readS32()});return fixtureRedraw(window,rect,region,flags);};
rpc.exports.refreshfixture=()=>{refreshConversation(hwnd);return {draws:fixtureDraws,snapshot:rpc.exports.snapshot()};};
'''
    script=session.create_script(code);errors=[];script.on('message',lambda m,d:errors.append(m) if m['type']=='error' else None);script.load()
    report=script.exports_sync.refreshfixture();draws=report['draws'];assert draws,report
    assert all(d['window'] not in (info['editor'],info['send']) and d['bottom']<400 and d['flags']==0x61 for d in draws),draws
    assert not errors,errors
    (work/'result.json').write_text(json.dumps(report,indent=2),encoding='utf8')
    script.exports_sync.stop();script.unload();print('Native Win32 repaint: conversation requests only; real RichEdit and Send HWNDs/regions excluded')
finally:
    if session:session.detach()
    if process.poll() is None:process.terminate()
    process.wait(timeout=5)
