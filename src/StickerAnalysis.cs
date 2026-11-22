using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgitCon {
    [DataContract] sealed class StickerDescription {
        [DataMember(Name="text")]public string Text;
        [DataMember(Name="description")]public string Description;
        [DataMember(Name="reactions")]public string[] Reactions;
        [DataMember(Name="spec")]public string Spec;
        [DataMember(Name="hash")]public string Hash;
        internal bool TextFirst {get{return (Text??"").Count(c=>c>='가'&&c<='힣')>=4||((Text??"").Count(Char.IsLetter)>=2&&(AnalysisEvidence.WrittenReaction(this)!=null||RecommendationQuery.Negated(Text)))||((Text??"").Count(Char.IsLetter)>=12&&(Text??"").Split(new[]{' ','\n'},StringSplitOptions.RemoveEmptyEntries).Length>=2);}}
        // Generated/inferred labels are proposals, never independent evidence.
        // Adding them here would make an erroneous proposal confirm itself.
        internal string Document {get{return TextFirst?Text:String.Join(". ",new[]{Text,Description}.Where(s=>!String.IsNullOrWhiteSpace(s)));}}
    }
    static class StickerAnalysis {
        internal const string Spec="qwen3vl2b-mixed-caption-v4";
        internal const string ModelSpec="qwen3vl2b-mixed-caption-v1";
        static string fingerprint;static bool verified;
        internal static bool Ready {get{try{
            string root=Path.Combine(SemanticSearch.Root,"gemma2","web","models","caption");
            if(File.ReadAllText(Path.Combine(root,"ready.txt"))!=ModelSpec)return false;
            string manifestPath=Path.Combine(SemanticSearch.Root,"caption-assets.json");var manifest=GemmaHost.Read<TrustedModelManifest>(File.ReadAllText(manifestPath));
            if(manifest.Spec!=ModelSpec||manifest.Files==null||manifest.Files.Count!=13)return false;
            string prefix=Path.GetFullPath(root).TrimEnd('\\')+"\\";var stamp=new FileInfo(manifestPath);string state=stamp.Length+":"+stamp.LastWriteTimeUtc.Ticks;var paths=new List<string>();
            foreach(var row in manifest.Files){string path=Path.GetFullPath(Path.Combine(root,row.Path));if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return false;var info=new FileInfo(path);if(!info.Exists||info.Length!=row.Bytes)return false;state+="|"+info.Length+":"+info.LastWriteTimeUtc.Ticks;paths.Add(path);}
            if(state==fingerprint)return verified;
            bool valid=true;for(int i=0;i<paths.Count;i++)using(var stream=File.OpenRead(paths[i]))using(var sha=SHA256.Create()){if(!String.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",""),manifest.Files[i].Sha256,StringComparison.OrdinalIgnoreCase)){valid=false;break;}}
            fingerprint=state;verified=valid;return valid;
        }catch{return false;}}}
        internal static string CacheDirectory {get{return Path.Combine(SemanticSearch.Root,"descriptions",Spec);}}
        static bool ValidHash(string hash){return hash!=null&&hash.Length==64&&hash.All(Uri.IsHexDigit);}
        static string Clean(string text,int length){if(text==null)return "";return new string(text.Where(c=>!Char.IsControl(c)||c=='\n').Take(length).ToArray()).Trim();}
        internal static StickerDescription Parse(string output,string hash){
            if(!ValidHash(hash))throw new InvalidDataException("Invalid image digest");
            if(output==null||output.Length>12000)throw new InvalidDataException("Invalid automatic description");
            int start=output.IndexOf('{'),end=output.LastIndexOf('}');if(start<0||end<start)throw new InvalidDataException("Incomplete automatic description");
            var value=GemmaHost.Read<StickerDescription>(output.Substring(start,end-start+1));
            value.Text=Clean(value.Text,240);value.Description=Clean(value.Description,640);
            if(value.Text=="exact legible writing in its original language, or empty")value.Text="";
            var allowed=RecommendationQuery.Reactions.Select(r=>r.Key).ToArray();
            value.Reactions=(value.Reactions??new string[0]).Where(r=>r!=null).Select(r=>r.Trim().ToLowerInvariant()).Where(r=>allowed.Contains(r)).Distinct().Take(3).ToArray();
            if(value.Reactions.Length>1)value.Reactions=value.Reactions.Where(r=>r!="neutral").ToArray();
            if(String.IsNullOrWhiteSpace(value.Description))throw new InvalidDataException("Empty automatic description");
            value.Spec=Spec;value.Hash=hash;return value;
        }
        internal static StickerDescription Read(string hash,string directory=null){try{
            if(!ValidHash(hash))return null;
            var value=GemmaHost.Read<StickerDescription>(File.ReadAllText(Path.Combine(directory??CacheDirectory,hash+".json")));
            if(value.Spec!=Spec||value.Hash!=hash)return null;return Parse(GemmaHost.Json(value),hash);
        }catch{return null;}}
        internal static void Write(StickerDescription value,string directory=null){
            if(!ValidHash(value.Hash)||value.Spec!=Spec)throw new InvalidDataException("Invalid description cache key");
            string folder=directory??CacheDirectory;Directory.CreateDirectory(folder);string file=Path.Combine(folder,value.Hash+".json"),temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllText(temporary,GemmaHost.Json(value),new UTF8Encoding(false));if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);
        }
    }
    // Import/indexing only. Never receives a draft, window or chat history.
    interface IStickerDescriptionHost:IDisposable{Task Start(CancellationToken cancel);Task<string> Describe(string path,CancellationToken cancel);}
    interface IStickerDescriptionRetryHost{Task<string> DescribeBrief(string path,CancellationToken cancel);}
    sealed class StickerAnalyzer:IDisposable {
        IStickerDescriptionHost host;readonly Func<IStickerDescriptionHost> factory;readonly string cacheDirectory;
        internal StickerAnalyzer(Func<IStickerDescriptionHost> factory=null,string cacheDirectory=null){this.factory=factory??(()=>new GemmaHost(Path.Combine(SemanticSearch.Root,"gemma2","caption-profile"),"caption.html"));this.cacheDirectory=cacheDirectory;}
        public async Task<StickerDescription> Analyze(string path,string hash,CancellationToken cancel){
            try{if(host==null){host=factory();await host.Start(cancel);}
                string output=await host.Describe(path,cancel);cancel.ThrowIfCancellationRequested();StickerDescription value=null;
                try{value=StickerAnalysis.Parse(output,hash);}catch(InvalidDataException){}catch(SerializationException){}
                if(value==null){var retry=host as IStickerDescriptionRetryHost;if(retry==null)throw new InvalidDataException("Invalid automatic description");output=await retry.DescribeBrief(path,cancel);cancel.ThrowIfCancellationRequested();value=StickerAnalysis.Parse(output,hash);}
                StickerAnalysis.Write(value,cacheDirectory);return value;
            }catch{Dispose();throw;}
        }
        public void Dispose(){if(host!=null){host.Dispose();host=null;}}
    }
}
