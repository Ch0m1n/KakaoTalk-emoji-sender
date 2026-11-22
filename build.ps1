param([string]$OutputName = 'AgitCon-next.exe', [switch]$StagePayload, [switch]$DeveloperTests)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
if (!(Test-Path -LiteralPath $csc)) { throw '.NET Framework 4.8 C# compiler not found.' }
if ($OutputName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.exe$') { throw 'OutputName must be an executable filename.' }
$buildDir = Join-Path $root ('artifacts/build/' + [IO.Path]::GetFileNameWithoutExtension($OutputName))
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
$source = Join-Path $root 'src\AgitCon.cs'
$shopSource = Join-Path $root 'src\DcShop.cs'
$testSource = Join-Path $root 'src\LazyTests.cs'
$uiSource = Join-Path $root 'src\UiTheme.cs'
$shopUiSource = Join-Path $root 'src\ShopForm.cs'
$collectionSource = Join-Path $root 'src\Collection.cs'
$semanticSource = Join-Path $root 'src\SemanticSearch.cs'
$bundleTestSource = Join-Path $root 'src\BundleTests.cs'
$arcaSource = Join-Path $root 'src\ArcaShop.cs'
$arcaTestSource = Join-Path $root 'src\ArcaTests.cs'
$codecSource = Join-Path $root 'src\AssetCodec.cs'
$imageClipboardSource = Join-Path $root 'src\ImageClipboard.cs'
$previewPasteSource = Join-Path $root 'src\PreviewPaste.cs'
$floatingSource = Join-Path $root 'src\ChatFloating.cs'
$librarySource = Join-Path $root 'src\LibraryView.cs'
$gemmaSource = Join-Path $root 'src\GemmaHost.cs'
$draftSource = Join-Path $root 'src\DraftRecommendations.cs'
$modelAssetsSource = Join-Path $root 'src\ModelAssets.cs'
$aliasSource = Join-Path $root 'src\AliasEditor.cs'
$regressionSource = Join-Path $root 'src\SearchRegression.cs'
$metadataSource = Join-Path $root 'src\EntryMetadata.cs'
$indexSource = Join-Path $root 'src\CatalogIndexer.cs'
$rankerSource = Join-Path $root 'src\AutomaticRanker.cs'
$promptSource = Join-Path $root 'src\ReactionPrompts.cs'
$autoAuditSource = Join-Path $root 'src\AutoTuningAudit.cs'
$qualitySource = Join-Path $root 'src\QualityAudit.cs'
$webview = Join-Path $root 'dist\Microsoft.Web.WebView2.Core.dll'
if (!(Test-Path -LiteralPath $webview)) { throw 'Run Python semantic/prepare.py --sdk-only from dist/semantic first.' }
if ([System.IO.Path]::GetFileName($OutputName) -ne $OutputName) { throw 'OutputName must be a filename.' }
$appIcon = Join-Path $root 'assets\AgitCon.ico'
if (!(Test-Path -LiteralPath $appIcon)) { throw 'Run tools/build-icon.py first.' }
$output = Join-Path $buildDir $OutputName
$version = Get-Content -LiteralPath (Join-Path $root 'version.json') -Raw | ConvertFrom-Json
if($version.version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$'){throw 'Invalid app version.'}
$assemblyInfo=Join-Path $buildDir 'AssemblyInfo.cs'
('[assembly:System.Reflection.AssemblyVersion("'+$version.version+'.0")][assembly:System.Reflection.AssemblyFileVersion("'+$version.version+'.0")][assembly:System.Reflection.AssemblyProduct("KakaoTalk emoji sender")]') | Set-Content -LiteralPath $assemblyInfo -Encoding utf8
$updateSources=@((Join-Path $root 'src/DpiPolicyTests.cs'),(Join-Path $root 'src/ThumbnailMemory.cs'),(Join-Path $root 'src/LongPreviewVision.cs'),(Join-Path $root 'src/ArcaBrowserSearch.cs'),(Join-Path $root 'src/ArcaSearchTests.cs'),(Join-Path $root 'src/AppUpdateCore.cs'),(Join-Path $root 'src/AppUpdates.cs'),(Join-Path $root 'src/AppUpdateTests.cs'),$assemblyInfo)
$compression=@('/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll')
$client = Join-Path $wpf 'UIAutomationClient.dll'
$types = Join-Path $wpf 'UIAutomationTypes.dll'
$base = Join-Path $wpf 'WindowsBase.dll'
$developerSources=@(); $developerDefines=@()
$winrt=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/System.Runtime.WindowsRuntime.dll'
$runtime=Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/System.Runtime') -Filter System.Runtime.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
$metadata=Get-ChildItem 'C:/Program Files (x86)/Windows Kits/10/UnionMetadata' -Directory | Where-Object Name -match '^10\.' | Sort-Object Name -Descending | ForEach-Object {Join-Path $_.FullName 'Windows.winmd'} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
if(!$metadata -or !$runtime -or !(Test-Path -LiteralPath $winrt)){throw 'Windows SDK metadata / .NET Windows Runtime references required.'}
$visionArgs=@("/reference:$winrt","/reference:$runtime","/reference:$metadata","/resource:$(Join-Path $root 'assets/kakao-settings-gear.png'),AgitCon.KakaoSettingsGear.png",(Join-Path $root 'src/ConfirmationVision.cs'),(Join-Path $root 'src/ConfirmationNavigator.cs'),(Join-Path $root 'src/ConfirmationVisionTests.cs'),(Join-Path $root 'src/OcrSetup.cs'),(Join-Path $root 'src/DpiGeometry.cs'))
if($DeveloperTests){$developerSources=@($regressionSource,$qualitySource,$autoAuditSource);$developerDefines=@('/define:AGITCON_DEV')}
& $csc $compression $developerDefines /nologo "/win32icon:$appIcon" "/win32manifest:$(Join-Path $root 'assets/AgitCon.manifest')" "/resource:$appIcon,AgitCon.AppIcon.ico" /target:winexe /platform:x64 /optimize+ /utf8output "/out:$output" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll "/reference:$client" "/reference:$types" "/reference:$base" $updateSources $source (Join-Path $root 'src/CacheMaintenance.cs') (Join-Path $root 'src/LongCon.cs') (Join-Path $root 'src/UiZoom.cs') (Join-Path $root 'src/LongConNative.cs') (Join-Path $root 'src/LongConTests.cs') $shopSource $testSource $uiSource $shopUiSource $collectionSource $semanticSource $bundleTestSource $arcaSource $arcaTestSource $codecSource $imageClipboardSource $previewPasteSource $floatingSource $librarySource $gemmaSource $draftSource $modelAssetsSource $aliasSource $developerSources $promptSource $rankerSource $indexSource $metadataSource (Join-Path $root 'src/StickerAnalysis.cs') (Join-Path $root 'src/AnalysisEvidence.cs') "/reference:$webview" $visionArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $csc /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$(Join-Path $buildDir 'AgitCon.Updater.exe')" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll $compression (Join-Path $root 'src/AppUpdateCore.cs') (Join-Path $root 'tools/AgitConUpdater.cs') $assemblyInfo
if($LASTEXITCODE -ne 0){throw 'Updater build failed.'}
Copy-Item -LiteralPath (Join-Path $root 'version.json') -Destination (Join-Path $buildDir 'version.json') -Force
# Each build owns its manifest and payload. StagePayload is accepted for old commands.
# Building never overwrites the installed app, its data or semantic environment.
$semanticDir = Join-Path $buildDir 'semantic'
New-Item -ItemType Directory -Path $semanticDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'semantic\prepare.py') -Destination (Join-Path $semanticDir 'prepare.py') -Force
Copy-Item -LiteralPath (Join-Path $root 'semantic\worker.py') -Destination (Join-Path $semanticDir 'worker.py') -Force
Copy-Item -LiteralPath (Join-Path $root 'semantic\download-sizes.json') -Destination (Join-Path $semanticDir 'download-sizes.json') -Force
Copy-Item -LiteralPath (Join-Path $root 'semantic\download-integrity.json') -Destination (Join-Path $semanticDir 'download-integrity.json') -Force
$gemmaWeb = Join-Path $semanticDir 'gemma2\web'
New-Item -ItemType Directory -Path $gemmaWeb -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'semantic\gemma.html') -Destination (Join-Path $gemmaWeb 'gemma.html') -Force
Copy-Item -LiteralPath (Join-Path $root 'semantic\gemma.mjs') -Destination (Join-Path $gemmaWeb 'gemma.mjs') -Force
Copy-Item -LiteralPath (Join-Path $root 'semantic\request-queue.mjs') -Destination (Join-Path $gemmaWeb 'request-queue.mjs') -Force
Copy-Item -LiteralPath (Join-Path $root 'semantic/prepare-caption.py') -Destination (Join-Path $semanticDir 'prepare-caption.py') -Force
Copy-Item -LiteralPath (Join-Path $root 'semantic/caption-assets.json') -Destination (Join-Path $semanticDir 'caption-assets.json') -Force
Copy-Item -LiteralPath (Join-Path $root 'semantic/caption.html') -Destination (Join-Path $semanticDir 'gemma2/web/caption.html') -Force
Copy-Item -LiteralPath (Join-Path $root 'semantic/caption.mjs') -Destination (Join-Path $semanticDir 'gemma2/web/caption.mjs') -Force
Copy-Item -LiteralPath (Join-Path $root 'semantic/tensor-memory.mjs') -Destination (Join-Path $gemmaWeb 'tensor-memory.mjs') -Force
$codecDir = Join-Path $buildDir 'codecs'
New-Item -ItemType Directory -Path $codecDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'codecs\convert.py') -Destination (Join-Path $codecDir 'convert.py') -Force
Copy-Item -LiteralPath $appIcon -Destination (Join-Path $buildDir 'AgitCon.ico') -Force
foreach ($name in @('Microsoft.Web.WebView2.Core.dll','WebView2Loader.dll')) {
    Copy-Item -LiteralPath (Join-Path $root ('dist/' + $name)) -Destination (Join-Path $buildDir $name) -Force
}
Copy-Item -LiteralPath (Join-Path $root 'semantic/animated-webp.py') -Destination (Join-Path $semanticDir 'animated-webp.py') -Force
$payloadFiles = @('prepare.py','worker.py','download-sizes.json','download-integrity.json','gemma2/web/gemma.html','gemma2/web/gemma.mjs','gemma2/web/request-queue.mjs','prepare-caption.py','caption-assets.json','gemma2/web/caption.html','gemma2/web/caption.mjs','gemma2/web/tensor-memory.mjs','animated-webp.py')
$entries = @($payloadFiles | ForEach-Object { @{ path=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $semanticDir $_)).Hash } })
@{ updaterHash=(Get-FileHash -LiteralPath (Join-Path $buildDir 'AgitCon.Updater.exe')).Hash; versionHash=(Get-FileHash -LiteralPath (Join-Path $buildDir 'version.json')).Hash; buildHash=(Get-FileHash -LiteralPath $output).Hash; codecHash=(Get-FileHash -LiteralPath (Join-Path $codecDir "convert.py")).Hash; files=$entries } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $semanticDir 'build.json') -Encoding utf8
Write-Output "Built: $output"
