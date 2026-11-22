using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace AgitCon {
    [DataContract] sealed class GemmaMessage {
        [DataMember(Name="id")]public int Id;
        [DataMember(Name="type")]public string Type;
        [DataMember(Name="error")]public string Error;
        [DataMember(Name="vector")]public float[] Vector;
        [DataMember(Name="text")]public string Text;
        [DataMember(Name="confidence")]public double Confidence;
        [DataMember(Name="regions")]public List<OcrRegion> Regions;
    }
    [DataContract] sealed class GemmaRequest {
        [DataMember(Name="cancelId",EmitDefaultValue=false)]public int CancelId;
        [DataMember(Name="id")]public int Id;
        [DataMember(Name="action")]public string Action;
        [DataMember(Name="query",EmitDefaultValue=false)]public string Query;
        [DataMember(Name="frames",EmitDefaultValue=false)]public string[] Frames;
    }
    // Hidden compute surface only. It never observes another application or uses
    // the clipboard, input APIs, a remote model hub, or a local HTTP listener.
    interface IEmbeddingHost:IDisposable {Task Start(CancellationToken cancel);Task<float[]> Query(string query,CancellationToken cancel);Task<float[]> Image(string path,CancellationToken cancel);int BlockedRequests{get;}}
    [DataContract] sealed class OcrRegion {[DataMember(Name="text")]public string Text;[DataMember(Name="confidence")]public double Confidence;}
    [DataContract] sealed class OcrText {[DataMember]public string Text;[DataMember]public double Confidence;[DataMember]public List<OcrRegion> Regions;}
    interface IImageViewHost {Task<float[]> ImageView(string path,string view,CancellationToken cancel);}
    interface IDocumentHost {Task<float[]> Document(string text,CancellationToken cancel);}
    sealed class LocalImageException:Exception {public LocalImageException(Exception inner):base("이미지 파일을 읽지 못했어요.",inner){}}
    sealed class GemmaHost:IEmbeddingHost,IDocumentHost,IImageViewHost,IStickerDescriptionHost,IStickerDescriptionRetryHost {
        const string Origin="https://ajitcon.local/";
        readonly string profileDirectory;readonly string page;
        public GemmaHost(string profileDirectory=null,string page="gemma.html"){this.page=page;this.profileDirectory=profileDirectory??Path.Combine(SemanticSearch.Root,"gemma2","profile");}
        Form surface;CoreWebView2Controller controller;CoreWebView2Environment environment;
        readonly TaskCompletionSource<bool> ready=new TaskCompletionSource<bool>();
        readonly Dictionary<int,TaskCompletionSource<float[]>> pending=new Dictionary<int,TaskCompletionSource<float[]>>();
        readonly Dictionary<int,TaskCompletionSource<OcrText>> pendingText=new Dictionary<int,TaskCompletionSource<OcrText>>();
        int sequence;bool disposed;
        public Action<string> Diagnostics;
        public int BlockedRequests {get;private set;}
        internal static string Json<T>(T value){using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);return Encoding.UTF8.GetString(stream.ToArray());}}
        internal static T Read<T>(string json){using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(json)))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);}
        static async Task<T> Wait<T>(Task<T> task,CancellationToken cancel){var delay=Task.Delay(TimeSpan.FromMinutes(2),cancel);if(await Task.WhenAny(task,delay)!=task){cancel.ThrowIfCancellationRequested();throw new TimeoutException("로컬 GPU 분석이 오래 걸려요. 잠시 후 다시 시도해주세요.");}return await task;}
        public async Task Start(CancellationToken cancel){
            surface=new Form{ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.None,Size=new Size(32,32)};
            IntPtr handle=surface.Handle; // Never Show/Activate this form.
            environment=await CoreWebView2Environment.CreateAsync(null,profileDirectory);
            cancel.ThrowIfCancellationRequested();if(disposed)throw new OperationCanceledException();
            controller=await environment.CreateCoreWebView2ControllerAsync(handle);
            if(disposed){controller.Close();throw new OperationCanceledException();}
            controller.IsVisible=false;controller.Bounds=new Rectangle(0,0,32,32);
            var web=controller.CoreWebView2;
            web.Settings.AreDevToolsEnabled=false;web.Settings.AreDefaultContextMenusEnabled=false;web.Settings.IsStatusBarEnabled=false;
            web.Settings.IsWebMessageEnabled=true;
            web.SetVirtualHostNameToFolderMapping("ajitcon.local",Path.Combine(SemanticSearch.Root,"gemma2","web"),CoreWebView2HostResourceAccessKind.DenyCors);
            web.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
            web.WebResourceRequested+=(s,e)=>{if(!e.Request.Uri.StartsWith(Origin,StringComparison.Ordinal)){BlockedRequests++;e.Response=environment.CreateWebResourceResponse(new MemoryStream(),403,"Offline only","Content-Type: text/plain");}};
            web.NavigationStarting+=(s,e)=>{if(!e.Uri.StartsWith(Origin,StringComparison.Ordinal))e.Cancel=true;};
            web.NewWindowRequested+=(s,e)=>e.Handled=true;
            web.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;
            web.WebMessageReceived+=(s,e)=>{try{var message=Read<GemmaMessage>(e.WebMessageAsJson);if(message.Type=="ocr-stage"){if(Diagnostics!=null)Diagnostics(message.Text);return;}if(message.Type=="ready")ready.TrySetResult(true);else if(message.Type=="fatal")ready.TrySetException(new Exception(message.Error));else{TaskCompletionSource<OcrText> textTask;if(pendingText.TryGetValue(message.Id,out textTask)){pendingText.Remove(message.Id);if(!String.IsNullOrEmpty(message.Error))textTask.TrySetException(new Exception(message.Error));else textTask.TrySetResult(new OcrText{Text=message.Text??"",Confidence=message.Confidence,Regions=message.Regions});return;}TaskCompletionSource<float[]> task;if(pending.TryGetValue(message.Id,out task)){pending.Remove(message.Id);if(!String.IsNullOrEmpty(message.Error))task.TrySetException(new Exception(message.Error));else{try{task.TrySetResult(Normalize(message.Vector));}catch{task.TrySetException(new Exception("모델이 올바른 벡터를 반환하지 않았어요."));}}}}}catch{ready.TrySetException(new Exception("로컬 모델 응답을 읽지 못했어요."));}};
            web.Navigate(Origin+page);await Wait(ready.Task,cancel);
        }
        async Task<float[]> Call(string action,string query,string[] frames,CancellationToken cancel){cancel.ThrowIfCancellationRequested();if(disposed)throw new ObjectDisposedException("GemmaHost");int id=++sequence;var task=new TaskCompletionSource<float[]>();pending[id]=task;try{controller.CoreWebView2.PostWebMessageAsJson(Json(new GemmaRequest{Id=id,Action=action,Query=query,Frames=frames}));return await Wait(task.Task,cancel);}finally{pending.Remove(id);if(cancel.IsCancellationRequested&&!task.Task.IsCompleted&&!disposed)try{controller.CoreWebView2.PostWebMessageAsJson(Json(new GemmaRequest{Action="cancel",CancelId=id}));}catch{}}}
        public async Task<OcrText> ReadText(string path,CancellationToken cancel){cancel.ThrowIfCancellationRequested();if(disposed)throw new ObjectDisposedException("GemmaHost");int id=++sequence;var task=new TaskCompletionSource<OcrText>();pendingText[id]=task;try{controller.CoreWebView2.PostWebMessageAsJson(Json(new GemmaRequest{Id=id,Action="ocr",Frames=ReadFrames(path)}));return await Wait(task.Task,cancel);}finally{pendingText.Remove(id);if(cancel.IsCancellationRequested&&!task.Task.IsCompleted&&!disposed)try{controller.CoreWebView2.PostWebMessageAsJson(Json(new GemmaRequest{Action="cancel",CancelId=id}));}catch{}}}
        public Task<string> Describe(string path,CancellationToken cancel){return DescribeCall("caption",path,cancel);}
        public Task<string> DescribeBrief(string path,CancellationToken cancel){return DescribeCall("caption-brief",path,cancel);}
        async Task<string> DescribeCall(string action,string path,CancellationToken cancel){cancel.ThrowIfCancellationRequested();if(disposed)throw new ObjectDisposedException("GemmaHost");int id=++sequence;var task=new TaskCompletionSource<OcrText>();pendingText[id]=task;try{controller.CoreWebView2.PostWebMessageAsJson(Json(new GemmaRequest{Id=id,Action=action,Frames=ReadFrames(path)}));return (await Wait(task.Task,cancel)).Text;}finally{pendingText.Remove(id);if(cancel.IsCancellationRequested&&!task.Task.IsCompleted&&!disposed)try{controller.CoreWebView2.PostWebMessageAsJson(Json(new GemmaRequest{Action="cancel",CancelId=id}));}catch{}}}
        public Task<float[]> Classify(string text,CancellationToken cancel){return Call("classify",text,null,cancel);}
        public Task<float[]> Document(string text,CancellationToken cancel){return Call("document",text,null,cancel);}
        public Task<float[]> Query(string query,CancellationToken cancel){return Call("query",query,null,cancel);}
        public Task<float[]> Image(string path,CancellationToken cancel){return Call("image",null,ReadFrames(path),cancel);}
        public Task<float[]> ImageView(string path,string view,CancellationToken cancel){return Call("image",null,ReadFrames(path).Select(frame=>CropFrame(frame,view)).ToArray(),cancel);}
        static string CropFrame(string frame,string view){
            using(var input=new MemoryStream(Convert.FromBase64String(frame.Substring(frame.IndexOf(',')+1))))using(var image=System.Drawing.Image.FromStream(input)){
                Rectangle source=view=="top"?new Rectangle(0,0,image.Width,Math.Max(1,(int)(image.Height*.42))):new Rectangle((int)(image.Width*.15),(int)(image.Height*.15),Math.Max(1,(int)(image.Width*.70)),Math.Max(1,(int)(image.Height*.70)));
                using(var bitmap=new Bitmap(source.Width,source.Height)){using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.White);graphics.DrawImage(image,new Rectangle(Point.Empty,bitmap.Size),source,GraphicsUnit.Pixel);}using(var output=new MemoryStream()){bitmap.Save(output,ImageFormat.Png);return "data:image/png;base64,"+Convert.ToBase64String(output.ToArray());}}
            }
        }
        internal static string[] ReadFrames(string path){try{return Frames(path);}catch(Exception ex){if(ex is ArgumentException||ex is System.Runtime.InteropServices.ExternalException||ex is OutOfMemoryException||ex is IOException||ex is UnauthorizedAccessException)throw new LocalImageException(ex);throw;}}
        internal static float[] Normalize(float[] vector){if(vector==null||vector.Length!=768||vector.Any(v=>Single.IsNaN(v)||Single.IsInfinity(v)))throw new InvalidDataException("Invalid vector");double norm=Math.Sqrt(vector.Sum(v=>(double)v*v));if(norm<1e-12)throw new InvalidDataException("Empty vector");return vector.Select(v=>(float)(v/norm)).ToArray();}
        static string[] Frames(string path){using(var image=System.Drawing.Image.FromFile(path)){if(image.Width>4096||image.Height>4096)throw new InvalidDataException("Image dimensions exceed model limit");int count=1;bool animated=image.FrameDimensionsList.Contains(FrameDimension.Time.Guid);if(animated)count=image.GetFrameCount(FrameDimension.Time);var output=new List<string>();foreach(int index in new[]{0,count/2,count-1}.Distinct()){if(animated)image.SelectActiveFrame(FrameDimension.Time,index);double scale=Math.Min(1.0,512.0/Math.Max(image.Width,image.Height));using(var bitmap=new Bitmap(Math.Max(1,(int)(image.Width*scale)),Math.Max(1,(int)(image.Height*scale)))){using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.White);graphics.DrawImage(image,new Rectangle(Point.Empty,bitmap.Size));}using(var stream=new MemoryStream()){bitmap.Save(stream,ImageFormat.Png);output.Add("data:image/png;base64,"+Convert.ToBase64String(stream.ToArray()));}}}return output.ToArray();}}
        public void Dispose(){if(disposed)return;disposed=true;ready.TrySetCanceled();foreach(var task in pending.Values.ToArray())task.TrySetCanceled();pending.Clear();foreach(var task in pendingText.Values.ToArray())task.TrySetCanceled();pendingText.Clear();if(controller!=null){controller.Close();controller=null;}if(surface!=null){surface.Dispose();surface=null;}}
    }
    static class GemmaTests {
        public static void Logic(){try{var vector=new float[768];vector[0]=3;vector[1]=4;var normalized=GemmaHost.Normalize(vector);if(Math.Abs(normalized[0]-.6)>1e-6||Math.Abs(normalized[1]-.8)>1e-6)throw new Exception("Normalization");bool rejected=false;try{GemmaHost.Normalize(new float[512]);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("CLIP cache must be rejected");rejected=false;try{vector[2]=Single.NaN;GemmaHost.Normalize(vector);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("Invalid float accepted");var request=GemmaHost.Read<GemmaRequest>(GemmaHost.Json(new GemmaRequest{Id=1,Action="query",Query="고마워요"}));if(request.Query!="고마워요")throw new Exception("Korean protocol");File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gemma-test.txt"),"PASS: normalization, old vector rejection, finite vector guard, Korean protocol. No GPU, windows, input, or network.\r\n");}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gemma-test.txt"),"FAIL: "+ex.GetType().Name);Environment.ExitCode=1;}}
        public static void Smoke(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());var context=new ApplicationContext();var timer=new System.Windows.Forms.Timer{Interval=1};timer.Tick+=async(s,e)=>{timer.Stop();try{using(var search=new SemanticSearch(()=>new GemmaHost(Path.Combine(SemanticSearch.Root,"gemma2","smoke-profile")),()=>SemanticSearch.Ready,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-work","gemma-smoke-"+Guid.NewGuid().ToString("N")))){var catalog=Catalog.Load();var items=catalog.Entries.Where(i=>!String.IsNullOrEmpty(i.Path)&&File.Exists(i.Path)).Take(3).ToList();if(items.Count==0)throw new Exception("No cached images");var watch=System.Diagnostics.Stopwatch.StartNew();var first=await search.Search("고양이가 인사하는 모습",items,CancellationToken.None);long cold=watch.ElapsedMilliseconds;watch.Restart();var second=await search.Search("ㅋㅋㅋ",items,CancellationToken.None);long warm=watch.ElapsedMilliseconds;using(var cancel=new CancellationTokenSource()){search.Progress=message=>{if(message=="문장 분석 중")cancel.Cancel();};bool canceled=false;try{await search.Search("취소 시험",items,cancel.Token);}catch(OperationCanceledException){canceled=true;}if(!canceled)throw new Exception("Cancellation was not observed");search.Progress=null;}var repeated=await search.Search("재검색",items,CancellationToken.None);if(search.ModelInitializations!=1||search.LastAnalyzedImages!=items.Count)throw new Exception("Model reloaded after cancellation");if(search.LastAnalyzedImages!=items.Count)throw new Exception("Images were not analyzed");if(search.BlockedRequests!=0)throw new Exception("Unexpected external asset requests");File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gemma-smoke.txt"),"PASS: real EmbeddingGemma 2 q4 WebGPU, fresh vectors for "+items.Count+" local images, Korean/laughter queries, canceled search and reuse without reload. Cold ms="+cold+", warm ms="+warm+". External WebView requests: 0. No visible UI or KakaoTalk interaction. Recommendation quality not scored.\r\n");}}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gemma-smoke.txt"),"FAIL: "+ex);Environment.ExitCode=1;}finally{timer.Dispose();context.ExitThread();}};timer.Start();Application.Run(context);}
    }
}
