$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$attempt = "observability-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
$version = "0.0.0-local.$attempt"
$work = Join-Path $repo "artifacts/$attempt"
$feed = Join-Path $work 'feed'
$cache = Join-Path $work 'cache'
$config = Join-Path $work 'NuGet.Config'
$consumer = Join-Path $PSScriptRoot 'observability-consumer/ObservabilityConsumer.csproj'
New-Item -ItemType Directory -Path $feed -Force | Out-Null

function Invoke-DotNet {
    param([string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

# Unique versions and an isolated cache prevent older local packages from passing this check.
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="FluxFlow.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
'@ | Set-Content -LiteralPath $config

$projects = @(
    'src/Core/FluxFlow.Core/FluxFlow.Core.csproj',
    'src/Adapters/Observability/FluxFlow.Observability/FluxFlow.Observability.csproj',
    'src/Adapters/Observability/FluxFlow.Observability.OpenTelemetry/FluxFlow.Observability.OpenTelemetry.csproj',
    'src/Adapters/Observability/FluxFlow.Observability.AspNetCore/FluxFlow.Observability.AspNetCore.csproj'
)
foreach ($project in $projects) {
    Invoke-DotNet @('pack', (Join-Path $repo $project), '-c', 'Release', '-o', $feed, "-p:Version=$version", '--nologo', '-v', 'quiet')
}
Invoke-DotNet @('restore', $consumer, "-p:CandidateVersion=$version", '--configfile', $config, '--packages', $cache, '--no-cache', '--nologo')
foreach ($framework in @('net8.0', 'net10.0')) {
    Invoke-DotNet @('run', '--project', $consumer, '-c', 'Release', '-f', $framework, '--no-restore', "-p:CandidateVersion=$version")
}
@{
    Candidate = $version
    CompletedUtc = [DateTime]::UtcNow.ToString('O')
    Frameworks = @('net8.0', 'net10.0')
    Result = 'Passed'
    Scope = 'Package-only telemetry, processing error recovery, and net10 SSE endpoint registration/startup; no external collector or publication.'
    Packages = @(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' | Get-FileHash -Algorithm SHA256 | Select-Object Path, Hash)
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $work 'verification.json')
Write-Host "Verification: $work/verification.json"
