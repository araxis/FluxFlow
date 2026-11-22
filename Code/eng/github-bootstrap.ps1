#requires -Version 7

[CmdletBinding()]
param(
    [string]$Repository = "araxis/FluxFlow",
    [switch]$Push
)

$ErrorActionPreference = "Stop"

function Invoke-Step {
    param(
        [string] $Command,
        [string[]] $Arguments,
        [string] $FailureMessage
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

Invoke-Step "gh" @("auth", "status") "GitHub CLI authentication check failed."

if (-not (Test-Path -LiteralPath ".git")) {
    Invoke-Step "git" @("init") "Repository initialization failed."
    Invoke-Step "git" @("branch", "-M", "main") "Default branch rename failed."
}

& gh repo view $Repository *> $null
$repoExists = $LASTEXITCODE -eq 0

if (-not $repoExists) {
    Invoke-Step "gh" @(
        "repo",
        "create",
        $Repository,
        "--private",
        "--source",
        ".",
        "--remote",
        "origin"
    ) "Repository creation failed."
}
else {
    & git remote get-url origin *> $null
    if ($LASTEXITCODE -ne 0) {
        Invoke-Step "git" @(
            "remote",
            "add",
            "origin",
            "https://github.com/$Repository.git"
        ) "Adding the origin remote failed."
    }
}

if ([string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) {
    Write-Warning "Set NUGET_API_KEY in the current shell, then rerun this script to store the repository secret."
}
else {
    $env:NUGET_API_KEY | gh secret set NUGET_API_KEY --repo $Repository
    if ($LASTEXITCODE -ne 0) {
        throw "Storing the NUGET_API_KEY repository secret failed."
    }
}

if ($Push) {
    Invoke-Step "git" @("push", "-u", "origin", "HEAD:main") "Pushing main to origin failed."
}
