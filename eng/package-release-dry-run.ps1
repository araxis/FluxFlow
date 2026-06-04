param(
    [Parameter(Mandatory = $true)]
    [string] $Package,

    [string] $Version = "",

    [string] $Configuration = "Release",

    [string] $PackageSource = "artifacts/packages",

    [string[]] $AdditionalPackageSources = @(),

    [string] $Framework = "net8.0",

    [switch] $SkipSolutionBuild,

    [switch] $PrepareOnly
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

if ([string]::IsNullOrWhiteSpace($Package)) {
    throw "Package is required."
}

if ($Configuration -notmatch "^[A-Za-z][A-Za-z0-9_-]*$") {
    throw "Configuration '$Configuration' is not supported by this dry run."
}

if ($Framework -notmatch "^net\d+\.\d+$") {
    throw "Target framework '$Framework' is not supported by this dry run."
}

$repoRoot = (Get-Location).Path
$sourcePath = [System.IO.Path]::GetFullPath($PackageSource)
if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
    throw "Package source '$sourcePath' must be a directory."
}

New-Item -ItemType Directory -Path $sourcePath -Force | Out-Null

$environmentPath = Join-Path ([System.IO.Path]::GetTempPath()) "fluxflow-dry-run-$([Guid]::NewGuid().ToString('N')).env"
$resolverPath = Join-Path $repoRoot "eng/resolve-package-release.ps1"
$archiveInspectorPath = Join-Path $repoRoot "eng/package-archive-inspect.ps1"
$consumerSmokePath = Join-Path $repoRoot "eng/package-consumer-smoke.ps1"
$feedVerifyPath = Join-Path $repoRoot "eng/package-feed-verify.ps1"

try {
    $resolveArgs = @{
        Package = $Package
        EnvironmentPath = $environmentPath
    }

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $resolveArgs.Version = $Version
    }

    & $resolverPath @resolveArgs

    $resolved = Read-KeyValueFile $environmentPath
    $packageAlias = Require-Value $resolved "PACKAGE_ALIAS"
    $packageId = Require-Value $resolved "PACKAGE_ID"
    $packageProject = Require-Value $resolved "PACKAGE_PROJECT"
    $packageVersion = Require-Value $resolved "PACKAGE_VERSION"
    $releaseTag = Require-Value $resolved "RELEASE_TAG"

    Write-Host "DRY_RUN_PACKAGE_ALIAS=$packageAlias"
    Write-Host "DRY_RUN_PACKAGE_ID=$packageId"
    Write-Host "DRY_RUN_PACKAGE_PROJECT=$packageProject"
    Write-Host "DRY_RUN_PACKAGE_VERSION=$packageVersion"
    Write-Host "DRY_RUN_RELEASE_TAG=$releaseTag"
    Write-Host "DRY_RUN_PACKAGE_SOURCE=$sourcePath"

    if ($PrepareOnly) {
        return
    }

    if (-not $SkipSolutionBuild) {
        Invoke-Step "dotnet" @("restore", "FluxFlow.sln") "Solution restore failed."
        Invoke-Step "dotnet" @(
            "build",
            "FluxFlow.sln",
            "--configuration",
            $Configuration,
            "--no-restore",
            "-p:ContinuousIntegrationBuild=true"
        ) "Solution build failed."
        Invoke-Step "dotnet" @(
            "test",
            "FluxFlow.sln",
            "--configuration",
            $Configuration,
            "--no-build"
        ) "Solution tests failed."
    }

    Invoke-Step "dotnet" @(
        "pack",
        $packageProject,
        "--configuration",
        $Configuration,
        "--no-build",
        "--output",
        $sourcePath
    ) "Package pack failed."

    & $archiveInspectorPath -PackageId $packageId -Version $packageVersion -PackageSource $sourcePath
    & $consumerSmokePath -PackageId $packageId -Version $packageVersion -PackageSource $sourcePath -Framework $Framework

    $feedVerifyArgs = @{
        PackageId = $packageId
        Version = $packageVersion
        PackageSource = $sourcePath
        Framework = $Framework
        Attempts = 1
        DelaySeconds = 0
    }

    $resolvedAdditionalSources = @($AdditionalPackageSources | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($resolvedAdditionalSources.Count -gt 0) {
        $feedVerifyArgs.Add("AdditionalPackageSources", $resolvedAdditionalSources)
    }

    & $feedVerifyPath @feedVerifyArgs

    Write-Host "DRY_RUN_OK=$packageId"
}
finally {
    Remove-Item -LiteralPath $environmentPath -Force -ErrorAction SilentlyContinue
}
