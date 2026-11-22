Set-StrictMode -Version Latest

function New-AgitUpdatePlan {
    param([Parameter(Mandatory)][string]$ProjectRoot)
    $root = [IO.Path]::GetFullPath($ProjectRoot)
    $dist = Join-Path $root 'dist'
    $build = Join-Path $root 'artifacts/build/AgitCon-next'
    $next = Join-Path $build 'AgitCon-next.exe'
    if (!(Test-Path -LiteralPath $next -PathType Leaf)) { throw 'Build AgitCon-next.exe first.' }
    $hash = (Get-FileHash -LiteralPath $next).Hash
    $package = $null
    foreach ($folder in @('semantic')) {
        $candidate = Join-Path $build $folder
        $marker = Join-Path $candidate 'build.json'
        if (!(Test-Path -LiteralPath $marker)) { continue }
        $info = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($info.buildHash -eq $hash) { $package = $candidate; $manifest = $info; break }
    }
    if (!$package) { throw 'No matching payload manifest. Rebuild before updating.' }
    $allowed = @('prepare.py','worker.py','download-sizes.json','download-integrity.json','gemma2/web/gemma.html','gemma2/web/gemma.mjs','gemma2/web/request-queue.mjs','prepare-caption.py','caption-assets.json','gemma2/web/caption.html','gemma2/web/caption.mjs','gemma2/web/tensor-memory.mjs','animated-webp.py')
    if (@($manifest.files).Count -ne $allowed.Count) { throw 'Incomplete payload manifest.' }
    $files = @([pscustomobject]@{ Source=$next; Destination=(Join-Path $dist 'AgitCon.exe'); Hash=$hash })
    foreach ($relative in $allowed) {
        $entry = @($manifest.files | Where-Object { $_.path -ceq $relative })
        if ($entry.Count -ne 1) { throw "Missing payload entry: $relative" }
        $source = Join-Path $package $relative
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing payload file: $relative" }
        if ((Get-FileHash -LiteralPath $source).Hash -ne $entry[0].sha256) { throw "Payload hash mismatch: $relative" }
        $files += [pscustomobject]@{ Source=$source; Destination=(Join-Path (Join-Path $dist 'semantic') $relative); Hash=$entry[0].sha256 }
    }
    $codec = Join-Path $build 'codecs/convert.py'
    if (!(Test-Path -LiteralPath $codec) -or (Get-FileHash -LiteralPath $codec).Hash -ne $manifest.codecHash) { throw 'Codec payload mismatch. Rebuild first.' }
    $files += [pscustomobject]@{Source=$codec;Destination=(Join-Path $dist 'codecs/convert.py');Hash=$manifest.codecHash}
    foreach($item in @(@{name='AgitCon.Updater.exe';hash=$manifest.updaterHash},@{name='version.json';hash=$manifest.versionHash})){
        $source=Join-Path $build $item.name
        if((Get-FileHash -LiteralPath $source).Hash -ne $item.hash){throw 'Update helper/version hash mismatch.'}
        $files += [pscustomobject]@{Source=$source;Destination=(Join-Path $dist $item.name);Hash=$item.hash}
    }
    [pscustomobject]@{ Root=$root; Dist=$dist; Current=(Join-Path $dist 'AgitCon.exe'); Catalog=(Join-Path $dist 'data/catalog.json'); Files=$files }
}

function Restore-AgitUpdate {
    param([Parameter(Mandatory)]$Transaction)
    $errors = @()
    foreach ($entry in $Transaction.Journal) {
        try {
            if ($entry.Existed) {
                $temp = Join-Path $Transaction.Backup ([guid]::NewGuid().ToString('N') + '.restore')
                Copy-Item -LiteralPath $entry.Backup -Destination $temp
                if (Test-Path -LiteralPath $entry.Destination) { [IO.File]::Replace($temp,$entry.Destination,(Join-Path $Transaction.Backup ([guid]::NewGuid().ToString('N') + '.overwritten'))) }
                else { [IO.File]::Move($temp,$entry.Destination) }
            } elseif (Test-Path -LiteralPath $entry.Destination) {
                # Only exact files journaled inside this validated application folder.
                if (![IO.Path]::GetFullPath($entry.Destination).StartsWith($Transaction.Prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Restore path outside app folder.' }
                Remove-Item -LiteralPath $entry.Destination -Force
            }
        } catch { $errors += $_.Exception.Message }
    }
    if ($errors.Count) { throw ('Rollback incomplete; backups at ' + $Transaction.Backup + ': ' + ($errors -join '; ')) }
}

function Install-AgitUpdate {
    param([Parameter(Mandatory)]$Plan, [int]$FaultAfter=-1)
    $prefix = [IO.Path]::GetFullPath($Plan.Dist).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    foreach ($file in $Plan.Files) {
        if (![IO.Path]::GetFullPath($file.Destination).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Update destination outside app folder.' }
        if ((Get-FileHash -LiteralPath $file.Source).Hash -ne $file.Hash) { throw 'Payload changed after preflight.' }
    }
    $backup = Join-Path $Plan.Root ('archive/updates/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $transaction = [pscustomobject]@{ Backup=$backup; Prefix=$prefix; Journal=@() }
    # Include configuration so registration failures can undo settings changes too.
    $destinations = @($Plan.Files | ForEach-Object { $_.Destination }) + @($Plan.Catalog)
    foreach ($destination in $destinations) {
        $existed = Test-Path -LiteralPath $destination
        $saved = Join-Path $backup ($transaction.Journal.Count.ToString() + '.bak')
        if ($existed) { Copy-Item -LiteralPath $destination -Destination $saved }
        $transaction.Journal += [pscustomobject]@{ Destination=$destination; Existed=$existed; Backup=$saved }
    }
    $transaction | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backup 'transaction.json') -Encoding utf8
    try {
        $count = 0
        foreach ($file in $Plan.Files) {
            $temp = Join-Path $backup ([guid]::NewGuid().ToString('N') + '.new')
            Copy-Item -LiteralPath $file.Source -Destination $temp
            if ((Get-FileHash -LiteralPath $temp).Hash -ne $file.Hash) { throw 'Staged copy hash mismatch.' }
            New-Item -ItemType Directory -Path (Split-Path -Parent $file.Destination) -Force | Out-Null
            if (Test-Path -LiteralPath $file.Destination) { [IO.File]::Replace($temp,$file.Destination,(Join-Path $backup ([guid]::NewGuid().ToString('N') + '.replaced'))) }
            else { [IO.File]::Move($temp,$file.Destination) }
            $count++
            if ($count -eq $FaultAfter) { throw 'Injected isolated test failure.' }
        }
        return $transaction
    } catch {
        $failure = $_
        try { Restore-AgitUpdate -Transaction $transaction } catch { $_.Exception.Data['AjitRollbackFailed']=$true; throw }
        throw $failure
    }
}
