using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;

namespace AgitCon {
    [DataContract] sealed class TrustedModelAsset {
        [DataMember(Name="path")]public string Path;
        [DataMember(Name="bytes")]public long Bytes;
        [DataMember(Name="sha256")]public string Sha256;
        [DataMember(Name="required")]public bool Required;
    }
    [DataContract] sealed class TrustedModelManifest {
        [DataMember(Name="spec")]public string Spec;
        [DataMember(Name="files")]public List<TrustedModelAsset> Files;
    }
    static class ModelAssets {
        static string fingerprint;static bool verified;
        public static bool Ready(string root){try{
            if(File.ReadAllText(System.IO.Path.Combine(root,"gemma2","ready.txt"))!=SemanticSearch.Spec)return false;
            string manifestPath=System.IO.Path.Combine(root,"download-integrity.json");
            var manifest=GemmaHost.Read<TrustedModelManifest>(File.ReadAllText(manifestPath));
            if(manifest.Spec!=SemanticSearch.Spec||manifest.Files==null||manifest.Files.Count(f=>f.Required)!=12)return false;
            var files=manifest.Files.Where(f=>f.Required).ToList();var paths=new List<string>();
            string prefix=System.IO.Path.GetFullPath(root).TrimEnd('\\','/')+System.IO.Path.DirectorySeparatorChar;
            var stamp=new FileInfo(manifestPath);string state=manifestPath+":"+stamp.Length+":"+stamp.LastWriteTimeUtc.Ticks;
            foreach(var file in files){string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,file.Path));if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return false;var info=new FileInfo(path);if(!info.Exists||info.Length!=file.Bytes)return false;paths.Add(path);state+="|"+path+":"+info.Length+":"+info.LastWriteTimeUtc.Ticks;}
            if(state==fingerprint)return verified;
            bool valid=true;for(int i=0;i<files.Count;i++)using(var stream=File.OpenRead(paths[i]))using(var sha=SHA256.Create()){string digest=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");if(!String.Equals(digest,files[i].Sha256,StringComparison.OrdinalIgnoreCase)){valid=false;break;}}
            fingerprint=state;verified=valid;return valid;
        }catch{return false;}}
    }
}
