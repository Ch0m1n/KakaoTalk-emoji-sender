$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$work=Join-Path $root ('artifacts/tests/updater-identity-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $work -Force|Out-Null
$source=Join-Path $work 'fixture.cs'
@'
using System;using System.IO;using System.Diagnostics;using AgitCon.Updates;
class IdentityFixture {
 static int Main(string[] args){if(args.Length==0){System.Threading.Thread.Sleep(20000);return 0;}
 using(var parent=Process.GetProcessById(Int32.Parse(args[0]))){
  if(parent.StartTime.ToUniversalTime().Ticks!=Int64.Parse(args[2]))return 2;
  return String.Equals(UpdateCore.ProcessImagePath(parent.Id),Path.GetFullPath(args[1]),StringComparison.OrdinalIgnoreCase)?0:3;
 }}
}
'@ | Set-Content -LiteralPath $source -Encoding utf8
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
foreach($arch in @('x86','x64')){
    & $compiler /nologo /target:winexe "/platform:$arch" "/out:$(Join-Path $work "$arch.exe")" /reference:System.dll /reference:System.Core.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Runtime.Serialization.dll $source (Join-Path $root 'src/AppUpdateCore.cs')
    if($LASTEXITCODE){throw 'Identity fixture compilation failed'}
}
$parents=@()
try{
    foreach($arch in @('x86','x64')){
        $path=Join-Path $work "$arch.exe"
        $parent=Start-Process -FilePath $path -WindowStyle Hidden -PassThru
        $parents+=$parent
        $started=$parent.StartTime.ToUniversalTime().Ticks
        foreach($reader in @('x86','x64')){
            $probe=Start-Process -FilePath (Join-Path $work "$reader.exe") -ArgumentList ($parent.Id.ToString()+' "'+$path+'" '+$started) -WindowStyle Hidden -PassThru
            if(!$probe.WaitForExit(5000) -or $probe.ExitCode -ne 0){throw "Identity mismatch: $reader reader / $arch parent"}
        }
        $wrong=Start-Process -FilePath (Join-Path $work 'x86.exe') -ArgumentList ($parent.Id.ToString()+' "'+(Join-Path $work 'wrong.exe')+'" '+$started) -WindowStyle Hidden -PassThru
        if(!$wrong.WaitForExit(5000) -or $wrong.ExitCode -ne 3){throw 'Wrong parent path accepted'}
    }
    'PASS: all x86/x64 reader-parent combinations, creation time and wrong-path rejection. Owned processes only; no process memory, Kakao or input.'|Set-Content -LiteralPath (Join-Path $work 'result.txt')
    Get-Content -LiteralPath (Join-Path $work 'result.txt')
}finally{
    foreach($parent in $parents){if(!$parent.HasExited){Stop-Process -Id $parent.Id -ErrorAction SilentlyContinue};$parent.Dispose()}
}
