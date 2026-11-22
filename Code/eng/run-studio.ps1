[CmdletBinding()]
param(
    [ValidateSet('net8.0', 'net10.0', 'net11.0')][string] $Framework = 'net11.0',
    [string] $Url = 'http://127.0.0.1:5289'
)
$ErrorActionPreference = 'Stop'
$code = Split-Path $PSScriptRoot
$distribution = Join-Path $code "artifacts/studio-distribution/$Framework"
& (Join-Path $PSScriptRoot 'publish-studio.ps1') -Framework $Framework -Output $distribution
& dotnet (Join-Path $distribution 'FluxFlow.StudioWebHost.dll') --urls $Url --webroot (Join-Path $distribution 'site/wwwroot')
if ($LASTEXITCODE) { throw 'Studio host failed.' }
