param(
    [string] $Package = "",

    [string] $ManifestPath = "eng/packages.json",

    [switch] $AsJson
)

$ErrorActionPreference = "Stop"

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

$selectedPackages = $packages
if (-not [string]::IsNullOrWhiteSpace($Package)) {
    $resolvedPackage = Find-Package $Package
    if ($null -eq $resolvedPackage) {
        throw "Package '$Package' does not match an entry in '$ManifestPath'."
    }

    $selectedPackages = @($resolvedPackage)
}

$rows = foreach ($entry in $selectedPackages) {
    $version = Read-ProjectVersion $entry.project
    [pscustomobject]@{
        alias = $entry.alias
        version = $version
        tag = "$($entry.tagPrefix)-v$version"
        packageId = $entry.packageId
        project = $entry.project
    }
}

if ($AsJson) {
    $rows | ConvertTo-Json -Depth 4
    return
}

Write-Host "PACKAGE_COUNT=$($rows.Count)"
Write-Host "ALIAS`tVERSION`tTAG`tPACKAGE_ID`tPROJECT"
foreach ($row in $rows) {
    Write-Host "$($row.alias)`t$($row.version)`t$($row.tag)`t$($row.packageId)`t$($row.project)"
}
