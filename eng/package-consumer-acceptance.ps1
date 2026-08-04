param(
    [string] $PackageSource = "",

    [switch] $PackPackages,

    [string] $Configuration = "Release",

    [string] $Framework = "net8.0",

    [string] $ManifestPath = "eng/packages.json",

    [string] $FixturePath = "eng/package-consumer-acceptance",

    [string] $PublicPackageSource = "https://api.nuget.org/v3/index.json",

    [string] $WorkDirectory = "",

    [switch] $PrepareOnly
)

$ErrorActionPreference = "Stop"

$semanticVersionPattern = "^\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$"
$requiredAliases = @(
    "nodes",
    "mapping",
    "composition",
    "engine",
    "fluent",
    "engine-durable-input",
    "engine-durable-input-sqlfile",
    "engine-durable-output",
    "engine-durable-output-sqlfile"
)
$topLevelVersionProperties = [ordered]@{
    "engine" = "FluxFlowEngineVersion"
    "fluent" = "FluxFlowFluentVersion"
    "engine-durable-input-sqlfile" = "FluxFlowDurableInputSqlFileVersion"
    "engine-durable-output-sqlfile" = "FluxFlowDurableOutputSqlFileVersion"
}
$requiredMarkers = @(
    "PACKAGE_ACCEPTANCE_ENGINE_OK=True",
    "PACKAGE_ACCEPTANCE_FLUENT_OK=True",
    "PACKAGE_ACCEPTANCE_DURABILITY_OK=True",
    "PACKAGE_ACCEPTANCE_OK=True"
)

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

function Invoke-CapturedStep {
    param(
        [string] $Command,
        [string[]] $Arguments,
        [string] $FailureMessage
    )

    $lines = @(& $Command @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    foreach ($line in $lines) {
        Write-Host $line
    }

    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }

    return $lines
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

function Assert-PackageSource {
    param(
        [string] $Source,
        [string] $Name
    )

    if ([string]::IsNullOrWhiteSpace($Source)) {
        throw "$Name is required."
    }

    if ($Source -match "^[a-zA-Z][a-zA-Z0-9+.-]*://") {
        return
    }

    $sourcePath = [System.IO.Path]::GetFullPath($Source)
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
        throw "$Name '$sourcePath' was not found."
    }
}

function Read-ProjectVersion {
    param([string] $ProjectPath)

    $versionNode = Select-Xml -Path $ProjectPath -XPath "/Project/PropertyGroup/Version" |
        Select-Object -First 1
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.Node.InnerText)) {
        throw "Project '$ProjectPath' does not declare Version."
    }

    $version = $versionNode.Node.InnerText.Trim()
    if ($version -notmatch $semanticVersionPattern) {
        throw "Project '$ProjectPath' has invalid package version '$version'."
    }

    return $version
}

function Resolve-RequiredPackages {
    param(
        [string] $Root,
        [string] $ResolvedManifestPath
    )

    $manifest = Get-Content -LiteralPath $ResolvedManifestPath -Raw | ConvertFrom-Json
    $resolved = @()
    foreach ($alias in $requiredAliases) {
        $matches = @($manifest | Where-Object {
            [string]::Equals($_.alias, $alias, [System.StringComparison]::Ordinal)
        })
        if ($matches.Count -ne 1) {
            throw "Package manifest must contain exactly one '$alias' entry."
        }

        $entry = $matches[0]
        if ([string]::IsNullOrWhiteSpace($entry.packageId) -or
            [string]::IsNullOrWhiteSpace($entry.project)) {
            throw "Package manifest entry '$alias' must define packageId and project."
        }

        $projectPath = Resolve-RepoPath $Root $entry.project
        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
            throw "Package project '$projectPath' was not found."
        }

        $resolved += [pscustomobject]@{
            Alias = $alias
            PackageId = [string] $entry.packageId
            Project = $projectPath
            Version = Read-ProjectVersion $projectPath
        }
    }

    return $resolved
}

function Get-ExactCandidateArchive {
    param(
        [string] $SourcePath,
        [string] $PackageId,
        [string] $Version
    )

    $expectedName = "$PackageId.$Version.nupkg"
    $matches = @(Get-ChildItem -LiteralPath $SourcePath -File -Filter "*.nupkg" |
        Where-Object { [string]::Equals($_.Name, $expectedName, [System.StringComparison]::OrdinalIgnoreCase) })
    if ($matches.Count -ne 1) {
        throw "Candidate source '$SourcePath' must contain exactly one '$expectedName' archive."
    }

    return $matches[0].FullName
}

function Get-Sha256 {
    param([string] $Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([System.BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace("-", "")
        }
        finally {
            $algorithm.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-RestoredFluxFlowPackages {
    param(
        [string] $AssetsPath,
        [string] $SourcePath,
        [string] $PackageCachePath,
        [object[]] $RequiredPackages
    )

    if (-not (Test-Path -LiteralPath $AssetsPath -PathType Leaf)) {
        throw "Consumer restore did not create '$AssetsPath'."
    }

    $assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
    $projectLibraries = @($assets.libraries.PSObject.Properties | Where-Object {
        [string]::Equals($_.Value.type, "project", [System.StringComparison]::OrdinalIgnoreCase)
    })
    if ($projectLibraries.Count -ne 0) {
        throw "Consumer restore resolved a project library instead of package-only dependencies."
    }

    $fluxFlowLibraries = @($assets.libraries.PSObject.Properties | Where-Object {
        $_.Name.StartsWith("FluxFlow.", [System.StringComparison]::Ordinal)
    })
    $resolvedCoordinates = @($fluxFlowLibraries | ForEach-Object { $_.Name } | Sort-Object)
    $requiredCoordinates = @($RequiredPackages | ForEach-Object {
        "$($_.PackageId)/$($_.Version)"
    } | Sort-Object)
    $coordinateDifference = @(Compare-Object $requiredCoordinates $resolvedCoordinates)
    if ($coordinateDifference.Count -ne 0) {
        throw "Restored FluxFlow package closure does not match the explicit candidate closure: $($coordinateDifference -join '; ')."
    }

    foreach ($library in $fluxFlowLibraries) {
        $separator = $library.Name.LastIndexOf("/")
        if ($separator -le 0 -or $separator -eq $library.Name.Length - 1) {
            throw "Restored package coordinate '$($library.Name)' is invalid."
        }

        $packageId = $library.Name.Substring(0, $separator)
        $version = $library.Name.Substring($separator + 1)
        $candidatePath = Get-ExactCandidateArchive $SourcePath $packageId $version
        $cacheRelativePath = [string] $library.Value.path
        if ([string]::IsNullOrWhiteSpace($cacheRelativePath)) {
            throw "Restored package '$($library.Name)' does not declare a package-cache path."
        }

        $cachedArchiveName = "$($packageId.ToLowerInvariant()).$($version.ToLowerInvariant()).nupkg"
        $cachedArchivePath = Join-Path (Join-Path $PackageCachePath $cacheRelativePath) $cachedArchiveName
        if (-not (Test-Path -LiteralPath $cachedArchivePath -PathType Leaf)) {
            throw "Restored package archive '$cachedArchivePath' was not found."
        }

        $candidateHash = Get-Sha256 $candidatePath
        $cachedHash = Get-Sha256 $cachedArchivePath
        if (-not [string]::Equals(
                $candidateHash,
                $cachedHash,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Restored package '$($library.Name)' does not match candidate archive '$candidatePath'."
        }

        Write-Host "PACKAGE_ACCEPTANCE_VERIFIED=$($library.Name)"
    }
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

function Escape-Xml {
    param([string] $Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

if ($Configuration -notmatch "^[A-Za-z][A-Za-z0-9_-]*$") {
    throw "Configuration '$Configuration' is not supported by package-consumer acceptance."
}

if ($Framework -ne "net8.0") {
    throw "Framework '$Framework' is not supported; the acceptance consumer targets net8.0."
}

Assert-PackageSource $PublicPackageSource "Public package source"

$repoRoot = (Get-Location).Path
$resolvedManifestPath = Resolve-RepoPath $repoRoot $ManifestPath
$resolvedFixturePath = Resolve-RepoPath $repoRoot $FixturePath
if (-not (Test-Path -LiteralPath $resolvedManifestPath -PathType Leaf)) {
    throw "Package manifest '$resolvedManifestPath' was not found."
}
if (-not (Test-Path -LiteralPath $resolvedFixturePath -PathType Container)) {
    throw "Consumer fixture '$resolvedFixturePath' was not found."
}

$fixtureProject = Join-Path $resolvedFixturePath "FluxFlow.PackageConsumerAcceptance.csproj"
$fixtureProgram = Join-Path $resolvedFixturePath "Program.cs"
if (-not (Test-Path -LiteralPath $fixtureProject -PathType Leaf) -or
    -not (Test-Path -LiteralPath $fixtureProgram -PathType Leaf)) {
    throw "Consumer fixture must contain FluxFlow.PackageConsumerAcceptance.csproj and Program.cs."
}

[xml] $fixtureXml = Get-Content -LiteralPath $fixtureProject -Raw
if (@($fixtureXml.SelectNodes("//ProjectReference")).Count -ne 0) {
    throw "Consumer fixture cannot contain ProjectReference items."
}

$requiredPackages = @(Resolve-RequiredPackages $repoRoot $resolvedManifestPath)
$ownsPackageSource = $PackPackages -and [string]::IsNullOrWhiteSpace($PackageSource)
if (-not $PackPackages -and [string]::IsNullOrWhiteSpace($PackageSource)) {
    throw "PackageSource is required unless PackPackages is selected."
}

$sourcePath = if ($ownsPackageSource) {
    Join-Path ([System.IO.Path]::GetTempPath()) "fluxflow-package-candidates-$([Guid]::NewGuid().ToString('N'))"
}
else {
    Resolve-RepoPath $repoRoot $PackageSource
}
$ownsWorkDirectory = [string]::IsNullOrWhiteSpace($WorkDirectory)
$workRoot = if ($ownsWorkDirectory) {
    Join-Path ([System.IO.Path]::GetTempPath()) "fluxflow-package-consumer-acceptance-$([Guid]::NewGuid().ToString('N'))"
}
else {
    Resolve-RepoPath $repoRoot $WorkDirectory
}
$consumerProject = Join-Path $workRoot "FluxFlow.PackageConsumerAcceptance.csproj"
$packageCache = Join-Path $workRoot "packages"
$nugetConfig = Join-Path $workRoot "NuGet.config"

$versionArguments = @()
foreach ($entry in $topLevelVersionProperties.GetEnumerator()) {
    $package = @($requiredPackages | Where-Object {
        [string]::Equals($_.Alias, $entry.Key, [System.StringComparison]::Ordinal)
    })[0]
    $versionArguments += "-p:$($entry.Value)=$($package.Version)"
}

$restoreArguments = @(
    "restore",
    $consumerProject,
    "--no-cache",
    "--packages",
    $packageCache,
    "--configfile",
    $nugetConfig
) + $versionArguments
$buildArguments = @(
    "build",
    $consumerProject,
    "--configuration",
    $Configuration,
    "--framework",
    $Framework,
    "--no-restore"
) + $versionArguments
$runArguments = @(
    "run",
    "--project",
    $consumerProject,
    "--configuration",
    $Configuration,
    "--framework",
    $Framework,
    "--no-build",
    "--no-restore"
) + $versionArguments

Write-Host "PACKAGE_ACCEPTANCE_PACKAGE_SOURCE=$sourcePath"
Write-Host "PACKAGE_ACCEPTANCE_WORK_DIR=$workRoot"
Write-Host "PACKAGE_ACCEPTANCE_PACKAGE_CACHE=$packageCache"
Write-Host "PACKAGE_ACCEPTANCE_PACK_PACKAGES=$($PackPackages.IsPresent)"
foreach ($package in $requiredPackages) {
    Write-Host "PACKAGE_ACCEPTANCE_CANDIDATE=$($package.Alias)|$($package.PackageId)|$($package.Version)"
}
Write-Host "PACKAGE_ACCEPTANCE_RESTORE_COMMAND=$(Format-CommandLine 'dotnet' $restoreArguments)"
Write-Host "PACKAGE_ACCEPTANCE_BUILD_COMMAND=$(Format-CommandLine 'dotnet' $buildArguments)"
Write-Host "PACKAGE_ACCEPTANCE_RUN_COMMAND=$(Format-CommandLine 'dotnet' $runArguments)"

if ($PrepareOnly) {
    Write-Host "PACKAGE_ACCEPTANCE_PREPARED=True"
    return
}

try {
    if ($PackPackages) {
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            throw "Package source '$sourcePath' must be a directory."
        }
        New-Item -ItemType Directory -Path $sourcePath -Force | Out-Null

        foreach ($package in $requiredPackages) {
            $packArguments = @(
                "pack",
                $package.Project,
                "--configuration",
                $Configuration,
                "--no-build",
                "--no-restore",
                "--output",
                $sourcePath
            )
            Write-Host "PACKAGE_ACCEPTANCE_PACK_COMMAND=$(Format-CommandLine 'dotnet' $packArguments)"
            Invoke-Step "dotnet" $packArguments "Candidate package creation failed for '$($package.PackageId)'."
        }
    }
    elseif (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
        throw "Package source '$sourcePath' was not found."
    }

    foreach ($package in $requiredPackages) {
        $candidatePath = Get-ExactCandidateArchive $sourcePath $package.PackageId $package.Version
        Write-Host "PACKAGE_ACCEPTANCE_ARCHIVE=$candidatePath"
    }

    if (Test-Path -LiteralPath $workRoot) {
        if ($ownsWorkDirectory) {
            throw "Owned work directory '$workRoot' already exists."
        }

        if ($null -ne (Get-ChildItem -LiteralPath $workRoot -Force | Select-Object -First 1)) {
            throw "Caller-owned work directory '$workRoot' must be empty."
        }
    }
    else {
        New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
    }

    Copy-Item -LiteralPath $fixtureProject -Destination $consumerProject
    Copy-Item -LiteralPath $fixtureProgram -Destination (Join-Path $workRoot "Program.cs")

    $escapedCandidateSource = Escape-Xml $sourcePath
    $escapedPublicSource = Escape-Xml $PublicPackageSource
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$escapedCandidateSource" />
    <add key="public" value="$escapedPublicSource" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfig -Encoding utf8

    Invoke-Step "dotnet" $restoreArguments "Package-consumer restore failed."
    Assert-RestoredFluxFlowPackages `
        (Join-Path $workRoot "obj/project.assets.json") `
        $sourcePath `
        $packageCache `
        $requiredPackages
    Invoke-Step "dotnet" $buildArguments "Package-consumer build failed."
    $runOutput = @(Invoke-CapturedStep "dotnet" $runArguments "Package-consumer execution failed.")

    foreach ($marker in $requiredMarkers) {
        $count = @($runOutput | Where-Object {
            [string]::Equals($_, $marker, [System.StringComparison]::Ordinal)
        }).Count
        if ($count -ne 1) {
            throw "Package consumer must emit '$marker' exactly once; observed $count."
        }
    }

    Write-Host "PACKAGE_ACCEPTANCE_COMPLETE=True"
}
finally {
    if ($ownsWorkDirectory) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($ownsPackageSource) {
        Remove-Item -LiteralPath $sourcePath -Recurse -Force -ErrorAction SilentlyContinue
    }
}
