#requires -Version 7

[CmdletBinding()]
param(
    [string]$Repository = "araxis/FluxFlow",
    [switch]$Push
)

$ErrorActionPreference = "Stop"

gh auth status

if (-not (Test-Path -LiteralPath ".git")) {
    git init
    git branch -M main
}

& gh repo view $Repository *> $null
$repoExists = $LASTEXITCODE -eq 0

if (-not $repoExists) {
    gh repo create $Repository --private --source . --remote origin
}
else {
    & git remote get-url origin *> $null
    if ($LASTEXITCODE -ne 0) {
        git remote add origin "https://github.com/$Repository.git"
    }
}

if ([string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) {
    Write-Warning "Set NUGET_API_KEY in the current shell, then rerun this script to store the repository secret."
}
else {
    $env:NUGET_API_KEY | gh secret set NUGET_API_KEY --repo $Repository
}

if ($Push) {
    git push -u origin HEAD:main
}
