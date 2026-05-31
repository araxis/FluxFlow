param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $PackageName = "",

    [string] $ChangelogPath = "CHANGELOG.md",

    [string] $OutputPath = "artifacts/release-notes.md"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ChangelogPath)) {
    throw "Changelog '$ChangelogPath' was not found."
}

$lines = Get-Content -LiteralPath $ChangelogPath
$headingPatterns = New-Object System.Collections.Generic.List[string]
if (-not [string]::IsNullOrWhiteSpace($PackageName)) {
    $headingPatterns.Add("^\s*##\s+\[?$([regex]::Escape($PackageName))\s+$([regex]::Escape($Version))\]?\s*$")
}

if ([string]::IsNullOrWhiteSpace($PackageName) -or $PackageName -eq "FluxFlow.Engine") {
    $headingPatterns.Add("^\s*##\s+\[?$([regex]::Escape($Version))\]?\s*$")
}
$nextHeadingPattern = "^\s*##\s+"
$start = -1

for ($i = 0; $i -lt $lines.Count; $i++) {
    foreach ($headingPattern in $headingPatterns) {
        if ($lines[$i] -match $headingPattern) {
            $start = $i + 1
            break
        }
    }

    if ($start -ge 0) {
        break
    }
}

if ($start -lt 0) {
    if ([string]::IsNullOrWhiteSpace($PackageName)) {
        throw "Changelog does not contain a section for version '$Version'."
    }

    throw "Changelog does not contain a section for '$PackageName' version '$Version'."
}

$notes = New-Object System.Collections.Generic.List[string]
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $nextHeadingPattern) {
        break
    }

    $notes.Add($lines[$i])
}

$content = ($notes -join [Environment]::NewLine).Trim()
if ([string]::IsNullOrWhiteSpace($content)) {
    throw "Changelog section for version '$Version' is empty."
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

Set-Content -LiteralPath $OutputPath -Value $content -Encoding UTF8
Write-Host "Wrote release notes for $Version to $OutputPath"
