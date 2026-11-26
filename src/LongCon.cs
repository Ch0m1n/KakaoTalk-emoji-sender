using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgitCon {
    internal sealed class LongPreviewAttempts {
        readonly Dictionary<IntPtr,int> reads=new Dictionary<IntPtr,int>();
        internal bool Available(IntPtr window){int count;return !reads.TryGetValue(window,out count)||count<2;}
        internal void Finished(IntPtr window,bool readStarted){if(!readStarted)return;int count;reads.TryGetValue(window,out count);reads[window]=count+1;}
    }
    [DataContract] public sealed class LongPreset {
        [DataMember] public string Name;
        [DataMember] public List<string> Keys=new List<string>();
        public override string ToString(){return Name;}
    }
    internal sealed class LongPrepared:IDisposable {
        internal readonly List<ImageClipboard> Parts=new List<ImageClipboard>();
        internal static LongPrepared Create(IList<Entry> entries,CancellationToken token,Action<int> progress=null,bool resize100=false){
            if(entries.Count<2||entries.Count>20)throw new InvalidOperationException("정지 조각을 2~20개 선택해주세요.");
            var ready=new LongPrepared();long bytes=0;
            try{foreach(var entry in entries){token.ThrowIfCancellationRequested();string path=AssetCodec.ResizeForSend(DcShop.EnsureImage(entry,token),resize100,token);
                using(var image=Image.FromFile(path)){
                    if(ImageBundle.Animated(image))throw new InvalidOperationException("첫 버전의 롱콘은 정지 조각만 지원해요.");
                    bytes+=(long)image.Width*image.Height*16;
                    if(bytes>128L*1024*1024)throw new InvalidOperationException("롱콘 준비 용량이 128MB를 넘어요. 조각 크기를 줄여주세요.");
                    ready.Parts.Add(new ImageClipboard(image));
                    if(progress!=null)progress(ready.Parts.Count);
                }
            }token.ThrowIfCancellationRequested();return ready;}catch{ready.Dispose();throw;}
        }
        public void Dispose(){foreach(var part in Parts)part.Dispose();Parts.Clear();}
    }
    internal static class LongFocusWait {
        internal static async Task<bool> Ready(Func<bool> identity,Func<IntPtr> foreground,Func<bool> current,Func<CancellationToken,Task> pause,CancellationToken token){
            for(int attempt=0;attempt<50;attempt++){token.ThrowIfCancellationRequested();if(!identity())return false;if(foreground()!=IntPtr.Zero)return current();await pause(token);}return false;
        }
    }
    internal sealed class LongFlowChecks {
        internal Func<bool> Current;
        internal Func<CancellationToken,Task<bool>> Ready;
        internal Func<int,CancellationToken,Task> Paste;
        internal Func<CancellationToken,Task<object>> Preview;
        internal Func<object,bool> Enter;
        internal Func<object,CancellationToken,Task<bool>> Dismissed;
        internal Action<int> Progress;
    }
    internal sealed class LongFlowResult {internal int Requested,Pasted;internal bool Complete;internal string Stage;}
    internal static class LongFlow {
        internal static string Reason(string stage){switch(stage){case "before-paste":return "대상 채팅방이나 포커스가 바뀌었어요.";case "preview-proof":return "확인할 수 있는 이미지 전송창을 찾지 못했어요.";case "before-enter":return "전송 직전 창이나 포커스가 달라졌어요.";case "enter-result-unknown":return "Enter 요청 결과가 불확실해요.";case "after-enter":return "전송창이 닫히고 입력창으로 돌아온 것을 확인하지 못했어요.";default:return "전송을 계속할 수 없어 멈췄어요.";}}
        internal static async Task<LongFlowResult> Run(int count,LongFlowChecks c,CancellationToken token){
            if(count<2||count>20)throw new ArgumentOutOfRangeException("count");var result=new LongFlowResult();
            for(int i=0;i<count;i++){
                token.ThrowIfCancellationRequested();result.Stage="before-paste";if(!(c.Ready==null?c.Current():await c.Ready(token)))return result;
                await c.Paste(i,token);result.Pasted++;token.ThrowIfCancellationRequested();result.Stage="preview-proof";
                if(!(c.Ready==null?c.Current():await c.Ready(token)))return result;object proof=await c.Preview(token);if(proof==null)return result;
                token.ThrowIfCancellationRequested();result.Stage="before-enter";if(!(c.Ready==null?c.Current():await c.Ready(token)))return result;
                // Exactly one targeted Enter attempt per proved preview. Never retry.
                if(!c.Enter(proof)){result.Stage="enter-result-unknown";return result;}
                result.Requested++;if(c.Progress!=null)c.Progress(result.Requested);
                result.Stage="after-enter";if(!await c.Dismissed(proof,token))return result;
            }
            result.Complete=true;result.Stage="complete";return result;
        }
    }
    internal sealed class LongConForm:Form {
        readonly List<Entry> items;readonly List<LongPreset> presets;readonly Func<string,Entry> resolve;readonly Action save;
        readonly Func<CancellationToken,IProgress<string>,Task> run;
        readonly ListBox list=new ListBox{Dock=DockStyle.Fill};readonly FlowLayoutPanel preview=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};
        readonly TextBox name=new TextBox{Width=150};readonly ComboBox stored=new ComboBox{Width=170,DropDownStyle=ComboBoxStyle.DropDownList};
        readonly Label status=Ui.Label("콘 목록에서 위→아래 순서로 정지 조각을 골라주세요.",9),room=Ui.Label("",10,true);
        readonly UiButton start,stop;readonly List<Control> editing=new List<Control>();CancellationTokenSource running,previews=new CancellationTokenSource();
        internal void CancelForUpdate(){if(running!=null)running.Cancel();}
        internal bool Busy {get{return running!=null;}}
        internal LongConForm(List<Entry> items,List<LongPreset> presets,Func<string,Entry> resolve,Action save,Func<CancellationToken,IProgress<string>,Task> run){
            this.items=items;this.presets=presets;this.resolve=resolve;this.save=save;this.run=run;
            Text="아지트콘 · 롱콘 빠른 연속 전송";Icon=Ui.AppIcon();Font=Ui.Regular(10);BackColor=Ui.Paper;ClientSize=new Size(520,650);MinimumSize=new Size(480,600);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=7};foreach(int h in new[]{38,40,36})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));foreach(int h in new[]{42,52,68})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));Controls.Add(layout);
            room.Dock=DockStyle.Fill;layout.Controls.Add(room);
            var saved=new FlowLayoutPanel{Dock=DockStyle.Fill};var presetLabel=Ui.Label("저장한 롱콘",9);presetLabel.Size=new Size(90,28);saved.Controls.Add(presetLabel);saved.Controls.AddRange(new Control[]{stored,Ui.Button("불러오기",LoadPreset)});layout.Controls.Add(saved);editing.Add(saved);
            var naming=new FlowLayoutPanel{Dock=DockStyle.Fill};var nameLabel=Ui.Label("롱콘 이름",9);nameLabel.Size=new Size(90,28);naming.Controls.Add(nameLabel);naming.Controls.AddRange(new Control[]{name,Ui.Button("순서 저장",SavePreset)});layout.Controls.Add(naming);editing.Add(naming);
            var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));body.Controls.Add(list);body.Controls.Add(preview);layout.Controls.Add(body);editing.Add(list);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};actions.Controls.AddRange(new Control[]{Ui.Button("위로",()=>Move(-1)),Ui.Button("아래로",()=>Move(1)),Ui.Button("제외",Remove),Ui.Button("비우기",()=>{items.Clear();RefreshQueue();})});layout.Controls.Add(actions);editing.Add(actions);
            var sends=new FlowLayoutPanel{Dock=DockStyle.Fill};start=Ui.Button("롱콘 연속 전송",async()=>await Start(),true);start.Width=300;stop=Ui.Button("전송 중단",()=>{if(running!=null)running.Cancel();});stop.Enabled=false;sends.Controls.AddRange(new Control[]{start,stop});layout.Controls.Add(sends);
            status.Dock=DockStyle.Fill;layout.Controls.Add(status);FormClosing+=(s,e)=>{if(running!=null){running.Cancel();e.Cancel=true;status.Text="중단 처리 중이에요. 처리 후 창을 닫아주세요.";}};FormClosed+=(s,e)=>previews.Cancel();Disposed+=(s,e)=>previews.Dispose();UpdatePresets();RefreshQueue();
        }
        internal void Target(string title){room.Text="전송 대상: "+(String.IsNullOrEmpty(title)?"카톡 입력창을 먼저 눌러주세요":title);}
        internal void Report(string text){if(!IsDisposed)status.Text=text;}
        internal void Add(Entry entry){if(Busy)return;if(items.Count>=20){Report("최대 20조각까지 골라요.");return;}items.Add(entry);RefreshQueue();}
        void Move(int delta){int i=list.SelectedIndex,n=i+delta;if(i<0||n<0||n>=items.Count)return;var item=items[i];items.RemoveAt(i);items.Insert(n,item);RefreshQueue();list.SelectedIndex=n;}
        void Remove(){int i=list.SelectedIndex;if(i<0)return;items.RemoveAt(i);RefreshQueue();}
        void UpdatePresets(){stored.Items.Clear();stored.Items.AddRange(presets.Cast<object>().ToArray());if(stored.Items.Count>0)stored.SelectedIndex=0;}
        void SavePreset(){string title=name.Text.Trim();if(title.Length==0||title.Length>80||items.Count<2){Report("이름(1~80자)과 조각 두 개 이상이 필요해요.");return;}var old=presets.FirstOrDefault(p=>p.Name==title);if(old!=null)presets.Remove(old);if(presets.Count>=50){Report("저장한 롱콘은 최대 50개예요.");return;}presets.Add(new LongPreset{Name=title,Keys=items.Select(Catalog.Key).ToList()});save();UpdatePresets();Report("조각 순서를 저장했어요. 자동 전송은 시작하지 않았어요.");}
        void LoadPreset(){var preset=stored.SelectedItem as LongPreset;if(preset==null)return;if(preset.Keys==null||preset.Keys.Count<2||preset.Keys.Count>20){Report("저장한 조각 정보가 올바르지 않아요.");return;}var loaded=preset.Keys.Select(resolve).ToList();if(loaded.Any(e=>e==null)){Report("목록에서 빠진 콘이 있어 불러오지 않았어요. 팩을 다시 추가해주세요.");return;}items.Clear();items.AddRange(loaded);name.Text=preset.Name;RefreshQueue();}
        void RefreshQueue(){previews.Cancel();previews.Dispose();previews=new CancellationTokenSource();list.Items.Clear();list.Items.AddRange(items.Select((e,i)=>(i+1)+". "+(e.DisplayName??Path.GetFileName(e.Path))).Cast<object>().ToArray());foreach(Control c in preview.Controls.Cast<Control>().ToArray())c.Dispose();preview.Controls.Clear();
            foreach(var entry in items){var picture=new PictureBox{Size=new Size(175,175),SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.White,Margin=new Padding(0,0,0,2)};preview.Controls.Add(picture);picture.Disposed+=(s,e)=>{if(picture.Image!=null)picture.Image.Dispose();};LoadPreview(entry,picture,previews.Token);}
            start.Enabled=!Busy&&items.Count>=2;start.Text="롱콘 연속 전송 · "+items.Count+"장";Report("시작 후에는 Enter·전송을 직접 누르지 말고 기다려주세요. 정지 조각 2~20장.");
        }
        async void LoadPreview(Entry entry,PictureBox picture,CancellationToken token){Bitmap bitmap=null;try{bitmap=await Task.Run(()=>{using(var source=Image.FromFile(DcShop.EnsureImage(entry,token))){if(ImageBundle.Animated(source))throw new Exception("움짤 조각은 지원하지 않아요.");var small=new Bitmap(350,350);using(var g=Graphics.FromImage(small)){g.Clear(Color.White);g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(source,ImageBundle.Fit(source.Size,new Rectangle(0,0,350,350)),0,0,source.Width,source.Height,GraphicsUnit.Pixel);}return small;}},token);if(token.IsCancellationRequested||picture.IsDisposed)return;picture.Image=bitmap;bitmap=null;}catch(Exception ex){if(!token.IsCancellationRequested&&!picture.IsDisposed){picture.BackColor=Color.MistyRose;Report(ex.Message);}}finally{if(bitmap!=null)bitmap.Dispose();}}
        async Task Start(){if(Busy||items.Count<2)return;running=new CancellationTokenSource();foreach(var c in editing)c.Enabled=false;start.Enabled=false;stop.Enabled=true;
            try{await run(running.Token,new Progress<string>(Report));}catch(OperationCanceledException){Report("중단했어요. 이미 전송 요청한 조각은 카톡에서 확인해주세요.");}catch(Exception ex){Report("중단: "+ex.Message);}finally{running.Dispose();running=null;foreach(var c in editing)c.Enabled=true;start.Enabled=items.Count>=2;stop.Enabled=false;}
        }
    }
}
