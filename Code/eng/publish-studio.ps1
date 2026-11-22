[CmdletBinding()]
param(
    [ValidateSet('net8.0','net10.0','net11.0')][string] $Framework = 'net11.0',
    [string] $Output = (Join-Path (Split-Path $PSScriptRoot) "artifacts/studio-distribution/$Framework")
)
$ErrorActionPreference = 'Stop'
$code = Split-Path $PSScriptRoot
$destination = [IO.Path]::GetFullPath($Output)
$bundle = Join-Path $destination 'bundled-plugins'
$hostProject = Join-Path $code 'src/Studio/FluxFlow.StudioWebHost'
$browser = Join-Path $code 'src/Studio/FluxFlow.DesignerApp'
$browserFramework = & dotnet msbuild (Join-Path $browser 'FluxFlow.DesignerApp.csproj') -nologo -getProperty:TargetFramework
if ($LASTEXITCODE -or !$browserFramework) { throw 'Could not determine the Studio browser framework.' }
$browserArtifacts = Join-Path $code "artifacts/studio-browser-build/$($browserFramework.Trim())"
& dotnet publish $hostProject -c Release -f $Framework -o $destination
if ($LASTEXITCODE) { throw 'Studio companion publish failed.' }
# Runtime-loaded plugins can use shared assemblies that the static browser does not call.
# Single-target artifact pivots omit the framework. Isolate WebCIL caches across SDK upgrades.
& dotnet publish $browser -c Release --artifacts-path $browserArtifacts -p:PublishTrimmed=false -o (Join-Path $destination 'site')
if ($LASTEXITCODE) { throw 'Studio browser publish failed.' }
$families = @('Sources','Logging','Routing','Transformation','FlowControl')
foreach ($family in $families) {
    & dotnet pack (Join-Path $code "src/Plugins/FluxFlow.Plugins.$family") -c Release -o $bundle
    if ($LASTEXITCODE) { throw "Could not pack $family." }
}
$packages = @(Get-ChildItem -LiteralPath $bundle -File -Filter '*.nupkg')
if ($packages.Count -ne $families.Count) { throw 'The output contains older or unrelated packages. Publish to an empty distribution directory.' }
