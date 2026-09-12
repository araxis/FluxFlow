[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath (Join-Path $root '.git') -PathType Container)) {
    throw 'The project must have its own .git directory, not a linked-worktree pointer.'
}
if (Test-Path -LiteralPath (Join-Path $root '.git/objects/info/alternates')) {
    throw 'Repository uses an alternate object store; inspect its portability before continuing.'
}
$branch = & git -C $root branch --show-current
if ($LASTEXITCODE -ne 0) { throw 'Cannot read repository branch.' }
if ($branch -ne 'work/core-refactoring-transfer-20260912') {
    throw 'Unexpected branch. Inspect it; do not switch or reset automatically.'
}
$commit = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot read repository commit.' }
$changes = @(& git -C $root status --porcelain=v1)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read working-tree status.' }
if ($changes.Count) {
    $changes | Select-Object -First 30 | Write-Output
    throw 'Working tree has changes. Preserve and inspect them before continuing.'
}
& git -C $root fsck --connectivity-only --no-dangling
if ($LASTEXITCODE -ne 0) { throw 'Git object connectivity check failed.' }
Write-Output "Verified clean standalone source checkout on $branch at $commit."
Write-Output 'Generated artifacts and local feeds are intentionally absent. No restore, build or tests were run.'
