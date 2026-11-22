using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace AgitCon {
    [DataContract] public class Entry {
        [DataMember] public string Path;
        [DataMember] public string Aliases = "";
        [DataMember] public long LastUsed;
        [DataMember] public string DisplayName;
        [DataMember] public string PackId;
        [DataMember] public string SourceUrl;
        [DataMember] public string Format;
        [DataMember] public string RemoteId;
        [DataMember] public string OriginalPath;
    }
    [DataContract] public class Settings {
        [DataMember] public List<string> Folders = new List<string>();
        [DataMember] public List<Entry> Entries = new List<Entry>();
        [DataMember] public List<PackRecord> Packs = new List<PackRecord>();
        [DataMember] public bool UseSemantic;
        [DataMember] public bool? PreloadThumbnails;
        [DataMember] public bool OcrSetupPrompted;
        [DataMember] public bool SemanticPowerSaving;
        [DataMember] public bool AutomaticDescriptions;
        [DataMember] public bool AutoDraftRecommendations;
        [DataMember] public bool? ClickToAttach;
        [DataMember] public bool? ImagePastePreview;
        [DataMember] public bool GifAutoOpen;
        [DataMember] public bool GifDirectSend;
        [DataMember] public uint GifConfirmedProcess;
        [DataMember] public long GifConfirmedStart;
        [DataMember] public bool? FloatingPalette;
        [DataMember] public FloatingOffset FloatingPosition;
        [DataMember] public List<LongPreset> LongCons=new List<LongPreset>();
        [DataMember] public int UiScalePercent;
    }
    public static class Catalog {
        public static void AuthorizeGifOpen(Settings settings){settings.GifAutoOpen=true;settings.GifConfirmedProcess=0;settings.GifConfirmedStart=0;}
        public static string Normalize(string text) {
            try { return (text ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant(); }
            catch (ArgumentException) { return text ?? ""; }
        }
        public static bool Supported(string path) {
            return new[]{".png", ".jpg", ".jpeg", ".gif"}.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());
        }
        public static List<Entry> Search(IEnumerable<Entry> source, string query) {
            string[] tokens = Normalize(query).Split(new[]{' ', '\t', '\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
            return source.Select(e => new { E=e, Name=Normalize(e.DisplayName??System.IO.Path.GetFileNameWithoutExtension(e.Path)), Alias=Normalize(e.Aliases) })
                .Where(x=>tokens.All(t=>Matches(x.Name+" "+x.Alias,t)))
                .OrderByDescending(x=>tokens.Length>0 && tokens.Any(t=>(x.Alias??"").Split(',').Any(a=>a.Trim()==t)))
                .ThenByDescending(x=>x.E.LastUsed).ThenBy(x=>x.Name, StringComparer.Ordinal)
                .Take(3).Select(x=>x.E).ToList();
        }
        internal static bool Matches(string text,string token) {
            if(text.Contains(token))return true;
            // Small local vocabulary, not a model: Korean inflection differs here.
            if(token=="고맙")return text.Contains("고마워")||text.Contains("고마운");
            return false;
        }
        public static string DataPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "catalog.json");
        public static string Key(Entry e) {if((e.PackId??"").StartsWith("arca:")&&!String.IsNullOrEmpty(e.RemoteId))return e.PackId+"|"+e.RemoteId;return !String.IsNullOrEmpty(e.SourceUrl)?(e.PackId??"")+"|"+e.SourceUrl:e.Path;}
        public static void MergePack(Settings config,ImportedPack pack){var previous=config.Packs.FirstOrDefault(p=>p.Id==pack.Pack.Id);if(previous==null)config.Packs.Add(pack.Pack);else{previous.Title=pack.Pack.Title;previous.ThumbnailUrl=pack.Pack.ThumbnailUrl;previous.ThumbnailRemoteId=pack.Pack.ThumbnailRemoteId;previous.Seller=pack.Pack.Seller;}foreach(var entry in pack.Entries){var existing=config.Entries.FirstOrDefault(e=>Key(e)==Key(entry));if(existing==null)config.Entries.Add(entry);else{existing.SourceUrl=entry.SourceUrl;existing.Format=entry.Format;}}}
        public static Settings Load() {
            if (!File.Exists(DataPath)) return new Settings();
            using(var reader=new StreamReader(DataPath,Encoding.UTF8,true))using(var s=new MemoryStream(Encoding.UTF8.GetBytes(reader.ReadToEnd()))) {var config=(Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(s);if(config.Packs==null)config.Packs=new List<PackRecord>();if(config.LongCons==null)config.LongCons=new List<LongPreset>();return config;}
        }
        public static void Save(Settings config) {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DataPath));
            string temp=DataPath+".tmp";
            using(var s=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) {
                new DataContractJsonSerializer(typeof(Settings)).WriteObject(s, config);
                s.Flush(true);
            }
            if(File.Exists(DataPath)) File.Replace(temp,DataPath,DataPath+".bak");
            else File.Move(temp,DataPath);
        }
        public static List<Entry> Scan(Settings config, out int skipped) {
            int errors=0;
            var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string root in config.Folders) {
                var pending=new Stack<string>(); pending.Push(root);
                while(pending.Count>0) {
                    string dir=pending.Pop();
                    try {
                        foreach(string p in Directory.EnumerateFiles(dir)) if(Supported(p)) found.Add(System.IO.Path.GetFullPath(p));
                        foreach(string d in Directory.EnumerateDirectories(dir))
                            if((File.GetAttributes(d)&FileAttributes.ReparsePoint)==0) pending.Push(d);
                    } catch(IOException) { errors++; } catch(UnauthorizedAccessException) { errors++; }
                }
            }
            // Keep metadata for disconnected folders without displaying missing files.
            foreach(string path in found) if(!config.Entries.Any(e=>String.Equals(e.Path,path,StringComparison.OrdinalIgnoreCase)))
                config.Entries.Add(new Entry {Path=path});
            skipped=errors;
            return config.Entries.Where(e=>found.Contains(e.Path)||(e.PackId!=null&&(!String.IsNullOrEmpty(e.SourceUrl)||File.Exists(e.Path))&&(e.PackId=="personal"||config.Packs.Any(p=>p.Id==e.PackId)))).ToList();
        }
    }
    public static class Native {
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder text,int count);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint flags);
        public static string Title(IntPtr h) { var b=new StringBuilder(512); GetWindowText(h,b,b.Capacity); return b.ToString(); }
        public static bool IsKakao(IntPtr h) {
            if(h==IntPtr.Zero || !IsWindow(h)) return false;
            uint id; GetWindowThreadProcessId(h,out id);
            try { using(var p=Process.GetProcessById((int)id)) return String.Equals(p.ProcessName,"KakaoTalk",StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
    }
    public class MainForm:Form {
        Settings config;
        List<Entry> entries=new List<Entry>(), candidates=new List<Entry>();
        TextBox search=new TextBox(), aliases=new TextBox();
        Label status=new Label(), targetLabel=new Label(), detail=new Label();
        FlowLayoutPanel rows=new FlowLayoutPanel();
        LibraryView library;LibraryGrid rankedGrid;bool libraryMode=true;UiButton libraryTab,recommendTab;
        NotifyIcon tray;
        System.Windows.Forms.Timer watch=new System.Windows.Forms.Timer();
        CancellationTokenSource previewRequests=new CancellationTokenSource(),copyRequest;
        CheckBox follow=new CheckBox();
        IntPtr target=IntPtr.Zero;
        string targetTitle="";
        Entry selected;
        // Explicit, session-only mode: regular clicks otherwise keep their single-sticker behavior.
        bool collectionMode,collectionDouble;
        readonly List<Entry> collected=new List<Entry>();
        CollectionForm collectionWindow;UiButton collectionButton,doubleButton;LongConForm longWindow;bool longSelecting;readonly List<Entry> longPieces=new List<Entry>();PreviewChatTarget longDisplayedTarget;
        readonly System.Windows.Forms.Timer indexRefresh=new System.Windows.Forms.Timer{Interval=1200};
        readonly CatalogIndexer catalogIndexer,previewIndexer;
        readonly System.Windows.Forms.Timer previewSave=new System.Windows.Forms.Timer{Interval=350};bool previewDirty,previewRunning;int previewDone,previewFailed,previewTotal,previewCreated;string cachePhase="저장된 썸네일 확인 중",cacheProgress="";ShopForm activeShop;
        void FlushPreviewSave(){previewSave.Stop();if(previewDirty){previewDirty=false;Save();}}
        void ShowCacheProgress(string text){cacheProgress=text;if(activeShop!=null&&!activeShop.IsDisposed)activeShop.ShowCacheProgress(text);if(!IsDisposed&&libraryMode&&copyRequest==null)Tell(text);}
        void StartSemanticIndex(){if(previewOnly||IsDisposed||config==null)return;bool enabled=config.UseSemantic&&SemanticSearch.Ready;catalogIndexer.Update(entries,enabled);if(!enabled)semantic.ReleaseAnalyzer();}
        void ShowPreviewProgress(int done,int failed,int total){previewDone=done;previewFailed=failed;previewTotal=total;ShowCacheProgress(cachePhase+" · "+(done+failed)+" / "+total+" · "+(total==0?100:(done+failed)*100/total)+"%"+(failed>0?" · 실패 "+failed+"개":""));}
        void StopPreviews(){previewIndexer.Stop();previewRunning=false;FlushPreviewSave();ShowCacheProgress("썸네일 미리 불러오기 중지 · 저장한 캐시는 유지해요");StartSemanticIndex();}
        void QueuePreviews(bool force=false){if(previewOnly||IsDisposed||config==null)return;if(!force&&config.PreloadThumbnails==false){previewIndexer.Stop();previewRunning=false;ShowCacheProgress("썸네일 미리 불러오기 꺼짐 · 화면의 콘만 불러와요");StartSemanticIndex();return;}previewRunning=true;catalogIndexer.Stop();previewCreated=0;cachePhase="저장된 썸네일 확인 중";ShowPreviewProgress(0,0,config.Entries.Count);previewIndexer.Update(config.Entries.ToList(),true);}

        readonly SemanticSearch semantic=new SemanticSearch();CancellationTokenSource semanticRequest;int semanticVersion,collectionVersion;
        readonly PreviewPaste previewPaste;readonly bool previewOnly;
        TableLayoutPanel layout;bool compact,initialized,switchingRoom,libraryPinned;ChatAnchor anchor;long roomVersion;
        readonly Dictionary<string,string> roomQueries=new Dictionary<string,string>();
        readonly Dictionary<string,FloatingOffset> roomPositions=new Dictionary<string,FloatingOffset>();
        bool movingPalette;Point dragStart;string dragRoom;
        bool draftActive,draftProbeBusy;string draftText,draftSource;PreviewChatTarget draftTarget;int draftGeneration;DateTime nextDraftPoll=DateTime.MinValue;
        bool editing, refreshing, modal, shutdown;
        public MainForm(bool visualPreview=false) {
            previewSave.Tick+=(s,e)=>FlushPreviewSave();
            previewIndexer=new CatalogIndexer((entry,token)=>PreviewFiles.Prepare(entry,token,cached=>{cachePhase=cached?"저장된 썸네일 확인 중":"새 썸네일 준비 중";if(!cached)previewCreated++;ShowPreviewProgress(previewDone,previewFailed,previewTotal);}),(source,prepared)=>{if(IsDisposed||config==null||!config.Entries.Contains(source))return;bool changed=source.Path!=prepared.Path||source.OriginalPath!=prepared.OriginalPath||source.SourceUrl!=prepared.SourceUrl||source.Format!=prepared.Format;source.Path=prepared.Path;source.OriginalPath=prepared.OriginalPath;source.SourceUrl=prepared.SourceUrl;source.Format=prepared.Format;if(changed){previewDirty=true;if(!previewSave.Enabled)previewSave.Start();}if(library!=null)library.NotifyPrepared(source);if(rankedGrid!=null)rankedGrid.NotifyPrepared(source);},(done,failed)=>{previewRunning=false;FlushPreviewSave();ShowCacheProgress((previewCreated==0?"저장된 썸네일 확인 완료 · ":"썸네일 준비 완료 · ")+done+"개"+(failed>0?" · 실패 "+failed+"개 (새로고침으로 재시도)":" · 스크롤할 때 로컬에서 불러와요"));StartSemanticIndex();},true);
            previewIndexer.Progress=ShowPreviewProgress;
            catalogIndexer=new CatalogIndexer((entry,token)=>semantic.PrepareRemote(entry,token),(source,prepared)=>{if(IsDisposed||!entries.Contains(source))return;if(source.Path!=prepared.Path||source.OriginalPath!=prepared.OriginalPath){source.Path=prepared.Path;source.OriginalPath=prepared.OriginalPath;source.SourceUrl=prepared.SourceUrl;Save();}if(!indexRefresh.Enabled)indexRefresh.Start();},(done,failed)=>{semantic.ReleaseAnalyzer();if(!IsDisposed&&copyRequest==null&&(libraryMode||failed>0))Tell("추천 분석 · "+done+"개 준비"+(failed>0?" · "+failed+"개 실패\n일부 콘 분석을 멈췄어요. 새로고침으로 다시 시도할 수 있어요.":""));});
            catalogIndexer.Progress=(done,failed,total)=>{if(!IsDisposed&&libraryMode&&copyRequest==null)Tell("콘 자동 분석 · "+(done+failed)+" / "+total+(failed>0?" · "+failed+"개 실패":""));};
            indexRefresh.Tick+=(s,e)=>{indexRefresh.Stop();RefreshIndexedRecommendations();};
            previewOnly=visualPreview;previewPaste=new PreviewPaste(!visualPreview);semantic.Progress=message=>{if(copyRequest==null){Tell(message);SetRecommendationHint(message);}};
            Icon=Ui.AppIcon();Text="아지트콘"; Font=Ui.Regular(10); ForeColor=Ui.Ink;ClientSize=new Size(420,690);
            MinimumSize=new Size(350,590); FormBorderStyle=FormBorderStyle.Sizable;
            BackColor=Ui.Paper; StartPosition=FormStartPosition.CenterScreen;
            layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=1,RowCount=10};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,31));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,31));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,66));
            Controls.Add(layout);
            var heading=new Panel{Dock=DockStyle.Fill};var title=new Label{Text="아지트콘",Font=Ui.Bold(17),Dock=DockStyle.Fill};collectionButton=Ui.Button("콘 9분할",OpenCollection,true);collectionButton.Dock=DockStyle.Right;collectionButton.Width=118;doubleButton=Ui.Button("더블콘",OpenDouble,true);doubleButton.Dock=DockStyle.Right;doubleButton.Width=collectionButton.Width;heading.Controls.Add(title);heading.Controls.Add(collectionButton);heading.Controls.Add(doubleButton);layout.Controls.Add(heading,0,0);
            var toolbar=new FlowLayoutPanel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            toolbar.Controls.Add(Button("팩 관리",OpenShop)); toolbar.Controls.Add(Button("새로고침",Rescan));
            toolbar.Controls.Add(Button("설정",OpenSettings));toolbar.Controls.Add(Button("롱콘",OpenLong)); layout.Controls.Add(toolbar,0,1);
            follow.Text="카톡 입력창 따라가기"; follow.Dock=DockStyle.Fill; follow.CheckedChanged+=(s,e)=>{if(config!=null){config.FloatingPalette=follow.Checked;Save();}ApplyFloatingMode();if(initialized)TickWatch();}; layout.Controls.Add(follow,0,2);
            search.AccessibleName="파일명 또는 별칭 검색";
            search.TextChanged+=(s,e)=>{ClearDraft(false);if(!switchingRoom&&compact&&anchor!=null)roomQueries[anchor.Key]=search.Text;Render();};var searchField=Ui.Field(search,"콘 이름, 별칭, 팩으로 검색");searchField.Dock=DockStyle.Fill;layout.Controls.Add(searchField,0,3);
            rows.Dock=DockStyle.Fill; rows.FlowDirection=FlowDirection.TopDown; rows.WrapContents=false; rows.AutoScroll=true;
            rows.SizeChanged+=(s,e)=>{foreach(Control c in rows.Controls)c.Width=Math.Max(240,rows.ClientSize.Width-22);};
            var browse=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=Padding.Empty};browse.RowStyles.Add(new RowStyle(SizeType.Absolute,32));browse.RowStyles.Add(new RowStyle(SizeType.Percent,100));var browseTabs=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};libraryTab=Ui.Button("내 콘",()=>SetLibraryMode(true));recommendTab=Ui.Button("최근 사용",()=>SetLibraryMode(false));libraryTab.Size=recommendTab.Size=new Size(92,30);libraryTab.TabStyle=recommendTab.TabStyle=true;libraryTab.Active=true;browseTabs.Controls.AddRange(new Control[]{libraryTab,recommendTab});browse.Controls.Add(browseTabs,0,0);var browseBody=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};library=new LibraryView(Choose,ShowLibraryMenu,Save,BeginStickerSelection);rankedGrid=new LibraryGrid(Choose,ShowLibraryMenu,Save){InteractionStarting=BeginStickerSelection,Dock=DockStyle.Fill,Ranked=true,AccessibleName="문장 추천 격자",Visible=false};browseBody.Controls.Add(rows);browseBody.Controls.Add(rankedGrid);browseBody.Controls.Add(library);rows.Visible=false;browse.Controls.Add(browseBody,0,1);layout.Controls.Add(browse,0,4);
            detail.Text="결과 선택 후 별칭 편집 (쉼표로 구분)"; detail.Dock=DockStyle.Fill; layout.Controls.Add(detail,0,5);
            aliases.AccessibleName="선택한 콘의 별칭";
            aliases.TextChanged+=(s,e)=>{ if(!editing && selected!=null) detail.Text="별칭을 바꿨으면 저장을 눌러주세요";};var aliasField=Ui.Field(aliases,"선택한 콘의 별칭");aliasField.Dock=DockStyle.Fill;layout.Controls.Add(aliasField,0,6);
            var actions=new FlowLayoutPanel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            actions.Controls.Add(Button("별칭 저장",SaveAliases)); actions.Controls.Add(Button("원본 파일 복사",()=>Copy(selected,false,false)));
            layout.Controls.Add(actions,0,7);
            targetLabel.Dock=DockStyle.Fill; targetLabel.AutoEllipsis=true; targetLabel.Text="카톡을 활성화하면 위치를 따라가요"; layout.Controls.Add(targetLabel,0,8);
            status.Dock=DockStyle.Fill;status.ForeColor=Ui.Muted;status.Font=Ui.Regular(9);status.Text="카톡 입력창 클릭 → 정지 콘 선택 → 첨부 미리보기\n대상이 불확실하면 이미지 복사만 해요. 전송은 직접 해주세요."; layout.Controls.Add(status,0,9);
            tray=new NotifyIcon {Icon=Ui.AppIcon(16),Text="아지트콘 — 로컬 콘 검색",Visible=!visualPreview};
            var menu=new ContextMenuStrip(); menu.Items.Add("아지트콘 열기",null,(s,e)=>Open());menu.Items.Add("입력창 옆 플로팅으로 돌아가기",null,(s,e)=>{follow.Checked=true;TickWatch();});menu.Items.Add("로컬 폴더 관리",null,(s,e)=>{Open();ManageFolders();});
            menu.Items.Add("로컬 폴더 추가",null,(s,e)=>{Open();PickFolder();});menu.Items.Add("로컬 의미 검색 설정",null,(s,e)=>{Open();OpenSettings();});
            menu.Items.Add("플로팅 위치 자동 배치로 초기화",null,(s,e)=>{roomPositions.Clear();config.FloatingPosition=null;Save();if(compact)DockToTarget();Tell("플로팅 위치를 자동 배치로 되돌렸어요.");});
            var pasteMenu=new ToolStripMenuItem("콘 선택 시 카톡 첨부 미리보기"){CheckOnClick=true};menu.Opening+=(s,e)=>pasteMenu.Checked=config!=null&&config.ImagePastePreview!=false;pasteMenu.Click+=(s,e)=>{config.ImagePastePreview=pasteMenu.Checked;Save();};menu.Items.Add(pasteMenu);
            menu.Items.Add("종료",null,(s,e)=>{shutdown=true;Close();}); tray.ContextMenuStrip=menu; tray.DoubleClick+=(s,e)=>Open();
            FormClosing+=(s,e)=>{if(!shutdown && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
            FormClosed+=(s,e)=>{previewRequests.Cancel();if(copyRequest!=null)copyRequest.Cancel();if(semanticRequest!=null)semanticRequest.Cancel();previewIndexer.Dispose();FlushPreviewSave();previewSave.Dispose();catalogIndexer.Dispose();indexRefresh.Dispose();semantic.Dispose();previewPaste.Dispose();watch.Stop();tray.Dispose();ClearRows();};
            try {config=Catalog.Load();} catch(Exception ex) {config=new Settings(); MessageBox.Show("설정 파일을 읽지 못했어요. 원본 보존을 위해 앱을 종료해요.\n"+ex.Message);shutdown=true; Shown+=(s,e)=>Close();return;}
            Action<uint,long> verifiedGif=(pid,start)=>{config.GifConfirmedProcess=pid;config.GifConfirmedStart=start;Save();};
            Action suspendGif=()=>{config.GifAutoOpen=false;config.GifConfirmedProcess=0;config.GifConfirmedStart=0;Save();};
            previewPaste.ConfigurePersistentGif(config.GifAutoOpen,config.GifConfirmedProcess,config.GifConfirmedStart,suspendGif,verifiedGif);
            previewPaste.ConfigureGifDirectSend(config.GifDirectSend);
            var gifDirectMenu=new ToolStripMenuItem("움짤 클릭 시 바로 전송 (확인창 자동 처리)"){Checked=config.GifDirectSend,CheckOnClick=true};
            gifDirectMenu.Click+=(s,e)=>SetGifDirectSend(gifDirectMenu.Checked);
            tray.ContextMenuStrip.Items.Insert(tray.ContextMenuStrip.Items.Count-1,gifDirectMenu);
            tray.ContextMenuStrip.Opening+=(s,e)=>gifDirectMenu.Checked=config.GifDirectSend;
            semantic.PowerSaving=config.SemanticPowerSaving;
            semantic.AutomaticAnalysis=config.AutomaticDescriptions;
            config.ClickToAttach=false;
            follow.Checked=config.FloatingPalette!=false;ApplyFloatingMode();initialized=true;search.HandleCreated+=(s,e)=>UpdateSearchCue();UpdateSearchCue();
            watch.Interval=100; watch.Tick+=(s,e)=>TickWatch(); if(!visualPreview)watch.Start();
            ResizeBegin+=(s,e)=>{movingPalette=true;dragStart=Location;dragRoom=compact&&anchor!=null?anchor.Key:null;};
            ResizeEnd+=(s,e)=>{movingPalette=false;if(compact&&anchor!=null&&dragRoom==anchor.Key&&Location!=dragStart&&ChatFloating.Current(anchor)){var fresh=ChatFloating.Find(anchor.Window);if(fresh!=null){anchor=fresh;var offset=FloatingOffset.From(anchor.InputRect,Location);roomPositions[anchor.Key]=offset;config.FloatingPosition=offset;Save();Tell("플로팅 위치를 기억했어요 · 제목 표시줄을 끌어 다시 바꿀 수 있어요.");}}dragRoom=null;if(compact)DockToTarget();};
            VisibleChanged+=(s,e)=>{if(Visible){if(!refreshing)Render();}else{previewRequests.Cancel();if(copyRequest!=null)copyRequest.Cancel();if(semanticRequest!=null)semanticRequest.Cancel();if(collectionWindow!=null&&!collectionWindow.IsDisposed)collectionWindow.Hide();}};
            Shown+=async(s,e)=>{Rescan();TickWatch();if(!previewOnly)await CheckOcrAtStartup();};
        }
        async Task CheckOcrAtStartup(){if(config.GifDirectSend)return;var result=await OcrSupport.CheckAsync();if(IsDisposed||shutdown||modal||copyRequest!=null||!OcrSupport.ShouldPrompt(config.OcrSetupPrompted,result))return;modal=true;try{using(var setup=new OcrSetupForm(result))setup.ShowDialog(this);config.OcrSetupPrompted=true;Save();}finally{modal=false;}}
        void SetGifDirectSend(bool enabled){config.GifDirectSend=enabled;if(enabled)config.ImagePastePreview=true;previewPaste.ConfigureGifDirectSend(enabled);Save();Tell(enabled?"움짤 바로 전송 켜짐 · 클릭하면 설정 확인 없이 파일을 열고 전송을 요청해요.":"움짤 바로 전송 꺼짐 · 전송은 직접 해주세요.");}
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
            // These are form-local accelerators. No global keyboard hook or RegisterHotKey.
            if(!libraryMode&&(ContainsFocus||Native.GetForegroundWindow()==Handle) && (keyData&Keys.Modifiers)==Keys.Alt) {
                Keys key=keyData&Keys.KeyCode;int i=key>=Keys.NumPad1&&key<=Keys.NumPad3?(int)key-(int)Keys.NumPad1:(int)key-(int)Keys.D1;
                if(i>=0&&i<3) {if(i<candidates.Count) Choose(candidates[i],false);return true;}
            }
            if(keyData==Keys.Escape) {Hide();return true;}
            return base.ProcessCmdKey(ref msg,keyData);
        }
        Button Button(string text,Action action) {
            var b=new UiButton {Text=text,AutoSize=true,Height=30,Font=Ui.Regular(9),Quiet=true,Padding=new Padding(8,0,8,0),Margin=new Padding(0,0,6,0)};
            b.Click+=(s,e)=>action(); return b;
        }
        void Open() {BeginStickerSelection();Show();Activate();search.Focus();}
        public static void RenderDesign(bool recommendations=false,bool ranked=false){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);foreach(bool floating in new[]{true,false})using(var form=new MainForm(true)){form.follow.Checked=floating;form.ApplyFloatingMode();form.entries=form.config.Entries.ToList();if(recommendations||ranked)form.SetLibraryMode(false);using(var surface=new UserControl{Size=form.ClientSize,Font=form.Font,BackColor=Ui.Paper,ForeColor=Ui.Ink}){form.Controls.Remove(form.layout);surface.Controls.Add(form.layout);surface.CreateControl();form.Render(ranked?form.entries.Where(entry=>!String.IsNullOrEmpty(entry.Path)&&File.Exists(entry.Path)).Take(SemanticSearch.RecommendationLimit).ToList():null);if(ranked){surface.Size=form.ClientSize;surface.PerformLayout();}form.targetLabel.Text="카톡 입력창 옆에 · 전송은 직접";form.status.Text="클릭으로 콘 선택 · 전송은 직접";for(int i=0;i<20;i++){Application.DoEvents();Thread.Sleep(60);}using(var bitmap=new Bitmap(surface.Width,surface.Height)){surface.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,ranked?(floating?"ranked-floating.png":"ranked-library.png"):recommendations?(floating?"recent-floating.png":"recent-library.png"):(floating?"design-floating.png":"design-library.png")));}if(form.targetLabel.Bottom>form.layout.Height||form.library.Width<150)throw new Exception("Palette layout exceeds available area");}form.tray.Dispose();form.previewPaste.Dispose();form.watch.Dispose();}File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"design-render.txt"),"PASS: compact and expanded layouts rendered from actual controls. No visible form, tray icon, window observation hooks, clipboard, or input.\r\n");}
        public static void RenderRankedSample(List<Entry> found,string name,string note=null){using(var form=new MainForm(true)){form.SetLibraryMode(false);form.ClientSize=new Size(650,750);using(var surface=new UserControl{Size=form.ClientSize,Font=form.Font,BackColor=Ui.Paper}){form.Controls.Remove(form.layout);surface.Controls.Add(form.layout);surface.CreateControl();form.Render(found);if(note!=null)form.Tell(note);for(int i=0;i<15;i++){Application.DoEvents();System.Threading.Thread.Sleep(60);}using(var bitmap=new Bitmap(surface.Width,surface.Height)){surface.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name));}}form.tray.Dispose();form.previewPaste.Dispose();form.watch.Dispose();}}
        void SetLibraryMode(bool show,bool automatic=false){BeginStickerSelection();if(!automatic)libraryPinned=show;if(show)ClearDraft(false);rankedGrid.Visible=false;libraryMode=show;libraryTab.Active=show;recommendTab.Active=!show;libraryTab.Invalidate();recommendTab.Invalidate();library.Visible=show;rows.Visible=!show;ApplyFloatingMode();}
        void ShowLibraryMenu(Entry entry,Point point){var menu=new ContextMenuStrip();menu.Items.Add("별칭 편집",null,(s,e)=>EditAliases(entry));menu.Items.Add("카톡 호환 PNG/GIF 파일 복사",null,(s,e)=>Copy(entry,true,false));menu.Items.Add("원본 파일만 복사",null,(s,e)=>Copy(entry,false,false));menu.Closed+=(s,e)=>Ui.DisposeMenuLater(menu);menu.Show(point);}
        void ApplyFloatingMode(){if(layout==null)return;compact=follow.Checked;SuspendLayout();MinimumSize=compact?new Size(UiZoom.Installed(this)?UiZoom.Px(310):310,UiZoom.Installed(this)?UiZoom.Px(360):360):new Size(UiZoom.Installed(this)?UiZoom.Px(350):350,UiZoom.Installed(this)?UiZoom.Px(590):590);layout.Padding=new Padding(UiZoom.Installed(this)?UiZoom.Px(compact?10:14):(compact?10:14));int[] heights=compact?new[]{44,30,0,46,-1,0,0,0,28,32}:new[]{44,32,0,46,-1,26,31,36,30,42};for(int i=0;i<heights.Length;i++){var control=layout.GetControlFromPosition(0,i);if(control!=null)control.Visible=heights[i]!=0;if(i!=4){layout.RowStyles[i].SizeType=SizeType.Absolute;layout.RowStyles[i].Height=UiZoom.Installed(this)?UiZoom.Px(heights[i]):heights[i];}}ClientSize=compact?new Size(UiZoom.Installed(this)?UiZoom.Px(400):400,UiZoom.Installed(this)?UiZoom.Px(libraryMode?600:450):(libraryMode?600:450)):new Size(UiZoom.Installed(this)?UiZoom.Px(500):500,UiZoom.Installed(this)?UiZoom.Px(730):730);UpdateAliasLayout();ResumeLayout(true);if(config!=null&&!refreshing)Render();}
        void BindRoom(ChatAnchor next){if(anchor==null||anchor.Key!=next.Key){if(anchor!=null)roomQueries[anchor.Key]=search.Text;roomVersion++;if(copyRequest!=null)copyRequest.Cancel();anchor=next;target=next.Window;targetTitle=next.Title;string query;roomQueries.TryGetValue(next.Key,out query);switchingRoom=true;try{search.Text=query??"";}finally{switchingRoom=false;}}else anchor=next;}
        void SetRecommendationCaption(string text){recommendTab.Text=text;recommendTab.AccessibleName=text;recommendTab.Invalidate();}
        void SetRecommendationHint(string text){var hint=rows.Controls.OfType<Label>().FirstOrDefault();if(hint!=null)hint.Text=text;}
        void Tell(string text) {status.Text=text;if(collectionWindow!=null&&!collectionWindow.IsDisposed&&collectionWindow.Visible)collectionWindow.ShowStatus(text);}
        void Save() {if(previewOnly)return;try{Catalog.Save(config);}catch(Exception ex){Tell("저장 실패: "+ex.Message);}}
        void UpdateSearchCue(){Ui.Cue(search,config!=null&&config.AutoDraftRecommendations?"직접 검색 · 비워두면 카톡 문장 추천":"콘 이름, 별칭, 팩으로 검색");}
        async void ExitForUpdate(){BeginStickerSelection();if(longWindow!=null&&!longWindow.IsDisposed){longWindow.CancelForUpdate();for(int i=0;i<100&&longWindow.Busy;i++)await Task.Delay(100);if(longWindow.Busy){Tell("롱콘 중단 후 업데이트를 다시 실행해주세요.");return;}longWindow.Close();}shutdown=true;Close();}
        void OpenSettings(){BeginStickerSelection();modal=true;try{using(var settings=new SemanticSettingsForm(config.UseSemantic,value=>{config.UseSemantic=value;Save();if(!value){if(semanticRequest!=null)semanticRequest.Cancel();semanticVersion++;catalogIndexer.Stop();ClearDraft(false);semantic.Dispose();}},config.AutoDraftRecommendations,value=>{config.AutoDraftRecommendations=value;ClearDraft(false);Save();UpdateSearchCue();},config.SemanticPowerSaving,value=>{config.SemanticPowerSaving=value;semantic.PowerSaving=value;Save();},config.AutomaticDescriptions,value=>{catalogIndexer.Stop();config.AutomaticDescriptions=value;semantic.AutomaticAnalysis=value;if(semanticRequest!=null)semanticRequest.Cancel();semanticVersion++;Save();})){settings.AddThumbnailSettings(config.PreloadThumbnails!=false,value=>{config.PreloadThumbnails=value;Save();if(value)QueuePreviews(true);else StopPreviews();},()=>QueuePreviews(true),StopPreviews,()=>cacheProgress+"\n메모리: "+SharedPreviews.Status,()=>previewTotal==0?0:(previewDone+previewFailed)*100/previewTotal,()=>previewRunning);settings.AddOcrSettings(config.GifDirectSend,SetGifDirectSend);UiZoom.AddSettings(settings,follow.Checked,value=>follow.Checked=value,config.UiScalePercent,value=>{config.UiScalePercent=value;Save();},()=>{roomPositions.Clear();config.FloatingPosition=null;Save();if(compact)DockToTarget();});CacheMaintenance.AddTab(settings,flags=>{CacheMaintenance.Schedule(AppDomain.CurrentDomain.BaseDirectory,flags);if((flags&CacheMaintenance.Downloaded)!=0){StopPreviews();catalogIndexer.Stop();config.PreloadThumbnails=false;config.AutomaticDescriptions=false;semantic.AutomaticAnalysis=false;Save();}});AppUpdateForm.AddTab(settings,ExitForUpdate);settings.ShowDialog(this);}}finally{modal=false;if(!shutdown&&!IsDisposed){if(!previewRunning)StartSemanticIndex();Render();}}}
        void FinishCopy(CancellationTokenSource request,bool collection){if(copyRequest==request){if(collection&&collectionWindow!=null&&!collectionWindow.IsDisposed)collectionWindow.SetBusy(false);copyRequest=null;}request.Dispose();}
        bool AutomaticResultCurrent(int generation,bool draft){return copyRequest==null&&!(draft&&libraryMode&&libraryPinned)&&generation==(draft?draftGeneration:semanticVersion);}
        bool RefreshIndexedRecommendations(){if(IsDisposed||modal||refreshing||libraryMode||copyRequest!=null)return false;Render();return true;}
        internal static void TestCopyRefreshRace(){
            string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"copy-refresh-test.txt");int count=0;Action<bool> check=b=>{if(!b)throw new Exception("Copy refresh race check "+count);count++;};
            try{using(var form=new MainForm(true)){
                check(form.collectionButton.Size==form.doubleButton.Size);
                form.collected.Add(new Entry{DisplayName="first"});form.collected.Add(new Entry{DisplayName="second"});
                form.CompleteCollectionAttachment(false,true,form.collected.ToList());check(form.collected.Count==2);
                form.CompleteCollectionAttachment(true,true,form.collected.ToList());check(form.collected.Count==0);
                form.collectionMode=true;form.collectionDouble=false;form.collected.Add(new Entry{DisplayName="split"});
                form.CompleteCollectionAttachment(false,false,form.collected.ToList());check(form.collectionMode&&form.collected.Count==1);
                form.CompleteCollectionAttachment(true,false,form.collected.ToList());check(!form.collectionMode&&!form.collectionDouble&&form.collected.Count==0&&form.collectionButton.Text=="콘 9분할");

                form.config=new Settings();form.config.UseSemantic=true;form.libraryMode=false;form.entries.Clear();form.draftActive=false;form.semanticVersion=20;form.draftGeneration=30;form.semanticRequest=new CancellationTokenSource();
                form.BeginStickerSelection();check(form.semanticRequest.IsCancellationRequested);check(!form.AutomaticResultCurrent(20,false)&&!form.AutomaticResultCurrent(30,true));
                form.copyRequest=new CancellationTokenSource();var copying=form.copyRequest;var picked=new Entry{DisplayName="selected animated sticker"};form.selected=picked;string tab=form.recommendTab.Text;
                check(!form.AutomaticResultCurrent(form.semanticVersion,false));check(!form.AutomaticResultCurrent(form.draftGeneration,true));
                check(!form.RefreshIndexedRecommendations());check(!copying.IsCancellationRequested&&form.selected==picked&&form.recommendTab.Text==tab);
                form.copyRequest=null;copying.Dispose();check(form.AutomaticResultCurrent(form.semanticVersion,false)&&form.AutomaticResultCurrent(form.draftGeneration,true));
                // A deliberate user refresh still cancels a pending copy.
                form.copyRequest=new CancellationTokenSource();form.Render();check(form.copyRequest.IsCancellationRequested);form.copyRequest.Dispose();form.copyRequest=null;form.semanticRequest.Dispose();form.semanticRequest=null;
                var older=new CancellationTokenSource();form.copyRequest=older;form.BeginStickerSelection();check(older.IsCancellationRequested);var newer=new CancellationTokenSource();form.copyRequest=newer;form.FinishCopy(older,true);check(form.copyRequest==newer&&!newer.IsCancellationRequested);form.FinishCopy(newer,false);check(form.copyRequest==null);
                form.libraryMode=true;form.BeginStickerSelection();int pinnedVersion=form.draftGeneration;check(form.libraryPinned&&!form.AutomaticResultCurrent(pinnedVersion,true));
                form.copyRequest=new CancellationTokenSource();var pinnedCopy=form.copyRequest;form.FinishCopy(pinnedCopy,false);check(!form.AutomaticResultCurrent(form.draftGeneration,true)&&form.libraryMode);
                form.libraryMode=false;form.libraryPinned=false;check(form.AutomaticResultCurrent(form.draftGeneration,true));
                form.previewIndexer.Dispose();form.catalogIndexer.Dispose();form.previewSave.Dispose();form.indexRefresh.Dispose();form.semantic.Dispose();form.previewPaste.Dispose();form.watch.Dispose();form.tray.Dispose();
            }File.WriteAllText(report,"PASS: "+count+" stale draft/search invalidation, background refresh suppressed during copy, selected sticker/tab preserved, manual library pinned after copy, explicit recommendation mode resumes, explicit user refresh still cancels. Offscreen form only; no clipboard, key injection, UIA, GPU, network or KakaoTalk interaction.\r\n");}catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}
        }
        void BeginStickerSelection(){
            if(libraryMode)libraryPinned=true;
            // Invalidate pending draft/semantic callbacks before a mouse-up starts copying.
            if(copyRequest!=null)copyRequest.Cancel();draftGeneration++;semanticVersion++;indexRefresh.Stop();if(semanticRequest!=null)semanticRequest.Cancel();
        }
        void Choose(Entry entry,bool shift){if(longWindow!=null&&!longWindow.IsDisposed&&longWindow.Busy){Tell("롱콘 전송 중이에요. 중단 버튼을 먼저 눌러주세요.");return;}if(longSelecting&&longWindow!=null&&!longWindow.IsDisposed&&longWindow.Visible){BeginStickerSelection();longWindow.Add(entry);return;}if(collectionMode&&collectionDouble&&copyRequest!=null){Tell("콘을 준비 중이에요. 잠시 후 두 번째 콘을 골라주세요.");return;}BeginStickerSelection();if(refreshing){Tell("콘 목록을 갱신 중이에요. 잠시 후 골라주세요.");return;}Select(entry);if(collectionMode){ChooseSplitSource(entry);return;}if(shift){Tell("9분할은 위의 ‘콘 9분할’ 버튼을 눌러 사용해주세요.");return;}Copy(entry,true);}
        async void ChooseSplitSource(Entry entry){
            if(collectionDouble&&collected.Count>=2){Tell("두 개를 골랐어요. 더블콘 창에서 빼거나 전체 선택을 해제해주세요.");return;}if(!collectionDouble)collected.Clear();collectionVersion++;UpdateCollection();var request=new CancellationTokenSource();copyRequest=request;int version=collectionVersion;if(collectionWindow!=null)collectionWindow.SetBusy(true);
            try{string path=await Task.Run(()=>{string file=DcShop.EnsureImage(entry,request.Token);using(var image=Image.FromFile(file)){if(!collectionDouble&&ImageBundle.Animated(image))throw new InvalidOperationException("움짤은 9분할하지 않아요. 창을 닫고 개별 첨부해주세요.");if((!collectionDouble&&(image.Width<3||image.Height<3))||(long)image.Width*image.Height>16777216)throw new InvalidOperationException("이미지 크기가 9분할에 맞지 않아요.");}return file;},request.Token);
                if(request.IsCancellationRequested||IsDisposed||!collectionMode||version!=collectionVersion)return;
                entry.Path=path;if(!collectionDouble)collected.Clear();collected.Add(entry);collectionVersion++;UpdateCollection();Tell(collectionDouble?"더블콘 "+collected.Count+" / 2 · 원본 두 개를 함께 첨부해요.":"콘 하나를 골랐어요. 9분할 창에서 첨부해주세요.");
            }catch(Exception ex){if(!request.IsCancellationRequested&&!IsDisposed)Tell(ex.Message);}finally{FinishCopy(request,true);}
        }
        void UpdateCollection(){collectionButton.Text=collectionMode&&!collectionDouble?"9분할 선택 중":"콘 9분할";doubleButton.Text=collectionMode&&collectionDouble?"더블 "+collected.Count+"/2":"더블콘";if(collectionWindow!=null&&!collectionWindow.IsDisposed)collectionWindow.RefreshQueue();}
        void OpenLong(){BeginStickerSelection();if(collectionWindow!=null&&!collectionWindow.IsDisposed)collectionWindow.Close();longSelecting=true;
            if(longWindow==null||longWindow.IsDisposed){longWindow=new LongConForm(longPieces,config.LongCons,key=>config.Entries.FirstOrDefault(entry=>Catalog.Key(entry)==key),Save,RunLong);
                longWindow.Activated+=(sender,args)=>{if(!longWindow.Busy){longDisplayedTarget=previewPaste.Snapshot();longWindow.Target(longDisplayedTarget==null?null:longDisplayedTarget.Title);}};
                longWindow.FormClosed+=(sender,args)=>{longSelecting=false;if(copyRequest!=null)copyRequest.Cancel();};}
            longDisplayedTarget=previewPaste.Snapshot();longWindow.Target(longDisplayedTarget==null?null:longDisplayedTarget.Title);longWindow.Show(this);longWindow.Activate();
        }
        async Task RunLong(CancellationToken token,IProgress<string> progress){
            BeginStickerSelection();var destination=longDisplayedTarget;var current=previewPaste.Snapshot();
            if(destination==null||current==null||destination.Version!=current.Version||destination.Window!=current.Window||destination.Editor!=current.Editor)throw new InvalidOperationException("전송 대상을 다시 확인해주세요. 카톡 입력창 클릭 후 롱콘 창으로 돌아오세요.");
            var selected=longPieces.ToList();long versionAtStart=roomVersion;var request=CancellationTokenSource.CreateLinkedTokenSource(token);copyRequest=request;int lastRequested=0;string lastStage="prepare";
            Action<int,string> trace=(n,stage)=>{lastRequested=n;lastStage=stage;try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"longcon-last.txt"),DateTime.UtcNow.ToString("o")+"\r\nStage="+stage+"; Requested="+n+"; Total="+selected.Count);}catch(IOException){}};trace(0,"prepare");
            try{progress.Report("조각 "+selected.Count+"장을 모두 미리 준비하고 있어요…");using(var prepared=await Task.Run(()=>LongPrepared.Create(selected,request.Token,n=>progress.Report("미리 준비 "+n+" / "+selected.Count+"장")),request.Token)){
                request.Token.ThrowIfCancellationRequested();var outcome=await previewPaste.TryLongCon(destination,prepared,longWindow.Handle,request.Token,progress,()=>longSelecting&&roomVersion==versionAtStart,trace);
                if(outcome.Requested>0){foreach(var entry in selected.Take(outcome.Requested))entry.LastUsed=DateTime.UtcNow.Ticks;Save();}
                progress.Report(outcome.Complete?"롱콘 "+outcome.Requested+"장 전송 요청 완료 · 카톡에서 결과를 확인해주세요.":LongFlow.Reason(outcome.Stage)+" · 붙여넣기 "+outcome.Pasted+"장 / 자동 Enter "+outcome.Requested+"회. 이미 올라간 조각은 재시도하지 마세요.");
            }}catch(OperationCanceledException){trace(lastRequested,"canceled-"+lastStage);throw;}catch(Exception ex){trace(lastRequested,"failed-"+lastStage+" / "+ex.GetType().Name);throw;}finally{FinishCopy(request,false);}
        }
        void OpenCollection(){OpenCollectionMode(false);}
        void OpenDouble(){OpenCollectionMode(true);}
        void OpenCollectionMode(bool doubleMode){BeginStickerSelection();longSelecting=false;if(longWindow!=null&&!longWindow.IsDisposed)longWindow.Hide();if(collectionWindow!=null&&!collectionWindow.IsDisposed&&collectionDouble!=doubleMode){collectionWindow.Close();collected.Clear();}collectionDouble=doubleMode;collectionMode=true;
            if(collectionWindow==null||collectionWindow.IsDisposed){collectionWindow=new CollectionForm(collected,index=>{if(copyRequest!=null)copyRequest.Cancel();if(index<collected.Count)collected.RemoveAt(index);collectionVersion++;UpdateCollection();},()=>{if(copyRequest!=null)copyRequest.Cancel();collected.Clear();collectionVersion++;UpdateCollection();},CopyCollection,collectionDouble);
                collectionWindow.FormClosed+=(s,e)=>{collectionMode=false;collectionVersion++;if(copyRequest!=null)copyRequest.Cancel();collectionButton.Text="콘 9분할";doubleButton.Text="더블콘";};
            }
            UpdateCollection();collectionWindow.Show(this);collectionWindow.Activate();Tell(collectionDouble?"원본 두 개를 차례로 선택해주세요. 움짤도 함께 첨부할 수 있어요.":"콘 목록에서 정지 콘 하나를 선택해주세요. 창을 닫으면 개별 첨부로 돌아가요.");
        }
        async void CopyCollection(BundleMode mode){if(!collectionMode||(collectionDouble?(mode!=BundleMode.Double||collected.Count!=2):(mode!=BundleMode.SplitNine||collected.Count!=1)))return;BeginStickerSelection();var destination=previewPaste.Snapshot();long startedRoom=roomVersion;IntPtr sourceWindow=collectionWindow!=null&&!collectionWindow.IsDisposed?collectionWindow.Handle:Handle;if(compact&&(anchor==null||destination==null||destination.Window!=anchor.Window||destination.Editor!=anchor.Editor||destination.Title!=anchor.Title))destination=null;var request=new CancellationTokenSource();copyRequest=request;int version=collectionVersion;var chosen=collected[0];var selectedBatch=collected.ToList();bool isDouble=collectionDouble;copyAssetDescription=isDouble?"Double original file list":"SplitNine PNG file list";copyGeometryDescription="manual Open and Send";TraceCopy(request,"Preparing nine-slice files");if(collectionWindow!=null)collectionWindow.SetBusy(true);
            try{Tell(isDouble?"선택한 원본 두 개를 준비하고 있어요...":"선택한 콘을 9개의 PNG로 나누고 있어요...");var paths=await Task.Run(()=>{var originals=selectedBatch.Select(entry=>isDouble?DcShop.EnsureAttachment(entry,request.Token):DcShop.EnsureImage(entry,request.Token)).ToArray();return ImageBundle.Create(originals,isDouble?BundleMode.Double:BundleMode.SplitNine,request.Token,chosen.DisplayName);},request.Token);
                if(request.IsCancellationRequested||IsDisposed||!collectionMode||version!=collectionVersion){TraceCopy(request,"Nine-slice copy canceled before clipboard");return;}
                if(!ContainsFocus&&Native.GetForegroundWindow()!=Handle&&!SplitWindowForeground(collectionWindow,Native.GetForegroundWindow())){TraceCopy(request,"Nine-slice source window lost foreground");Tell("분할 완료 · 포커스가 바뀌어 복사를 멈췄어요. 다시 복사해주세요.");return;}
                if(startedRoom!=roomVersion)destination=null;
                var outcome=await previewPaste.TryFileSelection(destination,request.Token,paths,sourceWindow,isDouble?2:9);
                TraceCopy(request,(outcome.Requested?"Image files selected in picker":"Image-file picker stopped")+" ["+outcome.Stage+" / "+outcome.Block+"]");
                CompleteCollectionAttachment(outcome.Requested,isDouble,selectedBatch);Tell(outcome.Reason);
            }catch(Exception ex){TraceCopy(request,"Nine-slice copy failed: "+ex.GetType().Name);if(!request.IsCancellationRequested&&!IsDisposed)Tell("9분할 실패: "+ex.Message);}finally{FinishCopy(request,true);}
        }
        void CompleteCollectionAttachment(bool requested,bool isDouble,IEnumerable<Entry> selectedBatch){
            if(!requested)return;foreach(var entry in selectedBatch)entry.LastUsed=DateTime.UtcNow.Ticks;Save();
            collected.Clear();collectionVersion++;if(!isDouble){collectionMode=false;collectionDouble=false;if(collectionWindow!=null&&!collectionWindow.IsDisposed)collectionWindow.Hide();}UpdateCollection();
        }
        // Disabling the clicked copy button can clear ContainsFocus while the
        // owned split window remains foreground. Check its HWND, not its child focus.
        internal static bool SplitWindowForeground(Form window,IntPtr foreground){return window!=null&&!window.IsDisposed&&window.IsHandleCreated&&window.Visible&&foreground==window.Handle;}
        void OpenShop() {BeginStickerSelection();
            if(refreshing){Tell("새로고침이 끝난 뒤 열어주세요.");return;}modal=true;
            foreach(var pack in config.Packs)if(String.IsNullOrEmpty(pack.ThumbnailUrl)){var entry=config.Entries.FirstOrDefault(e=>e.PackId==pack.Id&&!String.IsNullOrEmpty(e.SourceUrl));if(entry!=null)pack.ThumbnailUrl=entry.SourceUrl;}
            try{using(var shop=new ShopForm(config.Packs,pack=>{
                Catalog.MergePack(config,pack);
                Save();QueuePreviews();
            },entry=>{if(!config.Entries.Any(e=>Catalog.Key(e)==Catalog.Key(entry)))config.Entries.Add(entry);Save();QueuePreviews();},id=>{config.Packs.RemoveAll(p=>p.Id==id);Save();})){activeShop=shop;shop.ShowCacheProgress(cacheProgress);shop.ShowDialog(this);}}
            finally{activeShop=null;modal=false;Rescan();}
        }
        void PickFolder() {BeginStickerSelection();
            if(refreshing){Tell("폴더 확인이 끝난 뒤 추가해주세요.");return;}
            modal=true;
            try {using(var d=new FolderBrowserDialog {Description="PNG · JPG · GIF 원본 폴더를 골라주세요",ShowNewFolderButton=false})
                if(d.ShowDialog(this)==DialogResult.OK) {
                    if(!config.Folders.Contains(d.SelectedPath,StringComparer.OrdinalIgnoreCase)) config.Folders.Add(d.SelectedPath);
                    Rescan(); Save();
                }
            } finally {modal=false;}
        }
        void ManageFolders() {BeginStickerSelection();
            if(refreshing){Tell("폴더 확인이 끝난 뒤 관리해주세요.");return;}
            modal=true;
            try {using(var f=new Form {Text="가져온 폴더",Size=new Size(580,300),StartPosition=FormStartPosition.CenterParent}) {
                var list=new ListBox {Dock=DockStyle.Fill}; list.Items.AddRange(config.Folders.Cast<object>().ToArray());
                var remove=new Button {Dock=DockStyle.Bottom,Text="선택 폴더 제외 (원본 파일은 유지)"};
                remove.Click+=(s,e)=>{if(list.SelectedItem!=null){config.Folders.Remove((string)list.SelectedItem);list.Items.Remove(list.SelectedItem);}};
                f.Controls.Add(list);f.Controls.Add(remove);f.ShowDialog(this);Rescan();Save();
            }} finally {modal=false;}
        }
        void Rescan() {
            if(refreshing||config==null)return; previewIndexer.Stop();FlushPreviewSave();catalogIndexer.Stop();if(semanticRequest!=null)semanticRequest.Cancel();semanticVersion++;refreshing=true;
            Tell("선택한 폴더를 확인하고 있어요...");
            var snapshot=new Settings {Folders=config.Folders.ToList(),Packs=config.Packs.ToList(), Entries=config.Entries.Select(e=>new Entry{Path=e.Path,Aliases=e.Aliases,LastUsed=e.LastUsed,DisplayName=e.DisplayName,PackId=e.PackId,SourceUrl=e.SourceUrl,Format=e.Format,RemoteId=e.RemoteId,OriginalPath=e.OriginalPath}).ToList()};
            var task=System.Threading.Tasks.Task.Factory.StartNew(()=>{
                int skip; var items=Catalog.Scan(snapshot,out skip);return Tuple.Create(items,skip);
            });
            task.ContinueWith(t=>{
                if(IsDisposed)return;
                BeginInvoke((Action)(()=>{
                    refreshing=false;
                    if(t.IsFaulted){Tell("폴더 읽기 실패: "+t.Exception.GetBaseException().Message);return;}
                    foreach(var item in snapshot.Entries) if(!config.Entries.Any(e=>String.Equals(Catalog.Key(e),Catalog.Key(item),StringComparison.OrdinalIgnoreCase)))config.Entries.Add(item);
                    var keys=new HashSet<string>(t.Result.Item1.Select(Catalog.Key),StringComparer.OrdinalIgnoreCase);
                    entries=config.Entries.Where(e=>keys.Contains(Catalog.Key(e))).ToList();Render();Save();QueueCatalogIndex();
                    Tell(entries.Count+"개 가져왔어요"+(t.Result.Item2>0?" · 접근 불가 폴더 "+t.Result.Item2+"개":"")+"\n내 콘: 전체 보기 · 오른쪽 탭: Alt+1~3으로 선택");
                }));
            });
        }
        void QueueCatalogIndex(){QueuePreviews();}
        void ClearRows() {foreach(Control c in rows.Controls.Cast<Control>().ToArray()){rows.Controls.Remove(c);c.Dispose();}}
        void Render(List<Entry> matched=null) {
            if(refreshing)return;
            if(matched==null){semanticVersion++;if(semanticRequest!=null)semanticRequest.Cancel();}
            if(matched==null&&copyRequest!=null)copyRequest.Cancel();
            previewRequests.Cancel();previewRequests.Dispose();previewRequests=new CancellationTokenSource();
            string query=draftActive?draftText:search.Text;
            bool emptyQuery=String.IsNullOrWhiteSpace(query);
            SetRecommendationCaption(matched!=null?"문장 추천":emptyQuery?"최근 사용":!libraryMode&&config!=null&&config.UseSemantic?"추천 준비 중":"검색 결과");
            ClearRows();rankedGrid.Visible=false;rows.Visible=!libraryMode; candidates=matched??Catalog.Search(emptyQuery?entries.Where(entry=>entry.LastUsed>0):entries,query);selected=null;UpdateAliasLayout(); editing=true;aliases.Text="";editing=false;
            if(libraryMode){library.UpdateCatalog(entries,config==null?new List<PackRecord>():config.Packs,search.Text);return;}
            if(matched!=null&&matched.Count==0){rows.Visible=true;rows.Controls.Add(new Label{Text="확실히 어울리는 콘을 찾지 못했어요.\n다른 표현으로 검색하거나\n콘을 우클릭해 별칭을 추가해주세요.",Width=330,Height=100});return;}
            if(matched!=null){rows.Visible=false;rankedGrid.SetItems(candidates,new List<PackRecord>());rankedGrid.Visible=true;if(compact&&ClientSize.Height<600)ClientSize=new Size(ClientSize.Width,600);return;}
            for(int i=0;i<candidates.Count;i++) {
                Entry entry=candidates[i];int number=i+1;
                int imageSize=compact?58:80,labelX=compact?76:94;var row=new RoundedPanel {Width=Math.Max(240,rows.ClientSize.Width-22),Height=compact?70:94,BackColor=Ui.Surface,Margin=new Padding(0,0,0,compact?6:8),TabStop=true};
                var picture=new PictureBox {Size=new Size(imageSize,imageSize),Location=new Point(6,6),SizeMode=PictureBoxSizeMode.Zoom};
                picture.Disposed+=(s,e)=>{if(picture.Image!=null){picture.Image.Dispose();picture.Image=null;}var stream=picture.Tag as Stream;if(stream!=null)stream.Dispose();};
                var name=new Label {Text="Alt+"+number+"  "+(entry.DisplayName??System.IO.Path.GetFileNameWithoutExtension(entry.Path)),Font=Ui.Regular(compact?9:10),Location=new Point(labelX,6),Size=new Size(row.Width-labelX-6,compact?28:40),AutoEllipsis=true};
                var tags=new Label {Text=(entry.Format??System.IO.Path.GetExtension(entry.Path)??"").ToUpperInvariant()+"  "+entry.Aliases,ForeColor=Ui.Muted,Font=Ui.Regular(compact?8:9),Location=new Point(labelX,compact?38:51),Size=new Size(row.Width-labelX-6,compact?26:34),AutoEllipsis=true};
                EventHandler choose=(s,e)=>Choose(entry,(ModifierKeys&Keys.Shift)==Keys.Shift);
                row.Click+=choose;picture.Click+=choose;name.Click+=choose;tags.Click+=choose;
                var editMenu=new ContextMenuStrip();editMenu.Items.Add("별칭 편집",null,(s,e)=>EditAliases(entry));
                editMenu.Items.Add("카톡 호환 PNG/GIF 파일 복사",null,(s,e)=>Copy(entry,true,false));editMenu.Items.Add("원본 파일만 복사",null,(s,e)=>Copy(entry,false,false));
                row.ContextMenuStrip=editMenu;picture.ContextMenuStrip=editMenu;name.ContextMenuStrip=editMenu;tags.ContextMenuStrip=editMenu;
                row.Disposed+=(s,e)=>Ui.DisposeMenuLater(editMenu);row.Controls.Add(picture);row.Controls.Add(name);row.Controls.Add(tags); rows.Controls.Add(row);
                LoadPreview(entry,picture,tags,previewRequests.Token);
            }
            if(candidates.Count==0)rows.Controls.Add(new Label {Text=entries.Count==0?"팩 추가 / 관리에서 디시콘을 찾아주세요.":(emptyQuery?"최근 사용한 콘이 없어요.\n내 콘에서 먼저 콘을 골라주세요.":config!=null&&config.UseSemantic?"문장에 어울리는 콘을 찾고 있어요...\n처음에는 모델 준비에 시간이 걸릴 수 있어요.":"검색 결과가 없어요. 별칭도 검색할 수 있어요."),Width=290,Height=70});
            if(matched==null&&config!=null&&config.UseSemantic&&!String.IsNullOrWhiteSpace(query)){semanticRequest=new CancellationTokenSource();FindSemantic(query,semanticVersion,semanticRequest.Token,draftActive);}
        }
        async void FindSemantic(string query,int version,CancellationToken token,bool fromDraft=false){try{await Task.Delay(500,token);var found=await semantic.Search(query,entries.ToList(),token);if(token.IsCancellationRequested||IsDisposed||!AutomaticResultCurrent(version,false)||!config.UseSemantic||(fromDraft&&!DraftContextCurrent()))return;if(found.Count>0){Render(found);if(semantic.UsedSmileFallback)SetRecommendationCaption("가벼운 웃음");Tell((semantic.UsedSmileFallback?"뚜렷한 웃음 후보가 없어 밝은 미소 콘을 보여줘요.":fromDraft?"카톡 작성 문장으로 추천했어요.":"로컬 의미 검색 · 받아둔 콘에서 찾았어요.")+(semantic.LastSkippedImages>0?" · "+semantic.LastSkippedImages+"개 파일 제외":"")+"\n클릭으로 하나씩 선택할 수 있어요.");}else if(semantic.LastAnalyzedImages>0){Render(found);Tell("받아둔 "+semantic.LastAnalyzedImages+"개 중 확실히 맞는 콘이 없어요.\n관련 없는 콘으로 추천 목록을 채우지 않았어요.");}else{SetRecommendationCaption("검색 결과");SetRecommendationHint(semantic.LastSkippedImages>0?"읽을 수 없는 이미지 "+semantic.LastSkippedImages+"개를 제외했어요. 다른 콘으로 검색해주세요.":"분석할 콘이 없어요. 먼저 내 콘에서 이미지를 열어주세요.");Tell("분석할 콘이 없어요. 먼저 콘을 보거나 선택해 받아주세요.\n지금은 파일명·별칭 검색 결과를 보여줘요.");}}catch(Exception ex){if(!token.IsCancellationRequested&&!IsDisposed&&version==semanticVersion){SetRecommendationCaption("검색 결과");SetRecommendationHint("문장 추천을 사용할 수 없어요.\n파일명·별칭으로 직접 검색할 수 있어요.");Tell("의미 검색: "+ex.Message+"\n파일명·별칭 검색은 계속 사용할 수 있어요.");}}}
        async void LoadPreview(Entry entry,PictureBox picture,Label tags,CancellationToken token) {
            picture.BackColor=Color.FromArgb(240,240,240);
            try {
                string path=await Task.Run(()=>DcShop.EnsureImage(entry,token),token);
                if(token.IsCancellationRequested||picture.IsDisposed||IsDisposed)return;
                var memory=new MemoryStream(File.ReadAllBytes(path));
                try{picture.Image=Image.FromStream(memory);picture.Tag=memory;}catch{memory.Dispose();throw;}
                bool changed=entry.Path!=path;entry.Path=path;entry.Format=Path.GetExtension(entry.OriginalPath??path).TrimStart('.');picture.BackColor=Color.White;tags.Text=(ImageBundle.Animated(picture.Image)?"움짤 · ":"")+(entry.Format??"").ToUpperInvariant()+"  "+entry.Aliases;if(changed)Save();
            }catch(Exception ex){if(!token.IsCancellationRequested&&!picture.IsDisposed&&!IsDisposed){picture.BackColor=Color.MistyRose;tags.Text="미리보기 실패 · 클릭해서 재시도";tags.AccessibleDescription=ex.Message;}}
        }
        void UpdateAliasLayout(){if(layout==null)return;bool show=!compact&&selected!=null;int[] heights={26,31,36};for(int i=5;i<=7;i++){var control=layout.GetControlFromPosition(0,i);if(control!=null)control.Visible=show;layout.RowStyles[i].Height=show?(UiZoom.Installed(this)?UiZoom.Px(heights[i-5]):heights[i-5]):0;}}
        void Select(Entry entry) {selected=entry;UpdateAliasLayout(); editing=true; aliases.Text=entry.Aliases; editing=false;detail.Text="별칭: "+(entry.DisplayName??System.IO.Path.GetFileName(entry.Path));}
        void EditAliases(Entry entry){BeginStickerSelection();if(refreshing||entry==null)return;ClearDraft(false);modal=true;bool saved=false;try{using(var editor=new AliasEditor(entry)){if(editor.ShowDialog(this)==DialogResult.OK){entry.Aliases=editor.Value;Save();saved=true;}}}finally{modal=false;Render();}if(saved)Tell("별칭을 저장했어요. 원본 이름은 그대로예요.");}
        void SaveAliases() {BeginStickerSelection();if(refreshing){Tell("폴더 확인이 끝난 뒤 저장해주세요.");return;}if(selected==null)return;selected.Aliases=aliases.Text.Trim();Save();Render();Tell("별칭을 저장했어요. 원본 이름은 그대로예요.");}
        string copyAssetDescription="",copyGeometryDescription="";
        void TraceCopy(CancellationTokenSource request,string stage){if(copyRequest!=request)return;try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"attachment-last.txt"),DateTime.UtcNow.ToString("o")+"\r\n"+stage+"\r\nAsset="+copyAssetDescription+"\r\nGeometry="+copyGeometryDescription+"\r\nLibrary="+libraryMode+"; Pinned="+libraryPinned+"; Visible="+Visible+"; ContainsFocus="+ContainsFocus+"\r\n");}catch{}}
        async void Copy(Entry entry,bool compatible=false,bool imageClipboard=true) {
            if(entry==null)return;
            BeginStickerSelection();
            PreviewChatTarget destination=previewPaste.Snapshot();
            long startedRoom=roomVersion;if(compact&&(anchor==null||destination==null||destination.Window!=anchor.Window||destination.Editor!=anchor.Editor||destination.Title!=anchor.Title))destination=null;
            if(copyRequest!=null)copyRequest.Cancel();
            var request=new CancellationTokenSource();copyRequest=request;copyAssetDescription="";copyGeometryDescription="";
            try {
                TraceCopy(request,"Preparing selected sticker");Tell("선택한 콘을 준비하고 있어요...");
                string path=await Task.Run(()=>DcShop.EnsureAttachment(entry,request.Token,compatible),request.Token);
                if(request.IsCancellationRequested||IsDisposed){TraceCopy(request,"Canceled before clipboard");return;}
                if(!Visible||(!ContainsFocus&&Native.GetForegroundWindow()!=Handle)){TraceCopy(request,"Source focus changed before clipboard");Tell("콘 준비 완료 · 창 포커스가 바뀌어 복사는 중단했어요. 다시 선택해주세요.");return;}
                if(Path.GetExtension(path)!=".webp")entry.Path=path;entry.Format=Path.GetExtension(path).TrimStart('.');
                bool gifFile=imageClipboard&&GifAttachment.AnimatedGif(path);
                copyAssetDescription=GifAttachment.Describe(path,imageClipboard)+(gifFile?" · GIF 파일 첨부":" · 정지 이미지 또는 파일 복사");copyGeometryDescription=destination==null?"no-target":DpiGeometry.Describe(destination.Window);
                bool copiedImage=imageClipboard&&!gifFile&&ImageClipboard.Put(entry.Path??path);
                if(gifFile)Clipboard.SetDataObject(GifAttachment.FilePayload(path),true,5,80);
                else if(!copiedImage){var files=new StringCollection();files.Add(path);Clipboard.SetFileDropList(files);}
                TraceCopy(request,gifFile?"GIF file prepared":"Image/file copied");uint clipboardSequence=PreviewPaste.GetClipboardSequenceNumber();entry.LastUsed=DateTime.UtcNow.Ticks;Save();Select(entry);
                if(compact&&startedRoom!=roomVersion)destination=null;
                if(GifAttachment.PreviewEnabled(imageClipboard,gifFile,copiedImage,config.ImagePastePreview!=false)){var outcome=gifFile?await previewPaste.TryPhotoAttach(destination,request.Token,path,Handle):await previewPaste.TryPaste(destination,request.Token,clipboardSequence,Handle);TraceCopy(request,(outcome.Requested?"Attachment step completed":"Attachment stopped")+" ["+outcome.Stage+" / "+outcome.Block+"]");Tell(outcome.Reason+"\n"+copyAssetDescription);}else Tell(copiedImage?"이미지 클립보드 복사 완료 · 카톡에서 직접 Ctrl+V":"파일 복사 완료 · 카톡의 파일 첨부에서 직접 골라주세요");
            } catch(Exception ex){TraceCopy(request,request.IsCancellationRequested?"Request canceled":"Copy failed: "+ex.GetType().Name);if(!request.IsCancellationRequested&&!IsDisposed)Tell("복사 실패: "+ex.Message);}
            finally{FinishCopy(request,false);}
        }
        void ClearDraft(bool render=true){bool changed=draftActive;draftGeneration++;draftActive=false;draftText=null;draftTarget=null;if(changed){semanticVersion++;if(semanticRequest!=null)semanticRequest.Cancel();if(render&&!IsDisposed&&!refreshing)Render();}}
        bool DraftContextCurrent(){return draftActive&&draftTarget!=null&&(Native.GetForegroundWindow()==Handle||ContainsFocus||PreviewPaste.IsFocused(draftTarget));}
        void ObserveDraft(){
            if(copyRequest!=null||(libraryMode&&libraryPinned))return;
            if(config==null||!config.AutoDraftRecommendations||!config.UseSemantic){if(draftActive)ClearDraft();return;}
            var source=previewPaste.Snapshot();IntPtr foreground=Native.GetForegroundWindow();
            if(source==null||foreground!=source.Window||!PreviewPaste.IsFocused(source)){
                if(foreground!=Handle&&!ContainsFocus&&draftActive)ClearDraft();return;
            }
            if(!String.IsNullOrWhiteSpace(search.Text))return;
            string key=source.ProcessId+":"+source.Window+":"+source.Editor+":"+source.Title+":"+source.Version;
            if(draftSource!=key){ClearDraft();draftSource=key;nextDraftPoll=DateTime.MinValue;}
            if(draftProbeBusy||DateTime.UtcNow<nextDraftPoll)return;
            nextDraftPoll=DateTime.UtcNow.AddMilliseconds(750);ReadDraft(source,draftGeneration);
        }
        async void ReadDraft(PreviewChatTarget source,int generation){draftProbeBusy=true;try{
            var result=await FocusedDraft.TryRead(source);
            var current=previewPaste.Snapshot();if(IsDisposed||!AutomaticResultCurrent(generation,true)||config==null||!config.AutoDraftRecommendations||!config.UseSemantic||current==null||current.Version!=source.Version||!PreviewPaste.IsFocused(source))return;
            if(!result.Supported){ClearDraft();nextDraftPoll=DateTime.UtcNow.AddSeconds(5);Tell(result.Reason);return;}
            string text=(result.Text??"").Trim();if(text.Length==0){if(draftActive)ClearDraft();return;}
            if(draftActive&&draftText==text)return;
            draftText=text;draftTarget=source;draftActive=true;
            if(libraryMode)SetLibraryMode(false,true);else Render();
            Tell("카톡 작성 문장으로 추천 중 · 검색창에 직접 입력해도 돼요.");
        }finally{draftProbeBusy=false;}}
        void TickWatch() {
            if(previewOnly||modal||IsDisposed)return;
            if(previewPaste.FileAttachmentInProgress)return;
            if(SplitWindowForeground(collectionWindow,Native.GetForegroundWindow())||SplitWindowForeground(longWindow,Native.GetForegroundWindow()))return;
            previewPaste.Observe();
            ObserveDraft();
            if(config!=null&&config.ImagePastePreview!=false)targetLabel.Text=previewPaste.Status;
            if(Program.ShowRequest!=null&&Program.ShowRequest.WaitOne(0))Open();
            IntPtr foreground=Native.GetForegroundWindow();
            if(movingPalette)return;
            if(follow.Checked){if(Native.IsKakao(foreground)&&!Native.IsIconic(foreground)){var next=ChatFloating.Find(foreground);if(next==null){if(Visible)Hide();return;}BindRoom(next);DockToTarget();if(!Visible)Show();}
                else if(foreground==Handle||ContainsFocus||(collectionWindow!=null&&!collectionWindow.IsDisposed&&collectionWindow.ContainsFocus)){if(anchor!=null){if(!ChatFloating.Current(anchor)){if(Visible)Hide();return;}var next=ChatFloating.Find(anchor.Window);if(next!=null){BindRoom(next);DockToTarget();}else if(Visible)Hide();}}
                else if(Visible)Hide();return;}
            if(Native.IsKakao(foreground)&&!Native.IsIconic(foreground)) {
                string title=Native.Title(foreground);
                // Main window and native dialogs are not considered bound chat targets.
                if(title!="카카오톡"&&title!="열기"&&title!=""&&title!="파일 전송") {
                    target=foreground;targetTitle=title;targetLabel.Text=config.ImagePastePreview!=false?previewPaste.Status:"위치 기준: "+title+" · 복사 전용";
                    if(follow.Checked) {DockToTarget();if(!Visible)Show();}
                } else if(follow.Checked && Visible) Hide();
            } else if(foreground==Handle || ContainsFocus || (collectionWindow!=null&&!collectionWindow.IsDisposed&&collectionWindow.ContainsFocus)) {
                if(follow.Checked && (!Native.IsWindow(target)||Native.IsIconic(target)||Native.Title(target)!=targetTitle)) Hide();
                else if(follow.Checked)DockToTarget();
            } else if(follow.Checked && Visible) Hide();
        }
        void DockToTarget() {
            if(movingPalette)return;
            if(compact&&anchor!=null){FloatingOffset offset;if(!roomPositions.TryGetValue(anchor.Key,out offset))offset=config.FloatingPosition;var bounds=offset==null?ChatFloating.Place(anchor.ChatRect,anchor.InputRect,Size,Screen.FromHandle(anchor.Window).WorkingArea):ChatFloating.PlaceCustom(anchor.InputRect,Size,offset,Screen.FromRectangle(offset.Desired(anchor.InputRect,Size)).WorkingArea);if(Bounds!=bounds)Bounds=bounds;return;}
            Native.Rect r;if(!Native.GetWindowRect(target,out r))return;
            var area=Screen.FromHandle(target).WorkingArea;
            int x=r.Right+8;
            if(x+Width>area.Right)x=r.Left-Width-8;
            x=Math.Max(area.Left,Math.Min(x,area.Right-Width));
            int y=Math.Max(area.Top,Math.Min(r.Top,area.Bottom-Height));
            Location=new Point(x,y);
        }
    }
    static class Program {
        internal static System.Threading.EventWaitHandle ShowRequest;
        [STAThread] static void Main(string[] args) {
#if AGITCON_DEV
            if(args.Contains("--quality-catalog-only")){QualityAudit.Run("catalog-only");return;}
            if(args.Contains("--quality-independent")){QualityAudit.Run("independent");return;}
#endif
            if(args.Contains("--catalog-load-test")){try{var config=Catalog.Load();if(config==null||config.Entries==null||config.Folders==null||config.Packs==null)throw new InvalidDataException("Missing collections");File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog-load-test.txt"),"PASS: actual packaged catalog loaded by application Catalog.Load; no changes to settings, clipboard or Kakao.");}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog-load-test.txt"),"FAIL: "+ex);Environment.ExitCode=1;}return;}
            if(args.Contains("--catalog-encoding-test")){SelfTest.CatalogEncoding();return;}
            if(args.Contains("--release-self-check")){var assembly=typeof(Program).Assembly;bool safe=!assembly.GetManifestResourceNames().Any(name=>name.Contains("SemanticMeanings"))&&assembly.GetType("AgitCon.QualityAudit")==null&&assembly.GetType("AgitCon.AutoTuningAudit")==null;File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"release-check.txt"),safe?"PASS: no per-image meaning resource or evaluation data readers in release executable.":"FAIL: developer-only content found");if(!safe)Environment.ExitCode=1;return;}
            if(args.Contains("--self-test")) {SelfTest.Run();return;}
            if(args.Contains("--photo-attachment-test")){PreviewPaste.TestPhotoPolicy();return;}
            if(args.Contains("--gear-readiness-test")){ConfirmationVisionTests.GearReadiness();return;}
            if(args.Contains("--confirmation-vision-test")){ConfirmationVisionTests.Run();return;}
            if(args.Contains("--confirmation-navigation-probe")){ConfirmationVisionTests.NavigationProbe();return;}
            if(args.Contains("--confirmation-vision-live")){ConfirmationVisionTests.LiveRead();return;}
            if(args.Contains("--photo-native-test")){PreviewPaste.TestPhotoNative();return;}
            if(args.Contains("--enable-gif-open")){var settings=Catalog.Load();Catalog.AuthorizeGifOpen(settings);Catalog.Save(settings);return;}
            if(args.Contains("--enable-gif-direct-send")){var settings=Catalog.Load();settings.GifDirectSend=true;settings.ImagePastePreview=true;Catalog.Save(settings);return;}
            if(args.Contains("--gif-open-settings-test")){PreviewPaste.TestGifPersistence();return;}
            if(args.Contains("--gif-direct-test")){PreviewPaste.TestDirectGif();return;}
            if(args.Contains("--attachment-flow-test")){PreviewPaste.TestFlow();return;}
            if(args.Contains("--scroll-repaint-test")){Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);LibraryGrid.TestScrollRepaint();return;}
            if(args.Contains("--rail-lifetime-test")){Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);LibraryView.TestRailLifetime();return;}
            if(args.Contains("--copy-refresh-test")){MainForm.TestCopyRefreshRace();return;}
            if(args.Contains("--webp-attachment-test")){GifAttachment.TestWebp();return;}
            if(args.Contains("--gif-attachment-test")){GifAttachment.Test();return;}
            if(args.Contains("--image-clipboard-files-test")){ImageClipboard.TestFiles(args.Skip(1).ToArray());return;}
            if(args.Contains("--image-clipboard-test")){ImageClipboard.Test();return;}
            if(args.Contains("--preview-paste-test")){PreviewPaste.Test();return;}
            if(args.Contains("--floating-test")){ChatFloating.Test();return;}
            if(args.Contains("--floating-position-test")){ChatFloating.TestPosition();return;}
            if(args.Contains("--enable-analysis")){if(!StickerAnalysis.Ready){Environment.ExitCode=1;return;}var settings=Catalog.Load();settings.AutomaticDescriptions=true;Catalog.Save(settings);return;}
            if(args.Contains("--enable-semantic")){if(!SemanticSearch.Ready){Environment.ExitCode=1;return;}var settings=Catalog.Load();settings.UseSemantic=true;Catalog.Save(settings);return;}
            if(args.Contains("--install-progress-test")){SemanticSettingsForm.ProgressTest();return;}
            if(args.Contains("--install-progress-render")){SemanticSettingsForm.RenderProgress();return;}
            if(args.Contains("--draft-test")){FocusedDraft.Test();return;}
            if(args.Contains("--enable-draft")){if(!SemanticSearch.Ready){Environment.ExitCode=1;return;}var settings=Catalog.Load();settings.UseSemantic=true;settings.AutoDraftRecommendations=true;Catalog.Save(settings);return;}
#if AGITCON_DEV
            if(args.Contains("--automatic-prototypes")){AutoTuningAudit.Prototypes();return;}
            if(args.Contains("--caption-error-holdout")){AutoTuningAudit.Captions(false,"error-holdout-fixtures.json","error-holdout-captions");return;}
            if(args.Contains("--publish-error-holdout")){AutoTuningAudit.PublishCaptions("error-holdout-fixtures.json","error-holdout-captions.json","error-holdout-publish.txt");return;}
            if(args.Contains("--error-holdout-before")){QualityAudit.Run("error-holdout-before");return;}
            if(args.Contains("--error-holdout-after")){QualityAudit.Run("error-holdout-after");return;}
            if(args.Contains("--caption-audit")){AutoTuningAudit.Captions();return;}
            if(args.Contains("--quality-caption-baseline")){QualityAudit.Run("caption-baseline");return;}
            if(args.Contains("--quality-catalog-analyzed")){QualityAudit.Run("catalog-analyzed");return;}
            if(args.Contains("--quality-caption-enabled")){QualityAudit.Run("caption-enabled");return;}
            if(args.Contains("--caption-publish")){AutoTuningAudit.PublishCaptions();return;}
            if(args.Contains("--caption-smoke")){AutoTuningAudit.CaptionSmoke();return;}
            if(args.Contains("--caption-catalog")){AutoTuningAudit.CaptionCatalog();return;}
            if(args.Contains("--caption-gemma-audit")){AutoTuningAudit.Captions(true);return;}
            if(args.Contains("--automatic-audit-q4")){AutoTuningAudit.Run("q4");return;}
#endif
#if AGITCON_DEV
            if(args.Contains("--automatic-audit-q8")){AutoTuningAudit.Run("q8");return;}
#endif
#if AGITCON_DEV
            if(args.Contains("--quality-laugh-library")){QualityAudit.Run("laugh-library");return;}
#endif
#if AGITCON_DEV
            if(args.Contains("--quality-full-library")){QualityAudit.Run("full-library");return;}
#endif
#if AGITCON_DEV
            if(args.Contains("--quality-benchmark-full")){QualityAudit.Run("benchmark-full");return;}
#endif
#if AGITCON_DEV
            if(args.Contains("--quality-ocr")){QualityAudit.Ocr();return;}
#endif
#if AGITCON_DEV
            if(args.Contains("--quality-training")){QualityAudit.Run("training");return;}
#endif
#if AGITCON_DEV
            if(args.Contains("--quality-benchmark")){QualityAudit.Run("benchmark");return;}
#endif
#if AGITCON_DEV
            if(args.Contains("--quality-laugh-control")){QualityAudit.Run("laugh-control");return;}
#endif
#if AGITCON_DEV
            if(args.Contains("--quality-laugh")){QualityAudit.Run("laugh");return;}
#endif
#if AGITCON_DEV
            if(args.Contains("--quality-before")){QualityAudit.Run("before");return;}
#endif
#if AGITCON_DEV
            if(args.Contains("--quality-after")){QualityAudit.Run("after");return;}
#endif
            if(args.Contains("--alias-render")){AliasEditor.Render();return;}
#if AGITCON_DEV
            if(args.Contains("--emotion-scopes")){QualityAudit.SmileScopes(true);return;}
            if(args.Contains("--smile-scopes")){QualityAudit.SmileScopes();return;}
            if(args.Contains("--search-regression")){SearchRegression.Run();return;}
#endif
            if(args.Contains("--gemma-test")){GemmaTests.Logic();return;}
            if(args.Contains("--gemma-smoke")){GemmaTests.Smoke();return;}
            if(args.Contains("--library-test")){LibraryModel.Test();return;}
            if(args.Contains("--pack-precache-test")){PreviewFiles.TestPackWarmup();return;}
            if(args.Contains("--motion-badge-render")){LibraryGrid.RenderMotionBadge();return;}
            if(args.Contains("--dpi-geometry-test")){DpiGeometry.Test();return;}
            if(args.Contains("--ocr-setup-test")){OcrSetupForm.Test();return;}
            if(args.Contains("--ocr-check")){OcrSetupForm.Probe();return;}
            if(args.Contains("--thumbnail-recovery-test")){LibraryGrid.TestRecovery();return;}
            if(args.Contains("--thumbnail-settings-test")){SemanticSettingsForm.TestThumbnails();return;}
            if(args.Contains("--thumbnail-memory-test")){EncodedPreviews.Test();return;}
            if(args.Contains("--thumbnail-cache-test")){LibraryGrid.TestPersistentCache();return;}
            if(args.Contains("--library-memory-speed")){LibraryGrid.TestCacheSpeed(true);return;}
            if(args.Contains("--library-cache-speed")){LibraryGrid.TestCacheSpeed();return;}
            if(args.Contains("--library-load-test")){LibraryGrid.TestLoading();return;}
            if(args.Contains("--library-render")){LibraryView.RenderSaved();return;}
            if(args.Contains("--ranked-render")){MainForm.RenderDesign(false,true);return;}
            if(args.Contains("--recommendation-render")){MainForm.RenderDesign(true);return;}
            if(args.Contains("--design-render")){MainForm.RenderDesign();return;}
            if(args.Contains("--lazy-test")) {LazyTests.Run(args.Contains("--live"));return;}
            if(args.Contains("--cache-maintenance-test")){CacheMaintenance.Test();return;}
            if(args.Contains("--ui-zoom-test")){UiZoom.Test();return;}
            if(args.Contains("--arca-search-test")){ArcaSearchTests.Run();return;}
            if(args.Contains("--app-update-test")){try{AppUpdateTests.Run();}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app-update-error.txt"),ex.ToString());Environment.ExitCode=1;}return;}
            if(args.Contains("--longcon-vision-test")){PreviewPaste.TestLongVision();return;}
            if(args.Contains("--longcon-test")){LongConTests.Run();return;}
            if(args.Contains("--bundle-test")){BundleTests.Run();return;}
            if(args.Contains("--collection-render")){BundleTests.RenderSaved();return;}
            if(args.Contains("--arca-test")){ArcaTests.Run(args.Contains("--live"));return;}
            int register=Array.IndexOf(args,"--register-arca");if(register>=0&&register+1<args.Length){ArcaTests.Register(args[register+1]);return;}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(args.Contains("--focus-guard-window")){using(var form=new Form{Text="아지트콘 포커스 안전장치 시험",ClientSize=new Size(400,150),BackColor=Ui.Paper,KeyPreview=true}){int pasted=0;var label=Ui.Label("외부 창 전환 시험 · Ctrl+V 수신 0회",11,true);label.Dock=DockStyle.Fill;form.Controls.Add(label);form.KeyDown+=(s,e)=>{if(e.Control&&e.KeyCode==Keys.V){pasted++;label.Text="Ctrl+V 수신 "+pasted+"회";e.SuppressKeyPress=true;}};Application.Run(form);File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"focus-guard-test.txt"),"Ctrl+V received by foreign test process: "+pasted+"\r\n");}return;}
            if(args.Contains("--shop-render")){LazyTests.RenderShop(args.Contains("--live"));return;}
            if(args.Contains("--shop-layout-render")){ShopForm.RenderLayoutPreview();return;}
            if(args.Contains("--render-test")) {
                Catalog.DataPath=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"render-test-data","catalog.json");
                using(var form=new MainForm()) {form.Show();for(int i=0;i<10;i++){Application.DoEvents();System.Threading.Thread.Sleep(30);}using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-smoke.png"));}}
                return;
            }
            try {Run();} catch(Exception ex) {
                File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-error.txt"),ex.ToString(),Encoding.UTF8);
                MessageBox.Show("아지트콘 실행 오류: "+ex.Message);
            }
        }
        static void Run() {
            ShowRequest=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,"Local\\AjitCon.Show");
            using(var mutex=new System.Threading.Mutex(false,"Local\\AjitCon.Personal")) {
                bool acquired;
                try{acquired=mutex.WaitOne(0,false);}catch(System.Threading.AbandonedMutexException){acquired=true;}
                if(!acquired){ShowRequest.Set();ShowRequest.Dispose();return;}
                try{CacheMaintenance.ApplyPending(AppDomain.CurrentDomain.BaseDirectory);UiZoom.Start(Catalog.Load().UiScalePercent);Application.Run(new MainForm());}finally{mutex.ReleaseMutex();ShowRequest.Dispose();}
            }
        }
    }
    static class SelfTest {
        static void Assert(bool pass,string message){if(!pass)throw new Exception(message);}
        public static void CatalogEncoding(){string original=Catalog.DataPath;string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog-encoding-test.txt");int checks=0;try{Catalog.DataPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-work","encoding-"+Guid.NewGuid().ToString("N"),"catalog.json");var expected=new Settings{GifAutoOpen=true};expected.Entries.Add(new Entry{Path=@"C:\콘\고마워.gif",Aliases="고마워요, 고맙"});Catalog.Save(expected);string json=File.ReadAllText(Catalog.DataPath,Encoding.UTF8);foreach(var encoding in new Encoding[]{new UTF8Encoding(false),new UTF8Encoding(true),Encoding.Unicode}){File.WriteAllText(Catalog.DataPath,json,encoding);var before=File.ReadAllBytes(Catalog.DataPath);var config=Catalog.Load();Assert(config.Entries.Count==1&&config.Entries[0].Aliases=="고마워요, 고맙"&&config.GifAutoOpen,"Encoding roundtrip");checks++;Assert(before.SequenceEqual(File.ReadAllBytes(Catalog.DataPath)),"Read must preserve original bytes");checks++;}File.WriteAllText(Catalog.DataPath,"{broken",new UTF8Encoding(true));var broken=File.ReadAllBytes(Catalog.DataPath);bool rejected=false;try{Catalog.Load();}catch(SerializationException){rejected=true;}Assert(rejected&&broken.SequenceEqual(File.ReadAllBytes(Catalog.DataPath)),"Malformed original preserved");checks++;File.WriteAllText(report,"PASS: "+checks+" actual Catalog.Load cases: UTF8 without/with BOM, UTF16 BOM, Korean aliases, byte-preserving reads, malformed settings rejected without overwriting. Isolated settings only.");}catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}finally{Catalog.DataPath=original;}}
        public static void Run() {
            string report=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt");
            try {
                var source=new[]{new Entry{Path="C:\\콘\\a.png",Aliases="고마워요, 감사",LastUsed=1},new Entry{Path="C:\\콘\\b.gif",Aliases="반가워요",LastUsed=3},new Entry{Path="C:\\콘\\감사.jpg",LastUsed=2},new Entry{Path="C:\\콘\\c.png",Aliases="고마워요",LastUsed=4}};
                Assert(Catalog.Search(source,"고맙").Count==2,"Korean thanks inflection");
                source[0].Aliases+=", 고맙";
                Assert(Catalog.Search(source,"고맙").First()==source[0],"Exact alias takes priority over inflection");
                Assert(Catalog.Search(source,"마워").Count==2,"Korean substring");
                Assert(Catalog.Search(source,"감사").Count==2,"Name and alias matching");
                Assert(Catalog.Search(source,"고맙 감사").Count==1,"Multiple tokens");
                Assert(Catalog.Search(source,"").Count==3,"Top three");
                Assert(Catalog.Search(source,"")[0]==source[3],"Recent ranking");
                Assert(Catalog.Search(source,"없는검색어").Count==0,"No results");
                Assert(Catalog.Supported("A.GIF") && Catalog.Supported("x.JPEG") && !Catalog.Supported("x.exe"),"Extension filtering");
                Assert(Catalog.Normalize("감사")=="감사","Unicode normalization");
                // Serializer roundtrip without touching the user's real catalog.
                using(var ms=new MemoryStream()) {var serializer=new DataContractJsonSerializer(typeof(Settings));var settings=new Settings();settings.Entries.Add(source[0]);serializer.WriteObject(ms,settings);ms.Position=0;var restored=(Settings)serializer.ReadObject(ms);Assert(restored.Entries[0].Aliases==source[0].Aliases,"Alias persistence");}
                File.WriteAllText(report,"PASS: 11 core checks. No KakaoTalk interaction, clipboard, or UI test performed.\r\n",Encoding.UTF8);
            }catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex,Encoding.UTF8);Environment.ExitCode=1;}
        }
    }
}
