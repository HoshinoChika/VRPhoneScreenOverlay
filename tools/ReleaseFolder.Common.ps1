function Assert-ReleasePath {
    param([string]$Path, [string]$RepositoryRoot)

    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
    if (-not $resolved.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to access a release path outside the repository: $resolved"
    }

    # Check ancestors before traversing a tree. Enumeration must never follow a
    # junction or symbolic link outside the verified repository boundary.
    $ancestor = $resolved
    while ($ancestor.Length -ge $root.Length) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Release paths must not contain reparse points: $ancestor"
            }
        }
        if ($ancestor -eq $root) { break }
        $ancestor = Split-Path -Parent $ancestor
    }
}

function Get-ReleaseFiles {
    param([string]$Path, [string]$RepositoryRoot)

    Assert-ReleasePath -Path $Path -RepositoryRoot $RepositoryRoot
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Release directory does not exist: $Path"
    }
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue($Path)
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Dequeue() -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Release trees must not contain reparse points: $($item.FullName)"
            }
            if ($item.PSIsContainer) {
                $pending.Enqueue($item.FullName)
            } else {
                $item
            }
        }
    }
}

function Remove-ReleaseTree {
    param([string]$Path, [string]$RepositoryRoot)

    Assert-ReleasePath -Path $Path -RepositoryRoot $RepositoryRoot
    if (Test-Path -LiteralPath $Path) {
        $null = @(Get-ReleaseFiles -Path $Path -RepositoryRoot $RepositoryRoot)
        Remove-Item -LiteralPath ([IO.Path]::GetFullPath($Path)) -Recurse -Force
    }
}

function Get-ReleaseContentHash {
    param([string]$Path, [string]$RepositoryRoot)

    $prefix = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/') + '\'
    $inventory = [Text.StringBuilder]::new()
    foreach ($file in @(Get-ReleaseFiles -Path $Path -RepositoryRoot $RepositoryRoot |
            Sort-Object FullName)) {
        $relative = $file.FullName.Substring($prefix.Length).Replace('\', '/').ToLowerInvariant()
        $fileHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $null = $inventory.Append($relative).Append('|').Append($file.Length).Append('|').AppendLine($fileHash)
    }
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($sha256.ComputeHash(
                [Text.Encoding]::UTF8.GetBytes($inventory.ToString()))).Replace('-', '').ToLowerInvariant()
    } finally {
        $sha256.Dispose()
    }
}

function Assert-ReleaseNotInUse {
    param([string]$Path, [string]$RepositoryRoot)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $prefix = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/') + '\'
    foreach ($process in Get-Process) {
        try { $executable = $process.Path } catch { $executable = $null }
        if ($executable -and $executable.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Release is running (PID $($process.Id)); close it before building: $Path"
        }
    }
    foreach ($file in @(Get-ReleaseFiles -Path $Path -RepositoryRoot $RepositoryRoot)) {
        # Do not stop another process. An exclusive read detects open files before
        # any rename, including fixtures that simulate a running/locked package.
        $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open,
            [IO.FileAccess]::Read, [IO.FileShare]::None)
        $stream.Dispose()
    }
}

function Publish-PreparedRelease {
    param(
        [string]$StagingDirectory,
        [string]$OutputDirectory,
        [string]$TransactionDirectory,
        [string]$RepositoryRoot
    )

    $previous = $OutputDirectory + '-previous'
    $retired = Join-Path $TransactionDirectory 'retired-previous'
    foreach ($path in @($StagingDirectory, $OutputDirectory, $previous, $retired)) {
        Assert-ReleasePath -Path $path -RepositoryRoot $RepositoryRoot
        if (Test-Path -LiteralPath $path) {
            $null = @(Get-ReleaseFiles -Path $path -RepositoryRoot $RepositoryRoot)
        }
    }
    if ((Test-Path -LiteralPath $previous) -and -not (Test-Path -LiteralPath $OutputDirectory)) {
        throw 'Only the previous release exists. Restore the interrupted release before preparing another version.'
    }
    $stagedHash = Get-ReleaseContentHash -Path $StagingDirectory -RepositoryRoot $RepositoryRoot
    if ((Test-Path -LiteralPath $OutputDirectory) -and
        (Get-ReleaseContentHash -Path $OutputDirectory -RepositoryRoot $RepositoryRoot) -eq $stagedHash) {
        Write-Host "Release contents are unchanged; preserving the previous version: $OutputDirectory" -ForegroundColor Green
        return
    }

    Assert-ReleaseNotInUse -Path $OutputDirectory -RepositoryRoot $RepositoryRoot
    Assert-ReleaseNotInUse -Path $previous -RepositoryRoot $RepositoryRoot
    $savedPrevious = $false
    $movedCurrent = $false
    try {
        if (Test-Path -LiteralPath $previous) {
            [IO.Directory]::Move($previous, $retired)
            $savedPrevious = $true
        }
        if (Test-Path -LiteralPath $OutputDirectory) {
            [IO.Directory]::Move($OutputDirectory, $previous)
            $movedCurrent = $true
        }
        # Same-volume renames publish an already validated complete directory.
        # No previous package is deleted until the new current directory exists.
        [IO.Directory]::Move($StagingDirectory, $OutputDirectory)
    } catch {
        $publishError = $_
        try {
            if ($movedCurrent) { [IO.Directory]::Move($previous, $OutputDirectory) }
            if ($savedPrevious) { [IO.Directory]::Move($retired, $previous) }
        } catch {
            $recoveryError = [InvalidOperationException]::new(
                "Release rollback needs attention. Preserved data: $TransactionDirectory. $($_.Exception.Message)",
                $_.Exception)
            $recoveryError.Data['PreserveReleaseTransaction'] = $true
            throw $recoveryError
        }
        throw $publishError
    }
    if ($savedPrevious) {
        try {
            Remove-ReleaseTree -Path $retired -RepositoryRoot $RepositoryRoot
        } catch {
            # Publication has committed. Keep recoverable older data in artifacts
            # if a late file lock prevents cleanup; release itself still has two versions.
            Write-Warning "Release updated; older artifact cleanup deferred: $retired"
        }
    }
    Write-Host "Latest release: $OutputDirectory" -ForegroundColor Green
    if ($movedCurrent) {
        Write-Host "Previous release: $previous" -ForegroundColor Green
    }
}
