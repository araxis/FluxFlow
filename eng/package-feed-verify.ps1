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
