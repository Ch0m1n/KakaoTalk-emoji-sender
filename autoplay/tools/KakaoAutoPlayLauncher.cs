using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

internal static class KakaoAutoPlayLauncher
{
    static string BundleRoot;
    static string Extract(string parent)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string digest;
        using (var data = assembly.GetManifestResourceStream("payload.zip"))
        using (var sha = SHA256.Create())
            digest = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        string root = Path.Combine(parent, digest.Substring(0,16));
        if (File.Exists(Path.Combine(root, ".bundle-ready"))) return root;
        Directory.CreateDirectory(parent);
        string staging = root + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        using (var data = assembly.GetManifestResourceStream("payload.zip"))
        using (var archive = new ZipArchive(data, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                string target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!target.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid bundle path");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (entry.Name.Length > 0) entry.ExtractToFile(target);
            }
        }
        File.WriteAllText(Path.Combine(staging, ".bundle-ready"), digest);
        try { Directory.Move(staging, root); }
        catch (IOException) { if (!File.Exists(Path.Combine(root, ".bundle-ready"))) throw; }
        return root;
    }

    static Process Launch(string action, bool test)
    {
        var info = new ProcessStartInfo(Path.Combine(BundleRoot, "runtime", test ? "python.exe" : "pythonw.exe"),
            "\"" + Path.Combine(BundleRoot, "tools", "kakao-portable-launcher.py") + "\" " + action);
        info.WorkingDirectory = BundleRoot;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = test;
        info.RedirectStandardError = test;
        return Process.Start(info);
    }

    [STAThread]
    static int Main(string[] args)
    {
        bool test = args.Length == 2 && args[0] == "--self-test";
        try
        {
            BundleRoot = Extract(test ? Path.GetFullPath(args[1]) : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakaoAutoPlay", "bundles"));
            if (test)
            {
                using (var process = Launch("check", true))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    File.WriteAllText(Path.Combine(Path.GetFullPath(args[1]), "self-test.txt"),
                        "exit=" + process.ExitCode + Environment.NewLine + output + error);
                    return process.ExitCode;
                }
            }
            Application.EnableVisualStyles();
            string previous=null;bool resume=false,startInTray=false;
            for(int i=0;i<args.Length;i++) {
                if(args[i]=="--upgrade-from"&&i+1<args.Length)previous=args[++i];
                else if(args[i]=="--resume")resume=true;
                else if(args[i]=="--tray")startInTray=true;
            }
            bool first;
            string instanceName="Local\\KakaoAutoPlay.UI."+Path.GetFileName(BundleRoot);
            using(var instance=new System.Threading.Mutex(true,instanceName,out first))
            using(var restore=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,instanceName+".Restore")) {
                if(!first){restore.Set();return 0;}
                using(var form=new AutoPlayForm(BundleRoot,previous,resume,startInTray))
                using(var restoreTimer=new System.Windows.Forms.Timer {Interval=300}) {
                    restoreTimer.Tick+=delegate {if(restore.WaitOne(0))form.RestoreWindow();};restoreTimer.Start();
                    Application.Run(form);
                }
                instance.ReleaseMutex();
            }
            return 0;
        }
        catch (Exception error)
        {
            try {
                string logRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KakaoAutoPlay");
                Directory.CreateDirectory(logRoot);
                string log=Path.Combine(logRoot,"launcher-bootstrap.log");
                if(File.Exists(log)&&new FileInfo(log).Length>262144) File.WriteAllText(log,"");
                File.AppendAllText(log,DateTimeOffset.Now.ToString("o")+" "+error.ToString()+Environment.NewLine);
            } catch { }
            if (test) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1],"self-test.txt"),error.ToString()); }
            else MessageBox.Show(error.Message, "Kakao AutoPlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
