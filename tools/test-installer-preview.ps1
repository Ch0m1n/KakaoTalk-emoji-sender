param([ValidateSet('x86','x64')][string]$Architecture='x64')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$version=(Get-Content (Join-Path $root 'version.json') -Raw|ConvertFrom-Json).version
$pointer=Get-Content (Join-Path $root "artifacts/packages/latest-$Architecture.json") -Raw|ConvertFrom-Json
$compiler=Join-Path $root 'artifacts/tooling/InnoSetup/ISCC.exe'
& $compiler /Q /DTestMode=1 /DDesignPreview=1 "/DVersion=$version" "/DArchitecture=$Architecture" "/DPayload=$($pointer.stage)" (Join-Path $root 'installer/AgitCon.iss')
if($LASTEXITCODE){throw 'Design preview compilation failed'}
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$helper=Join-Path $root "artifacts/tests/InstallerPreview-$Architecture.exe"
& $csc /nologo /target:exe /reference:System.Drawing.dll "/out:$helper" (Join-Path $root 'tools/InstallerPreview.cs')
if($LASTEXITCODE){throw 'Preview fixture compilation failed'}
$folder=Join-Path $root ('artifacts/tests/installer-original-'+$Architecture+'-'+[guid]::NewGuid().ToString('N'))
$setup=Join-Path $root "artifacts/tests/installer-build/AgitCon-Setup-$version-win-$Architecture.exe"
& $helper $setup $folder
if($LASTEXITCODE){throw 'Installer navigation/animation preview failed'}
Get-Content -LiteralPath (Join-Path $folder 'result.txt')
