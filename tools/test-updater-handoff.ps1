$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$run=Join-Path $root ('artifacts/tests/updater-handoff-'+[guid]::NewGuid().ToString('N'))
$app=Join-Path $run 'app';$package=Join-Path $run 'package'
New-Item -ItemType Directory -Path $app,$package,(Join-Path $app 'data') -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
'class Old {static void Main(){System.Threading.Thread.Sleep(2500);}}' | Set-Content (Join-Path $run 'old.cs')
'class New {static void Main(){System.IO.File.WriteAllText(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory,"new-started.txt"),"ok");System.Threading.Thread.Sleep(1500);}}' | Set-Content (Join-Path $run 'new.cs')
& $csc /nologo /target:winexe "/out:$(Join-Path $app 'AgitCon.exe')" (Join-Path $run 'old.cs')
if($LASTEXITCODE){throw 'Old fixture build failed'}
& $csc /nologo /target:winexe "/out:$(Join-Path $package 'AgitCon.exe')" (Join-Path $run 'new.cs')
if($LASTEXITCODE){throw 'New fixture build failed'}
Copy-Item (Join-Path $root 'artifacts/build/AgitCon-next/AgitCon.Updater.exe') (Join-Path $package 'AgitCon.Updater.exe')
Copy-Item (Join-Path $root 'version.json') (Join-Path $package 'version.json')
Set-Content (Join-Path $app 'data/catalog.json') 'private fixture' -NoNewline
$zip=Join-Path $run 'update.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($package,$zip)
$old=Start-Process (Join-Path $app 'AgitCon.exe') -WindowStyle Hidden -PassThru
$plan=Join-Path $run 'plan.json'
@{Root=$app;Package=$zip;Sha256=(Get-FileHash $zip).Hash.ToLowerInvariant();Version=(Get-Content (Join-Path $package 'version.json') -Raw|ConvertFrom-Json).version;Process=$old.Id;Started=$old.StartTime.ToUniversalTime().Ticks}|ConvertTo-Json|Set-Content $plan -Encoding utf8NoBOM
$helper=Start-Process (Join-Path $package 'AgitCon.Updater.exe') -ArgumentList @('--apply',('"'+$plan+'"')) -WindowStyle Hidden -PassThru
if(!$helper.WaitForExit(20000)){Stop-Process -Id $helper.Id;throw 'Fixture helper timed out'}
if($helper.ExitCode -ne 0){throw 'Helper failed'}
if(!(Test-Path (Join-Path $app 'new-started.txt')) -or (Get-Content (Join-Path $app 'data/catalog.json') -Raw) -cne 'private fixture'){throw 'Launch or preservation failed'}
Write-Output 'PASS: actual updater waited for identified old fixture, replaced files, launched new fixture, preserved settings. No Kakao interaction.'
