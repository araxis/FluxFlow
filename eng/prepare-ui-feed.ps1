[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [Parameter(Mandatory)][string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh output directory; existing package feeds are immutable.' }
$entries = @(Get-Content (Join-Path $root 'eng/packages.json') -Raw | ConvertFrom-Json)
if ($entries.packageId -match 'Designer') { throw 'The runtime manifest must not include editor packages.' }
& dotnet build (Join-Path $root 'FluxFlow.sln') -c Release "-p:Version=$Version" "-p:PackageVersion=$Version"
if ($LASTEXITCODE) { throw 'Runtime build failed.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
foreach ($entry in $entries) {
    & dotnet pack (Join-Path $root $entry.project) -c Release --no-build --no-restore "-p:Version=$Version" "-p:PackageVersion=$Version" --output $output
    if ($LASTEXITCODE) { throw "Packing $($entry.packageId) failed." }
}
$packages = @(Get-ChildItem -LiteralPath $output -Filter '*.nupkg')
if ($packages.Count -ne $entries.Count) { throw 'Package count does not match the runtime manifest.' }
Write-Output "Created $($entries.Count) runtime packages at $output. No UI packages, publication or service tests are included."
