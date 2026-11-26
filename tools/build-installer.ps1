param([ValidateSet('x86','x64')][string]$Architecture='x64',[string]$Compiler=(Join-Path $PSScriptRoot '../artifacts/tooling/InnoSetup/ISCC.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$version=(Get-Content (Join-Path $root 'version.json') -Raw|ConvertFrom-Json).version
$pointer=Get-Content (Join-Path $root "artifacts/packages/latest-$Architecture.json") -Raw|ConvertFrom-Json
$payload=[IO.Path]::GetFullPath($pointer.stage)
if(!$payload.StartsWith([IO.Path]::GetFullPath((Join-Path $root 'artifacts/packages/')),[StringComparison]::OrdinalIgnoreCase)){throw 'Payload outside package staging root'}
$manifest=Get-Content (Join-Path $payload 'package-manifest.json') -Raw|ConvertFrom-Json
if($manifest.version -ne $version -or $manifest.architecture -ne $Architecture){throw 'Installer package/version mismatch'}
foreach($item in $manifest.files){
    if([IO.Path]::IsPathRooted($item.path) -or ($item.path -split '[/\\]') -contains '..'){throw 'Invalid package path'}
    if((Get-FileHash -LiteralPath (Join-Path $payload $item.path)).Hash -ne $item.sha256){throw 'Package hash mismatch'}
}
& $Compiler "/DVersion=$version" "/DArchitecture=$Architecture" "/DPayload=$payload" (Join-Path $root 'installer/AgitCon.iss')
if($LASTEXITCODE){throw 'Installer compilation failed'}
$file=Join-Path $root "releases/$version/AgitCon-Setup-$version-win-$Architecture.exe"
$digest=(Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($file+'.sha256',$digest+'  '+[IO.Path]::GetFileName($file)+"`n")
Write-Output $file
