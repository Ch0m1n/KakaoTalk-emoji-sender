using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgitCon {
    public enum BundleMode { Originals, SplitNine, Merge, Compatible, Double }
    public static class ImageBundle {
        public static string OutputRoot=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","outbox");
        public static bool Animated(Image image){return image.FrameDimensionsList.Any(id=>image.GetFrameCount(new FrameDimension(id))>1);}
        internal static DataObject SplitPayload(IList<string> paths){
            if(paths.Count!=9||paths.Any(p=>!File.Exists(p)||!String.Equals(Path.GetExtension(p),".png",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("분할 PNG 9개가 필요해요.");
            var files=new System.Collections.Specialized.StringCollection();files.AddRange(paths.Select(Path.GetFullPath).ToArray());var payload=new DataObject();payload.SetFileDropList(files);return payload;
        }
        public static List<string> Create(IList<string> paths,BundleMode mode,CancellationToken token,string sourceName=null) {
            if(paths.Count<1||paths.Count>9)throw new Exception("1~9개의 콘을 모아주세요.");
            if(mode==BundleMode.SplitNine&&paths.Count!=1)throw new Exception("9분할은 콘 한 개를 모았을 때 사용할 수 있어요.");
            if(mode==BundleMode.Double&&paths.Count!=2)throw new Exception("더블콘은 콘 두 개를 선택해주세요.");
            if(mode==BundleMode.Merge&&(paths.Count<2||paths.Count>3))throw new Exception("합치기는 콘 2~3개를 모았을 때 사용할 수 있어요.");
            string dir=Path.Combine(OutputRoot,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            var output=new List<string>();
            string splitName=String.IsNullOrWhiteSpace(sourceName)?Path.GetFileNameWithoutExtension(paths[0]):sourceName;
            var invalid=Path.GetInvalidFileNameChars();splitName=new string(splitName.Select(c=>Char.IsControl(c)||invalid.Contains(c)?'_':c).ToArray()).Trim().TrimEnd(' ','.');
            if(new[]{".png",".jpg",".jpeg",".gif",".webp"}.Contains(Path.GetExtension(splitName).ToLowerInvariant()))splitName=Path.GetFileNameWithoutExtension(splitName);
            if(splitName.Length>80)splitName=splitName.Substring(0,80);if(splitName.Length==0)splitName="콘";
            string baseName=splitName;int duplicate=1;
            if(mode==BundleMode.SplitNine)while(Directory.EnumerateDirectories(OutputRoot).Where(folder=>(File.GetAttributes(folder)&FileAttributes.ReparsePoint)==0).Any(folder=>File.Exists(Path.Combine(folder,splitName+"_01.png"))))splitName=baseName+"_"+(++duplicate);

            try {
                if(mode==BundleMode.Originals||mode==BundleMode.Compatible||mode==BundleMode.Double){for(int i=0;i<paths.Count;i++){token.ThrowIfCancellationRequested();string path=Path.Combine(dir,(mode==BundleMode.Double?(i+1).ToString("D2")+"_"+Path.GetFileName(paths[i]):(i+1).ToString("D2")+Path.GetExtension(paths[i])));File.Copy(paths[i],path);output.Add(path);}return output;}
                var images=new List<Image>();try {
                    foreach(string path in paths){var image=Image.FromFile(path);images.Add(image);if(Animated(image))throw new Exception("움직이는 GIF는 원본 묶음 복사를 사용해주세요. 분할·합치기로 정지시키지 않아요.");}
                    if(mode==BundleMode.SplitNine){var source=images[0];if((long)source.Width*source.Height>16777216)throw new Exception("이미지 크기가 너무 커요.");if(source.Width<3||source.Height<3)throw new Exception("3×3 픽셀 이상의 이미지가 필요해요.");
                        for(int y=0;y<3;y++)for(int x=0;x<3;x++){token.ThrowIfCancellationRequested();var rect=new Rectangle(x*source.Width/3,y*source.Height/3,(x+1)*source.Width/3-x*source.Width/3,(y+1)*source.Height/3-y*source.Height/3);
                            using(var tile=((Bitmap)source).Clone(rect,PixelFormat.Format32bppArgb)){string path=Path.Combine(dir,splitName+"_"+(y*3+x+1).ToString("D2")+".png");tile.Save(path,ImageFormat.Png);output.Add(path);}}
                    }else{int w=images[0].Width,h=images[0].Height;if(images.Any(i=>i.Width!=w||i.Height!=h))throw new Exception("가로·세로 크기가 같은 콘끼리 합칠 수 있어요.");if((long)w*h*images.Count>16777216||w*images.Count>8192)throw new Exception("합친 이미지가 너무 커요.");
                        using(var merged=new Bitmap(w*images.Count,h,PixelFormat.Format32bppArgb)){using(var g=Graphics.FromImage(merged)){g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;for(int i=0;i<images.Count;i++){token.ThrowIfCancellationRequested();g.DrawImage(images[i],new Rectangle(i*w,0,w,h),0,0,w,h,GraphicsUnit.Pixel);}}string path=Path.Combine(dir,"01-merged.png");merged.Save(path,ImageFormat.Png);output.Add(path);}}
                }finally{foreach(var image in images)image.Dispose();}return output;
            }catch{foreach(string file in output)if(File.Exists(file))File.Delete(file);if(Directory.Exists(dir)&&!Directory.EnumerateFileSystemEntries(dir).Any())Directory.Delete(dir);throw;}
        }
        internal static Rectangle Fit(Size source,Rectangle cell){double scale=Math.Min((double)cell.Width/source.Width,(double)cell.Height/source.Height);int w=Math.Max(1,(int)Math.Round(source.Width*scale)),h=Math.Max(1,(int)Math.Round(source.Height*scale));return new Rectangle(cell.X+(cell.Width-w)/2,cell.Y+(cell.Height-h)/2,w,h);}
    }
    public class CollectionForm:Form {
        readonly List<Entry> items;readonly Action clear;readonly Action<BundleMode> copy;
        readonly bool doubleMode;
        readonly TableLayoutPanel grid=new TableLayoutPanel();readonly Label count=Ui.Label("9분할 미리보기",12,true),hint=Ui.Label("",9);
        readonly UiButton attach,empty;CancellationTokenSource previews=new CancellationTokenSource();bool busy,ready;
        public CollectionForm(List<Entry> queue,Action<int> removeItem,Action clearItems,Action<BundleMode> copyItems,bool doubleMode=false){
            this.doubleMode=doubleMode;this.removeItem=removeItem;
            items=queue;clear=clearItems;copy=copyItems;Icon=Ui.AppIcon();Text="아지트콘 · 콘 9분할";ClientSize=new Size(390,530);MinimumSize=new Size(350,490);Font=Ui.Regular(10);BackColor=Ui.Paper;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=5};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));Controls.Add(layout);
            var heading=new Panel{Dock=DockStyle.Fill};count.SetBounds(0,4,230,28);empty=Ui.Button("선택 해제",()=>{if(!busy)clear();});empty.Dock=DockStyle.Right;empty.Width=90;heading.Controls.AddRange(new Control[]{count,empty});layout.Controls.Add(heading);
            var stage=new Panel{Dock=DockStyle.Fill,BackColor=Ui.Soft,Margin=new Padding(0,4,0,8)};layout.Controls.Add(stage);
            grid.ColumnCount=doubleMode?2:3;grid.RowCount=doubleMode?1:3;grid.Margin=Padding.Empty;grid.BackColor=Ui.Line;for(int i=0;i<grid.ColumnCount;i++)grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/grid.ColumnCount));for(int i=0;i<grid.RowCount;i++)grid.RowStyles.Add(new RowStyle(SizeType.Percent,100f/grid.RowCount));stage.Controls.Add(grid);
            stage.Layout+=(s,e)=>{int width=Math.Max(1,Math.Min(stage.ClientSize.Width,stage.ClientSize.Height*(doubleMode?2:1))),height=doubleMode?Math.Max(1,width/2):width;var bounds=new Rectangle((stage.ClientSize.Width-width)/2,(stage.ClientSize.Height-height)/2,width,height);if(grid.Bounds!=bounds)grid.Bounds=bounds;};
            var help=Ui.Label("콘 하나 → PNG 9장 · 왼쪽 위부터 순서대로",9);help.Dock=DockStyle.Fill;help.ForeColor=Ui.Muted;layout.Controls.Add(help);
            if(doubleMode){Text="아지트콘 · 더블콘";help.Text="원본 두 개를 함께 첨부 · 칸을 누르면 제외";}
            attach=Ui.Button(doubleMode?"더블콘을 카톡에 첨부":"9개로 쪼개서 카톡에 첨부",()=>copy(doubleMode?BundleMode.Double:BundleMode.SplitNine),true);attach.Dock=DockStyle.Fill;layout.Controls.Add(attach);
            hint.Dock=DockStyle.Fill;hint.ForeColor=Ui.Muted;layout.Controls.Add(hint);
            FormClosed+=(s,e)=>previews.Cancel();Disposed+=(s,e)=>previews.Dispose();RefreshQueue();
        }
        public void SetBusy(bool value){busy=value;RefreshButtons();if(value)hint.Text=doubleMode?"원본 두 개를 준비하고 있어요...":"콘을 9개의 PNG로 나누고 있어요...";}
        public void ShowStatus(string text){hint.Text=text;}
        void RefreshButtons(){attach.Enabled=!busy&&ready&&items.Count==(doubleMode?2:1);empty.Enabled=!busy&&items.Count>0;}
        public void RefreshQueue(){previews.Cancel();previews.Dispose();previews=new CancellationTokenSource();ready=false;foreach(Control control in grid.Controls.Cast<Control>().ToArray()){grid.Controls.Remove(control);control.Dispose();}RefreshButtons();hint.Text="콘 목록에서 정지 콘 하나를 골라주세요.";
            count.Text=doubleMode?"더블콘  "+items.Count+" / 2":"9분할 미리보기";
            var pictures=new List<PictureBox>();for(int i=0;i<(doubleMode?2:9);i++){
                int index=i;var picture=new PictureBox{Dock=DockStyle.Fill,Margin=doubleMode?Padding.Empty:new Padding(1),SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.White,AccessibleName=(i+1)+"번 이미지"};grid.Controls.Add(picture,i%(doubleMode?2:3),doubleMode?0:i/3);pictures.Add(picture);picture.Disposed+=(s,e)=>{if(picture.Image!=null)picture.Image.Dispose();if(picture.Tag is Stream)((Stream)picture.Tag).Dispose();};if(doubleMode)picture.Click+=(s,e)=>{if(!busy&&index<items.Count)removeItem(index);};
            }
            if(doubleMode){hint.Text="콘 목록에서 원본 두 개를 순서대로 골라주세요. 움짤도 가능해요.";LoadDouble(items.ToList(),pictures,previews.Token);}
            else if(items.Count==1){hint.Text="9분할 미리보기를 준비하고 있어요...";LoadImage(items[0],pictures,previews.Token);}
        }
        readonly Action<int> removeItem;
        async void LoadDouble(List<Entry> entries,List<PictureBox> pictures,CancellationToken token){var built=new List<Tuple<Image,Stream>>();try{
            built=await Task.Run(()=>{var images=new List<Tuple<Image,Stream>>();try{foreach(var entry in entries){token.ThrowIfCancellationRequested();string path=DcShop.EnsureImage(entry,token);var memory=new MemoryStream(File.ReadAllBytes(path));try{images.Add(Tuple.Create(Image.FromStream(memory),(Stream)memory));}catch{memory.Dispose();throw;}}return images;}catch{foreach(var image in images){image.Item1.Dispose();image.Item2.Dispose();}throw;}},token);
            if(token.IsCancellationRequested||IsDisposed||pictures.Any(p=>p.IsDisposed))return;for(int i=0;i<built.Count;i++){pictures[i].Image=built[i].Item1;pictures[i].Tag=built[i].Item2;}built=null;ready=entries.Count==2;RefreshButtons();hint.Text=ready?"원본 두 개를 지정해요. 열기·전송은 직접 눌러주세요.":"두 번째 콘을 골라주세요.";
        }catch(Exception ex){if(!token.IsCancellationRequested&&!IsDisposed)hint.Text=ex.Message;}finally{if(built!=null)foreach(var image in built){image.Item1.Dispose();image.Item2.Dispose();}}}
        async void LoadImage(Entry entry,List<PictureBox> pictures,CancellationToken token){List<Bitmap> tiles=null;try{
            tiles=await Task.Run(()=>{var result=new List<Bitmap>();try{string path=DcShop.EnsureImage(entry,token);using(var image=new Bitmap(path)){
                if(ImageBundle.Animated(image))throw new InvalidOperationException("움짤은 분할하지 않아요. 개별 첨부해주세요.");
                if(image.Width<3||image.Height<3||(long)image.Width*image.Height>16777216)throw new InvalidOperationException("이미지 크기가 9분할에 맞지 않아요.");
                for(int y=0;y<3;y++)for(int x=0;x<3;x++){token.ThrowIfCancellationRequested();result.Add(image.Clone(new Rectangle(x*image.Width/3,y*image.Height/3,(x+1)*image.Width/3-x*image.Width/3,(y+1)*image.Height/3-y*image.Height/3),PixelFormat.Format32bppArgb));}}
                return result;}catch{foreach(var tile in result)tile.Dispose();throw;}},token);
            if(token.IsCancellationRequested||IsDisposed||pictures.Any(p=>p.IsDisposed))return;
            for(int i=0;i<9;i++)pictures[i].Image=tiles[i];tiles=null;ready=true;RefreshButtons();hint.Text="파일 9개를 지정해요. 열기·전송은 직접 눌러주세요.";
        }catch(Exception ex){if(!token.IsCancellationRequested&&!IsDisposed){ready=false;RefreshButtons();hint.Text=ex.Message;}}
        finally{if(tiles!=null)foreach(var tile in tiles)tile.Dispose();}}
    }
}
