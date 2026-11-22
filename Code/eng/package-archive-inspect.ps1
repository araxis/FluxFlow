param(
    [Parameter(Mandatory = $true)]
    [string] $PackageId,

    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $PackageSource = "artifacts/packages",

    [string[]] $Frameworks = @("net8.0", "net10.0"),

    [string] $ReadmeFile = "README.md"
)

$ErrorActionPreference = "Stop"

$semver = "^\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$"
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-Entry {
    param(
        [string[]] $Entries,
        [string] $Expected,
        [string] $ArchivePath
    )

    if ($Entries -notcontains $Expected) {
        throw "Archive '$ArchivePath' is missing entry '$Expected'."
    }
}

function Get-ArchiveEntries {
    param([string] $ArchivePath)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        return @($archive.Entries | ForEach-Object { $_.FullName })
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ArchiveText {
    param(
        [string] $ArchivePath,
        [string] $EntryName
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry) {
            throw "Archive '$ArchivePath' is missing entry '$EntryName'."
        }

        $reader = [System.IO.StreamReader]::new($entry.Open())
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Read-NuspecValue {
    param(
        [xml] $Nuspec,
        [string] $Name
    )

    $node = $Nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='$Name']")
    if ($null -eq $node) {
        return ""
    }

    return $node.InnerText.Trim()
}

function Assert-NuspecIdentity {
    param(
        [string] $ArchivePath,
        [string] $NuspecEntry,
        [bool] $ExpectReadme,
        [bool] $ExpectSymbolsPackageType
    )

    [xml] $nuspec = Get-ArchiveText $ArchivePath $NuspecEntry

    $id = Read-NuspecValue $nuspec "id"
    if ($id -ne $PackageId) {
        throw "Archive '$ArchivePath' nuspec id '$id' does not match '$PackageId'."
    }

    $packageVersion = Read-NuspecValue $nuspec "version"
    if ($packageVersion -ne $Version) {
        throw "Archive '$ArchivePath' nuspec version '$packageVersion' does not match '$Version'."
    }

    if ($ExpectReadme) {
        $readme = Read-NuspecValue $nuspec "readme"
        if ($readme -ne $ReadmeFile) {
            throw "Archive '$ArchivePath' nuspec readme '$readme' does not match '$ReadmeFile'."
        }
    }

    if ($ExpectSymbolsPackageType) {
        $node = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='packageTypes']/*[local-name()='packageType' and @name='SymbolsPackage']")
        if ($null -eq $node) {
            throw "Archive '$ArchivePath' does not declare SymbolsPackage package type."
        }
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

if ($Frameworks.Count -eq 0) {
    throw "At least one target framework is required."
}

foreach ($framework in $Frameworks) {
    if ($framework -notmatch "^net\d+\.\d+$") {
        throw "Target framework '$framework' is not supported by this archive inspection."
    }
}

$sourcePath = [System.IO.Path]::GetFullPath($PackageSource)
if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
    throw "Package source '$sourcePath' was not found."
}

$packageFile = Join-Path $sourcePath "$PackageId.$Version.nupkg"
$symbolFile = Join-Path $sourcePath "$PackageId.$Version.snupkg"

if (-not (Test-Path -LiteralPath $packageFile -PathType Leaf)) {
    throw "Package file '$packageFile' was not found."
}

if (-not (Test-Path -LiteralPath $symbolFile -PathType Leaf)) {
    throw "Symbol package file '$symbolFile' was not found."
}

$nuspecEntry = "$PackageId.nuspec"
$packageEntries = Get-ArchiveEntries $packageFile
$symbolEntries = Get-ArchiveEntries $symbolFile

Assert-Entry $packageEntries $nuspecEntry $packageFile
Assert-Entry $packageEntries $ReadmeFile $packageFile
Assert-Entry $symbolEntries $nuspecEntry $symbolFile

foreach ($framework in $Frameworks) {
    Assert-Entry $packageEntries "lib/$framework/$PackageId.dll" $packageFile
    Assert-Entry $symbolEntries "lib/$framework/$PackageId.pdb" $symbolFile
}

Assert-NuspecIdentity $packageFile $nuspecEntry $true $false
Assert-NuspecIdentity $symbolFile $nuspecEntry $false $true

Write-Host "ARCHIVE_OK=$PackageId"
Write-Host "PACKAGE_FILE=$packageFile"
Write-Host "SYMBOL_FILE=$symbolFile"
