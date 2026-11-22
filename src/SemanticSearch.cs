using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgitCon {
    public sealed class SemanticSearch:IDisposable {
        public static readonly string Root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"semantic");
        public const string Spec="gemma2-q4-image280-gif3-v1";
        public const int RecommendationLimit=24;
        public static bool Ready {get{return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Microsoft.Web.WebView2.Core.dll"))&&File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"WebView2Loader.dll"))&&File.Exists(Path.Combine(Root,"gemma2","web","gemma.html"))&&File.Exists(Path.Combine(Root,"gemma2","web","request-queue.mjs"))&&File.Exists(Path.Combine(Root,"gemma2","web","gemma.mjs"))&&File.Exists(Path.Combine(Root,"gemma2","web","tensor-memory.mjs"))&&ModelAssets.Ready(Root);}}
        readonly SemaphoreSlim gate=new SemaphoreSlim(1);
        readonly System.Windows.Forms.Timer idle=new System.Windows.Forms.Timer{Interval=120000};
        internal readonly Dictionary<string,float[]> reactionVectors=new Dictionary<string,float[]>();
        internal List<ReactionScore> LastRanking=new List<ReactionScore>();
        bool powerSaving;
        bool automaticAnalysis;StickerAnalyzer analyzer;
        public bool AutomaticAnalysis{get{return automaticAnalysis;}set{automaticAnalysis=value;if(!value)ReleaseAnalyzer();}}
        internal void ReleaseAnalyzer(){if(analyzer!=null){analyzer.Dispose();analyzer=null;}}
        public bool PowerSaving{get{return powerSaving;}set{powerSaving=value;idle.Stop();if(value&&host!=null)idle.Start();}}
        internal void ReleaseIdle(){idle.Stop();if(!PowerSaving)return;if(gate.CurrentCount==1)Stop();else idle.Start();}
        public int LastAnalyzedImages{get;private set;}
        public bool UsedSmileFallback{get;private set;}
                
        const int VectorMemoryLimit=2048;
        readonly Dictionary<string,float[]> imageMemory=new Dictionary<string,float[]>(),documentMemory=new Dictionary<string,float[]>();
        readonly Queue<string> imageOrder=new Queue<string>(),documentOrder=new Queue<string>();
        static void Remember(Dictionary<string,float[]> cache,Queue<string> order,string key,float[] vector){if(!cache.ContainsKey(key))order.Enqueue(key);cache[key]=vector;while(cache.Count>VectorMemoryLimit)cache.Remove(order.Dequeue());}
        readonly Func<IEmbeddingHost> factory;readonly Func<bool> isReady;readonly string vectorDirectory;readonly Func<string,StickerDescription> descriptionReader;
        IEmbeddingHost host;Task initialization;CancellationTokenSource lifetime;public Action<string> Progress;
        public int LastSkippedImages{get;private set;}
        public int ModelInitializations{get;private set;}
        public int BlockedRequests {get{return host==null?0:host.BlockedRequests;}}
        public SemanticSearch():this(()=>new GemmaHost(),()=>Ready,Path.Combine(Root,"vectors",Spec)){}
        internal SemanticSearch(Func<IEmbeddingHost> factory,Func<bool> isReady,string vectorDirectory,Func<string,StickerDescription> descriptionReader=null){this.factory=factory;this.isReady=isReady;this.vectorDirectory=vectorDirectory;this.descriptionReader=descriptionReader??(hash=>StickerAnalysis.Read(hash));idle.Tick+=(s,e)=>ReleaseIdle();}
        static void ObserveInitialization(Task initialization){initialization.ContinueWith(task=>{var ignored=task.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        void Report(string message){if(Progress!=null)Progress(message);}
        static async Task AwaitInitialization(Task task,CancellationToken cancel){if(!task.IsCompleted&&cancel.CanBeCanceled){var canceled=new TaskCompletionSource<bool>();using(cancel.Register(()=>canceled.TrySetResult(true))){if(await Task.WhenAny(task,canceled.Task)!=task)cancel.ThrowIfCancellationRequested();}}await task;cancel.ThrowIfCancellationRequested();}
        public async Task<List<Entry>> Search(string query,List<Entry> entries,CancellationToken cancel){
            if(!isReady())throw new Exception("모델 파일을 확인하지 못했어요. 설정에서 모델·환경 다시 확인을 실행해주세요.");
            if(String.IsNullOrWhiteSpace(query))return new List<Entry>();
            string rawQuery=query;query=RecommendationQuery.Meaning(query);
            await gate.WaitAsync(cancel);idle.Stop();LastSkippedImages=0;LastAnalyzedImages=0;UsedSmileFallback=false;LastRanking.Clear();
            try{
                if(host==null){Report("EmbeddingGemma 2 q4 · WebGPU 준비 중");host=factory();ModelInitializations++;lifetime=new CancellationTokenSource();initialization=host.Start(lifetime.Token);ObserveInitialization(initialization);}
                await AwaitInitialization(initialization,cancel);
                var candidates=entries.Where(e=>!String.IsNullOrEmpty(e.Path)&&File.Exists(e.Path)).ToList();
                var vectors=new List<float[]>();var documents=new List<float[]>();var hashes=new List<string>();var descriptions=new List<StickerDescription>();var indexed=new List<Entry>();Directory.CreateDirectory(vectorDirectory);
                for(int i=0;i<candidates.Count;i++){
                    cancel.ThrowIfCancellationRequested();var entry=candidates[i];if(!imageMemory.Any())Report("콘 자동 분석 중 · "+(i+1)+" / "+candidates.Count);
                    try{
                        if(new FileInfo(entry.Path).Length>32*1024*1024){LastSkippedImages++;continue;}
                        var embedded=await ImageVector(entry,cancel);float[] vector=embedded.Item2;
                        var description=AutomaticAnalysis?descriptionReader(embedded.Item1):null;var document=await DocumentVector(entry,cancel,description);
                        descriptions.Add(description);documents.Add(document);hashes.Add(embedded.Item1);
                        vectors.Add(vector);indexed.Add(entry);
                    }catch(Exception ex){if(ex is LocalImageException||ex is IOException||ex is UnauthorizedAccessException){LastSkippedImages++;continue;}throw;}
                }
                LastAnalyzedImages=indexed.Count;
                if(indexed.Count==0)return new List<Entry>();
                Report("문장 분석 중");var intent=RecommendationQuery.Intent(rawQuery);float[] text;
                foreach(var reaction in RecommendationQuery.Reactions){if(!reactionVectors.ContainsKey(reaction.Key))reactionVectors[reaction.Key]=await host.Query(ReactionPrompts.Text(reaction,AutomaticRanker.PromptMode),cancel);}
                text=intent==null?await host.Query(query,cancel):reactionVectors[intent.Key];
                cancel.ThrowIfCancellationRequested();
                var concepts=RecommendationQuery.Reactions.Select(reaction=>reactionVectors[reaction.Key]).ToArray();
                var keys=RecommendationQuery.Reactions.Select(reaction=>reaction.Key).ToArray();int intentIndex=intent==null?-1:Array.IndexOf(RecommendationQuery.Reactions,intent);
                var scores=await Task.Run(()=>AutomaticRanker.Score(vectors,documents,concepts,keys,intentIndex,text,cancel,hashes),cancel);
                bool uncertainNegation=intent==null&&RecommendationQuery.AvoidGuessing(rawQuery);
                LastRanking=indexed.Select((entry,i)=>{
                    var prediction=scores[i];double bonus=RecommendationQuery.MetadataBonus(entry,rawQuery)+AnalysisEvidence.TextBonus(descriptions[i],rawQuery),score=prediction.Margin+bonus;
                    bool accepted=prediction.Accepted;
                    if(intent!=null&&descriptions[i]!=null){
                        bool supports=AnalysisEvidence.Supports(descriptions[i],intent.Key,prediction);
                        // Generated labels may overlap (e.g. tearful thumbs-up).
                        // They are import-time evidence, never a fixed image table.
                        accepted=supports;
                        if(supports)score=AnalysisEvidence.Rank(descriptions[i],intent.Key,prediction,bonus);
                    }
                    if(bonus>=.16)score=Math.Max(.60,score)+bonus;
                    return new ReactionScore{Entry=entry,Hash=hashes[i],Similarity=prediction.Similarity,Margin=prediction.Margin,RawMargin=prediction.RawMargin,ImageValue=prediction.ImageValue,ImageMargin=prediction.ImageMargin,Score=score,Accepted=(!uncertainNegation&&accepted)||bonus>0};
                }).OrderByDescending(item=>item.Score).ThenByDescending(item=>item.Entry.LastUsed).ToList();
                var results=LastRanking.Where(x=>x.Accepted).GroupBy(x=>x.Hash).Select(group=>group.First()).Take(RecommendationLimit).Select(x=>x.Entry).ToList();
                if(results.Count==0&&AutomaticAnalysis&&intent!=null&&intent.Key=="laugh"&&AnalysisEvidence.GentleLaughter(rawQuery)){
                    int happy=Array.IndexOf(keys,"happy");var related=await Task.Run(()=>AutomaticRanker.Score(vectors,documents,concepts,keys,happy,concepts[happy],cancel,hashes),cancel);
                    results=indexed.Select((entry,i)=>new{Entry=entry,Index=i}).Where(row=>AnalysisEvidence.RelatedSmile(descriptions[row.Index],related[row.Index])).OrderByDescending(row=>related[row.Index].Margin).ThenByDescending(row=>row.Entry.LastUsed).GroupBy(row=>hashes[row.Index]).Select(group=>group.First().Entry).Take(RecommendationLimit).ToList();
                    UsedSmileFallback=results.Count>0;
                }
                return results;
            }catch(OperationCanceledException){if(!cancel.IsCancellationRequested)Stop();throw;}catch{Stop();throw;}finally{gate.Release();if(host!=null&&PowerSaving)idle.Start();}
        }
        async Task<Tuple<string,float[]>> ImageVector(Entry entry,CancellationToken cancel){
            Directory.CreateDirectory(vectorDirectory);
            string digest=await Task.Run(()=>{using(var stream=File.OpenRead(entry.Path))using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();},cancel);
            float[] vector;if(imageMemory.TryGetValue(digest,out vector))return Tuple.Create(digest,vector);
            string folder=vectorDirectory+"-automatic-weighted3-v3",file=Path.Combine(folder,digest+".json");
            if(File.Exists(file))try{vector=GemmaHost.Normalize(GemmaHost.Read<float[]>(File.ReadAllText(file)));Remember(imageMemory,imageOrder,digest,vector);return Tuple.Create(digest,vector);}catch{}
            string original=Path.Combine(vectorDirectory,digest+".json");float[] full=null;
            if(File.Exists(original))try{full=GemmaHost.Normalize(GemmaHost.Read<float[]>(File.ReadAllText(original)));}catch{}
            if(full==null){full=await host.Image(entry.Path,cancel);cancel.ThrowIfCancellationRequested();WriteVector(original,full);}
            var viewHost=host as IImageViewHost;
            if(viewHost==null)vector=full;
            else{
                List<float[]> views=null;string cachedViews=Path.Combine(vectorDirectory+"-views-v1",digest+".json");
                if(File.Exists(cachedViews))try{views=GemmaHost.Read<List<float[]>>(File.ReadAllText(cachedViews));if(views.Count!=3)throw new InvalidDataException("Invalid views");views=views.Select(GemmaHost.Normalize).ToList();}catch{views=null;}
                if(views==null)views=new List<float[]>{full,await viewHost.ImageView(entry.Path,"top",cancel),await viewHost.ImageView(entry.Path,"center",cancel)};
                vector=GemmaHost.Normalize(Enumerable.Range(0,768).Select(i=>AutomaticRanker.FullImageWeight*views[0][i]+(1-AutomaticRanker.FullImageWeight)/2*(views[1][i]+views[2][i])).ToArray());
            }
            cancel.ThrowIfCancellationRequested();Directory.CreateDirectory(folder);WriteVector(file,vector);Remember(imageMemory,imageOrder,digest,vector);return Tuple.Create(digest,vector);
        }
        static void WriteVector(string file,float[] vector){string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temporary,GemmaHost.Json(vector),new UTF8Encoding(false));if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);}
        async Task<float[]> DocumentVector(Entry entry,CancellationToken cancel,StickerDescription analysis=null){
            var documentHost=host as IDocumentHost;string caption=EntryMetadata.Caption(entry);
            if(analysis!=null)caption=String.Join(". ",new[]{caption,analysis.Document}.Where(s=>!String.IsNullOrWhiteSpace(s)));
            if(documentHost==null||String.IsNullOrWhiteSpace(caption))return null;
            string digest;using(var sha=System.Security.Cryptography.SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(caption))).Replace("-","").ToLowerInvariant();
            float[] cached;if(documentMemory.TryGetValue(digest,out cached))return cached;
            string folder=vectorDirectory+EntryMetadata.CacheSuffix,file=Path.Combine(folder,digest+".json");if(File.Exists(file))try{cached=GemmaHost.Normalize(GemmaHost.Read<float[]>(File.ReadAllText(file)));Remember(documentMemory,documentOrder,digest,cached);return cached;}catch{}
            var vector=await documentHost.Document(caption,cancel);cancel.ThrowIfCancellationRequested();Directory.CreateDirectory(folder);string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temporary,GemmaHost.Json(vector),new UTF8Encoding(false));if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);Remember(documentMemory,documentOrder,digest,vector);return vector;
        }
        internal async Task PrepareRemote(Entry entry,CancellationToken cancel){try{entry.Path=await Task.Run(()=>DcShop.EnsureImage(entry,cancel),cancel);}catch(OperationCanceledException){throw;}catch(Exception ex){throw new LocalImageException(ex);}await Index(entry,cancel);}
        public async Task Index(Entry entry,CancellationToken cancel){
            if(!isReady())return;
            await gate.WaitAsync(cancel);idle.Stop();
            try{
                if(host==null){host=factory();ModelInitializations++;lifetime=new CancellationTokenSource();initialization=host.Start(lifetime.Token);ObserveInitialization(initialization);}
                await AwaitInitialization(initialization,cancel);
                if(new FileInfo(entry.Path).Length>32*1024*1024)throw new IOException("Image size limit");
                var embedded=await ImageVector(entry,cancel);var description=AutomaticAnalysis?descriptionReader(embedded.Item1):null;
                if(AutomaticAnalysis&&description==null&&await Task.Run(()=>StickerAnalysis.Ready,cancel)){
                    Report("글자·표정·동작 자동 분석 중");if(analyzer==null)analyzer=new StickerAnalyzer();
                    try{description=await analyzer.Analyze(entry.Path,embedded.Item1,cancel);}
                    catch(OperationCanceledException){throw;}catch(Exception ex){throw new IOException("콘 자동 설명을 만들지 못했어요. 이미지 검색은 유지해요.",ex);}
                }
                await DocumentVector(entry,cancel,description);cancel.ThrowIfCancellationRequested();
            }catch(OperationCanceledException){ReleaseAnalyzer();if(!cancel.IsCancellationRequested)Stop();throw;}
            catch(LocalImageException){throw;}catch(IOException){throw;}catch(UnauthorizedAccessException){throw;}catch{Stop();throw;}
            finally{gate.Release();if(host!=null&&PowerSaving)idle.Start();}
        }
        static double Dot(float[] left,float[] right){return left.Select((value,j)=>(double)value*right[j]).Sum();}
        void Stop(){ReleaseAnalyzer();imageMemory.Clear();imageOrder.Clear();documentMemory.Clear();documentOrder.Clear();reactionVectors.Clear();if(lifetime!=null){lifetime.Cancel();lifetime.Dispose();lifetime=null;}if(host!=null){host.Dispose();host=null;}initialization=null;}
        public void Dispose(){idle.Stop();Stop();}
        public static string BootstrapPython(){string bundled=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime","python","python.exe");if(File.Exists(bundled))return bundled;foreach(string version in new[]{"314","313","312","311","310"}){string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Python","Python"+version,"python.exe");if(File.Exists(path))return path;}return "python.exe";}
    }
    [DataContract] sealed class ReactionScore {
        [DataMember]public string Hash;[DataMember]public bool Accepted;[DataMember]public Entry Entry;[DataMember]public double Similarity,Margin,RawMargin,Score,ImageValue,ImageMargin;
    }
    [DataContract] sealed class InstallProgress {
        [DataMember(Name="type")]public string Type;
        [DataMember(Name="phase")]public string Phase;
        [DataMember(Name="file")]public string File;
        [DataMember(Name="percent")]public int Percent;
        [DataMember(Name="done")]public long Done;
        [DataMember(Name="total")]public long Total;
        [DataMember(Name="remaining")]public long Remaining;
        [DataMember(Name="rate")]public double Rate;
        [DataMember(Name="eta")]public long Eta;
        [DataMember(Name="completed")]public int Completed;
        [DataMember(Name="count")]public int Count;
    }
    sealed class InstallBar:Control {
        readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer{Interval=60};int value,position;ProgressBarStyle style;
        public int Maximum=100;
        public int Value{get{return value;}set{this.value=Math.Max(0,Math.Min(Maximum,value));AccessibleDescription=this.value+"%";Invalidate();}}
        public ProgressBarStyle Style{get{return style;}set{style=value;animation.Enabled=style==ProgressBarStyle.Marquee&&Visible;Invalidate();}}
        public InstallBar(){DoubleBuffered=true;AccessibleName="설치 진행률";AccessibleRole=AccessibleRole.ProgressBar;animation.Tick+=(s,e)=>{position=(position+3)%130;Invalidate();};VisibleChanged+=(s,e)=>animation.Enabled=style==ProgressBarStyle.Marquee&&Visible;}
        protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Parent==null?Ui.Paper:Parent.BackColor);if(Width<12||Height<4)return;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var track=Ui.Round(new Rectangle(0,0,Width-1,Height-1),Height/2)){using(var brush=new SolidBrush(Ui.Line))e.Graphics.FillPath(brush,track);e.Graphics.SetClip(track);int width=style==ProgressBarStyle.Marquee?Width/4:(int)(Width*(double)value/Math.Max(1,Maximum));int x=style==ProgressBarStyle.Marquee?(int)(Width*(position-30)/100.0):0;using(var brush=new SolidBrush(Ui.Accent))e.Graphics.FillRectangle(brush,x,0,width,Height);e.Graphics.ResetClip();}}
        protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
    }
    public class SemanticSettingsForm:Form {
        readonly CheckBox enabled=new CheckBox();readonly Label status=Ui.Label("",9);
        readonly InstallBar progress=new InstallBar{Dock=DockStyle.Top,Height=9,Maximum=100,Style=ProgressBarStyle.Continuous};
        readonly Action<bool> changed;readonly TableLayoutPanel layout;UiButton prepareButton,abortButton,analysisButton;CheckBox analysisOption;bool installing,cancelRequested;Process setup;
        public SemanticSettingsForm(bool use,Action<bool> onChange,bool autoDraft=false,Action<bool> onDraftChange=null,bool powerSaving=false,Action<bool> onPowerSavingChange=null,bool automaticAnalysis=false,Action<bool> onAnalysisChange=null){
            changed=onChange;Icon=Ui.AppIcon();Text="아지트콘 · 로컬 의미 검색";ClientSize=new Size(560,610);BackColor=Ui.Paper;Font=Ui.Regular(10);StartPosition=FormStartPosition.CenterParent;
            layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=6};foreach(int h in new[]{42,116,120,76,142,40})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));Controls.Add(layout);
            var title=Ui.Label("문장으로 콘 찾기",17,true);title.Dock=DockStyle.Fill;layout.Controls.Add(title,0,0);
            var desc=Ui.Label("EmbeddingGemma 2 q4 · WebGPU로 문장과 콘을 비교해요.\n등록한 팩을 자동 분석하며 검색 문장은 저장·전송하지 않아요.\n전체 그림과 세부 영역, GIF의 여러 프레임을 참고해요.\n처음 한 번 모델 약 316MB와 실행 파일을 받아요.\n추가 분석: Qwen3-VL 2B · 약 1.98GB · 분석 뒤 메모리에서 내려요.",9);desc.Dock=DockStyle.Fill;desc.ForeColor=Ui.Muted;layout.Controls.Add(desc,0,1);
            enabled.Text="의미 검색 사용 (받아둔 콘 대상)";enabled.AutoSize=true;enabled.Enabled=SemanticSearch.Ready;enabled.Checked=use&&SemanticSearch.Ready;enabled.CheckedChanged+=(s,e)=>changed(enabled.Checked);var options=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};enabled.Margin=new Padding(0,0,0,5);options.Controls.Add(enabled);var draft=new CheckBox();draft.Text="카톡 작성 중 문장으로 자동 추천 (접근성 지원 시)";draft.AutoSize=true;draft.Checked=autoDraft;draft.CheckedChanged+=(s,e)=>{if(onDraftChange!=null)onDraftChange(draft.Checked);};options.Controls.Add(draft);var power=new CheckBox{Text="절전: 2분 동안 검색하지 않으면 모델 해제",AutoSize=true,Checked=powerSaving};power.CheckedChanged+=(s,e)=>{if(onPowerSavingChange!=null)onPowerSavingChange(power.Checked);};options.Controls.Add(power);layout.Controls.Add(options,0,2);
            prepareButton=Ui.Button(SemanticSearch.Ready?"모델·환경 다시 확인":"환경·모델 다운로드",async()=>await Prepare(),true);prepareButton.Width=220;layout.Controls.Add(prepareButton,0,3);
            analysisOption=new CheckBox{Text="팩의 글자·표정·동작 자동 분석 (별도 로컬 모델)",AutoSize=true,Checked=automaticAnalysis,Enabled=StickerAnalysis.Ready};
            analysisOption.CheckedChanged+=(s,e)=>{if(onAnalysisChange!=null)onAnalysisChange(analysisOption.Checked);};options.Controls.Add(analysisOption);
            layout.Controls.Remove(prepareButton);var installers=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};prepareButton.Height=30;installers.Controls.Add(prepareButton);
            analysisButton=Ui.Button(StickerAnalysis.Ready?"자동 분석 모델 다시 확인":"자동 분석 모델 추가 · 약 1.98GB",async()=>await Prepare(true));analysisButton.Width=290;analysisButton.Height=30;analysisButton.Enabled=SemanticSearch.Ready;installers.Controls.Add(analysisButton);layout.Controls.Add(installers,0,3);
            var progressArea=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,8,0,0),ColumnCount=1,RowCount=2};progressArea.RowStyles.Add(new RowStyle(SizeType.Absolute,20));progressArea.RowStyles.Add(new RowStyle(SizeType.Percent,100));progressArea.Controls.Add(progress,0,0);status.Dock=DockStyle.Fill;status.Padding=new Padding(0,4,0,0);progressArea.Controls.Add(status,0,1);layout.Controls.Add(progressArea,0,4);
            progress.Value=SemanticSearch.Ready?100:0;status.Text=SemanticSearch.Ready?"설치 완료 · 100%\n"+(enabled.Checked?"의미 검색이 켜져 있어요.":"위에서 의미 검색을 켤 수 있어요."):"아직 설치하지 않았어요.\n버튼을 누르면 다운로드를 시작해요.";
            var links=new FlowLayoutPanel{Dock=DockStyle.Fill};abortButton=Ui.Button("설치 취소",CancelInstall);abortButton.Enabled=false;abortButton.Width=90;abortButton.Height=28;links.Controls.Add(abortButton);var link=new LinkLabel{Text="모델 안내",AutoSize=true,Margin=new Padding(10,5,0,0)};link.LinkClicked+=(s,e)=>Process.Start(new ProcessStartInfo("https://huggingface.co/onnx-community/embeddinggemma-2-ONNX"){UseShellExecute=true});links.Controls.Add(link);layout.Controls.Add(links,0,5);
            FormClosing+=(s,e)=>{if(installing){e.Cancel=true;status.Text="설치 중이에요. 취소하려면 ‘설치 취소’를 눌러주세요.";}};
        }
        public void AddThumbnailSettings(bool preload,Action<bool> onChange,Action start,Action stop,Func<string> state,Func<int> percent,Func<bool> busy){
            Text="아지트콘 · 설정";ClientSize=new Size(560,650);Controls.Remove(layout);var tabs=new TabControl{Dock=DockStyle.Fill};var models=new TabPage("문장 추천"){BackColor=Ui.Paper};models.Controls.Add(layout);var previews=new TabPage("썸네일"){BackColor=Ui.Paper};tabs.TabPages.Add(models);tabs.TabPages.Add(previews);Controls.Add(tabs);
            var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=7};foreach(int height in new[]{44,86,54,44,22,110})panel.RowStyles.Add(new RowStyle(SizeType.Absolute,height));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));previews.Controls.Add(panel);
            var heading=Ui.Label("썸네일 미리 불러오기",17,true);heading.Dock=DockStyle.Fill;panel.Controls.Add(heading);
            var description=Ui.Label("등록한 콘의 이미지를 미리 받아 썸네일을 준비해요.\n저장한 썸네일은 앱을 다시 켜도 재사용해요.\n화면에 보이는 콘은 미리 불러오기보다 먼저 처리해요.",10);description.Dock=DockStyle.Fill;panel.Controls.Add(description);
            var automatic=new CheckBox{Text="앱 시작·팩 추가 시 자동으로 미리 불러오기",Checked=preload,AutoSize=true};automatic.CheckedChanged+=(s,e)=>onChange(automatic.Checked);panel.Controls.Add(automatic);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};var prepare=Ui.Button("지금 미리 불러오기",start,true);prepare.Width=180;var cancel=Ui.Button("중지",stop);cancel.Width=80;buttons.Controls.AddRange(new Control[]{prepare,cancel});panel.Controls.Add(buttons);
            var bar=new InstallBar{Dock=DockStyle.Top,Height=9,Maximum=100};panel.Controls.Add(bar);var summary=Ui.Label("",10);summary.Dock=DockStyle.Fill;panel.Controls.Add(summary);var help=Ui.Label("이미 받은 원본과 썸네일은 다시 내려받지 않아요.\n등록된 팩의 아직 안 받은 이미지는 인터넷으로 받아요.\n메모리 보관 한도는 128 MB이며 오래 안 본 것부터 비워요.\n움짤은 썸네일을 먼저 띄우고 화면에서 최대 2개를 재생해요.",9);help.Dock=DockStyle.Fill;help.ForeColor=Ui.Muted;panel.Controls.Add(help);
            var poll=new System.Windows.Forms.Timer{Interval=250};Action refresh=()=>{if(IsDisposed)return;summary.Text=state();bar.Value=Math.Max(0,Math.Min(100,percent()));prepare.Enabled=!busy();cancel.Enabled=busy();};poll.Tick+=(s,e)=>refresh();FormClosed+=(s,e)=>poll.Dispose();refresh();poll.Start();
        }
        public void AddOcrSettings(bool direct=false,Action<bool> directChanged=null){
            var tabs=Controls.OfType<TabControl>().Single();var page=new TabPage("움짤"){BackColor=Ui.Paper};tabs.TabPages.Add(page);
            var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=6};foreach(int height in new[]{44,40,70,70,65,44})panel.RowStyles.Add(new RowStyle(SizeType.Absolute,height));page.Controls.Add(panel);
            var title=Ui.Label("움짤 전송 방식",17,true);title.Dock=DockStyle.Fill;panel.Controls.Add(title);
            var automatic=new CheckBox{Text="움짤 클릭 시 바로 전송",Checked=direct,Dock=DockStyle.Fill,AutoSize=true};panel.Controls.Add(automatic);
            var description=Ui.Label("켜면 클릭한 움짤을 카톡으로 바로 보내요.\n끄면 전송 확인창에서 직접 전송해요.",9);description.Dock=DockStyle.Fill;panel.Controls.Add(description);
            var ocr=Ui.Label("전송 전 확인 모드에서 카톡 설정을 읽을 때 한국어 OCR을 사용해요.\n바로 전송 모드에서는 OCR 설치 없이 움짤을 선택할 수 있어요.",9);ocr.Dock=DockStyle.Fill;panel.Controls.Add(ocr);
            var status=Ui.Label("탭을 열면 OCR 설치 상태를 확인해요.",9);status.Dock=DockStyle.Fill;panel.Controls.Add(status);
            var button=Ui.Button("한국어 OCR 확인·설치",()=>{using(var setup=new OcrSetupForm())setup.ShowDialog(this);});button.Width=230;panel.Controls.Add(button);
            Action update=()=>{ocr.Visible=status.Visible=button.Visible=!automatic.Checked;};update();automatic.CheckedChanged+=(s,e)=>{update();if(directChanged!=null)directChanged(automatic.Checked);};
            Func<Task> refresh=async()=>{status.Text="한국어 OCR 확인 중…";var result=await OcrSupport.CheckAsync();if(!IsDisposed)status.Text=result.Caption;};tabs.SelectedIndexChanged+=async(s,e)=>{if(tabs.SelectedTab==page&&!automatic.Checked)await refresh();};button.Click+=async(s,e)=>await refresh();
        }
        internal static void TestThumbnails(){string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"thumbnail-settings-test.txt");try{Application.EnableVisualStyles();int changes=0,starts=0,stops=0;bool busy=false;using(var form=new SemanticSettingsForm(false,value=>{})){form.AddThumbnailSettings(true,value=>changes++,()=>{starts++;busy=true;},()=>{stops++;busy=false;},()=>"저장된 썸네일 확인 중 · 5 / 10\n메모리: 5개 · 0.5 / 128 MB",()=>50,()=>busy);var tabs=form.Controls.OfType<TabControl>().Single();tabs.SelectedIndex=1;form.Controls.Remove(tabs);using(var surface=new UserControl{Size=form.ClientSize,BackColor=Ui.Paper}){surface.Controls.Add(tabs);surface.CreateControl();surface.PerformLayout();var panel=(TableLayoutPanel)tabs.TabPages[1].Controls[0];var automatic=panel.Controls.OfType<CheckBox>().Single();automatic.Checked=false;var buttons=panel.Controls.OfType<FlowLayoutPanel>().Single();buttons.Controls.OfType<UiButton>().First().PerformClick();var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<350){Application.DoEvents();Thread.Sleep(1);}buttons.Controls.OfType<UiButton>().Last().PerformClick();if(changes!=1||starts!=1||stops!=1)throw new Exception("Controls did not invoke callbacks");using(var bitmap=new Bitmap(surface.Width,surface.Height)){surface.DrawToBitmap(bitmap,surface.ClientRectangle);bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"thumbnail-settings-preview.png"));}if(panel.Controls.Cast<Control>().Any(c=>c.Bottom>panel.ClientSize.Height))throw new Exception("Settings overflow");}}File.WriteAllText(report,"PASS: thumbnail tab, persisted-option callback, start/stop callbacks, progress/usage layout rendered offscreen. No real preload/model/network/Kakao interaction.");}catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}}
        static string MB(double bytes){return (Math.Max(0,bytes)/1000000.0).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" MB";}
        internal static string Describe(InstallProgress p){
            if(p.Phase=="done")return "설치 완료 · 100%\n다운로드와 파일 검증이 끝났어요.";
            if(p.Phase=="verify")return "다운로드 확인 완료 · 파일 검증 중\n검증이 끝나면 설치 완료로 표시해요.";
            string title=p.Phase=="cached"?"받아둔 파일 확인":"다운로드";
            string percentage=p.Percent>=0?Math.Min(99,p.Percent)+"% · 남은 "+(100-Math.Min(99,p.Percent))+"%":"전체 용량 확인 중";
            string size=p.Total>0?MB(p.Done)+" / "+MB(p.Total)+" · 남은 "+MB(p.Remaining):"받은 용량 "+MB(p.Done);
            string speed=p.Rate>0?MB(p.Rate)+"/s":"속도 측정 중";
            string eta=p.Eta>0?(p.Eta<60?"약 "+p.Eta+"초 남음":"약 "+(long)Math.Ceiling(p.Eta/60.0)+"분 남음"):"남은 시간 계산 중";
            return title+" "+percentage+"\n"+size+"\n"+speed+" · "+eta+"\n"+p.File+" · 완료 "+p.Completed+" / "+p.Count+"개";
        }
        void ShowProgress(InstallProgress p){if(cancelRequested)return;progress.Style=p.Percent<0?ProgressBarStyle.Marquee:ProgressBarStyle.Continuous;if(p.Percent>=0)progress.Value=p.Phase=="done"?100:Math.Max(0,Math.Min(99,p.Percent));status.Text=Describe(p);}
        void Receive(string line){if(String.IsNullOrWhiteSpace(line)||!line.StartsWith("{"))return;try{var p=GemmaHost.Read<InstallProgress>(line);if(p.Type!="progress"||IsDisposed||!IsHandleCreated)return;BeginInvoke((Action)(()=>{if(!IsDisposed&&installing)ShowProgress(p);}));}catch{} }
        async Task Prepare(bool analysis=false){
            if(analysis&&!SemanticSearch.Ready){status.Text="기본 의미 검색 모델·환경을 먼저 준비해주세요.";return;}
            if(installing)return;installing=true;cancelRequested=false;enabled.Enabled=false;prepareButton.Enabled=false;abortButton.Enabled=true;progress.Style=ProgressBarStyle.Marquee;
            analysisButton.Enabled=false;analysisOption.Enabled=false;
            status.Text="다운로드 용량을 확인하고 있어요.\n속도가 측정되면 예상 남은 시간을 표시해요.";
            try{var info=new ProcessStartInfo(SemanticSearch.BootstrapPython(),"\""+Path.Combine(SemanticSearch.Root,analysis?"prepare-caption.py":"prepare.py")+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};info.EnvironmentVariables["PYTHONUTF8"]="1";
                int exit=await Task.Run(()=>{using(var process=Process.Start(info)){setup=process;try{process.OutputDataReceived+=(s,e)=>Receive(e.Data);process.ErrorDataReceived+=(s,e)=>{};process.BeginOutputReadLine();process.BeginErrorReadLine();process.WaitForExit();return process.ExitCode;}finally{setup=null;}}});
                if(cancelRequested){status.Text="설치를 취소했어요.\n다운로드가 끝난 파일은 다음에 다시 사용해요.";return;}
                if(exit!=0||!(analysis?StickerAnalysis.Ready:SemanticSearch.Ready))throw new Exception("설치에 실패했어요. 네트워크 연결과 Python 설치를 확인해주세요.");
                if(analysis)analysisOption.Checked=true;progress.Style=ProgressBarStyle.Continuous;progress.Value=100;status.Text="설치 완료 · 100%\n"+(analysis?"팩 자동 분석을 켰어요. 새 콘을 한 개씩 분석해요.":enabled.Checked?"의미 검색이 켜져 있어요.":"위에서 의미 검색을 켤 수 있어요.");
            }catch(Exception ex){status.Text=ex.Message;}finally{installing=false;progress.Style=ProgressBarStyle.Continuous;enabled.Enabled=SemanticSearch.Ready;prepareButton.Enabled=true;analysisButton.Enabled=SemanticSearch.Ready;analysisOption.Enabled=StickerAnalysis.Ready;abortButton.Enabled=false;}
        }
        void CancelInstall(){var process=setup;if(!installing||process==null)return;try{if(!process.HasExited){cancelRequested=true;abortButton.Enabled=false;Process.Start(new ProcessStartInfo("taskkill.exe","/PID "+process.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true});status.Text="설치를 취소하고 있어요...";}}catch(Exception ex){status.Text="설치 취소: "+ex.Message;}}
        public static void RenderProgress(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var form=new SemanticSettingsForm(true,value=>{}))using(var surface=new UserControl{Size=form.ClientSize,Font=form.Font,BackColor=Ui.Paper}){form.Controls.Remove(form.layout);surface.Controls.Add(form.layout);surface.CreateControl();form.installing=true;form.prepareButton.Enabled=false;form.abortButton.Enabled=true;form.ShowProgress(new InstallProgress{Phase="download",Percent=62,Done=219000000,Total=353164458,Remaining=134164458,Rate=7200000,Eta=19,Completed=9,Count=14,File="vision_encoder_q4.onnx_data"});using(var bitmap=new Bitmap(surface.Width,surface.Height)){surface.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"install-progress-preview.png"));}form.installing=false;}}
        public static void ProgressTest(){try{var p=new InstallProgress{Phase="download",Percent=50,Done=50000000,Total=100000000,Remaining=50000000,Rate=5000000,Eta=10,Completed=2,Count=14,File="model_q4.onnx_data"};string text=Describe(p);if(!text.Contains("남은 50%")||!text.Contains("50.0 MB")||!text.Contains("약 10초"))throw new Exception("Progress display");p.Eta=-1;p.Rate=0;if(!Describe(p).Contains("남은 시간 계산 중"))throw new Exception("Unknown ETA");p.Phase="verify";if(Describe(p).Contains("100%"))throw new Exception("Premature completion");p.Phase="done";if(!Describe(p).Contains("100%"))throw new Exception("Final completion");File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"install-progress-test.txt"),"PASS: percent, remaining bytes, ETA, unknown speed, verification and final completion. No installer, GPU, or UI interaction.\r\n");}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"install-progress-test.txt"),"FAIL: "+ex.Message);Environment.ExitCode=1;}}
    }
}
