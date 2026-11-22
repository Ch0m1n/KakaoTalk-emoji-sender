using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using AgitCon.Updates;

static class AgitConUpdater {
    [STAThread]static void Main(string[] args){
        string result=null;UpdatePlan plan=null;bool parentExited=false;
        try{
            if(args.Length!=2||args[0]!="--apply")throw new ArgumentException("업데이트 요청 파일이 필요해요.");
            string planPath=Path.GetFullPath(args[1]);result=Path.Combine(Path.GetDirectoryName(planPath),"result.txt");
            using(var input=File.OpenRead(planPath))plan=UpdateCore.Read<UpdatePlan>(input);
            if(plan.Process<=0||plan.Started<=0||!Path.IsPathRooted(plan.Root)||!Path.IsPathRooted(plan.Package)||!System.Text.RegularExpressions.Regex.IsMatch(plan.Sha256??"",@"^[0-9a-f]{64}$"))throw new InvalidDataException("업데이트 요청 내용이 올바르지 않아요.");
            UpdateCore.ParseVersion(plan.Version);string expected=Path.GetFullPath(Path.Combine(plan.Root,"AgitCon.exe"));
            try{using(var process=Process.GetProcessById(plan.Process)){
                if(process.StartTime.ToUniversalTime().Ticks!=plan.Started||!String.Equals(Path.GetFullPath(process.MainModule.FileName),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("종료할 앱의 신원이 달라졌어요.");
                if(!process.WaitForExit(30000))throw new IOException("아지트콘이 아직 종료 중이에요. 앱을 완전히 종료한 뒤 다시 업데이트해주세요.");
            }}catch(ArgumentException){}
            parentExited=true;string stage=Path.Combine(Path.GetDirectoryName(planPath),"install-"+Guid.NewGuid().ToString("N"));
            UpdateCore.Apply(plan,stage,exe=>{
                var process=Process.Start(new ProcessStartInfo(exe){UseShellExecute=false,WorkingDirectory=plan.Root});
                if(process==null)throw new IOException("새 버전을 실행하지 못했어요.");
                if(process.WaitForExit(900))throw new IOException("새 버전이 시작 직후 종료됐어요.");
            });
            File.WriteAllText(result,"업데이트 완료: "+plan.Version);
        }catch(Exception ex){
            if(parentExited&&plan!=null&&Object.Equals(ex.Data["RollbackComplete"],true))try{Process.Start(new ProcessStartInfo(Path.Combine(plan.Root,"AgitCon.exe")){UseShellExecute=false,WorkingDirectory=plan.Root});}catch{}
            try{if(result!=null)File.WriteAllText(result,"업데이트 실패: "+ex);}catch{}
            MessageBox.Show("업데이트를 완료하지 못했어요. 기존 파일을 복구했거나 백업에 보관했어요.\n"+ex.Message,"아지트콘 업데이트",MessageBoxButtons.OK,MessageBoxIcon.Warning);Environment.ExitCode=1;
        }
    }
}
