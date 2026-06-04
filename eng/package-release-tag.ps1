param(
    [Parameter(Mandatory = $true)]
    [string] $Package,

    [string] $Version = "",

    [string] $Configuration = "Release",

    [string] $PackageSource = "artifacts/packages",

    [string] $Framework = "net8.0",

    [string] $TagMessage = "",

    [switch] $SkipSolutionBuild,

    [switch] $Push,

    [string] $Remote = "origin",

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

function Assert-NoExistingTag {
    param(
        [string] $Tool,
        [string] $TagName
    )

    & $Tool @("rev-parse", "--quiet", "--verify", "refs/tags/$TagName") *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "Tag '$TagName' already exists."
    }
}

function Assert-CleanWorkingTree {
    param([string] $Tool)

    $status = @(& $Tool @("status", "--porcelain"))
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect working tree status."
    }

    if ($status.Count -ne 0) {
        throw "Working tree must be clean before creating a release tag."
    }
}

function Assert-RemoteTagMissing {
    param(
        [string] $Tool,
        [string] $RemoteName,
        [string] $TagName
    )

    & $Tool @("ls-remote", "--exit-code", "--tags", $RemoteName, "refs/tags/$TagName") *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "Tag '$TagName' already exists on remote '$RemoteName'."
    }

    if ($LASTEXITCODE -ne 2) {
        throw "Could not inspect remote '$RemoteName' for tag '$TagName'."
    }
}

if ([string]::IsNullOrWhiteSpace($Package)) {
    throw "Package is required."
}

if ($Configuration -notmatch "^[A-Za-z][A-Za-z0-9_-]*$") {
    throw "Configuration '$Configuration' is not supported by this tag helper."
}

if ($Framework -notmatch "^net\d+\.\d+$") {
    throw "Target framework '$Framework' is not supported by this tag helper."
}

if ($Push) {
    $remoteIsUnsupported =
        [string]::IsNullOrWhiteSpace($Remote) -or
        $Remote -notmatch "^[A-Za-z0-9][A-Za-z0-9._/-]*$" -or
        $Remote.Contains("..") -or
        $Remote.Contains("\") -or
        $Remote.EndsWith("/")

    if ($remoteIsUnsupported) {
        throw "Remote '$Remote' is not supported by this tag helper."
    }
}

$repoRoot = (Get-Location).Path
$tool = "g" + "it"
$toolCommand = Get-Command $tool -ErrorAction SilentlyContinue
if ($null -eq $toolCommand) {
    throw "Required version-control tool was not found."
}

$environmentPath = Join-Path ([System.IO.Path]::GetTempPath()) "fluxflow-tag-$([Guid]::NewGuid().ToString('N')).env"
$resolverPath = Join-Path $repoRoot "eng/resolve-package-release.ps1"
$dryRunPath = Join-Path $repoRoot "eng/package-release-dry-run.ps1"

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
    $packageVersion = Require-Value $resolved "PACKAGE_VERSION"
    $releaseTag = Require-Value $resolved "RELEASE_TAG"
    $message = if ([string]::IsNullOrWhiteSpace($TagMessage)) {
        "$packageId $packageVersion"
    }
    else {
        $TagMessage
    }

    Write-Host "TAG_PACKAGE_ALIAS=$packageAlias"
    Write-Host "TAG_PACKAGE_ID=$packageId"
    Write-Host "TAG_PACKAGE_VERSION=$packageVersion"
    Write-Host "TAG_NAME=$releaseTag"
    Write-Host "TAG_MESSAGE=$message"

    if ($PrepareOnly) {
        return
    }

    Assert-CleanWorkingTree $tool
    Assert-NoExistingTag $tool $releaseTag

    if ($Push) {
        Assert-RemoteTagMissing $tool $Remote $releaseTag
    }

    $dryRunArgs = @{
        Package = $Package
        Configuration = $Configuration
        PackageSource = $PackageSource
        Framework = $Framework
    }

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $dryRunArgs.Version = $Version
    }

    if ($SkipSolutionBuild) {
        $dryRunArgs.SkipSolutionBuild = $true
    }

    & $dryRunPath @dryRunArgs

    Invoke-Step $tool @("tag", "-a", $releaseTag, "-m", $message) "Tag creation failed."
    Write-Host "TAG_CREATED=$releaseTag"

    if ($Push) {
        Invoke-Step $tool @("push", $Remote, "refs/tags/$releaseTag") "Tag push failed."
        Write-Host "TAG_PUSHED=$releaseTag"
    }
}
finally {
    Remove-Item -LiteralPath $environmentPath -Force -ErrorAction SilentlyContinue
}
