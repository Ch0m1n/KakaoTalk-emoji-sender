using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgitCon {
    public class ShopForm:Form {
        readonly Action<ImportedPack> imported;readonly Action<Entry> personal;readonly Action<string> removed;
        readonly List<PackRecord> installed;
        readonly TextBox query=new TextBox(),url=new TextBox(),name=new TextBox();
        readonly FlowLayoutPanel results=new FlowLayoutPanel();readonly Panel body=new Panel();
        readonly Label status=Ui.Label("",9),count=Ui.Label("",9),pageLabel=Ui.Label("",9);
        readonly Panel searchBar=new Panel(),footer=new Panel();
        readonly TableLayoutPanel layout;
        readonly UiButton find,prev,next,stop;
        readonly UiButton browser,htmlImport;
        readonly UiButton[] tabs;
        CancellationTokenSource cancel,previews=new CancellationTokenSource();
        int page=1,currentTab,searchSource;string term="";bool canNext,arranging;
        List<PackResult> searchResults=new List<PackResult>();
        readonly HashSet<string> adding=new HashSet<string>();
        public ShopForm(List<PackRecord> packs,Action<ImportedPack> addPack,Action<Entry> addPersonal,Action<string> remove) {
            imported=addPack;personal=addPersonal;removed=remove;installed=packs;
            Icon=Ui.AppIcon();Text="아지트콘 · 팩 둘러보기";ClientSize=new Size(540,660);MinimumSize=new Size(470,530);Font=Ui.Regular(10);BackColor=Ui.Paper;ForeColor=Ui.Ink;StartPosition=FormStartPosition.CenterParent;
            layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22,18,22,14),ColumnCount=1,RowCount=5};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,66));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64));Controls.Add(layout);
            var nav=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=false};
            tabs=new[]{Ui.Button("디시콘샵",()=>Tab(0)),Ui.Button("아카콘샵",()=>Tab(3)),Ui.Button("보관한 팩",()=>Tab(1)),Ui.Button("개별 콘",()=>Tab(2))};
            foreach(var tab in tabs){tab.Width=88;tab.Height=34;tab.Margin=new Padding(0,0,8,0);nav.Controls.Add(tab);}layout.Controls.Add(nav,0,0);
            searchBar.Dock=DockStyle.Fill;searchBar.Margin=new Padding(0,8,0,8);var field=Ui.Field(query,"팩 이름 또는 아카콘 링크 / 번호");field.Dock=DockStyle.Fill;find=Ui.Button("검색",()=>{term=query.Text.Trim();page=1;Find();},true);find.Dock=DockStyle.Right;find.Width=82;var gap=new Panel{Dock=DockStyle.Right,Width=10};searchBar.Controls.Add(field);searchBar.Controls.Add(gap);searchBar.Controls.Add(find);layout.Controls.Add(searchBar,0,1);
            query.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;term=query.Text;page=1;Find();}};
            var countBar=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};count.Dock=DockStyle.Fill;count.Margin=Padding.Empty;count.ForeColor=Ui.Muted;browser=Ui.Button("상점 열기",()=>Process.Start(new ProcessStartInfo(ArcaShop.SearchUrl(query.Text,1)){UseShellExecute=true}));browser.Dock=DockStyle.Right;browser.Width=84;htmlImport=Ui.Button("HTML 등록",ImportHtml);htmlImport.Dock=DockStyle.Right;htmlImport.Width=84;countBar.Controls.AddRange(new Control[]{count,browser,htmlImport});layout.Controls.Add(countBar,0,2);
            body.Dock=DockStyle.Fill;body.Margin=Padding.Empty;layout.Controls.Add(body,0,3);
            results.Dock=DockStyle.Fill;results.WrapContents=false;results.FlowDirection=FlowDirection.TopDown;results.AutoScroll=true;results.BackColor=Ui.Paper;body.Controls.Add(results);
            results.SizeChanged+=(s,e)=>{ResizeRows();LoadVisible();};results.Scroll+=(s,e)=>LoadVisible();
            footer.Dock=DockStyle.Fill;footer.Margin=new Padding(0,10,0,0);layout.Controls.Add(footer,0,4);
            status.SetBounds(0,0,490,22);status.ForeColor=Ui.Muted;status.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;footer.Controls.Add(status);
            prev=Ui.Button("이전",()=>{if(page>1){page--;Find();}});next=Ui.Button("다음",()=>{page++;Find();});stop=Ui.Button("취소",()=>{if(cancel!=null)cancel.Cancel();});
            footer.Controls.AddRange(new Control[]{prev,pageLabel,next,stop});footer.SizeChanged+=(s,e)=>ArrangeFooter();
            FormClosing+=(s,e)=>{if(cancel!=null){cancel.Cancel();e.Cancel=true;status.Text="요청을 취소하고 있어요. 잠시 후 닫아주세요.";}else previews.Cancel();};
            FormClosed+=(s,e)=>{previews.Cancel();ClearRows();};Tab(0);
        }
        void Tab(int index){if(cancel!=null)return;bool shop=index==0||index==3;if(shop&&index!=searchSource){searchSource=index;term="";query.Text="";page=1;canNext=false;searchResults.Clear();}currentTab=index;for(int i=0;i<tabs.Length;i++){tabs[i].Active=new[]{0,3,1,2}[i]==index;tabs[i].Invalidate();}searchBar.Visible=shop;prev.Visible=next.Visible=pageLabel.Visible=shop;browser.Visible=htmlImport.Visible=index==3;stop.Visible=false;
            layout.RowStyles[1].Height=shop?66:0;layout.RowStyles[4].Height=shop?64:38;
            previews.Cancel();previews.Dispose();previews=new CancellationTokenSource();ClearRows();
            if(url.Parent!=null)url.Parent.Controls.Remove(url);if(name.Parent!=null)name.Parent.Controls.Remove(name);
            foreach(Control control in body.Controls.Cast<Control>().Where(c=>c!=results).ToArray()){body.Controls.Remove(control);control.Dispose();}results.Visible=index!=2;
            if(shop)RenderSearch();else if(index==1)RenderInstalled();else RenderPersonal();
        }
        void ClearRows(){foreach(Control row in results.Controls.Cast<Control>().ToArray()){results.Controls.Remove(row);row.Dispose();}}
        void ResizeRows(){if(arranging)return;arranging=true;try{results.SuspendLayout();results.AutoScroll=false;foreach(Control row in results.Controls)row.Width=Math.Max(300,results.ClientSize.Width-22);results.AutoScroll=true;results.ResumeLayout(true);}finally{arranging=false;}}
        void ArrangeFooter(){status.Width=Math.Max(280,footer.Width);prev.SetBounds(0,25,60,28);pageLabel.SetBounds(68,28,80,22);next.SetBounds(150,25,60,28);stop.SetBounds(Math.Max(230,footer.Width-60),25,60,28);}
        void Empty(string title,string text){var panel=new Panel{Width=430,Height=140};var head=Ui.Label(title,13,true);head.SetBounds(16,24,390,32);var desc=Ui.Label(text,10);desc.ForeColor=Ui.Muted;desc.SetBounds(16,64,390,56);panel.Controls.AddRange(new Control[]{head,desc});results.Controls.Add(panel);ResizeRows();}
        void RenderSearch(){ClearRows();count.Text=term==""?(currentTab==3?"아카콘 팩 링크로 바로 등록해요":"디시콘샵에서 이름으로 찾아보세요"):searchResults.Count+"개 팩 · "+term;status.Text="팩을 추가하면 전체 이미지를 받아 썸네일을 미리 준비해요.";pageLabel.Text=page+" 페이지";prev.Enabled=page>1;next.Enabled=canNext;
            if(searchResults.Count==0){Empty(term==""?"어떤 콘을 찾고 있나요?":"찾는 팩이 없어요",term==""?(currentTab==3?"팩 이름 또는 arca.live/e/번호 링크를 입력해주세요.\n이름 검색이 막히면 링크로 등록할 수 있어요.":"위 검색창에 팩 이름을 입력해주세요."):"짧은 이름으로 검색하거나 이전 페이지를 확인해주세요.");return;}
            foreach(var pack in searchResults){var selected=pack;bool exists=installed.Any(p=>p.Id==pack.Id);var row=new PackRow(pack,pack.Seller,exists?"추가됨":"내 콘에 추가",()=>Add(selected),!exists);row.Action.Enabled=!exists&&cancel==null;results.Controls.Add(row);}ResizeRows();LoadVisible();
        }
        void RenderInstalled(){count.Text="보관한 팩 "+installed.Count+"개";status.Text="팩을 제외해도 받아둔 콘은 그대로 보관해요.";if(installed.Count==0){Empty("아직 보관한 팩이 없어요","디시콘샵에서 마음에 드는 팩을 추가해주세요.");return;}
            foreach(var pack in installed.ToArray()){var selected=pack;var row=new PackRow(pack,(pack.Id.StartsWith("arca:")?"아카콘":"디시콘")+" · "+(String.IsNullOrEmpty(pack.Seller)?"내 콘에 보관 중":pack.Seller),"제외",()=>{removed(selected.Id);ClearRows();RenderInstalled();},false);results.Controls.Add(row);}ResizeRows();LoadVisible();
        }
        void RenderPersonal(){count.Text="디시콘 · 아카콘 이미지 링크로 추가";status.Text="";var form=new TableLayoutPanel{Dock=DockStyle.Top,Height=280,ColumnCount=1,RowCount=6,Margin=Padding.Empty};
            form.RowStyles.Add(new RowStyle(SizeType.Absolute,30));form.RowStyles.Add(new RowStyle(SizeType.Absolute,48));form.RowStyles.Add(new RowStyle(SizeType.Absolute,36));form.RowStyles.Add(new RowStyle(SizeType.Absolute,48));form.RowStyles.Add(new RowStyle(SizeType.Absolute,52));form.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
            form.Controls.Add(Ui.Label("콘 이미지 링크",10,true));var link=Ui.Field(url,"콘 이미지 링크");link.Dock=DockStyle.Fill;form.Controls.Add(link);form.Controls.Add(Ui.Label("이름 또는 검색할 별칭",10,true));var alias=Ui.Field(name,"개별 콘 이름");alias.Dock=DockStyle.Fill;form.Controls.Add(alias);var add=Ui.Button("내 콘에 추가",AddPersonal,true);add.Width=130;form.Controls.Add(add);var hint=Ui.Label("WebP는 PNG 또는 GIF로, 영상은 GIF로 바꿔요.\nPNG · JPG · GIF 원본은 그대로 사용해요.",9);hint.Dock=DockStyle.Fill;hint.ForeColor=Ui.Muted;form.Controls.Add(hint);body.Controls.Add(form);}
        void LoadVisible(){if(IsDisposed||!Visible||currentTab==2)return;Rectangle visible=results.ClientRectangle;foreach(var row in results.Controls.OfType<PackRow>().ToArray()){if(row.PreviewStarted||!visible.IntersectsWith(row.Bounds))continue;var pack=row.Pack;if(String.IsNullOrEmpty(pack.ThumbnailUrl))continue;row.PreviewStarted=true;LoadThumbnail(row,pack.ThumbnailUrl,previews.Token);}}
        async void LoadThumbnail(PackRow row,string url,CancellationToken token){try{string path=await Task.Run(()=>DcShop.EnsureImage(new Entry{SourceUrl=url,PackId=row.Pack.Id,RemoteId=row.Pack.ThumbnailRemoteId},token),token);if(token.IsCancellationRequested||row.IsDisposed||IsDisposed)return;var memory=new MemoryStream(File.ReadAllBytes(path));try{row.Picture.Image=Image.FromStream(memory);row.Picture.Tag=memory;}catch{memory.Dispose();throw;}}catch{if(!token.IsCancellationRequested&&!row.IsDisposed){row.Picture.AccessibleDescription="대표 이미지를 가져오지 못했어요";}}}
        protected override void OnShown(EventArgs e){base.OnShown(e);ArrangeFooter();ResizeRows();LoadVisible();query.Focus();}
        async void Find(){if(currentTab!=0&&currentTab!=3)return;if(String.IsNullOrWhiteSpace(term)){status.Text="팩 이름이나 링크를 입력해주세요.";return;}await Work(async token=>{previews.Cancel();previews.Dispose();previews=new CancellationTokenSource();count.Text="검색 중이에요...";var packs=await Task.Run(()=>currentTab==3?ArcaShop.Search(term,page,token):DcShop.Search(term,page,token));searchResults=packs;canNext=packs.Count>=15&&!(currentTab==3&&ArcaShop.IsPackLink(term));RenderSearch();});}
        string cacheStatus="";
        public void ShowCacheProgress(string text){cacheStatus=text??"";if(cancel==null&&!IsDisposed&&!String.IsNullOrEmpty(cacheStatus))status.Text=cacheStatus;}
        async void Add(PackResult pack){if(installed.Any(p=>p.Id==pack.Id)||adding.Contains(pack.Id))return;adding.Add(pack.Id);await Work(async token=>{var result=await Task.Run(()=>pack.Id.StartsWith("arca:")?ArcaShop.Import(pack.Id,token):DcShop.Import(pack.Id,token,s=>{}));token.ThrowIfCancellationRequested();result.Pack.ThumbnailUrl=pack.ThumbnailUrl??result.Pack.ThumbnailUrl;result.Pack.Seller=pack.Seller;if(!String.IsNullOrWhiteSpace(pack.Title))result.Pack.Title=pack.Title;imported(result);RenderSearch();status.Text=result.Pack.Title+" · "+result.Entries.Count+"개 콘 등록 · 썸네일은 백그라운드에서 준비해요";});adding.Remove(pack.Id);}
        async void ImportHtml(){if(cancel!=null)return;using(var dialog=new OpenFileDialog{Title="브라우저에서 저장한 아카콘 상세 페이지 선택",Filter="HTML 페이지|*.html;*.htm",CheckFileExists=true}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;string path=dialog.FileName;await Work(async token=>{var result=await Task.Run(()=>{if(new FileInfo(path).Length>3*1024*1024)throw new Exception("3MB 이하의 아카콘 페이지를 선택해주세요.");return ArcaShop.ParseSaved(File.ReadAllText(path));});token.ThrowIfCancellationRequested();imported(result);status.Text=result.Pack.Title+" · "+result.Entries.Count+"개 콘 등록 · 썸네일은 백그라운드에서 준비해요";});}}
        async void AddPersonal(){string link=url.Text,title=name.Text;await Work(async token=>{var entry=await Task.Run(()=>DcShop.ImportPersonal(link,title,token));token.ThrowIfCancellationRequested();personal(entry);status.Text="내 콘에 추가했어요.";});}
        async Task Work(Func<CancellationToken,Task> work){if(cancel!=null)return;cancel=new CancellationTokenSource();find.Enabled=prev.Enabled=next.Enabled=browser.Enabled=htmlImport.Enabled=false;foreach(var b in tabs)b.Enabled=false;foreach(var row in results.Controls.OfType<PackRow>())row.Action.Enabled=false;stop.Visible=true;status.Text="잠시만 기다려주세요...";bool worked=false;try{await work(cancel.Token);worked=true;}catch(Exception ex){status.Text=cancel.IsCancellationRequested?"취소했어요. 등록한 목록은 그대로예요.":"가져오지 못했어요 · "+ex.Message;}finally{cancel.Dispose();cancel=null;find.Enabled=browser.Enabled=htmlImport.Enabled=true;prev.Enabled=page>1;next.Enabled=canNext;foreach(var b in tabs)b.Enabled=true;foreach(var row in results.Controls.OfType<PackRow>())row.Action.Enabled=currentTab==1||!installed.Any(p=>p.Id==row.Pack.Id);stop.Visible=false;if(worked&&!String.IsNullOrEmpty(cacheStatus))status.Text=cacheStatus;}}
        internal void ShowPreviewResults(string search,List<PackResult> packs){query.Text=term=search;searchResults=packs;canNext=packs.Count>=15;RenderSearch();}
        internal void ShowArcaPreview(ImportedPack pack){Tab(3);query.Text=term="https://arca.live/e/55611?p=1";searchResults=new List<PackResult>{new PackResult{Id=pack.Pack.Id,Title=pack.Pack.Title,Seller=pack.Pack.Seller,ThumbnailUrl=pack.Pack.ThumbnailUrl,ThumbnailRemoteId=pack.Pack.ThumbnailRemoteId}};canNext=false;RenderSearch();}
        internal void VerifyTabNavigation(){Tab(1);Tab(2);Tab(3);Tab(0);Tab(2);if(url.IsDisposed||name.IsDisposed)throw new Exception("개별 콘 입력란이 탭 전환 중 해제됐어요.");Tab(0);if(results.HorizontalScroll.Visible)throw new Exception("검색 결과에 불필요한 가로 스크롤이 있어요.");}
        public static void RenderLayoutPreview(){
            string root=AppDomain.CurrentDomain.BaseDirectory;
            try{var catalog=Catalog.Load();using(var form=new ShopForm(catalog.Packs,p=>{},e=>{},id=>{}))using(var surface=new UserControl{Size=new Size(540,660),Font=form.Font,BackColor=Ui.Paper}){
                form.Controls.Remove(form.layout);surface.Controls.Add(form.layout);surface.CreateControl();
                foreach(var size in new[]{new Size(540,660),new Size(454,540)}){
                    surface.Size=size;form.Tab(1);surface.PerformLayout();
                    foreach(var row in form.results.Controls.OfType<PackRow>()){
                        var entry=catalog.Entries.FirstOrDefault(e=>e.PackId==row.Pack.Id&&!String.IsNullOrEmpty(e.Path)&&File.Exists(e.Path));
                        if(entry!=null)using(var image=Image.FromFile(entry.Path))row.Picture.Image=new Bitmap(image);
                    }
                    using(var bitmap=new Bitmap(size.Width,size.Height)){surface.DrawToBitmap(bitmap,new Rectangle(Point.Empty,size));bitmap.Save(Path.Combine(root,size.Width==540?"shop-installed-layout.png":"shop-installed-layout-small.png"));}
                    if(form.body.Height<300||form.results.Height<300)throw new Exception("Installed pack viewport collapsed");
                    form.Tab(0);surface.PerformLayout();using(var bitmap=new Bitmap(size.Width,size.Height)){surface.DrawToBitmap(bitmap,new Rectangle(Point.Empty,size));}
                    if(form.body.Height<250)throw new Exception("Shop viewport collapsed");
                    form.VerifyTabNavigation();
                }
            }File.WriteAllText(Path.Combine(root,"shop-layout-check.txt"),"PASS: installed/shop viewports fill remaining space at normal/minimum sizes; repeated tab navigation; no visible windows, network, input or KakaoTalk interaction.");}
            catch(Exception ex){File.WriteAllText(Path.Combine(root,"shop-layout-check.txt"),"FAIL: "+ex);Environment.ExitCode=1;}
        }
    }
}
