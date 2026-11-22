using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AgitCon.Updates;

namespace AgitCon {
    internal sealed class AppUpdateForm:Form {
        internal static void AddTab(Form settings,Action exit){
            var tabs=System.Linq.Enumerable.Single(System.Linq.Enumerable.OfType<TabControl>(settings.Controls));var page=new TabPage("업데이트"){BackColor=Ui.Paper};tabs.TabPages.Add(page);
            var text=Ui.Label("현재 버전 "+Current+"\nGitHub 정식 릴리스를 확인하고 업데이트해요.\n설정·팩·모델은 유지해요.",11);text.Dock=DockStyle.Top;text.Height=100;
            var button=Ui.Button("업데이트 확인",()=>{using(var window=new AppUpdateForm(exit))window.ShowDialog(settings);});button.Dock=DockStyle.Top;button.Height=44;page.Padding=new Padding(22);page.Controls.Add(button);page.Controls.Add(text);
        }
        internal static string Current {get{return typeof(MainForm).Assembly.GetName().Version.ToString(3);}}
        readonly Label status=Ui.Label("",10);readonly UiButton check,install;readonly Action exit;
        CancellationTokenSource operation;ReleaseInfo release;ReleaseAsset asset;bool handingOff;
        internal AppUpdateForm(Action exit){
            this.exit=exit;Text="아지트콘 · 업데이트";Icon=Ui.AppIcon();Font=Ui.Regular(10);BackColor=Ui.Paper;ClientSize=new Size(500,320);MinimumSize=new Size(460,300);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=5};foreach(int h in new[]{45,38})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));Controls.Add(layout);
            var heading=Ui.Label("아지트콘 업데이트",17,true);heading.Dock=DockStyle.Fill;layout.Controls.Add(heading);var version=Ui.Label("현재 버전 "+Current,10);version.Dock=DockStyle.Fill;layout.Controls.Add(version);status.Dock=DockStyle.Fill;status.Text="GitHub 정식 릴리스에서 새 버전을 확인해요.\n설정·팩·모델·캐시는 유지해요.";layout.Controls.Add(status);
            check=Ui.Button("업데이트 확인",async()=>await Check());check.Dock=DockStyle.Fill;layout.Controls.Add(check);install=Ui.Button("다운로드 및 업데이트",async()=>await Install(),true);install.Dock=DockStyle.Fill;install.Enabled=false;layout.Controls.Add(install);
            Shown+=async(s,e)=>await Check();FormClosing+=(s,e)=>{if(operation!=null&&!handingOff){operation.Cancel();e.Cancel=true;status.Text="요청을 취소하고 있어요…";}};
        }
        async Task Check(){if(operation!=null)return;operation=new CancellationTokenSource();check.Enabled=install.Enabled=false;status.Text="GitHub에서 새 버전을 확인하고 있어요…";
            try{release=await UpdateCore.Check(operation.Token);asset=release==null?null:UpdateCore.Select(release,Current);status.Text=release==null?"아직 공개된 릴리스가 없어요.":asset==null?"최신 버전을 사용하고 있어요. ("+Current+")":"새 버전 "+release.Tag+"이 있어요.\n업데이트를 누르면 다운로드·검증 후 앱을 재시작해요.";}
            catch(Exception ex){asset=null;status.Text=operation.IsCancellationRequested?"확인을 취소했어요.":ex.Message;}finally{operation.Dispose();operation=null;check.Enabled=true;install.Enabled=asset!=null;}
        }
        async Task Install(){if(operation!=null||asset==null)return;operation=new CancellationTokenSource();check.Enabled=install.Enabled=false;
            try{
                string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AgitCon","updates",release.Tag.TrimStart('v'),Guid.NewGuid().ToString("N"));
                string package=await UpdateCore.Download(asset,folder,operation.Token,new Progress<string>(text=>{if(!IsDisposed)status.Text=text;}));operation.Token.ThrowIfCancellationRequested();status.Text="배포 파일을 확인하고 있어요…";
                string stage=Path.Combine(folder,"verified");await Task.Run(()=>UpdateCore.Extract(package,stage,release.Tag),operation.Token);operation.Token.ThrowIfCancellationRequested();
                var plan=new UpdatePlan{Root=AppDomain.CurrentDomain.BaseDirectory,Package=package,Sha256=asset.Digest.Substring(7).ToLowerInvariant(),Version=release.Tag.TrimStart('v'),Process=Process.GetCurrentProcess().Id,Started=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks};
                string planPath=Path.Combine(folder,"install.json");UpdateCore.Write(planPath,plan);
                string updater=Path.Combine(stage,"AgitCon.Updater.exe");var start=new ProcessStartInfo(updater,"--apply \""+planPath+"\""){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=folder};
                if(Process.Start(start)==null)throw new IOException("업데이트 도우미를 실행하지 못했어요.");handingOff=true;Close();exit();
            }catch(Exception ex){status.Text=operation.IsCancellationRequested?"다운로드를 취소했어요. 기존 앱은 그대로예요.":ex.Message;}
            finally{operation.Dispose();operation=null;if(!IsDisposed){check.Enabled=true;install.Enabled=asset!=null;}}
        }
    }
}
