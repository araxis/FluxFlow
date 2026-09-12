[CmdletBinding()]
param([switch]$Full)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $root 'artifacts/transfer/manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw 'Transfer manifest missing. A source-only clone does not include the local feeds/evidence; copy the complete project folder.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (-not (Test-Path -LiteralPath (Join-Path $root '.git') -PathType Container)) {
    throw 'The project must have its own .git directory, not a linked-worktree pointer.'
}
$branch = & git -C $root branch --show-current
if ($LASTEXITCODE -ne 0) { throw 'Cannot read repository branch.' }
$commit = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot read repository commit.' }
if ($branch -ne $manifest.Branch -or $commit -ne $manifest.Commit) {
    throw 'Branch or commit differs from the transfer checkpoint. Inspect the change; do not reset it automatically.'
}
$files = if ($Full) { @($manifest.Files) } else { @($manifest.Files | Where-Object Essential) }
$failures = [Collections.Generic.List[string]]::new()
$checked = 0
foreach ($file in $files) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $file.Path))
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Manifest path escapes this repository.'
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $failures.Add("Missing: $($file.Path)")
        continue
    }
    $stream = [IO.File]::OpenRead($path)
    try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
    finally { $stream.Dispose() }
    if ($hash -ne $file.Hash) { $failures.Add("Changed: $($file.Path)") }
    $checked++
}
if ($files.Count -eq 0) { throw 'Manifest contains no selected files.' }
if ($failures.Count) {
    $failures | Select-Object -First 30 | Write-Output
    throw "$($failures.Count) transfer differences. Historical build/qualification results are not a fresh validation of changed files."
}
Write-Output "Verified $checked files on $branch at $commit."
Write-Output "Candidate: $($manifest.CandidateVersion). Original qualification is preserved; no tests or restore were run."
& git -C $root status --short
if ($LASTEXITCODE -ne 0) { throw 'Cannot read working-tree status.' }
