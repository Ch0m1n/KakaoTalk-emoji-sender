$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$tooling=Join-Path $root 'artifacts/tooling'
$compiler=Join-Path $tooling 'InnoSetup/ISCC.exe'
if(Test-Path -LiteralPath $compiler){Write-Output $compiler;return}
New-Item -ItemType Directory -Path $tooling -Force|Out-Null
$setup=Join-Path $tooling 'innosetup-7.1.1-x64.exe'
Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' -OutFile $setup
if((Get-AuthenticodeSignature -FilePath $setup).Status -ne 'Valid'){throw 'Invalid official compiler signature'}
$installArguments=@('/PORTABLE=1','/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="'+(Join-Path $tooling 'InnoSetup')+'"'))
$process=Start-Process -FilePath $setup -ArgumentList $installArguments -WindowStyle Hidden -PassThru -Wait
if($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)){throw 'Compiler preparation failed'}
Write-Output $compiler
