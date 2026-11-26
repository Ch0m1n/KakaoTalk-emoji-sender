using System;using System.IO;using System.Threading;using System.Threading.Tasks;
namespace AgitCon {
 internal static class QtTransferTests {
  internal static void Run(){int count=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Qt transfer guard check "+count);count++;};string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"qt-transfer-test.txt");try{
   Func<QtTransferState> image=()=>new QtTransferState{Surface="popup",Send="send",Header="클립보드 이미지 전송",Sends=1,Edits=1,Images=1,Visible=true,Enabled=true,Empty=true};
   check(QtTransferRules.Valid(image(),QtTransferKind.Image));
   var s=image();s.Images=0;check(QtTransferRules.Valid(s,QtTransferKind.Image));s.Edits=0;check(!QtTransferRules.Valid(s,QtTransferKind.Image));
   foreach(Action<QtTransferState> change in new Action<QtTransferState>[] {v=>v.Header="일반 채팅",v=>v.Sends=2,v=>v.Send=null,v=>v.Surface=null,v=>v.Empty=false,v=>v.Edits=2,v=>v.ContainsComposer=true,v=>v.Visible=false,v=>v.Enabled=false,v=>v.Images=2}){s=image();change(s);check(!QtTransferRules.Valid(s,QtTransferKind.Image));}
   s=image();s.Header="파일 전송";s.FileMatches=s.SingleFile=true;check(QtTransferRules.Valid(s,QtTransferKind.File));s.FileMatches=false;check(!QtTransferRules.Valid(s,QtTransferKind.File));s.FileMatches=true;s.SingleFile=false;check(!QtTransferRules.Valid(s,QtTransferKind.File));
   int attempts=0,sends=0;check(QtTransferRules.InvokeOnce(ref attempts,()=>true,()=>sends++));check(!QtTransferRules.InvokeOnce(ref attempts,()=>true,()=>sends++));check(sends==1);
   attempts=0;sends=0;int reads=0;check(!QtTransferRules.InvokeOnce(ref attempts,()=>++reads==1,()=>sends++));check(!QtTransferRules.InvokeOnce(ref attempts,()=>true,()=>sends++));check(sends==0);
   attempts=0;sends=0;check(!QtTransferRules.InvokeOnce(ref attempts,()=>true,()=>{sends++;throw new Exception("unknown outcome");}));check(!QtTransferRules.InvokeOnce(ref attempts,()=>true,()=>sends++));check(sends==1);
   attempts=0;check(!QtTransferRules.InvokeOnce(ref attempts,()=>false,()=>{throw new Exception("Must not send");}));
   attempts=0;check(!QtTransferRules.InvokeOnce(ref attempts,()=>{throw new Exception("lost UI element");},()=>{throw new Exception("Must not send");}));
   check(QtTransferRules.FileLabel("sample.gif, 120KB","sample.gif"));check(QtTransferRules.FileLabel("sample.gif 120KB","sample.gif"));check(!QtTransferRules.FileLabel("sample.gif.exe","sample.gif"));check(!QtTransferRules.FileLabel("other.gif, 120KB","sample.gif"));
   foreach(int size in new[]{1,2,3,8})check(QtTransferRules.Shim(true,true,true,"",size,size));check(!QtTransferRules.Shim(true,true,true,"",400,300));check(!QtTransferRules.Shim(false,true,true,"",1,1));check(!QtTransferRules.Shim(true,false,true,"",1,1));check(!QtTransferRules.Shim(true,true,false,"",1,1));
   var budget=new QtObservationBudget();var clock=new DateTime(2026,1,1);int focused=0,deep=0;for(int ms=0;ms<10000;ms+=100){bool tree;if(budget.Begin("same-room",clock.AddMilliseconds(ms),out tree)){focused++;if(tree)deep++;}}check(focused<=14&&deep<=4);bool rescan;budget.FocusChanged();check(budget.Begin("same-room",clock.AddMilliseconds(10001),out rescan)&&!rescan);check(budget.Begin("new-room",clock.AddMilliseconds(10002),out rescan)&&rescan);check(ChatFloating.ChatTitle("나와의 채팅"));check(!ChatFloating.ChatTitle("클립보드 이미지 전송"));check(!ChatFloating.ChatTitle("파일 전송"));
   Task.Run(async()=>{
    foreach(int parts in new[]{2,4,9,20}){int pasted=0,confirmed=0,closed=0;int[] used=new int[parts];
     var flow=new LongFlowChecks{Current=()=>true,Paste=(n,t)=>{pasted++;return Task.FromResult(0);},Preview=t=>Task.FromResult((object)(pasted-1)),Enter=p=>QtTransferRules.InvokeOnce(ref used[(int)p],()=>true,()=>confirmed++),Dismissed=(p,t)=>{closed++;return Task.FromResult(true);}};
     var result=await LongFlow.Run(parts,flow,CancellationToken.None);check(result.Complete&&result.Requested==parts&&pasted==parts&&confirmed==parts&&closed==parts);
    }
    int entered=0,seen=0;bool current=true;
    var stop=new LongFlowChecks{Current=()=>current,Paste=(n,t)=>Task.FromResult(0),Preview=t=>Task.FromResult(++seen==2?null:(object)"confirmed popup"),Enter=p=>{entered++;return true;},Dismissed=(p,t)=>Task.FromResult(true)};
    var incomplete=await LongFlow.Run(4,stop,CancellationToken.None);check(!incomplete.Complete&&incomplete.Requested==1&&incomplete.Pasted==2&&entered==1);
    entered=0;seen=0;current=true;stop.Preview=t=>{current=false;return Task.FromResult((object)"stale popup");};incomplete=await LongFlow.Run(4,stop,CancellationToken.None);check(!incomplete.Complete&&entered==0);
   }).GetAwaiter().GetResult();
   check(QtTransferRules.Header("클립보드 이미지 전송",QtTransferKind.Image));check(!QtTransferRules.Header("파일 전송",QtTransferKind.Image));check(QtTransferRules.Send("1개 전송",QtTransferKind.File));check(!QtTransferRules.Send("2개 전송",QtTransferKind.File));
   File.WriteAllText(report,"PASS: "+count+" Qt image/file popup proof, original-composer exclusion, empty-caption/duplicate/foreign/stale guards, exact-file/count matching, at-most-once uncertain invoke, 2/4/9/20-piece ordered completion and stop/no-retry checks. Fake metadata/actions only. No live Kakao, UIA inspection, clipboard, keyboard or send.");
  }catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}}
 }
}
