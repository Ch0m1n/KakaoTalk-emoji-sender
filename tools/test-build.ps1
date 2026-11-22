param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*\.exe$')][string]$OutputName='AgitCon-next.exe',
    [string[]]$Flags=@('--release-self-check','--catalog-load-test','--catalog-encoding-test','--library-test','--rail-lifetime-test','--copy-refresh-test')
)
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$name=[IO.Path]::GetFileNameWithoutExtension($OutputName)
$build=Join-Path $project ('artifacts/build/'+$name)
$safe=@('--hover-animation-test','--thumbnail-memory-test','--longcon-vision-test','--arca-search-test','--app-update-test','--ui-zoom-test','--longcon-test','--cache-maintenance-test','--bundle-test','--gif-direct-test','--gear-readiness-test','--confirmation-vision-test','--scroll-repaint-test','--release-self-check','--catalog-load-test','--catalog-encoding-test','--library-test','--rail-lifetime-test','--copy-refresh-test','--dpi-geometry-test','--gif-attachment-test','--ocr-setup-test','--thumbnail-recovery-test','--thumbnail-cache-test','--image-clipboard-test','--photo-native-test','--photo-attachment-test','--attachment-flow-test','--gif-open-settings-test','--draft-test','--preview-paste-test','--floating-test','--floating-position-test','--install-progress-test','--install-progress-render')
if (!$Flags.Count -or @($Flags | Where-Object { $_ -notin $safe }).Count) { throw 'Only offline, non-Kakao test flags are allowed.' }
if (!(Test-Path -LiteralPath (Join-Path $build $OutputName))) { throw 'Build this executable first.' }
$run=Join-Path $project ('artifacts/tests/'+$name+'/'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $run -Force | Out-Null
foreach($file in @($OutputName,'AgitCon.ico','Microsoft.Web.WebView2.Core.dll','WebView2Loader.dll')){Copy-Item -LiteralPath (Join-Path $build $file) -Destination (Join-Path $run $file)}
# Copy only the build's small, hash-verified scripts. Never copy personal data/models/profiles.
$manifest=Get-Content -LiteralPath (Join-Path $build 'semantic/build.json') -Raw | ConvertFrom-Json
if((Get-FileHash -LiteralPath (Join-Path $run $OutputName)).Hash -ne $manifest.buildHash){throw 'Build manifest does not match executable.'}
foreach($item in $manifest.files){
    if([IO.Path]::IsPathRooted($item.path) -or ($item.path -split '[/\\]') -contains '..'){throw 'Invalid payload path.'}
    $source=Join-Path (Join-Path $build 'semantic') $item.path
    if((Get-FileHash -LiteralPath $source).Hash -ne $item.sha256){throw 'Build payload hash mismatch.'}
    $target=Join-Path (Join-Path $run 'semantic') $item.path
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
New-Item -ItemType Directory -Path (Join-Path $run 'data') | Out-Null
@{Entries=@();Folders=@();Packs=@();UseSemantic=$false;AutoDraftRecommendations=$false;AutomaticDescriptions=$false;PreloadThumbnails=$false;OcrSetupPrompted=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'data/catalog.json') -Encoding utf8
foreach($flag in $Flags){
    $task=Start-Process -FilePath (Join-Path $run $OutputName) -ArgumentList $flag -WorkingDirectory $run -WindowStyle Hidden -PassThru
    if(!$task.WaitForExit(30000)){Stop-Process -Id $task.Id -ErrorAction SilentlyContinue;throw ('Test timed out: '+$flag+'; '+$run)}
    if($task.ExitCode -ne 0){$errorFile=Join-Path $run 'app-update-error.txt';if(Test-Path $errorFile){Get-Content $errorFile};throw ('Test failed: '+$flag+'; '+$run)}
    $requiredReport=@{'--hover-animation-test'='hover-animation-test.txt';'--thumbnail-memory-test'='thumbnail-memory-test.txt'}
    if($requiredReport.ContainsKey($flag)){$report=Join-Path $run $requiredReport[$flag];if(!(Test-Path -LiteralPath $report) -or !(Get-Content -LiteralPath $report -Raw).StartsWith('PASS')){throw ('Missing/failed test report: '+$flag)}}
}
@{Status='PASS';Build=$build;Flags=$Flags;Results=$run} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'test-run.json') -Encoding utf8
Write-Output ('PASS: '+$Flags.Count+' offline tests. Results: '+$run)
