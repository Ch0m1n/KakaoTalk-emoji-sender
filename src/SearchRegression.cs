using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgitCon {
    static class SearchRegression {
        sealed class DescriptionHost:IStickerDescriptionHost,IStickerDescriptionRetryHost {
            public bool Disposed;public int Starts,Calls;public string Path;
            public TaskCompletionSource<string> Reply=new TaskCompletionSource<string>();
            public Task Start(CancellationToken cancel){Starts++;return Task.FromResult(0);}
            public Task<string> Describe(string path,CancellationToken cancel){Calls++;Path=path;return Reply.Task;}
            public Task<string> DescribeBrief(string path,CancellationToken cancel){Calls++;return Task.FromResult("{\"text\":\"\",\"description\":\"A character smiling.\",\"reactions\":[\"happy\"]}");}
            public void Dispose(){Disposed=true;}
        }
        sealed class Host:IEmbeddingHost,IDocumentHost {
            public readonly TaskCompletionSource<bool> Ready=new TaskCompletionSource<bool>();
            public bool FailFirstDocument;
            public int Documents;public string LastDocument;public int Queries;public int Starts;public bool Disposed;public int BlockedRequests{get{return 0;}}
            public Task Start(CancellationToken cancel){Starts++;return Ready.Task;}
            public Task<float[]> Query(string query,CancellationToken cancel){Queries++;var vector=new float[768];vector[RecommendationQuery.Reactions.Any(r=>r.Description==query||ReactionPrompts.Text(r,AutomaticRanker.PromptMode)==query)?1:0]=1;return Task.FromResult(vector);}
            public Task<float[]> Document(string text,CancellationToken cancel){Documents++;if(FailFirstDocument&&Documents==1)throw new IOException("Fixture document error");LastDocument=text;var vector=new float[768];vector[0]=1;return Task.FromResult(vector);}
            public Task<float[]> Image(string path,CancellationToken cancel){GemmaHost.ReadFrames(path);var vector=new float[768];vector[0]=1;return Task.FromResult(vector);}
            public void Dispose(){Disposed=true;}
        }
        static bool MeaningIndexPresent(){return typeof(SemanticSearch).Assembly.GetManifestResourceNames().Any(name=>name.Contains("SemanticMeanings"));}
        public static void Run(){Application.EnableVisualStyles();SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());var context=new ApplicationContext();var timer=new System.Windows.Forms.Timer{Interval=1};timer.Tick+=async(s,e)=>{timer.Stop();string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"search-regression.txt");try{
            int checks=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Regression check "+checks);checks++;};
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-work","search-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string good=Path.Combine(root,"good.png"),bad=Path.Combine(root,"bad.png");using(var image=new Bitmap(8,8)){image.Save(good,ImageFormat.Png);}File.WriteAllText(bad,"broken image fixture");
            string hash=new string('a',64),otherHash=new string('b',64),descriptionFolder=Path.Combine(root,"descriptions");
            string caption="{\"text\":\"\",\"description\":\"Smiling and hugging with blushing cheeks.\",\"reactions\":[\" Happy \",\"neutral\",\"unknown\"]}";
            var parsed=StickerAnalysis.Parse("```json\n"+caption+"\n```",hash);
            check(parsed.Hash==hash&&parsed.Spec==StickerAnalysis.Spec&&parsed.Reactions.SequenceEqual(new[]{"happy"}));
            var evidence=AnalysisEvidence.Reactions(parsed);check(evidence.Contains("happy")&&evidence.Contains("love")&&!evidence.Contains("shy"));
            parsed.Reactions=new[]{"surprise","sleep","laugh"};parsed.Description="Big cartoon eyes, closed smiling eyes, slightly blushing.";
            evidence=AnalysisEvidence.Reactions(parsed);check(!evidence.Contains("surprise")&&!evidence.Contains("sleep")&&!evidence.Contains("laugh")&&!evidence.Contains("shy"));
            parsed.Description="A character crying and giving a thumbs-up.";parsed.Reactions=new[]{"sad"};evidence=AnalysisEvidence.Reactions(parsed);check(evidence.Contains("sad")&&evidence.Contains("approval"));
            parsed.Text="안녕";evidence=AnalysisEvidence.Reactions(parsed);check(evidence.SetEquals(new[]{"greeting"}));
            parsed.Text="ㅋㅋㅋ";check(AnalysisEvidence.Reactions(parsed).SetEquals(new[]{"laugh"}));
            parsed.Text="즐거운 주말 보내요";check(AnalysisEvidence.Reactions(parsed).SetEquals(new[]{"greeting"}));
            parsed.Text="";parsed.Description="Holding a cup of orange juice.";parsed.Reactions=new string[0];check(AnalysisEvidence.Reactions(parsed).Contains("food"));
            check(AnalysisEvidence.Corroborated(parsed,"food",new AutomaticScore{ImageValue=-1,ImageMargin=-1}));
            parsed.Description="Smiling happily.";parsed.Reactions=new[]{"happy"};check(!AnalysisEvidence.Corroborated(parsed,"happy",new AutomaticScore{ImageValue=-1,ImageMargin=-1}));
            check(AnalysisEvidence.Corroborated(parsed,"happy",new AutomaticScore{ImageValue=.02,ImageMargin=-.02}));
            parsed.Text="고마워요";check(AnalysisEvidence.TextBonus(parsed,"고마워")>0&&AnalysisEvidence.TextBonus(parsed,"고") ==0);
            parsed.Text="";
            parsed.Text="그 말은 가짜예요";parsed.Description="A happy character hugging.";check(parsed.TextFirst&&parsed.Document==parsed.Text);parsed.Text="";
            parsed.Description="A character smiling cheerfully with closed eyes.";parsed.Reactions=new[]{"happy"};check(AnalysisEvidence.GentleSmile(parsed));
            parsed.Description="A shy character smiling with hearts and affection.";check(!AnalysisEvidence.GentleSmile(parsed));
            parsed.Description="A character smiling and holding a camera.";check(!AnalysisEvidence.GentleSmile(parsed));
            check(AnalysisEvidence.CertainClaim("A cup of tea, possibly green tea.",@"\bcup\b"));
            check(!AnalysisEvidence.CertainClaim("Possibly a sandwich.",@"\bsandwich\b"));
            check(!AnalysisEvidence.CertainClaim("No visible sandwich.",@"\bsandwich\b"));
            check(!AnalysisEvidence.CertainClaim("Appearing asleep or very tired.",@"\basleep\b"));
            check(AnalysisEvidence.CertainClaim("Sleeping with Zzz above the head.",@"\bsleeping\b"));
            parsed.Description="A piece of food, possibly a sandwich.";parsed.Reactions=new[]{"food"};check(!AnalysisEvidence.Corroborated(parsed,"food",new AutomaticScore{ImageValue=-.01,ImageMargin=-.01}));
            parsed.Description="A face with red cheeks and a small smile.";parsed.Reactions=new[]{"shy","happy"};check(!AnalysisEvidence.Reactions(parsed).Contains("shy"));
            parsed.Description="A bashful, embarrassed face with a small smile.";check(AnalysisEvidence.Reactions(parsed).Contains("shy")&&!AnalysisEvidence.Reactions(parsed).Contains("happy"));
            parsed.Description="A happy and slightly embarrassed face.";check(AnalysisEvidence.Reactions(parsed).Contains("happy"));
            parsed.Description="A static angry face with furrowed brows.";parsed.Reactions=new[]{"angry","disapproval"};check(!AnalysisEvidence.Corroborated(parsed,"angry",new AutomaticScore{ImageValue=.1,ImageMargin=.1}));
            parsed.Description="An angry face shouting furiously.";check(AnalysisEvidence.Corroborated(parsed,"angry",new AutomaticScore{ImageValue=.1,ImageMargin=.1}));
            parsed.Description="Smiling happily.";parsed.Text="";parsed.Reactions=new[]{"happy"};string noLabels=parsed.Document;parsed.Reactions=new[]{"shy","sad"};check(parsed.Document==noLabels);
            check(AnalysisEvidence.Rank(parsed,"happy",new AutomaticScore{ImageValue=.08,ImageMargin=.04,Margin=.05},0)>AnalysisEvidence.Rank(parsed,"happy",new AutomaticScore{ImageValue=.01,ImageMargin=0,Margin=.01},0));
            parsed.Text="안 웃겨ㅋㅋ";check(AnalysisEvidence.WrittenReaction(parsed)==null&&parsed.TextFirst&&!AnalysisEvidence.Supports(parsed,"laugh",new AutomaticScore{Accepted=true,ImageValue=.1,Margin=.1}));parsed.Text="";
            foreach(string input in new[]{"불가능","그건 불가능함","그 말은 가짜예요","거절","안돼"})check(RecommendationQuery.Intent(input).Key=="disapproval");
            foreach(string input in new[]{"불가능하지 않아","가짜가 아니야"})check(RecommendationQuery.Intent(input)==null);
            check(AnalysisEvidence.GentleLaughter("ㅋㅋㅋ")&&AnalysisEvidence.GentleLaughter("ㅎㅎ!")&&!AnalysisEvidence.GentleLaughter("ㅋㅋㅠㅠ")&&!AnalysisEvidence.GentleLaughter("진짜 빵터졌어"));
            bool invalidDescription=false;try{StickerAnalysis.Parse("{\"reactions\":[\"happy\"]}",hash);}catch(InvalidDataException){invalidDescription=true;}check(invalidDescription);
            invalidDescription=false;try{StickerAnalysis.Parse(caption,"../outside");}catch(InvalidDataException){invalidDescription=true;}check(invalidDescription);
            var recovered=new StickerDescription{Text="",Description="A sleepy character hanging from a train handhold.",Reactions=new string[0]};
            var nearZero=new AutomaticScore{Accepted=true,ImageValue=-.015,ImageMargin=-.015,Margin=.02,RawMargin=.03};
            check(AnalysisEvidence.Supports(recovered,"sleep",nearZero));
            recovered.Description="A character possibly sleeping or tired.";check(!AnalysisEvidence.Supports(recovered,"sleep",nearZero));
            recovered.Description="A girl smiling with closed eyes, appearing happy and content.";recovered.Reactions=new[]{"happy"};check(AnalysisEvidence.Supports(recovered,"happy",nearZero));
            check(!AnalysisEvidence.Supports(recovered,"happy",new AutomaticScore{Accepted=true,ImageValue=-.1,ImageMargin=-.08,Margin=.02,RawMargin=.03}));
            recovered.Description="A character looking surprised with wide eyes.";recovered.Reactions=new[]{"surprise"};
            check(AnalysisEvidence.Supports(recovered,"surprise",new AutomaticScore{ImageValue=.04,ImageMargin=.01}));
            check(!AnalysisEvidence.Supports(recovered,"surprise",new AutomaticScore{ImageValue=.04,ImageMargin=-.01}));
            recovered.Description="A character holding a martini glass.";recovered.Reactions=new string[0];check(AnalysisEvidence.Supports(recovered,"food",new AutomaticScore{ImageValue=-.02,ImageMargin=-.02}));
            recovered.Description="A character holding a piece of food, possibly a sandwich.";check(!AnalysisEvidence.Supports(recovered,"food",new AutomaticScore{ImageValue=-.02,ImageMargin=-.02}));
            recovered.Description="A pleading character with heart-shaped cheeks.";check(AnalysisEvidence.Supports(recovered,"love",new AutomaticScore{ImageValue=.04,ImageMargin=.01}));
            recovered.Description="A frustrated character yelling angrily.";recovered.Text="취소하 방금그러";recovered.Reactions=new[]{"angry","disapproval"};
            check(recovered.TextFirst&&AnalysisEvidence.Supports(recovered,"angry",new AutomaticScore{ImageValue=.04,ImageMargin=.02,RawMargin=.01}));
            recovered.Text="안녕";check(!AnalysisEvidence.Supports(recovered,"angry",new AutomaticScore{ImageValue=.04,ImageMargin=.02}));
            recovered.Text="천만의 말씀 만만의 콩떡";check(AnalysisEvidence.WrittenReaction(recovered)=="thanks");
            recovered.Text="별 말씀을요";check(AnalysisEvidence.WrittenReaction(recovered)=="thanks");
            recovered.Text="천만의 말씀은 아니야";check(AnalysisEvidence.WrittenReaction(recovered)==null);
            var sleepyOnly=new StickerDescription{Text="",Description="A slightly sleepy expression with closed eyes.",Reactions=new[]{"sleep"}};
            check(!AnalysisEvidence.Supports(sleepyOnly,"sleep",new AutomaticScore{ImageValue=.02,ImageMargin=-.01,Margin=-.002,RawMargin=-.01}));
            sleepyOnly.Description="A character looking sleepy.";check(AnalysisEvidence.Supports(sleepyOnly,"sleep",new AutomaticScore{ImageValue=-.015,ImageMargin=-.007,Margin=.02,RawMargin=.03}));
            var unknownOcr=new StickerDescription{Text="사려만다",Description="A distressed crying character with a worried expression.",Reactions=new[]{"sad","worry"}};
            check(!AnalysisEvidence.Supports(unknownOcr,"sad",new AutomaticScore{ImageValue=.04,ImageMargin=.006,Margin=-.02,RawMargin=-.03}));
            var smile=new StickerDescription{Text="",Description="A smiling character raising its arms in a cheerful excited pose.",Reactions=new[]{"happy"}};
            var smileScore=new AutomaticScore{ImageValue=.02,ImageMargin=-.001};
            check(AnalysisEvidence.RelatedSmile(smile,smileScore));
            smile.Description="A cheerful character raising one arm in a gesture.";check(AnalysisEvidence.RelatedSmile(smile,smileScore));
            foreach(string wrong in new[]{"Smiling and giving a thumbs-up.","Smiling while waving.","Smiling and hugging with floating hearts.","Smiling but crying sadly.","A cheerful wink and peace sign.","A smiling character sleeping."}){smile.Description=wrong;check(!AnalysisEvidence.RelatedSmile(smile,smileScore));}
            smile.Description="A smiling cheerful face.";check(!AnalysisEvidence.RelatedSmile(smile,new AutomaticScore{ImageValue=-.01,ImageMargin=.02}));check(!AnalysisEvidence.RelatedSmile(smile,new AutomaticScore{ImageValue=.02,ImageMargin=-.04}));
            foreach(string query in new[]{"ㅋ","ㅋㅋ","ㅋㅋㅋㅋ","ㅋㅋㅋㅋㅋㅋ","ㅎㅎㅎ"})check(AnalysisEvidence.GentleLaughter(query)&&RecommendationQuery.Intent(query).Key=="laugh");
            check(!AnalysisEvidence.GentleLaughter("안 웃겨")&&!AnalysisEvidence.GentleLaughter("ㅋㅋㅠㅠ"));
            var gesture=new StickerDescription{Text="",Description="A cat waving its paw cheerfully.",Reactions=new[]{"greeting","approval"}};
            var certainScore=new AutomaticScore{Accepted=true,ImageValue=.1,ImageMargin=.02,Margin=.1};
            check(!AnalysisEvidence.Supports(gesture,"greeting",certainScore));
            gesture.Description="A character greeting hello with an open palm.";check(AnalysisEvidence.Supports(gesture,"greeting",certainScore));
            gesture.Text="\uFFFD\uFFFD~";check(!AnalysisEvidence.Supports(gesture,"greeting",certainScore));
            gesture.Text="안녕";check(AnalysisEvidence.Supports(gesture,"greeting",certainScore));
            gesture.Text="좋아해요";check(!AnalysisEvidence.Supports(gesture,"greeting",certainScore));
            gesture.Text="";gesture.Description="A character possibly greeting hello.";check(!AnalysisEvidence.Supports(gesture,"greeting",certainScore));
            StickerAnalysis.Write(parsed,descriptionFolder);check(StickerAnalysis.Read(hash,descriptionFolder)!=null&&StickerAnalysis.Read(otherHash,descriptionFolder)==null);
            File.WriteAllText(Path.Combine(descriptionFolder,hash+".json"),"{\"spec\":\"old\",\"hash\":\""+hash+"\"}");check(StickerAnalysis.Read(hash,descriptionFolder)==null);
            var descriptionHost=new DescriptionHost();descriptionHost.Reply.SetResult(caption);
            using(var worker=new StickerAnalyzer(()=>descriptionHost,descriptionFolder)){var generated=await worker.Analyze(good,hash,CancellationToken.None);check(generated.Hash==hash&&descriptionHost.Path==good&&descriptionHost.Starts==1&&descriptionHost.Calls==1);}
            check(descriptionHost.Disposed&&StickerAnalysis.Read(hash,descriptionFolder)!=null);
            var retryHost=new DescriptionHost();retryHost.Reply.SetResult("{\"reactions\":[\"happy\"]}");using(var worker=new StickerAnalyzer(()=>retryHost,descriptionFolder)){var retried=await worker.Analyze(good,hash,CancellationToken.None);check(retryHost.Calls==2&&retried.Description=="A character smiling.");}
            var canceledDescriptionHost=new DescriptionHost();using(var worker=new StickerAnalyzer(()=>canceledDescriptionHost,descriptionFolder))using(var canceledDescription=new CancellationTokenSource()){
                var pendingDescription=worker.Analyze(good,otherHash,canceledDescription.Token);canceledDescription.Cancel();canceledDescriptionHost.Reply.SetResult(caption);
                bool canceled=false;try{await pendingDescription;}catch(OperationCanceledException){canceled=true;}check(canceled&&canceledDescriptionHost.Disposed&&StickerAnalysis.Read(otherHash,descriptionFolder)==null);
            }
            var items=new List<Entry>{new Entry{Path=bad,DisplayName="bad"},new Entry{Path=good,DisplayName="good"}};
            var host=new Host();int factories=0;
            using(var search=new SemanticSearch(()=>{factories++;return host;},()=>true,Path.Combine(root,"vectors"))){
                var cancel=new CancellationTokenSource();var first=search.Search("첫 요청",items,cancel.Token);cancel.Cancel();bool canceled=false;try{await first;}catch(OperationCanceledException){canceled=true;}
                check(canceled);check(!host.Disposed&&host.Starts==1&&factories==1);
                host.Ready.SetResult(true);var result=await search.Search("다음 요청",items,CancellationToken.None);
                check(result.Count==1&&result[0].Path==good);check(search.LastSkippedImages==1);check(host.Starts==1&&factories==1&&!host.Disposed);
                result=await search.Search("반복 요청",items,CancellationToken.None);check(result.Count==1&&host.Starts==1);
                var many=new List<Entry>();for(int i=0;i<26;i++){string path=Path.Combine(root,"unique-"+i+".png");using(var image=new Bitmap(8,8)){image.SetPixel(i%8,i/8,Color.FromArgb(255,i*7,10,30));image.Save(path,ImageFormat.Png);}many.Add(new Entry{Path=path,DisplayName="fixture "+i,PackId=i%2==0?"dc":"arca"});}result=await search.Search("여러 후보",many,CancellationToken.None);check(result.Count==24&&result.SequenceEqual(many.Take(24)));
            }
            check(host.Disposed);
            string secondPath=Path.Combine(root,"second.png");using(var image=new Bitmap(8,8)){image.SetPixel(0,0,Color.Blue);image.Save(secondPath,ImageFormat.Png);}
            var alignmentHost=new Host{FailFirstDocument=true};alignmentHost.Ready.SetResult(true);int descriptionsRead=0;
            using(var search=new SemanticSearch(()=>alignmentHost,()=>true,Path.Combine(root,"alignment"),digest=>++descriptionsRead==1?new StickerDescription{Description="An angry scowling character.",Reactions=new[]{"angry"}}:new StickerDescription{Description="A character smiling happily.",Reactions=new[]{"happy"}})){
                search.AutomaticAnalysis=true;var firstEntry=new Entry{Path=good,DisplayName="first"};var secondEntry=new Entry{Path=secondPath,DisplayName="second"};
                var aligned=await search.Search("행복해",new List<Entry>{firstEntry,secondEntry},CancellationToken.None);check(aligned.Count==1&&aligned[0]==secondEntry&&search.LastSkippedImages==1);
            }
            check(!MeaningIndexPresent());check(!EntryMetadata.Meaningful("콘 313862390")&&!EntryMetadata.Meaningful("아카콘"));
            check(EntryMetadata.Caption(new Entry{DisplayName="123",Aliases=""})=="");
            var dedupHost=new Host();dedupHost.Ready.SetResult(true);string duplicate=Path.Combine(root,"duplicate.png");File.Copy(good,duplicate);
            using(var search=new SemanticSearch(()=>dedupHost,()=>true,Path.Combine(root,"duplicate-vectors"))){var unique=await search.Search("중복 검사",new List<Entry>{new Entry{Path=good},new Entry{Path=duplicate}},CancellationToken.None);check(unique.Count==1);}

            foreach(string input in new[]{"굳","굿!","굳굳","굿 굿","GOOD","nice","엄지척"})check(RecommendationQuery.Meaning(input).Contains("엄지"));
            foreach(string input in new[]{"굳이","굿나잇","굿다이노","좋아하지 않아","good night","최고가 아니야"})check(RecommendationQuery.Meaning(input)==input);
            check(RecommendationQuery.MetadataBonus(new Entry{DisplayName="313862390",Aliases=""},"굳")==0);
            check(RecommendationQuery.MetadataBonus(new Entry{DisplayName="굿다이노"},"굳")==0);
            check(RecommendationQuery.MetadataBonus(new Entry{Aliases="웃음, 엄지척"},"굳")>0);
            foreach(string input in new[]{"ㅋㅋㅋ","ㅎㅎ!","ㅋ ㅋ","lol","웃겨"})check(RecommendationQuery.Intent(input).Key=="laugh");
            foreach(string input in new[]{"ㅠㅠ","ㅜㅜ!"})check(RecommendationQuery.Intent(input).Key=="sad");
            check(!AutomaticRanker.Accept(1,-.04));check(!AutomaticRanker.Accept(1,.005));check(AutomaticRanker.Accept(.02,.01));check(!AutomaticRanker.Accept(-.01,.16));
            check(!GemmaHost.Read<Settings>(GemmaHost.Json(new Settings())).SemanticPowerSaving);
            var rankHost=new Host();rankHost.Ready.SetResult(true);
            using(var search=new SemanticSearch(()=>rankHost,()=>true,Path.Combine(root,"rank-vectors"))){
                var unrelated=new Entry{Path=good,DisplayName="1"};var tagged=new Entry{Path=good,DisplayName="2",Aliases="엄지척"};
                var ranked=await search.Search("굳",new List<Entry>{unrelated,tagged},CancellationToken.None);
                check(ranked.Count==1&&ranked[0]==tagged);
                ranked=await search.Search("다른 문장",new List<Entry>{unrelated,tagged},CancellationToken.None);
                check(ranked[0]==unrelated);
                check(!search.PowerSaving);search.ReleaseIdle();check(!rankHost.Disposed);
                search.PowerSaving=true;search.ReleaseIdle();check(rankHost.Disposed);
                search.PowerSaving=false;check(!search.PowerSaving);
            }

            var indexHost=new Host();indexHost.Ready.SetResult(true);
            using(var search=new SemanticSearch(()=>indexHost,()=>true,Path.Combine(root,"index-vectors"))){await search.Index(new Entry{Path=good},CancellationToken.None);check(indexHost.Queries==0&&indexHost.Starts==1);string cacheRoot=DcShop.CacheRoot;try{DcShop.CacheRoot=Path.Combine(root,"remote-cache");DcShop.TestTransport=(uri,body,token,limit,imageOnly)=>File.ReadAllBytes(good);var remote=new Entry{SourceUrl="https://dcimg5.dcinside.com/dccon.php?no=abcdef",PackId="fixture",DisplayName="remote"};await search.PrepareRemote(remote,CancellationToken.None);check(!String.IsNullOrEmpty(remote.Path)&&File.Exists(remote.Path)&&indexHost.Starts==1&&indexHost.Queries==0);}finally{DcShop.TestTransport=null;DcShop.CacheRoot=cacheRoot;}await search.Search("다음 검색",new List<Entry>{new Entry{Path=good}},CancellationToken.None);check(indexHost.Starts==1);}
            var documentHost=new Host();documentHost.Ready.SetResult(true);
            using(var search=new SemanticSearch(()=>documentHost,()=>true,Path.Combine(root,"document-vectors"))){var captioned=new Entry{Path=good,DisplayName="123",Aliases="first label"};await search.Index(captioned,CancellationToken.None);check(documentHost.Documents==1&&documentHost.Queries==0);await search.Index(captioned,CancellationToken.None);check(documentHost.Documents==1);captioned.Aliases="new label";await search.Index(captioned,CancellationToken.None);check(documentHost.Documents==2&&documentHost.LastDocument=="new label"&&documentHost.Starts==1);}
            var concepts=RecommendationQuery.Reactions.Select((r,i)=>{var vector=new float[768];vector[i]=1;return vector;}).ToArray();var keys=RecommendationQuery.Reactions.Select(r=>r.Key).ToArray();
            var single=AutomaticRanker.Score(new List<float[]>{concepts[0]},new List<float[]>{null},concepts,keys,0,concepts[0],CancellationToken.None);check(single[0].Accepted);
            var duplicates=Enumerable.Range(0,40).Select(i=>concepts[0]).ToList();var emptyDocuments=Enumerable.Range(0,40).Select(i=>(float[])null).ToList();
            var repeated=AutomaticRanker.Score(duplicates,emptyDocuments,concepts,keys,0,concepts[0],CancellationToken.None,Enumerable.Repeat("same image",40).ToList());check(repeated.All(item=>item.Accepted));check(Math.Abs(repeated[0].Margin-single[0].Margin)<1e-9);
            var flat=AutomaticRanker.Score(duplicates,emptyDocuments,concepts,keys,0,concepts[0],CancellationToken.None);check(flat.All(item=>item.Accepted));
            var ambiguous=AutomaticRanker.Score(new List<float[]>{concepts[0]},new List<float[]>{null},Enumerable.Range(0,17).Select(i=>concepts[0]).ToArray(),keys,0,concepts[0],CancellationToken.None);check(!ambiguous[0].Accepted);
            var misleading=new float[768];misleading[0]=.6f;misleading[1]=.7f;misleading[2]=(float)Math.Sqrt(.15);
            var cohortRows=new List<float[]>{misleading};cohortRows.AddRange(Enumerable.Range(0,40).Select(i=>concepts[1]));
            var protectedScores=AutomaticRanker.Score(cohortRows,cohortRows.Select(i=>(float[])null).ToList(),concepts,keys,0,concepts[0],CancellationToken.None);
            check(protectedScores[0].Margin>0&&protectedScores[0].RawMargin<0&&!protectedScores[0].Accepted);
            check(!AutomaticRanker.Accept(1,.5,-.001));check(AutomaticRanker.Accept(1,.1,.02));
            var blocked=new TaskCompletionSource<bool>();var applied=new List<string>();int attempts=0;
            using(var indexer=new CatalogIndexer(async(entry,token)=>{attempts++;if(entry.DisplayName=="old")await blocked.Task;entry.Path=good;},(source,copy)=>applied.Add(source.DisplayName),(done,failed)=>{})){
                indexer.Update(new[]{new Entry{DisplayName="old"}},true);var oldTask=indexer.Pending;
                indexer.Update(new[]{new Entry{DisplayName="new pack"}},true);await indexer.Pending;
                check(applied.SequenceEqual(new[]{"new pack"}));blocked.SetResult(true);await oldTask;check(applied.Count==1);
                indexer.Update(new[]{new Entry{DisplayName="disabled"}},false);check(attempts==2);
            }
            int prepared=0,failures=0;
            using(var indexer=new CatalogIndexer((entry,token)=>{if(entry.DisplayName=="bad")throw new IOException("fixture");prepared++;return Task.FromResult(0);},(source,copy)=>{},(done,failed)=>failures=failed)){
                indexer.Update(new[]{new Entry{DisplayName="bad"},new Entry{DisplayName="good"}},true);await indexer.Pending;check(prepared==1&&failures==1);
            }
            foreach(string input in new[]{"진짜 웃기네ㅋㅋ","너무 좋아요","미안해요","너무 슬퍼","안녕","고마워요"})check(RecommendationQuery.Intent(input)!=null);
            foreach(string input in new[]{"안미안해","너무 안 좋아","좋다고 하지만 아니야"})check(RecommendationQuery.Intent(input)==null);
            check(RecommendationQuery.MetadataBonus(new Entry{Aliases="안웃겨"},"ㅋㅋㅋ")==0);
            check(RecommendationQuery.Intent("안 웃겨").Key=="disapproval");check(RecommendationQuery.Intent("안 좋아").Key=="disapproval");
            check(RecommendationQuery.Intent("고양이가 인사하는 모습").Key=="greeting");check(RecommendationQuery.Intent("감사 인사하는 콘").Key=="thanks");
            foreach(string input in new[]{"ㅎㅇ","ㅂㅂ","ㄱㅅ","ㅈㅅ","ㅇㅋ","ㅊㅋ","ㄴㄴ","배고파","감기 걸렸어","행복해","부끄러워"})check(RecommendationQuery.Intent(input)!=null);
            check(RecommendationQuery.Intent("ㅋㅋㅠㅠ")==null&&RecommendationQuery.AvoidGuessing("ㅋㅋㅠㅠ"));
            string assets=Path.Combine(root,"assets");Directory.CreateDirectory(Path.Combine(assets,"gemma2"));File.WriteAllText(Path.Combine(assets,"gemma2","ready.txt"),SemanticSearch.Spec);
            var manifest=new TrustedModelManifest{Spec=SemanticSearch.Spec,Files=new List<TrustedModelAsset>()};for(int i=0;i<12;i++){var bytes=new byte[]{(byte)i,1};string path="asset-"+i;File.WriteAllBytes(Path.Combine(assets,path),bytes);using(var sha=SHA256.Create())manifest.Files.Add(new TrustedModelAsset{Path=path,Bytes=bytes.Length,Sha256=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-",""),Required=true});}
            File.WriteAllText(Path.Combine(assets,"download-integrity.json"),GemmaHost.Json(manifest));check(ModelAssets.Ready(assets));
            string corrupt=Path.Combine(assets,"asset-5");File.WriteAllBytes(corrupt,new byte[]{99,1});File.SetLastWriteTimeUtc(corrupt,DateTime.UtcNow.AddSeconds(2));check(!ModelAssets.Ready(assets));
            File.Delete(Path.Combine(assets,"asset-1"));check(!ModelAssets.Ready(assets));
            File.WriteAllText(report,"PASS: "+checks+" checks: canceled startup reused, bad image isolated, repeat search preserved host, shutdown disposed, valid/missing/same-size-corrupt model detected; approval slang, negation/context boundaries, numeric metadata ignored and alias reranking isolated by query. Includes default retention/optional idle release, content deduplication, no embedded per-image meaning table, metadata document cache invalidation, new remote image indexing, catalog replacement cancellation and per-image isolation. Fixtures only; no GPU, network, KakaoTalk or visible UI.\r\n");
        }catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}finally{timer.Dispose();context.ExitThread();}};timer.Start();Application.Run(context);}
    }
}
