param(
    [Parameter(Mandatory = $true)]
    [string] $PackageId,

    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $PackageSource,

    [string[]] $AdditionalPackageSources = @(),

    [string] $Framework = "net8.0",

    [int] $Attempts = 12,

    [int] $DelaySeconds = 15,

    [int] $IndexAttempts = 40,

    [int] $IndexDelaySeconds = 15,

    [string] $WorkDirectory = "",

    [switch] $KeepWorkDirectory,

    [switch] $PrepareOnly
)

$ErrorActionPreference = "Stop"

$semver = "^\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$"

function Escape-Xml {
    param([string] $Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function Escape-CSharpString {
    param([string] $Value)

    return $Value.Replace("\", "\\").Replace('"', '\"')
}

function Resolve-PackageSource {
    param([string] $Source)

    if ([string]::IsNullOrWhiteSpace($Source)) {
        throw "Package source is required."
    }

    if (Test-Path -LiteralPath $Source -PathType Container) {
        return [System.IO.Path]::GetFullPath($Source)
    }

    $uri = $null
    if ([System.Uri]::TryCreate($Source, [System.UriKind]::Absolute, [ref] $uri)) {
        if ($uri.IsFile) {
            throw "Package source '$Source' must be an absolute URI or an existing directory."
        }

        return $Source
    }

    throw "Package source '$Source' must be an absolute URI or an existing directory."
}

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

function Resolve-FlatContainerBase {
    param([string] $ServiceIndexUrl)

    try {
        $index = Invoke-RestMethod -Uri $ServiceIndexUrl -Method Get -TimeoutSec 30
    }
    catch {
        return $null
    }

    if ($null -eq $index -or $null -eq $index.resources) {
        return $null
    }

    $resource = $index.resources |
        Where-Object { $_.'@type' -eq 'PackageBaseAddress/3.0.0' } |
        Select-Object -First 1

    if ($null -eq $resource) {
        return $null
    }

    $base = [string] $resource.'@id'
    if ([string]::IsNullOrWhiteSpace($base)) {
        return $null
    }

    if (-not $base.EndsWith("/")) {
        $base += "/"
    }

    return $base
}

function Wait-PackageIndexed {
    param(
        [string] $FlatContainerBase,
        [string] $PackageId,
        [string] $Version,
        [int] $Attempts,
        [int] $DelaySeconds
    )

    $indexUrl = "$FlatContainerBase$($PackageId.ToLowerInvariant())/index.json"

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        Write-Host "INDEX_WAIT_ATTEMPT=$attempt"
        try {
            $listing = Invoke-RestMethod -Uri $indexUrl -Method Get -TimeoutSec 30
            if ($null -ne $listing -and
                $null -ne $listing.versions -and
                ($listing.versions -contains $Version)) {
                Write-Host "INDEX_OK=$PackageId/$Version"
                return $true
            }
        }
        catch {
            # A 404 until the first version is indexed, or a transient network
            # error, both mean the version is not visible yet.
        }

        if ($attempt -lt $Attempts) {
            Start-Sleep -Seconds $DelaySeconds
        }
    }

    return $false
}

function Invoke-ConsumerCheck {
    param(
        [string] $ProjectPath,
        [string] $PackageCachePath
    )

    Invoke-Step "dotnet" @(
        "restore",
        $ProjectPath,
        "--no-cache",
        "--packages",
        $PackageCachePath
    ) "Consumer restore failed."

    Invoke-Step "dotnet" @(
        "build",
        $ProjectPath,
        "--configuration",
        "Release",
        "--no-restore"
    ) "Consumer build failed."

    Invoke-Step "dotnet" @(
        "run",
        "--project",
        $ProjectPath,
        "--configuration",
        "Release",
        "--no-build"
    ) "Consumer run failed."
}

if ([string]::IsNullOrWhiteSpace($PackageId)) {
    throw "Package id is required."
}

if ($PackageId -notmatch "^FluxFlow(?:\.[A-Za-z0-9]+)+$") {
    throw "Package id '$PackageId' is outside the allowed package family."
}

if ($Version -notmatch $semver) {
    throw "Package version '$Version' is not a valid semantic version."
}

if ($Framework -notmatch "^net\d+\.\d+$") {
    throw "Target framework '$Framework' is not supported by this feed verification."
}

if ($Attempts -lt 1) {
    throw "Attempts must be at least 1."
}

if ($DelaySeconds -lt 0) {
    throw "Delay seconds cannot be negative."
}

if ($IndexAttempts -lt 0) {
    throw "Index attempts cannot be negative."
}

if ($IndexDelaySeconds -lt 0) {
    throw "Index delay seconds cannot be negative."
}

$resolvedSources = @()
$resolvedSources += Resolve-PackageSource $PackageSource
foreach ($source in $AdditionalPackageSources) {
    if (-not [string]::IsNullOrWhiteSpace($source)) {
        $resolvedSources += Resolve-PackageSource $source
    }
}

$resolvedSources = @($resolvedSources | Select-Object -Unique)
$restoreSources = [string]::Join(";", ($resolvedSources | ForEach-Object { Escape-Xml $_ }))

$ownsWorkDirectory = [string]::IsNullOrWhiteSpace($WorkDirectory)
$workRoot = if ($ownsWorkDirectory) {
    Join-Path ([System.IO.Path]::GetTempPath()) "fluxflow-feed-verify-$([Guid]::NewGuid().ToString('N'))"
}
else {
    [System.IO.Path]::GetFullPath($WorkDirectory)
}

New-Item -ItemType Directory -Path $workRoot -Force | Out-Null

$packageCachePath = Join-Path $workRoot "packages"
$projectPath = Join-Path $workRoot "FeedVerify.csproj"
$programPath = Join-Path $workRoot "Program.cs"
$escapedPackageId = Escape-Xml $PackageId
$escapedVersion = Escape-Xml $Version
$escapedFramework = Escape-Xml $Framework
$escapedPackageCachePath = Escape-Xml $packageCachePath
$csharpPackageId = Escape-CSharpString $PackageId

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$escapedFramework</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RestoreSources>$restoreSources</RestoreSources>
    <RestorePackagesPath>$escapedPackageCachePath</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$escapedPackageId" Version="$escapedVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $projectPath

@"
using System.Reflection;

var packageId = "$csharpPackageId";
var assembly = Assembly.Load(packageId);
_ = assembly.GetTypes();
Console.WriteLine($"FEED_OK={packageId}");
"@ | Set-Content -LiteralPath $programPath

Write-Host "WORK_DIR=$workRoot"
Write-Host "PACKAGE_SOURCE=$([string]::Join(';', $resolvedSources))"
Write-Host "PACKAGE_CACHE=$packageCachePath"

if ($PrepareOnly) {
    return
}

# Poll the flat-container listing before the restore-based check. The listing
# is exactly what `dotnet restore` reads for the version, and a GET is far
# cheaper than a restore, so this absorbs nuget.org indexing lag without
# burning restore attempts. Skipped for local directory sources.
$primaryIsHttpSource = -not (Test-Path -LiteralPath $PackageSource -PathType Container)
if ($IndexAttempts -ge 1 -and $primaryIsHttpSource) {
    $flatContainerBase = Resolve-FlatContainerBase $PackageSource
    if ($null -ne $flatContainerBase) {
        Write-Host "FLAT_CONTAINER_BASE=$flatContainerBase"
        if (-not (Wait-PackageIndexed `
                    -FlatContainerBase $flatContainerBase `
                    -PackageId $PackageId `
                    -Version $Version `
                    -Attempts $IndexAttempts `
                    -DelaySeconds $IndexDelaySeconds)) {
            Write-Host "INDEX_WAIT_TIMED_OUT=$PackageId/$Version"
        }
    }
    else {
        Write-Host "FLAT_CONTAINER_BASE=unavailable"
    }
}

$lastError = $null

try {
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            Write-Host "VERIFY_ATTEMPT=$attempt"
            Invoke-ConsumerCheck $projectPath $packageCachePath
            Write-Host "FEED_OK=$PackageId"
            return
        }
        catch {
            $lastError = $_.Exception.Message

            if ($attempt -eq $Attempts) {
                break
            }

            Start-Sleep -Seconds $DelaySeconds
        }
    }

    throw "Package '$PackageId' version '$Version' could not be restored and loaded from the configured package source after $Attempts attempt(s). Last error: $lastError"
}
finally {
    if ($ownsWorkDirectory -and -not $KeepWorkDirectory) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
