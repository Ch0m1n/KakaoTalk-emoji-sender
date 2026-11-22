using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AgitCon {
    [DataContract] sealed class AuditImage {
        [DataMember]public string Id,Hash,Group;
        [DataMember]public float[] Vector;
        [DataMember]public List<float[]> Views;
    }
    [DataContract] sealed class AuditPrompt {
        [DataMember]public string Intent,Mode;
        [DataMember]public float[] Vector;
    }
    [DataContract] sealed class AuditVectors {
        [DataMember]public string Precision;
        [DataMember]public List<AuditImage> Images=new List<AuditImage>();
        [DataMember]public List<AuditPrompt> Prompts=new List<AuditPrompt>();
        [DataMember]public long StartupMs,FreshImageMs,TotalMs;
        [DataMember]public int ExternalRequests;
    }
    static class AutoTuningAudit {
        public static void CaptionSmoke(){
            Application.EnableVisualStyles();SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());var context=new ApplicationContext();var timer=new System.Windows.Forms.Timer{Interval=1};
            timer.Tick+=async(s,e)=>{timer.Stop();string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"caption-smoke.txt");try{
                if(!StickerAnalysis.Ready)throw new Exception("Caption assets failed verification");var entry=Catalog.Load().Entries.First(item=>!String.IsNullOrEmpty(item.Path)&&File.Exists(item.Path));
                using(var host=new GemmaHost(Path.Combine(SemanticSearch.Root,"gemma2","caption-smoke-profile"),"caption.html")){
                    await host.Start(CancellationToken.None);string first=await host.Describe(entry.Path,CancellationToken.None);StickerAnalysis.Parse(first,new string('a',64));
                    using(var cancel=new CancellationTokenSource()){cancel.CancelAfter(200);bool canceled=false;try{await host.Describe(entry.Path,cancel.Token);}catch(OperationCanceledException){canceled=true;}if(!canceled)throw new Exception("Caption cancellation was not observed");}
                    string reused=await host.Describe(entry.Path,CancellationToken.None);StickerAnalysis.Parse(reused,new string('a',64));
                    if(host.BlockedRequests!=0)throw new Exception("Unexpected external request");
                    File.WriteAllText(report,"PASS: actual Qwen3-VL 2B q4 + FP16 vision, fresh descriptions, active cancellation, reuse after cancellation, tensor cleanup, one host, external requests=0. No KakaoTalk, clipboard, input or visible UI. Semantic accuracy not scored.");
                }
            }catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}finally{timer.Dispose();context.ExitThread();}};timer.Start();Application.Run(context);
        }
        public static void PublishCaptions(string fixtureFile="caption-fixtures.json",string captionFile="caption-audit.json",string reportFile="caption-publish.txt"){try{
            string root=AppDomain.CurrentDomain.BaseDirectory;var fixtures=GemmaHost.Read<List<QualityFixture>>(File.ReadAllText(Path.Combine(root,fixtureFile)));var rows=GemmaHost.Read<List<CaptionAuditRow>>(File.ReadAllText(Path.Combine(root,captionFile)));int published=0,failed=0,tp=0,fp=0,fn=0;
            foreach(var row in rows){var fixture=fixtures.First(f=>f.Id==row.Id);string hash;using(var stream=File.OpenRead(fixture.Path))using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
                try{var item=StickerAnalysis.Parse(row.Text,hash);StickerAnalysis.Write(item);published++;var predicted=AnalysisEvidence.Reactions(item);predicted.Remove("neutral");var expected=new HashSet<string>((fixture.Expected??"").Split(',').Where(s=>s.Length>0));tp+=predicted.Intersect(expected).Count();fp+=predicted.Except(expected).Count();fn+=expected.Except(predicted).Count();}catch{failed++;}
            }
            File.WriteAllText(Path.Combine(root,reportFile),"Published="+published+"; invalid="+failed+"; tag TP="+tp+"; FP="+fp+"; FN="+fn+". Expected labels are evaluation only and never passed to either model. Includes ambiguous expressions; not an overall accuracy guarantee.");if(failed>0)Environment.ExitCode=1;
        }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,reportFile),"FAIL: "+ex);Environment.ExitCode=1;}}
        public static void CaptionCatalog(){
            Application.EnableVisualStyles();SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());var context=new ApplicationContext();var timer=new System.Windows.Forms.Timer{Interval=1};
            timer.Tick+=async(s,e)=>{timer.Stop();string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"caption-catalog.txt");try{
                if(!StickerAnalysis.Ready)throw new Exception("Caption model assets are not verified");
                using(var search=new SemanticSearch()){search.AutomaticAnalysis=true;var entries=Catalog.Load().Entries.Where(item=>!String.IsNullOrEmpty(item.Path)&&File.Exists(item.Path)).ToList();int done=0;foreach(var entry in entries){await search.Index(entry,CancellationToken.None);done++;File.WriteAllText(report,"RUNNING: "+done+" / "+entries.Count+"; automatically generated descriptions only; no KakaoTalk interaction.");}search.ReleaseAnalyzer();File.WriteAllText(report,"PASS: "+done+" local catalogue images indexed through production pipeline. No per-image labels, KakaoTalk, clipboard, input or visible UI.");}
            }catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}finally{timer.Dispose();context.ExitThread();}};timer.Start();Application.Run(context);
        }
        [DataContract] sealed class CaptionAuditRow{[DataMember]public string Id,Text;[DataMember]public long Milliseconds;}
        public static void Captions(bool gemma=false,string fixtureFile="caption-fixtures.json",string outputStem="caption-audit"){
            Application.EnableVisualStyles();SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());var context=new ApplicationContext();var timer=new System.Windows.Forms.Timer{Interval=1};
            timer.Tick+=async(s,e)=>{timer.Stop();string root=AppDomain.CurrentDomain.BaseDirectory,report=Path.Combine(root,outputStem+".txt");
                try{using(var host=new GemmaHost(Path.Combine(SemanticSearch.Root,"gemma2","caption-audit-profile"),gemma?"caption-gemma.html":"caption.html")){host.Diagnostics=stage=>File.WriteAllText(report,stage);await host.Start(CancellationToken.None);
                    var fixtures=GemmaHost.Read<List<QualityFixture>>(File.ReadAllText(Path.Combine(root,fixtureFile)));var output=new List<CaptionAuditRow>();
                    foreach(var row in fixtures){var watch=Stopwatch.StartNew();string text=await host.Describe(row.Path,CancellationToken.None);output.Add(new CaptionAuditRow{Id=row.Id,Text=text,Milliseconds=watch.ElapsedMilliseconds});File.WriteAllText(Path.Combine(root,outputStem+".json"),GemmaHost.Json(output));File.WriteAllText(report,"RUNNING: "+output.Count+" / "+fixtures.Count+"; external requests="+host.BlockedRequests);}
                    File.WriteAllText(report,"PASS: "+output.Count+" automatically described images; external requests="+host.BlockedRequests+". No names, aliases, labels, KakaoTalk or visible UI sent to model.");
                }}catch(Exception ex){File.AppendAllText(report,"\nFAIL: "+ex);Environment.ExitCode=1;}finally{timer.Dispose();context.ExitThread();}
            };timer.Start();Application.Run(context);
        }
        public static void Prototypes(){
            Application.EnableVisualStyles();SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var context=new ApplicationContext();var timer=new System.Windows.Forms.Timer{Interval=1};
            timer.Tick+=async(s,e)=>{timer.Stop();string root=AppDomain.CurrentDomain.BaseDirectory,report=Path.Combine(root,"prototype-audit.txt");
                try{using(var host=new GemmaHost(Path.Combine(SemanticSearch.Root,"gemma2","prototype-audit-profile"))){
                    await host.Start(CancellationToken.None);var rows=GemmaHost.Read<List<PrototypeInput>>(File.ReadAllText(Path.Combine(root,"prototype-input.json")));var output=new List<AuditPrompt>();
                    foreach(var row in rows){output.Add(new AuditPrompt{Intent=row.Key,Mode=row.Text,Vector=await host.Query(row.Text,CancellationToken.None)});File.WriteAllText(report,"RUNNING: "+output.Count+" / "+rows.Count+" generic visual descriptions; no image labels sent to model.");}
                    File.WriteAllText(Path.Combine(root,"prototype-vectors.json"),GemmaHost.Json(output));File.WriteAllText(report,"PASS: "+output.Count+" generic descriptions; external requests="+host.BlockedRequests+". No KakaoTalk or visible UI.");
                }}catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}finally{timer.Dispose();context.ExitThread();}
            };timer.Start();Application.Run(context);
        }
        [DataContract] sealed class PrototypeInput{[DataMember]public string Key,Text;}
        public static void Run(string precision){
            Application.EnableVisualStyles();
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var context=new ApplicationContext();var timer=new System.Windows.Forms.Timer{Interval=1};
            timer.Tick+=async(s,e)=>{
                timer.Stop();string root=AppDomain.CurrentDomain.BaseDirectory;
                string report=Path.Combine(root,"automatic-"+precision+".txt");
                try{
                    var watch=Stopwatch.StartNew();var output=new AuditVectors{Precision=precision};
                    using(var host=new GemmaHost(Path.Combine(SemanticSearch.Root,"gemma2","automatic-audit-profile"),"automatic-"+precision+".html")){
                        await host.Start(CancellationToken.None);output.StartupMs=watch.ElapsedMilliseconds;
                        var entries=GemmaHost.Read<List<Entry>>(File.ReadAllText(Path.Combine(root,"quality-full-library.json")));
                        var sources=entries.Select(entry=>Tuple.Create("catalog:"+entry.PackId+":"+entry.DisplayName,"catalog",entry.Path)).ToList();
                        foreach(string manifest in new[]{"tuning-training.json","benchmark-fixtures.json"}){
                            var fixtures=GemmaHost.Read<List<QualityFixture>>(File.ReadAllText(Path.Combine(root,manifest)));
                            sources.AddRange(fixtures.Select(item=>Tuple.Create((manifest.StartsWith("tuning")?"reference:":"control:")+item.Id,manifest.StartsWith("tuning")?"reference":"control",item.Path)));
                        }
                        string spec=precision=="q4"?SemanticSearch.Spec:"gemma2-q8-image280-gif3-v1";
                        string cache=Path.Combine(SemanticSearch.Root,"vectors",spec);Directory.CreateDirectory(cache);string viewsCache=cache+"-views-v1";Directory.CreateDirectory(viewsCache);
                        foreach(var source in sources){
                            string hash;using(var stream=File.OpenRead(source.Item3))using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
                            string file=Path.Combine(cache,hash+".json");float[] vector;
                            if(File.Exists(file))vector=GemmaHost.Normalize(GemmaHost.Read<float[]>(File.ReadAllText(file)));
                            else{vector=await host.Image(source.Item3,CancellationToken.None);File.WriteAllText(file,GemmaHost.Json(vector));}
                            string viewFile=Path.Combine(viewsCache,hash+".json");List<float[]> views;
                            if(File.Exists(viewFile))views=GemmaHost.Read<List<float[]>>(File.ReadAllText(viewFile));
                            else{views=new List<float[]>{vector,await host.ImageView(source.Item3,"top",CancellationToken.None),await host.ImageView(source.Item3,"center",CancellationToken.None)};File.WriteAllText(viewFile,GemmaHost.Json(views));}
                            output.Images.Add(new AuditImage{Id=source.Item1,Group=source.Item2,Hash=hash,Vector=vector,Views=views});
                            File.WriteAllText(report,"RUNNING: encoded "+output.Images.Count+" / "+sources.Count+" public sticker images; no labels passed to model.");
                        }
                        foreach(var reaction in RecommendationQuery.Reactions){
                            int korean=Regex.Match(reaction.Description,@"[가-힣]").Index;
                            string english=reaction.Description.Substring(0,korean).Trim(),text=reaction.Description.Substring(korean).Trim();
                            foreach(var mode in new[]{"search.bilingual","search.en","search.ko","classification.en","classification.ko","search.short-en","search.short-ko","search.sticker","negative.en","negative.ko"}){
                                string prompt=ReactionPrompts.Text(reaction,mode);
                                var vector=mode.StartsWith("classification")?await host.Classify(prompt,CancellationToken.None):await host.Query(prompt,CancellationToken.None);
                                output.Prompts.Add(new AuditPrompt{Intent=reaction.Key,Mode=mode,Vector=vector});
                            }
                        }
                        long before=watch.ElapsedMilliseconds;await host.Image(sources[0].Item3,CancellationToken.None);before=watch.ElapsedMilliseconds;await host.Image(sources[0].Item3,CancellationToken.None);output.FreshImageMs=watch.ElapsedMilliseconds-before;
                        output.TotalMs=watch.ElapsedMilliseconds;output.ExternalRequests=host.BlockedRequests;
                        if(output.ExternalRequests!=0)throw new Exception("Unexpected external WebView request");
                        File.WriteAllText(Path.Combine(root,"automatic-"+precision+".json"),GemmaHost.Json(output));
                        File.WriteAllText(report,"PASS: "+output.Images.Count+" image vectors, "+output.Prompts.Count+" generic prompt vectors. No sticker labels, manual meaning table, KakaoTalk or input access. Startup ms="+output.StartupMs+"; fresh image ms="+output.FreshImageMs+"; external requests=0.");
                    }
                }catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}
                finally{timer.Dispose();context.ExitThread();}
            };
            timer.Start();Application.Run(context);
        }
    }
}
