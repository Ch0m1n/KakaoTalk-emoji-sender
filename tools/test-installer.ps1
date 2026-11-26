param([ValidateSet('x86','x64')][string]$Architecture='x64')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$version=(Get-Content (Join-Path $root 'version.json') -Raw|ConvertFrom-Json).version
$pointer=Get-Content (Join-Path $root "artifacts/packages/latest-$Architecture.json") -Raw|ConvertFrom-Json
$compiler=Join-Path $root 'artifacts/tooling/InnoSetup/ISCC.exe'
& $compiler /Q /DTestMode=1 "/DVersion=$version" "/DArchitecture=$Architecture" "/DPayload=$($pointer.stage)" (Join-Path $root 'installer/AgitCon.iss')
if($LASTEXITCODE){throw 'Fixture installer compilation failed'}
$setup=Join-Path $root "artifacts/tests/installer-build/AgitCon-Setup-$version-win-$Architecture.exe"
$run=Join-Path $root ('artifacts/tests/installer-'+$Architecture+'-'+[guid]::NewGuid().ToString('N'))
$target=Join-Path $run 'app'
New-Item -ItemType Directory -Path $run -Force|Out-Null
function Invoke-Fixture([string]$file,[string]$arguments){
    $p=Start-Process -FilePath $file -ArgumentList $arguments -PassThru -WindowStyle Hidden
    if(!$p.WaitForExit(120000)){throw 'Installer fixture timed out'}
    if($p.ExitCode){throw ('Installer fixture failed: '+$p.ExitCode)}
}
$installArguments='/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /TASKS="" /DIR="'+$target+'"'
$installed=$false
try{
    Invoke-Fixture $setup ($installArguments+' /LOG="'+(Join-Path $run 'install.log')+'"');$installed=$true
    if(!(Test-Path (Join-Path $target 'unins000.exe')) -or !(Test-Path (Join-Path $target 'AgitCon.exe'))){throw 'Install payload missing'}
    New-Item -ItemType Directory -Path (Join-Path $target 'data'),(Join-Path $target 'semantic/models') -Force|Out-Null
    [IO.File]::WriteAllText((Join-Path $target 'data/catalog.json'),'private-fixture')
    [IO.File]::WriteAllText((Join-Path $target 'semantic/models/keep.txt'),'model-fixture')
    Invoke-Fixture $setup ($installArguments+' /LOG="'+(Join-Path $run 'upgrade.log')+'"')
    if([IO.File]::ReadAllText((Join-Path $target 'data/catalog.json')) -ne 'private-fixture'){throw 'Upgrade changed user settings'}
    Invoke-Fixture (Join-Path $target 'unins000.exe') '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART';$installed=$false
    if(Test-Path (Join-Path $target 'AgitCon.exe')){throw 'Application files remained after uninstall'}
    if([IO.File]::ReadAllText((Join-Path $target 'data/catalog.json')) -ne 'private-fixture' -or [IO.File]::ReadAllText((Join-Path $target 'semantic/models/keep.txt')) -ne 'model-fixture'){throw 'Uninstall removed user data'}
    "PASS: $Architecture isolated install -> upgrade -> uninstall, settings/models preserved. Test-only registration; no shortcuts or app launch."|Set-Content (Join-Path $run 'result.txt')
    Get-Content (Join-Path $run 'result.txt')
}finally{
    if($installed -and (Test-Path (Join-Path $target 'unins000.exe'))){Invoke-Fixture (Join-Path $target 'unins000.exe') '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'}
}
