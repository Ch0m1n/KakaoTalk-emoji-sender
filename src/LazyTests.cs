using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgitCon {
    static class LazyTests {
        internal static void RenderShop(bool live) {
            string root=AppDomain.CurrentDomain.BaseDirectory;
            try {
                var packs=live?DcShop.Search("괴렘",1,CancellationToken.None):DcShop.ParseSearch(File.ReadAllText(Path.Combine(root,"..","dccon-target-search.html")));
                var installed=Catalog.Load().Packs;
                using(var form=new ShopForm(installed,p=>{},e=>{},id=>{})) {
                    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);
                    form.Show();form.ShowPreviewResults("괴렘",packs);
                    var watch=System.Diagnostics.Stopwatch.StartNew();while(watch.ElapsedMilliseconds<5000){Application.DoEvents();Thread.Sleep(25);}
                    using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(root,"shop-preview.png"));}
                    form.ClientSize=new Size(454,540);Application.DoEvents();
                    using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(root,"shop-preview-small.png"));}
                    form.VerifyTabNavigation();
                }
                File.WriteAllText(Path.Combine(root,"shop-render.txt"),"PASS: normal/minimum-size renders, repeated tab navigation, no horizontal overflow. No KakaoTalk interaction.\r\n");
            }catch(Exception ex){File.WriteAllText(Path.Combine(root,"shop-render.txt"),"FAIL: "+ex);Environment.ExitCode=1;}
        }
        static int checks;
        static void Assert(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
        public static void Run(bool live) {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lazy-test-data",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);DcShop.CacheRoot=Path.Combine(root,"images");
            string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lazy-test.txt");
            try {
                checks=0;
                if(live) {
                    var pack=DcShop.Import("173617",CancellationToken.None,s=>{});
                    Assert(pack.Pack.Title=="괴렘콘 촉촉팩","Live title");
                    Assert(pack.Entries.Count==128,"Live 128 entries");
                    Assert(pack.Entries.All(e=>e.Path==null),"Live metadata only");
                    Assert(!Directory.Exists(DcShop.CacheRoot),"Live import created no image cache");
                    string path=DcShop.EnsureImage(pack.Entries[0],CancellationToken.None);
                    Assert(File.Exists(path),"One live image fetched");
                    Assert(Directory.GetFiles(DcShop.CacheRoot,"*",SearchOption.AllDirectories).Count(p=>Catalog.Supported(p))==1,"Only one live image cached");
                    Assert(DcShop.EnsureImage(pack.Entries[0],CancellationToken.None)==path,"Live cached image reused");
                } else {
                    byte[] png;using(var bitmap=new Bitmap(2,2))using(var stream=new MemoryStream()){bitmap.SetPixel(0,0,Color.Red);bitmap.Save(stream,ImageFormat.Png);png=stream.ToArray();}
                    string json="{\"info\":{\"package_idx\":\"173617\",\"title\":\"괴렘콘 촉촉팩\"},\"detail\":["+String.Join(",",Enumerable.Range(1,128).Select(i=>"{\"idx\":\""+i+"\",\"title\":\"콘"+i+"\",\"ext\":\"png\",\"path\":\""+i.ToString("x")+"\"}"))+ "]}";
                    int metadata=0,images=0;bool fail=false;
                    DcShop.TestTransport=(uri,body,token,limit,imageOnly)=>{token.ThrowIfCancellationRequested();if(imageOnly){Interlocked.Increment(ref images);if(fail)throw new IOException("simulated image failure");return png;}metadata++;return Encoding.UTF8.GetBytes(json);};
                    var pack=DcShop.Import("173617",CancellationToken.None,s=>{});
                    Assert(metadata==1&&images==0,"Pack addition makes only one metadata request");
                    Assert(pack.Entries.Count==128&&pack.Entries.All(e=>e.Path==null),"128 uncached entries registered");
                    Assert(!Directory.Exists(DcShop.CacheRoot),"No image files during registration");
                    var config=new Settings();config.Packs.Add(pack.Pack);config.Entries.AddRange(pack.Entries);int skipped;
                    Assert(Catalog.Scan(config,out skipped).Count==128,"Uncached entries survive scan");
                    Assert(Catalog.Search(config.Entries,"").Count==3,"Only top three preview candidates");
                    var personal=DcShop.ImportPersonal("https://ac.arca.live/example.gif","인사",CancellationToken.None);
                    Assert(personal.Path==null&&images==0,"Personal link registration is lazy too");
                    string first=DcShop.EnsureImage(pack.Entries[0],CancellationToken.None);
                    Assert(images==1&&File.ReadAllBytes(first).SequenceEqual(png),"First selection fetches exact bytes");
                    Assert(DcShop.EnsureImage(pack.Entries[0],CancellationToken.None)==first&&images==1,"URL cache works before catalog path is saved");
                    pack.Entries[0].Path=first;
                    Assert(DcShop.EnsureImage(pack.Entries[0],CancellationToken.None)==first&&images==1,"Existing local cache reused");
                    var tasks=Enumerable.Range(0,3).Select(i=>Task.Run(()=>DcShop.EnsureImage(pack.Entries[1],CancellationToken.None))).ToArray();Task.WaitAll(tasks);
                    Assert(images==2,"Concurrent preview and selection share one download");
                    var cancelled=new CancellationTokenSource();cancelled.Cancel();try{DcShop.EnsureImage(pack.Entries[2],cancelled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
                    Assert(images==2,"Cancelled preview makes no request");
                    fail=true;try{DcShop.EnsureImage(pack.Entries[3],CancellationToken.None);throw new Exception("Failure ignored");}catch(IOException){}fail=false;
                    string retry=DcShop.EnsureImage(pack.Entries[3],CancellationToken.None);
                    Assert(images==4&&File.Exists(retry),"Failed image can be retried");
                    config.Packs.Clear();Assert(Catalog.Scan(config,out skipped).Count==0,"Removed pack remains hidden, cache preserved");
                    Assert(File.Exists(first),"Removing pack preserves cached files");
                    Catalog.DataPath=Path.Combine(root,"catalog.json");Catalog.Save(config);var restored=Catalog.Load();
                    Assert(restored.Entries.Count==128&&restored.Entries[10].SourceUrl==pack.Entries[10].SourceUrl&&restored.Entries[10].Path==null,"Uncached metadata persists across restart");
                }
                File.WriteAllText(report,"PASS: "+checks+" "+(live?"live network":"isolated regression")+" checks. No KakaoTalk or clipboard test.\r\n",Encoding.UTF8);
            }catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex,Encoding.UTF8);Environment.ExitCode=1;}
            finally{DcShop.TestTransport=null;}
        }
    }
}
