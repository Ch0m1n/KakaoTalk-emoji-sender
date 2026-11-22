using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgitCon.Updates;
class VerifyPublishedUpdate {
 static int Main(string[] args){try{Run(args[0]).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 static async Task Run(string folder){
  var release=await UpdateCore.Check(CancellationToken.None);if(release==null)throw new Exception("No release");var asset=UpdateCore.Select(release,"0.9.0");if(asset==null)throw new Exception("Release not detected");
  const string settings="{\"Entries\":[],\"Folders\":[],\"Packs\":[]}";
  var zip=await UpdateCore.Download(asset,folder,CancellationToken.None,null);string root=Path.Combine(folder,"app");Directory.CreateDirectory(Path.Combine(root,"data"));Directory.CreateDirectory(Path.Combine(root,"semantic","models"));File.WriteAllText(Path.Combine(root,"AgitCon.exe"),"old-version");File.WriteAllText(Path.Combine(root,"data","catalog.json"),settings);File.WriteAllText(Path.Combine(root,"semantic","models","keep.txt"),"private-model");
  bool launched=false;var plan=new UpdatePlan{Root=root,Package=zip,Sha256=asset.Digest.Substring(7),Version=release.Tag.TrimStart('v')};
  UpdateCore.Apply(plan,Path.Combine(folder,"stage"),exe=>{var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe,"--catalog-load-test"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root});if(!process.WaitForExit(15000)||process.ExitCode!=0)throw new Exception("Isolated package startup failed");launched=true;});
  if(!launched||File.ReadAllText(Path.Combine(root,"data","catalog.json"))!=settings||File.ReadAllText(Path.Combine(root,"semantic","models","keep.txt"))!="private-model")throw new Exception("User files changed");
  if(UpdateCore.Select(release,"1.0.0")!=null)throw new Exception("Already-current check failed");
  Console.WriteLine("PASS: public GitHub API -> trusted asset -> SHA-256 -> extract -> isolated install -> packaged app self-check; data/model preserved. No Kakao interaction.");
 }
}
