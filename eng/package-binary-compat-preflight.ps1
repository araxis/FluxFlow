param(
    [Parameter(Mandatory = $true)]
    [string] $Package,

    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $BaselineVersion = "",

    [string] $PackageSource = "",

    [string] $Configuration = "Release",

    [string] $ManifestPath = "eng/packages.json",

    [switch] $PrepareOnly
)

$ErrorActionPreference = "Stop"

$semverPattern = "^\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$"

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

function Resolve-RepoPath {
    param(
        [string] $Root,
        [string] $Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $Root $Path))
}

function Read-ProjectProperty {
    param(
        [string] $ProjectPath,
        [string] $PropertyName
    )

    $node = Select-Xml -Path $ProjectPath -XPath "/Project/PropertyGroup/$PropertyName" |
        Select-Object -First 1

    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.Node.InnerText)) {
        return ""
    }

    return $node.Node.InnerText.Trim()
}

function Get-TargetFrameworks {
    param([string] $ProjectPath)

    $targetFrameworks = Read-ProjectProperty $ProjectPath "TargetFrameworks"
    if ([string]::IsNullOrWhiteSpace($targetFrameworks)) {
        $targetFrameworks = Read-ProjectProperty $ProjectPath "TargetFramework"
    }

    if ([string]::IsNullOrWhiteSpace($targetFrameworks)) {
        throw "Project '$ProjectPath' does not define TargetFramework or TargetFrameworks."
    }

    return @($targetFrameworks.Split(";", [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Assert-ReleaseBuildOutput {
    param(
        [string] $ProjectPath,
        [string] $ConfigurationName
    )

    $projectDirectory = Split-Path -Parent $ProjectPath
    $assemblyName = Read-ProjectProperty $ProjectPath "AssemblyName"
    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    }

    $targetFrameworks = Get-TargetFrameworks $ProjectPath
    foreach ($targetFramework in $targetFrameworks) {
        $assemblyPath = Join-Path $projectDirectory "bin/$ConfigurationName/$targetFramework/$assemblyName.dll"
        if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
            throw "Release build output '$assemblyPath' was not found. Run the controlled Release build before binary compatibility preflight."
        }
    }
}

function Assert-PackageSource {
    param([string] $Source)

    if ([string]::IsNullOrWhiteSpace($Source)) {
        return
    }

    if ($Source -match "^[a-zA-Z][a-zA-Z0-9+.-]*://") {
        return
    }

    $sourcePath = [System.IO.Path]::GetFullPath($Source)
    if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
        throw "Package source '$sourcePath' must be a directory or package source URL."
    }

    if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
        throw "Package source '$sourcePath' was not found."
    }
}

function Normalize-PackageSource {
    param([string] $Source)

    if ([string]::IsNullOrWhiteSpace($Source)) {
        return ""
    }

    if ($Source -match "^[a-zA-Z][a-zA-Z0-9+.-]*://") {
        return $Source
    }

    return [System.IO.Path]::GetFullPath($Source)
}

function New-BaselineRestoreProject {
    param(
        [string] $PackageId,
        [string] $PackageVersion
    )

    $directory = Join-Path ([System.IO.Path]::GetTempPath()) "fluxflow-binary-compat-restore-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $projectPath = Join-Path $directory "BaselinePackageRestore.csproj"

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$PackageId" Version="$PackageVersion" />
  </ItemGroup>
</Project>
"@ | Out-File -FilePath $projectPath -Encoding utf8

    return $projectPath
}

function Format-CommandArgument {
    param([string] $Argument)

    if ($Argument -notmatch "\s") {
        return $Argument
    }

    return "'" + $Argument.Replace("'", "''") + "'"
}

function Format-CommandLine {
    param(
        [string] $Command,
        [string[]] $Arguments
    )

    return (@($Command) + ($Arguments | ForEach-Object { Format-CommandArgument $_ })) -join " "
}

if ([string]::IsNullOrWhiteSpace($Package)) {
    throw "Package is required."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version is required."
}

if ($Version -notmatch $semverPattern) {
    throw "Invalid package version '$Version'."
}

if ([string]::IsNullOrWhiteSpace($BaselineVersion)) {
    $BaselineVersion = $Version
}

if ($BaselineVersion -notmatch $semverPattern) {
    throw "Invalid baseline package version '$BaselineVersion'."
}

if ($Configuration -notmatch "^[A-Za-z][A-Za-z0-9_-]*$") {
    throw "Configuration '$Configuration' is not supported by this binary compatibility preflight."
}

Assert-PackageSource $PackageSource
$normalizedPackageSource = Normalize-PackageSource $PackageSource

$repoRoot = (Get-Location).Path
$environmentPath = Join-Path ([System.IO.Path]::GetTempPath()) "fluxflow-binary-compat-$([Guid]::NewGuid().ToString('N')).env"
$resolverPath = Join-Path $repoRoot "eng/resolve-package-release.ps1"
$packageOutput = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts/binary-compat"))
$baselineRestoreProject = ""

try {
    $resolveArgs = @{
        Package = $Package
        Version = $Version
        ManifestPath = $ManifestPath
        EnvironmentPath = $environmentPath
    }

    & $resolverPath @resolveArgs 6> $null

    $resolved = Read-KeyValueFile $environmentPath
    $packageAlias = Require-Value $resolved "PACKAGE_ALIAS"
    $packageId = Require-Value $resolved "PACKAGE_ID"
    $packageProject = Require-Value $resolved "PACKAGE_PROJECT"
    $packageVersion = Require-Value $resolved "PACKAGE_VERSION"
    $projectPath = Resolve-RepoPath $repoRoot $packageProject

    $packArguments = @(
        "pack",
        $projectPath,
        "--configuration",
        $Configuration,
        "--no-build",
        "--output",
        $packageOutput,
        "-p:EnablePackageValidation=true",
        "-p:PackageValidationBaselineName=$packageId",
        "-p:PackageValidationBaselineVersion=$BaselineVersion"
    )

    $restoreArguments = @(
        "restore",
        "<baseline-restore-project>"
    )

    if (-not [string]::IsNullOrWhiteSpace($normalizedPackageSource)) {
        $packArguments += "-p:RestoreAdditionalProjectSources=$normalizedPackageSource"
        $restoreArguments += @(
            "--source",
            $normalizedPackageSource,
            "--source",
            "https://api.nuget.org/v3/index.json"
        )
    }

    Write-Host "BINARY_COMPAT_PACKAGE_ALIAS=$packageAlias"
    Write-Host "BINARY_COMPAT_PACKAGE_ID=$packageId"
    Write-Host "BINARY_COMPAT_PACKAGE_PROJECT=$packageProject"
    Write-Host "BINARY_COMPAT_PACKAGE_VERSION=$packageVersion"
    Write-Host "BINARY_COMPAT_BASELINE_VERSION=$BaselineVersion"
    Write-Host "BINARY_COMPAT_PACKAGE_OUTPUT=$packageOutput"
    if (-not [string]::IsNullOrWhiteSpace($normalizedPackageSource)) {
        Write-Host "BINARY_COMPAT_PACKAGE_SOURCE=$normalizedPackageSource"
    }

    Write-Host "BINARY_COMPAT_PACK_COMMAND=$(Format-CommandLine "dotnet" $packArguments)"
    Write-Host "BINARY_COMPAT_BASELINE_RESTORE_COMMAND=$(Format-CommandLine "dotnet" $restoreArguments)"
    Write-Host "BINARY_COMPAT_BASELINE_RESTORE=True"

    if ($PrepareOnly) {
        Write-Host "BINARY_COMPAT_PREPARED=True"
        return
    }

    Assert-ReleaseBuildOutput $projectPath $Configuration

    $baselineRestoreProject = New-BaselineRestoreProject $packageId $BaselineVersion
    $baselineRestoreArguments = @("restore", $baselineRestoreProject)
    if (-not [string]::IsNullOrWhiteSpace($normalizedPackageSource)) {
        $baselineRestoreArguments += @(
            "--source",
            $normalizedPackageSource,
            "--source",
            "https://api.nuget.org/v3/index.json"
        )
    }

    Invoke-Step "dotnet" $baselineRestoreArguments "Baseline package restore failed."

    New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null

    $stalePackagePattern = "^$([regex]::Escape($packageId))\.\d[^/\\]*\.s?nupkg$"
    Get-ChildItem -LiteralPath $packageOutput -File |
        Where-Object { $_.Name -match $stalePackagePattern } |
        Remove-Item -Force

    Invoke-Step "dotnet" $packArguments "Binary compatibility package validation failed."

    Write-Host "BINARY_COMPAT_OK=$packageId"
}
finally {
    Remove-Item -LiteralPath $environmentPath -Force -ErrorAction SilentlyContinue
    if (-not [string]::IsNullOrWhiteSpace($baselineRestoreProject)) {
        $baselineRestoreDirectory = Split-Path -Parent $baselineRestoreProject
        Remove-Item -LiteralPath $baselineRestoreDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
