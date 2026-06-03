param(
    [Parameter(Mandatory = $true)]
    [string] $PackageId,

    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $PackageSource = "artifacts/packages",

    [string] $Framework = "net8.0",

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
    throw "Target framework '$Framework' is not supported by this smoke check."
}

$sourcePath = [System.IO.Path]::GetFullPath($PackageSource)
if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
    throw "Package source '$sourcePath' was not found."
}

$packageFile = Join-Path $sourcePath "$PackageId.$Version.nupkg"
if (-not (Test-Path -LiteralPath $packageFile -PathType Leaf)) {
    throw "Package file '$packageFile' was not found."
}

$ownsWorkDirectory = [string]::IsNullOrWhiteSpace($WorkDirectory)
$workRoot = if ($ownsWorkDirectory) {
    Join-Path ([System.IO.Path]::GetTempPath()) "fluxflow-consumer-smoke-$([Guid]::NewGuid().ToString('N'))"
}
else {
    [System.IO.Path]::GetFullPath($WorkDirectory)
}

New-Item -ItemType Directory -Path $workRoot -Force | Out-Null

$projectPath = Join-Path $workRoot "ConsumerSmoke.csproj"
$programPath = Join-Path $workRoot "Program.cs"
$escapedSourcePath = Escape-Xml $sourcePath
$escapedPackageId = Escape-Xml $PackageId
$escapedVersion = Escape-Xml $Version
$escapedFramework = Escape-Xml $Framework
$csharpPackageId = Escape-CSharpString $PackageId

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$escapedFramework</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RestoreAdditionalProjectSources>$escapedSourcePath</RestoreAdditionalProjectSources>
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
Console.WriteLine($"SMOKE_OK={packageId}");
"@ | Set-Content -LiteralPath $programPath

Write-Host "WORK_DIR=$workRoot"
Write-Host "PACKAGE_FILE=$packageFile"

if ($PrepareOnly) {
    return
}

try {
    Invoke-Step "dotnet" @("restore", $projectPath) "Consumer restore failed."
    Invoke-Step "dotnet" @("build", $projectPath, "--configuration", "Release", "--no-restore") "Consumer build failed."
    Invoke-Step "dotnet" @("run", "--project", $projectPath, "--configuration", "Release", "--no-build") "Consumer run failed."
}
finally {
    if ($ownsWorkDirectory -and -not $KeepWorkDirectory) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
