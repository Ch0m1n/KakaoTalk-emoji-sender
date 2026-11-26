using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgitCon {
    internal static class CacheMaintenance {
        internal const int Thumbnails=1,Temporary=2,Downloaded=4;
        static string Data(string root){return Path.Combine(Path.GetFullPath(root),"data");}
        static string Pending(string root){return Path.Combine(Data(root),"cache-cleanup.pending");}
        static bool Plain(string path){return Directory.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0;}
        static IEnumerable<string> Roots(string root,int flags){
            string data=Data(root);if(!Plain(data))yield break;
            string images=Path.Combine(data,"images");
            if((flags&Downloaded)!=0){if(Plain(images))yield return images;}
            else if((flags&Thumbnails)!=0&&Plain(images)){string thumbs=Path.Combine(images,"thumbs-v1");if(Plain(thumbs))yield return thumbs;}
            if((flags&Temporary)!=0){string outbox=Path.Combine(data,"outbox");if(Plain(outbox))yield return outbox;}
        }
        internal static void Schedule(string root,int flags){
            if(flags<1||flags>7)throw new ArgumentException("삭제할 캐시를 선택해주세요.");
            string data=Data(root);Directory.CreateDirectory(data);if(!Plain(data))throw new IOException("연결된 폴더에는 삭제를 예약하지 않아요.");
            if(File.Exists(Pending(root))&&(File.GetAttributes(Pending(root))&FileAttributes.ReparsePoint)!=0)throw new IOException("연결된 파일에는 예약하지 않아요.");
            File.WriteAllText(Pending(root),flags.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        static void Walk(string path,HashSet<string> keep,bool delete,ref long bytes,ref int files,ref int failures){
            if(!Plain(path))return;
            try{
                foreach(string file in Directory.GetFiles(path)){
                    try{if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0||keep.Contains(Path.GetFullPath(file)))continue;long size=new FileInfo(file).Length;if(delete)File.Delete(file);bytes+=size;files++;}
                    catch(IOException){failures++;}catch(UnauthorizedAccessException){failures++;}
                }
                foreach(string child in Directory.GetDirectories(path)){
                    if(!Plain(child))continue;Walk(child,keep,delete,ref bytes,ref files,ref failures);
                    if(delete)try{if(!Directory.EnumerateFileSystemEntries(child).Any())Directory.Delete(child);}catch(IOException){failures++;}catch(UnauthorizedAccessException){failures++;}
                }
            }catch(IOException){failures++;}catch(UnauthorizedAccessException){failures++;}
        }
        static HashSet<string> Protected(Settings settings){
            var keep=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in settings.Entries.Where(e=>String.IsNullOrWhiteSpace(e.SourceUrl)))foreach(string path in new[]{entry.Path,entry.OriginalPath})if(!String.IsNullOrWhiteSpace(path))try{keep.Add(Path.GetFullPath(path));}catch(ArgumentException){}
            return keep;
        }
        internal static string Process(string root,int flags,Settings settings,bool delete){
            long bytes=0;int files=0,failures=0;var keep=Protected(settings);
            foreach(string folder in Roots(root,flags))Walk(folder,keep,delete,ref bytes,ref files,ref failures);
            return files+"개 · "+(bytes/1048576.0).ToString("0.0")+" MB"+(failures>0?" · 처리하지 못한 항목 "+failures+"개":"");
        }
        internal static void ApplyPending(string root){
            if(!Plain(Data(root)))return;string pending=Pending(root);if(!File.Exists(pending))return;
            try{
                if(!Plain(Data(root))||(File.GetAttributes(pending)&FileAttributes.ReparsePoint)!=0)throw new IOException("Unsafe cache request");
                int flags;if(!Int32.TryParse(File.ReadAllText(pending),out flags)||flags<1||flags>7)throw new InvalidDataException("Invalid cache request");
                string result=Process(root,flags,Catalog.Load(),true);Log(root,"삭제 결과: "+result);File.Delete(pending);
            }catch(Exception ex){Log(root,"캐시 정리를 완료하지 못했어요: "+ex.GetType().Name);}
        }
        static void Log(string root,string text){try{string path=Path.Combine(Data(root),"cache-cleanup-last.txt");if(Plain(Data(root))&&(!File.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0))File.WriteAllText(path,text);}catch(IOException){}catch(UnauthorizedAccessException){}}
        internal static void AddTab(Form settings,Action<int> schedule){
            var tabs=settings.Controls.OfType<TabControl>().Single();var page=new TabPage("캐시"){BackColor=Ui.Paper};tabs.TabPages.Add(page);
            var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};page.Controls.Add(panel);
            var title=Ui.Label("저장된 이미지 정리",17,true);title.Size=new Size(470,42);panel.Controls.Add(title);
            var description=Ui.Label("사용 중인 파일과 충돌하지 않도록 다음 앱 실행 때 삭제해요.\n팩 목록·별칭·모델·외부 로컬 폴더는 유지해요.\n첨부 중인 파일은 먼저 전송하거나 취소해주세요.",9);description.Size=new Size(470,74);panel.Controls.Add(description);
            var thumbs=new CheckBox{Text="저장된 썸네일",Checked=true,AutoSize=true};var temp=new CheckBox{Text="전송용 축소·9분할·합성 임시 이미지",Checked=true,AutoSize=true};var originals=new CheckBox{Text="다운로드한 콘 이미지도 삭제",AutoSize=true};panel.Controls.Add(thumbs);panel.Controls.Add(temp);panel.Controls.Add(originals);
            var note=Ui.Label("다운로드 이미지는 다시 볼 때 받아요. 다운로드 주소가 없는\n이미지는 보존해요. 원본 삭제 예약 시 미리 불러오기와\n팩 자동 분석을 꺼서 바로 다시 받지 않게 해요.",9);note.Size=new Size(470,76);note.ForeColor=Ui.Muted;panel.Controls.Add(note);
            var status=Ui.Label("삭제할 항목을 선택해주세요.",9);status.Size=new Size(470,62);
            Func<int> flags=()=> (thumbs.Checked?Thumbnails:0)|(temp.Checked?Temporary:0)|(originals.Checked?Downloaded:0);
            var size=Ui.Button("선택한 캐시 용량 확인",async()=>{try{int selected=flags();status.Text="용량 확인 중…";status.Text=await Task.Run(()=>Process(AppDomain.CurrentDomain.BaseDirectory,selected,Catalog.Load(),false));}catch(Exception ex){status.Text="용량 확인 실패: "+ex.Message;}});size.Size=new Size(250,36);panel.Controls.Add(size);
            var remove=Ui.Button("선택한 캐시 삭제 예약",()=>{try{schedule(flags());status.Text="예약했어요. 아지트콘을 종료한 뒤 다시 켜면 삭제해요.";}catch(Exception ex){status.Text=ex.Message;}},true);remove.Size=new Size(250,38);panel.Controls.Add(remove);panel.Controls.Add(status);
        }
        internal static void Test(){
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-work","cache-"+Guid.NewGuid().ToString("N"));string data=Data(root);Directory.CreateDirectory(Path.Combine(data,"images","thumbs-v1"));Directory.CreateDirectory(Path.Combine(data,"outbox"));Directory.CreateDirectory(Path.Combine(root,"semantic","model"));
            string thumb=Path.Combine(data,"images","thumbs-v1","a.png"),remote=Path.Combine(data,"images","remote.png"),local=Path.Combine(data,"images","local.png"),temp=Path.Combine(data,"outbox","01.png"),model=Path.Combine(root,"semantic","model","weights.bin"),catalog=Path.Combine(data,"catalog.json");
            foreach(string p in new[]{thumb,remote,local,temp,model,catalog})File.WriteAllText(p,"preserve-or-remove");
            var settings=new Settings();settings.Entries.Add(new Entry{Path=local});
            Process(root,3,settings,true);if(File.Exists(thumb)||File.Exists(temp)||!File.Exists(remote)||!File.Exists(local)||!File.Exists(model)||!File.Exists(catalog))throw new Exception("Selective cache cleanup failed");
            Process(root,7,settings,true);if(File.Exists(remote)||!File.Exists(local)||!File.Exists(model)||!File.Exists(catalog))throw new Exception("Cache boundaries/protection failed");
            Schedule(root,3);if(File.ReadAllText(Pending(root))!="3")throw new Exception("Cleanup scheduling failed");
            using(var form=new SemanticSettingsForm(false,value=>{})){
                form.AddThumbnailSettings(false,value=>{},()=>{},()=>{},()=>"test",()=>0,()=>false);int requested=0;AddTab(form,value=>requested=value);
                var tabs=form.Controls.OfType<TabControl>().Single();tabs.SelectedIndex=tabs.TabCount-1;form.Controls.Remove(tabs);
                using(var surface=new UserControl{Size=form.ClientSize,BackColor=Ui.Paper}){surface.Controls.Add(tabs);surface.CreateControl();surface.PerformLayout();
                    var panel=(FlowLayoutPanel)tabs.SelectedTab.Controls[0];panel.Controls.OfType<UiButton>().Last().PerformClick();if(requested!=3)throw new Exception("Default cleanup options failed");
                    panel.Controls.OfType<CheckBox>().Last().Checked=true;panel.Controls.OfType<UiButton>().Last().PerformClick();if(requested!=7)throw new Exception("Downloaded image cleanup option failed");
                    using(var bitmap=new Bitmap(surface.Width,surface.Height)){surface.DrawToBitmap(bitmap,surface.ClientRectangle);bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cache-settings-preview.png"));}
                }
            }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cache-maintenance-test.txt"),"PASS: selected cache deletion, local-only sources protected, settings/model boundaries and scheduling. Isolated generated files only.");
        }
    }
}
