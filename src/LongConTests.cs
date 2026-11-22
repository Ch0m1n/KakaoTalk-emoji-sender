using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgitCon {
    internal static class LongConTests {
        internal static void Run(){int checks=0;Action<bool> check=b=>{if(!b)throw new Exception("Long flow check "+checks);checks++;};
            try{Task.Run(async()=>{
                for(int count=2;count<=20;count++){int pastes=0,enters=0,dismissed=0;var proof=new object();var c=new LongFlowChecks{Current=()=>true,Paste=(i,t)=>{check(i==pastes++);return Task.FromResult(0);},Preview=t=>Task.FromResult(proof),Enter=p=>{check(p==proof);enters++;return true;},Dismissed=(p,t)=>{dismissed++;return Task.FromResult(true);}};var result=await LongFlow.Run(count,c,CancellationToken.None);check(result.Complete&&result.Requested==count&&pastes==count&&enters==count&&dismissed==count);}
                for(int stage=0;stage<4;stage++){int sent=0,pasted=0;var c=new LongFlowChecks{Current=()=>stage!=0,Paste=(i,t)=>{pasted++;return Task.FromResult(0);},Preview=t=>Task.FromResult(stage==1?null:new object()),Enter=p=>{sent++;return stage!=2;},Dismissed=(p,t)=>Task.FromResult(stage!=3)};var result=await LongFlow.Run(3,c,CancellationToken.None);check(!result.Complete&&sent<2&&pasted<2);check(stage!=0||pasted==0);check(stage!=1||sent==0);}
                using(var cancel=new CancellationTokenSource()){int sent=0;var c=new LongFlowChecks{Current=()=>true,Paste=(i,t)=>Task.FromResult(0),Preview=t=>{cancel.Cancel();return Task.FromResult(new object());},Enter=p=>{sent++;return true;},Dismissed=(p,t)=>Task.FromResult(true)};bool canceled=false;try{await LongFlow.Run(2,c,cancel.Token);}catch(OperationCanceledException){canceled=true;}check(canceled&&sent==0);}
                int currentCalls=0,entered=0;var race=new LongFlowChecks{Current=()=>++currentCalls<3,Paste=(i,t)=>Task.FromResult(0),Preview=t=>Task.FromResult(new object()),Enter=p=>{entered++;return true;},Dismissed=(p,t)=>Task.FromResult(true)};check(!(await LongFlow.Run(2,race,CancellationToken.None)).Complete&&entered==0);
            }).GetAwaiter().GetResult();
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-work","long-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string image=Path.Combine(folder,"piece.png");using(var bitmap=new Bitmap(80,80)){using(var g=Graphics.FromImage(bitmap))g.Clear(Color.Red);bitmap.Save(image);}
                using(var ready=LongPrepared.Create(new[]{new Entry{Path=image},new Entry{Path=image}},CancellationToken.None)){check(ready.Parts.Count==2);check(((Bitmap)ready.Parts[1].Data.GetData(DataFormats.Bitmap)).GetPixel(20,20).R==255);}
                string previous=Catalog.DataPath;try{Catalog.DataPath=Path.Combine(folder,"catalog.json");var settings=new Settings{UiScalePercent=175,FloatingPalette=false};settings.LongCons.Add(new LongPreset{Name="기린 롱콘",Keys=new List<string>{"head","body","body","tail"}});Catalog.Save(settings);var restored=Catalog.Load();check(restored.UiScalePercent==175&&restored.FloatingPalette==false&&restored.LongCons[0].Keys.SequenceEqual(new[]{"head","body","body","tail"}));}finally{Catalog.DataPath=previous;}
                PreviewPaste.TestLongNative(check);
                using(var form=new LongConForm(new List<Entry>{new Entry{Path=image,DisplayName="머리"},new Entry{Path=image,DisplayName="몸통"},new Entry{Path=image,DisplayName="꼬리"}},new List<LongPreset>(),key=>null,()=>{},(token,progress)=>Task.FromResult(0))){
                    form.Target("자체 화면 검증 · 실제 전송 없음");var layout=form.Controls[0];form.Controls.Remove(layout);
                    using(var surface=new UserControl{Size=form.ClientSize,BackColor=Ui.Paper,Font=form.Font}){surface.Controls.Add(layout);surface.CreateControl();surface.PerformLayout();for(int i=0;i<60;i++){Application.DoEvents();Thread.Sleep(10);}using(var bitmap=new Bitmap(surface.Width,surface.Height)){surface.DrawToBitmap(bitmap,surface.ClientRectangle);bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"longcon-preview.png"));}}
                }
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"longcon-test.txt"),"PASS: "+checks+" ordered 2~20-piece, cancellation/context/unknown-preview/no-retry, prebuilt payload and own-window-only targeted Enter checks. No KakaoTalk, global keyboard input or system clipboard interaction.");
            }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"longcon-test.txt"),"FAIL: "+ex);Environment.ExitCode=1;}
        }
    }
    public sealed partial class PreviewPaste {
        sealed class LongFixture:Form {protected override bool ShowWithoutActivation{get{return true;}}}
        internal static void TestLongNative(Action<bool> check){
            using(var monitor=new PreviewPaste(false))using(var room=new LongFixture{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),Size=new Size(400,500)})using(var dialog=new LongFixture{Text="클립보드 이미지 전송",ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),Size=new Size(300,400)}){
                int sent=0,roomKeys=0;var draft=new TextBox{Text="보존할 작성 문장",Dock=DockStyle.Bottom};draft.KeyDown+=(s,e)=>roomKeys++;room.Controls.Add(draft);
                var caption=new TextBox{Dock=DockStyle.Top};var send=new Button{Text="전송",Dock=DockStyle.Bottom};dialog.Controls.Add(caption);dialog.Controls.Add(send);dialog.AcceptButton=send;send.Click+=(s,e)=>{sent++;dialog.Close();};room.Show();dialog.Show(room);Application.DoEvents();
                check(monitor.LongStructure(dialog.Handle));caption.Text="caption changed";check(!monitor.LongStructure(dialog.Handle));caption.Clear();
                var extra=new Button{Text="전송",Dock=DockStyle.Top};dialog.Controls.Add(extra);Application.DoEvents();check(!monitor.LongStructure(dialog.Handle));extra.Dispose();
                check(monitor.LongStructure(dialog.Handle));check(LongEnterToWindow(dialog.Handle));var clock=System.Diagnostics.Stopwatch.StartNew();while(clock.ElapsedMilliseconds<500&&sent==0){Application.DoEvents();Thread.Sleep(1);}check(sent==1&&roomKeys==0&&draft.Text=="보존할 작성 문장");
            }
        }
    }
}
