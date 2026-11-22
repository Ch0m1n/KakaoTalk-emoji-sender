$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Update-Core.ps1')
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $workspace ('artifacts/tests/update-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$payloadNames = @('prepare.py','worker.py','download-sizes.json','download-integrity.json','gemma2/web/gemma.html','gemma2/web/gemma.mjs','gemma2/web/request-queue.mjs','prepare-caption.py','caption-assets.json','gemma2/web/caption.html','gemma2/web/caption.mjs','gemma2/web/tensor-memory.mjs','animated-webp.py')
function Fixture([string]$name) {
    $root=Join-Path $testRoot $name; $dist=Join-Path $root 'dist'; $build=Join-Path $root 'artifacts/build/AgitCon-next'; $payload=Join-Path $build 'semantic'
    New-Item -ItemType Directory -Path (Join-Path $dist 'data') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $dist 'AgitCon.exe') -Value 'old-exe' -NoNewline
    New-Item -ItemType Directory -Path $build -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $build 'AgitCon-next.exe') -Value 'new-exe' -NoNewline
    Set-Content -LiteralPath (Join-Path $dist 'data/catalog.json') -Value 'old-catalog' -NoNewline
    $entries=@()
    foreach($relative in $payloadNames){$file=Join-Path $payload $relative;New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force | Out-Null;Set-Content -LiteralPath $file -Value ('new-'+$relative) -NoNewline;$entries+=@{path=$relative;sha256=(Get-FileHash -LiteralPath $file).Hash}}
    $codec=Join-Path $build 'codecs/convert.py';New-Item -ItemType Directory -Path (Split-Path -Parent $codec) -Force | Out-Null;Set-Content -LiteralPath $codec -Value 'new-codec' -NoNewline
    foreach($name in @('AgitCon.Updater.exe','version.json')){Set-Content -LiteralPath (Join-Path $build $name) -Value 'fixture' -NoNewline}
    @{updaterHash=(Get-FileHash -LiteralPath (Join-Path $build 'AgitCon.Updater.exe')).Hash;versionHash=(Get-FileHash -LiteralPath (Join-Path $build 'version.json')).Hash;buildHash=(Get-FileHash -LiteralPath (Join-Path $build 'AgitCon-next.exe')).Hash;codecHash=(Get-FileHash -LiteralPath $codec).Hash;files=$entries}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $payload 'build.json')
    return $root
}
function Check([bool]$pass,[string]$message){if(!$pass){throw $message}}
$root=Fixture 'success';$plan=New-AgitUpdatePlan $root;$transaction=Install-AgitUpdate $plan
Check ((Get-Content -LiteralPath $plan.Current -Raw) -eq 'new-exe') 'Update was not committed'
Set-Content -LiteralPath $plan.Catalog -Value 'changed-by-registration' -NoNewline
Restore-AgitUpdate $transaction
Check ((Get-Content -LiteralPath $plan.Current -Raw) -eq 'old-exe') 'Executable rollback failed'
Check ((Get-Content -LiteralPath $plan.Catalog -Raw) -eq 'old-catalog') 'Configuration rollback failed'
Check (!(Test-Path -LiteralPath (Join-Path $root 'dist/semantic/prepare.py'))) 'New file was not removed on rollback'
$root=Fixture 'injected-failure';$plan=New-AgitUpdatePlan $root;$failed=$false
try{Install-AgitUpdate $plan -FaultAfter 2|Out-Null}catch{$failed=$true}
Check $failed 'Failure was not injected'
Check ((Get-Content -LiteralPath $plan.Current -Raw) -eq 'old-exe') 'Partial copy did not restore old executable'
Check (!(Test-Path -LiteralPath (Join-Path $root 'dist/semantic/prepare.py'))) 'Partial payload remained'
$root=Fixture 'missing-file';Remove-Item -LiteralPath (Join-Path $root 'artifacts/build/AgitCon-next/semantic/gemma2/web/gemma.mjs');$failed=$false
try{New-AgitUpdatePlan $root|Out-Null}catch{$failed=$true}
Check $failed 'Missing payload was accepted'
Check ((Get-Content -LiteralPath (Join-Path $root 'dist/AgitCon.exe') -Raw) -eq 'old-exe') 'Preflight changed executable'
$root=Fixture 'bad-hash';Set-Content -LiteralPath (Join-Path $root 'artifacts/build/AgitCon-next/semantic/prepare.py') -Value 'damaged';$failed=$false
try{New-AgitUpdatePlan $root|Out-Null}catch{$failed=$true}
Check $failed 'Corrupt payload was accepted'
$root=Fixture 'changed-after-plan';$plan=New-AgitUpdatePlan $root;Set-Content -LiteralPath (Join-Path $root 'artifacts/build/AgitCon-next/semantic/prepare.py') -Value 'changed';$failed=$false
try{Install-AgitUpdate $plan|Out-Null}catch{$failed=$true}
Check $failed 'Payload change after preflight was accepted'
Check ((Get-Content -LiteralPath $plan.Current -Raw) -eq 'old-exe') 'Changed payload replaced executable'
Write-Output 'PASS: commit, executable/config rollback, new-file rollback, injected partial failure, missing payload, corrupt payload, changed payload. Isolated fixture files only; no processes stopped or launched.'
