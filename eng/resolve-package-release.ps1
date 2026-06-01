param(
    [string] $Package = "",

    [string] $Version = "",

    [string] $RefName = "",

    [string] $ManifestPath = "eng/packages.json",

    [string] $EnvironmentPath = ""
)

$ErrorActionPreference = "Stop"

$semverPattern = "\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?"
$semver = "^$semverPattern$"

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw "Package manifest '$ManifestPath' was not found."
}

$packages = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$packages = @($packages)
if ($packages.Count -eq 0) {
    throw "Package manifest '$ManifestPath' is empty."
}

function Find-Package {
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $normalized = $Value.Trim()
    foreach ($candidate in $packages) {
        if ($candidate.alias -ieq $normalized -or
            $candidate.packageId -ieq $normalized -or
            $candidate.tagPrefix -ieq $normalized) {
            return $candidate
        }
    }

    return $null
}

function Read-ProjectVersion {
    param([string] $ProjectPath)

    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        throw "Project '$ProjectPath' was not found."
    }

    $versionNode = Select-Xml -Path $ProjectPath -XPath "/Project/PropertyGroup/Version" |
        Select-Object -First 1

    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.Node.InnerText)) {
        throw "Project '$ProjectPath' does not define a Version property."
    }

    return $versionNode.Node.InnerText.Trim()
}

$resolvedPackage = Find-Package $Package
$resolvedVersion = $Version.Trim()
$tagVersion = ""

if (-not [string]::IsNullOrWhiteSpace($RefName)) {
    $trimmedRef = $RefName.Trim()

    if ($trimmedRef -match "^(?<prefix>.+)-v(?<version>$semverPattern)$") {
        $tagPackage = Find-Package $Matches.prefix
        if ($null -eq $tagPackage) {
            throw "Tag prefix '$($Matches.prefix)' does not match a package in '$ManifestPath'."
        }

        if ($null -ne $resolvedPackage -and $resolvedPackage.packageId -ne $tagPackage.packageId) {
            throw "Input package '$($resolvedPackage.packageId)' does not match tag package '$($tagPackage.packageId)'."
        }

        $resolvedPackage = $tagPackage
        $tagVersion = $Matches.version
    }
}

if ($null -eq $resolvedPackage) {
    throw "Package was not provided and could not be resolved from ref '$RefName'."
}

$projectVersion = Read-ProjectVersion $resolvedPackage.project

if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
    $resolvedVersion = if ([string]::IsNullOrWhiteSpace($tagVersion)) { $projectVersion } else { $tagVersion }
}

if ($resolvedVersion -notmatch $semver) {
    throw "Invalid package version '$resolvedVersion'."
}

if ($resolvedVersion -ne $projectVersion) {
    throw "Version '$resolvedVersion' does not match project version '$projectVersion' for '$($resolvedPackage.packageId)'."
}

$isPrerelease = $resolvedVersion.Contains("-")
$releaseTag = "$($resolvedPackage.tagPrefix)-v$resolvedVersion"

$values = [ordered]@{
    PACKAGE_ALIAS = $resolvedPackage.alias
    PACKAGE_ID = $resolvedPackage.packageId
    PACKAGE_PROJECT = $resolvedPackage.project
    PACKAGE_VERSION = $resolvedVersion
    RELEASE_TAG = $releaseTag
    IS_PRERELEASE = $isPrerelease
}

if (-not [string]::IsNullOrWhiteSpace($EnvironmentPath)) {
    foreach ($entry in $values.GetEnumerator()) {
        "$($entry.Key)=$($entry.Value)" | Out-File -FilePath $EnvironmentPath -Append
    }
}

foreach ($entry in $values.GetEnumerator()) {
    Write-Host "$($entry.Key)=$($entry.Value)"
}
