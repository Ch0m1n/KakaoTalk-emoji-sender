using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using AgitCon.Updates;
namespace AgitCon {
 internal static class AppUpdateTests {
  static int count;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;}
  static void Reject(Action action,string why){try{action();}catch{count++;return;}throw new Exception("Accepted: "+why);}
  static string Zip(string root,string name,string extra=null){string file=Path.Combine(root,name+".zip");using(var zip=ZipFile.Open(file,ZipArchiveMode.Create)){
   foreach(var pair in new Dictionary<string,string>{{"AgitCon.exe","MZnew"},{"AgitCon.Updater.exe","MZhelper"},{"version.json","{\"version\":\"1.0.0\",\"repository\":\""+UpdateCore.Repository+"\"}"},{"semantic/worker.py","new-worker"}})using(var writer=new StreamWriter(zip.CreateEntry(pair.Key).Open()))writer.Write(pair.Value);
   if(extra!=null)using(var writer=new StreamWriter(zip.CreateEntry(extra).Open()))writer.Write("bad");
  }return file;}
  internal static void Run(){string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-work","app-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   Check(String.Equals(UpdateCore.ProcessImagePath(System.Diagnostics.Process.GetCurrentProcess().Id),Path.GetFullPath(typeof(Program).Assembly.Location),StringComparison.OrdinalIgnoreCase),"limited-query process image identity");Reject(()=>UpdateCore.ProcessImagePath(0),"invalid process identity");
   Check(UpdateCore.ParseVersion("v1.10.0")>UpdateCore.ParseVersion("1.9.9"),"numeric version");foreach(string text in new[]{"1.0","1.0.0-beta","01.0.0","1.0.0/../x","latest",null})Reject(()=>UpdateCore.ParseVersion(text),"version");
   foreach(string path in new[]{"../evil","/evil","a\\b","a:b","a/../b","a//b","a./b","a /b"})Reject(()=>UpdateCore.SafeRelative(path),"path");
   foreach(string path in new[]{"data/catalog.json","semantic/models/a","runtime/.venv/a","secret.txt","profile/cache"})Check(!UpdateCore.AllowedFile(path),"protected payload");
   foreach(string url in new[]{"http://github.com/x","https://github.com.evil/x","https://github.com:444/x","https://user@github.com/x"})Check(!UpdateCore.UrlAllowed(new Uri(url)),"download host");
   var asset=new ReleaseAsset{Name=UpdateCore.AssetName,State="uploaded",Url="https://github.com/"+UpdateCore.Repository+"/releases/download/v1.0.0/"+UpdateCore.AssetName,Size=100,Digest="sha256:"+new string('a',64)};
   var release=new ReleaseInfo{Tag="v1.0.0",Assets=new List<ReleaseAsset>{asset}};Check(UpdateCore.Select(release,"0.9.0")==asset,"new release");Check(UpdateCore.Select(release,"1.0.0")==null,"already current");Check(UpdateCore.Select(release,"2.0.0")==null,"no downgrade");
   asset.Digest=null;Reject(()=>UpdateCore.Select(release,"0.9.0"),"missing digest");asset.Digest="sha256:"+new string('a',64);release.Assets.Add(asset);Reject(()=>UpdateCore.Select(release,"0.9.0"),"ambiguous asset");release.Assets.RemoveAt(1);release.Prerelease=true;Reject(()=>UpdateCore.Select(release,"0.9.0"),"prerelease");
   release.Prerelease=false;var setup=new ReleaseAsset{Name=UpdateCore.InstallerName(release.Tag),State="uploaded",Size=100,Digest=asset.Digest};setup.Url="https://github.com/"+UpdateCore.Repository+"/releases/download/"+release.Tag+"/"+setup.Name;release.Assets.Add(setup);Check(UpdateCore.Select(release,"0.9.0",true)==setup,"installed app selects setup");Check(UpdateCore.Select(release,"0.9.0")==asset,"portable app selects ZIP");setup.Url+="bad";Reject(()=>UpdateCore.Select(release,"0.9.0",true),"installer URL mismatch");release.Assets.Remove(setup);
   string package=Zip(root,"good");Check(UpdateCore.Extract(package,Path.Combine(root,"extract"),"1.0.0").Count==4,"extract");Reject(()=>UpdateCore.Extract(package,Path.Combine(root,"wrong-version"),"1.0.1"),"version mismatch");
   int n=0;foreach(string bad in new[]{"../escape","data/catalog.json","semantic/models/weights","AgitCon.exe","AGITCON.EXE"}){string zip=Zip(root,"bad"+(n++),bad);Reject(()=>UpdateCore.Extract(zip,Path.Combine(root,"bad-stage"+n),"1.0.0"),"malicious archive");}
   for(int fault=0;fault<=4;fault++){
    string install=Path.Combine(root,"install"+fault);Directory.CreateDirectory(Path.Combine(install,"data"));Directory.CreateDirectory(Path.Combine(install,"semantic","models"));File.WriteAllText(Path.Combine(install,"AgitCon.exe"),"old");File.WriteAllText(Path.Combine(install,"data","catalog.json"),"private");File.WriteAllText(Path.Combine(install,"semantic","models","weights"),"model");
    var plan=new UpdatePlan{Root=install,Package=package,Sha256=UpdateCore.Hash(package),Version="1.0.0"};int launched=0;
    if(fault==0)UpdateCore.Apply(plan,Path.Combine(root,"apply"+fault),exe=>launched++);
    else {int at=fault;Reject(()=>UpdateCore.Apply(plan,Path.Combine(root,"apply"+fault),exe=>launched++,i=>{if(i==at)throw new IOException("injected");}),"rollback");}
    Check(File.ReadAllText(Path.Combine(install,"AgitCon.exe"))==(fault==0?"MZnew":"old"),"transaction file");Check(launched==(fault==0?1:0),"launch after commit only");Check(File.ReadAllText(Path.Combine(install,"data","catalog.json"))=="private","settings preserved");Check(File.ReadAllText(Path.Combine(install,"semantic","models","weights"))=="model","models preserved");if(fault>0)Check(!File.Exists(Path.Combine(install,"AgitCon.Updater.exe")),"new files rolled back");
   }
   string locked=Path.Combine(root,"locked");Directory.CreateDirectory(locked);File.WriteAllText(Path.Combine(locked,"AgitCon.exe"),"locked-old");using(var hold=new FileStream(Path.Combine(locked,"AgitCon.exe"),FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>UpdateCore.Apply(new UpdatePlan{Root=locked,Package=package,Sha256=UpdateCore.Hash(package),Version="1.0.0"},Path.Combine(root,"locked-stage"),exe=>{throw new Exception("must not launch");}),"locked app");Check(File.ReadAllText(Path.Combine(locked,"AgitCon.exe"))=="locked-old","locked unchanged");
   File.WriteAllText(Path.Combine(root,"result.txt"),"PASS "+count+" update checks; offline fixtures only.");
  }
 }
}
