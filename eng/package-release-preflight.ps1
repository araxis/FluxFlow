param(
    [Parameter(Mandatory = $true)]
    [string] $Package,

    [string] $Version = "",

    [string] $ManifestPath = "eng/packages.json",

    [string] $ChangelogPath = "CHANGELOG.md"
)

$ErrorActionPreference = "Stop"

function Read-KeyValueFile {
    param([string] $Path)

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $separator = $line.IndexOf("=")
        if ($separator -le 0) {
            continue
        }

        $key = $line.Substring(0, $separator).Trim()
        $value = $line.Substring($separator + 1).Trim()
        $values[$key] = $value
    }

    return $values
}

function Require-Value {
    param(
        [hashtable] $Values,
        [string] $Key
    )

    if (-not $Values.ContainsKey($Key) -or [string]::IsNullOrWhiteSpace($Values[$Key])) {
        throw "Resolved release value '$Key' is missing."
    }

    return $Values[$Key]
}

function Find-Package {
    param(
        [object[]] $Packages,
        [string] $Alias
    )

    foreach ($candidate in $Packages) {
        if ($candidate.alias -ieq $Alias) {
            return $candidate
        }
    }

    return $null
}

if ([string]::IsNullOrWhiteSpace($Package)) {
    throw "Package is required."
}

$repoRoot = (Get-Location).Path
$environmentPath = Join-Path ([System.IO.Path]::GetTempPath()) "fluxflow-preflight-$([Guid]::NewGuid().ToString('N')).env"
$notesPath = Join-Path ([System.IO.Path]::GetTempPath()) "fluxflow-preflight-notes-$([Guid]::NewGuid().ToString('N')).md"
$resolverPath = Join-Path $repoRoot "eng/resolve-package-release.ps1"
$notesHelperPath = Join-Path $repoRoot "eng/get-release-notes.ps1"

try {
    $resolveArgs = @{
        Package = $Package
        ManifestPath = $ManifestPath
        EnvironmentPath = $environmentPath
    }

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $resolveArgs.Version = $Version
    }

    & $resolverPath @resolveArgs 6> $null

    $resolved = Read-KeyValueFile $environmentPath
    $packageAlias = Require-Value $resolved "PACKAGE_ALIAS"
    $packageId = Require-Value $resolved "PACKAGE_ID"
    $packageProject = Require-Value $resolved "PACKAGE_PROJECT"
    $packageVersion = Require-Value $resolved "PACKAGE_VERSION"
    $releaseTag = Require-Value $resolved "RELEASE_TAG"

    $packages = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $packages = @($packages)
    $manifestEntry = Find-Package $packages $packageAlias
    if ($null -eq $manifestEntry -or [string]::IsNullOrWhiteSpace($manifestEntry.notesName)) {
        throw "Package '$packageAlias' does not define a release notes name in '$ManifestPath'."
    }

    $notesName = $manifestEntry.notesName
    & $notesHelperPath -PackageName $notesName -Version $packageVersion -ChangelogPath $ChangelogPath -OutputPath $notesPath 6> $null

    $dryRunCommand = "./eng/package-release-dry-run.ps1 -Package $packageAlias -Version $packageVersion"
    $fastDryRunCommand = "$dryRunCommand -SkipSolutionBuild"
    $tagCommand = "./eng/package-release-tag.ps1 -Package $packageAlias -Version $packageVersion"
    $tagPushCommand = "$tagCommand -Push"

    Write-Host "PREFLIGHT_PACKAGE_ALIAS=$packageAlias"
    Write-Host "PREFLIGHT_PACKAGE_ID=$packageId"
    Write-Host "PREFLIGHT_PACKAGE_PROJECT=$packageProject"
    Write-Host "PREFLIGHT_PACKAGE_VERSION=$packageVersion"
    Write-Host "PREFLIGHT_RELEASE_TAG=$releaseTag"
    Write-Host "PREFLIGHT_CHANGELOG_NAME=$notesName"
    Write-Host "PREFLIGHT_CHANGELOG_OK=True"
    Write-Host "PREFLIGHT_DRY_RUN_COMMAND=$dryRunCommand"
    Write-Host "PREFLIGHT_FAST_DRY_RUN_COMMAND=$fastDryRunCommand"
    Write-Host "PREFLIGHT_TAG_COMMAND=$tagCommand"
    Write-Host "PREFLIGHT_TAG_PUSH_COMMAND=$tagPushCommand"
}
finally {
    Remove-Item -LiteralPath $environmentPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $notesPath -Force -ErrorAction SilentlyContinue
}
