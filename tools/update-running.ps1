param([string]$ArcaPack = '', [switch]$EnableSemantic, [switch]$EnableDraft, [switch]$VerifySemantic, [switch]$EnableAnalysis, [switch]$EnableGifFileOpen, [switch]$EnableGifDirectSend)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Update-Core.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# All package validation happens before stopping the current app.
$plan = New-AgitUpdatePlan -ProjectRoot $projectRoot
if ($ArcaPack -and $ArcaPack -notmatch '^\d{1,12}$') { throw 'Invalid Arca pack number.' }
$currentExe = $plan.Current
$transaction = $null
$wasRunning = $false
$newProcess = $null
$runningExe = $currentExe
try {
    $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'AgitCon.exe' OR Name = 'AjitCon.exe'" | Where-Object { [string]::Equals($_.ExecutablePath,$currentExe,[StringComparison]::OrdinalIgnoreCase) -or [string]::Equals($_.ExecutablePath,(Join-Path $plan.Dist 'AjitCon.exe'),[StringComparison]::OrdinalIgnoreCase) })
    foreach ($process in $processes) {
        $runningApp = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
        if ($runningApp) { $wasRunning=$true; $runningExe=$process.ExecutablePath; Stop-Process -Id $process.ProcessId; if (!$runningApp.WaitForExit(5000)) { throw 'App did not exit; files were not replaced.' } }
    }
    $transaction = Install-AgitUpdate -Plan $plan
    $commands = @()
    if ($ArcaPack) { $commands += ,@('--register-arca',$ArcaPack) }
    if ($EnableSemantic) { $commands += ,@('--enable-semantic') }
    if ($EnableAnalysis) { $commands += ,@('--enable-analysis') }
    if ($EnableDraft) { $commands += ,@('--enable-draft') }
    if ($EnableGifFileOpen) { $commands += ,@('--enable-gif-open') }
    if ($EnableGifDirectSend) { $commands += ,@('--enable-gif-direct-send') }
    if ($VerifySemantic) { $commands += ,@('--gemma-smoke') }
    foreach ($arguments in $commands) {
        $setupProcess = Start-Process -FilePath $currentExe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
        if ($setupProcess.ExitCode -ne 0) { throw ('App configuration failed: ' + $arguments[0]) }
    }
    $newProcess = Start-Process -FilePath $currentExe -PassThru -WindowStyle Hidden
    if ($newProcess.WaitForExit(800)) { throw ('Updated app exited immediately: ' + $newProcess.ExitCode) }
} catch {
    $failure = $_
    if ($newProcess -and !$newProcess.HasExited) { Stop-Process -Id $newProcess.Id; [void]$newProcess.WaitForExit(5000) }
    if ($transaction) { Restore-AgitUpdate -Transaction $transaction }
    if ($wasRunning -and !$failure.Exception.Data.Contains("AjitRollbackFailed") -and (Test-Path -LiteralPath $runningExe)) { Start-Process -FilePath $runningExe -WindowStyle Hidden }
    throw $failure
}
# Migrate only this application's already authorized startup shortcut.
try {
    $startupDirectory=[Environment]::GetFolderPath('Startup')
    $appIcon=Join-Path $projectRoot 'dist\AgitCon.ico'
    $legacyExe=Join-Path $plan.Dist 'AjitCon.exe'
    foreach($linkName in @('AjitCon.lnk','AgitCon.lnk')) {
        $existingLink=Join-Path $startupDirectory $linkName
        if (!(Test-Path -LiteralPath $existingLink)) { continue }
        $shell=New-Object -ComObject WScript.Shell
        $shortcut=$shell.CreateShortcut($existingLink)
        if (![string]::Equals($shortcut.TargetPath,$currentExe,[StringComparison]::OrdinalIgnoreCase) -and ![string]::Equals($shortcut.TargetPath,$legacyExe,[StringComparison]::OrdinalIgnoreCase)) { continue }
        $shortcut.TargetPath=$currentExe; $shortcut.WorkingDirectory=$plan.Dist; $shortcut.IconLocation=$appIcon+',0'; $shortcut.Description='아지트콘 (AgitCon)'; $shortcut.Save()
        if($linkName -eq 'AjitCon.lnk') {
            $newLink=Join-Path $startupDirectory 'AgitCon.lnk'
            if(!(Test-Path -LiteralPath $newLink)){Rename-Item -LiteralPath $existingLink -NewName 'AgitCon.lnk'}
            else { $newShortcut=$shell.CreateShortcut($newLink); if([string]::Equals($newShortcut.TargetPath,$currentExe,[StringComparison]::OrdinalIgnoreCase)){Move-Item -LiteralPath $existingLink -Destination (Join-Path $transaction.Backup 'legacy-startup-shortcut.lnk')} }
        }
    }
} catch { Write-Warning 'App updated; startup shortcut migration was skipped.' }
Write-Output ('Updated and opened AgitCon.exe. Rollback backup: ' + $transaction.Backup)
