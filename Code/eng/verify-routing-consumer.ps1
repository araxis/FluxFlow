[CmdletBinding()]
param([string] $WorkDirectory = "")
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot
if (-not $WorkDirectory) { $WorkDirectory = Join-Path ([IO.Path]::GetTempPath()) ("fluxflow-routing-consumer-" + [Guid]::NewGuid().ToString('N')) }
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $WorkDirectory) { throw 'Use a fresh consumer directory.' }
$version = '0.0.0-local.' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$feed = Join-Path $WorkDirectory 'feed'
$consumer = Join-Path $WorkDirectory 'consumer'
New-Item -ItemType Directory -Path $feed, $consumer | Out-Null
& dotnet pack "$repositoryRoot/src/FluxFlow.Core/FluxFlow.Core.csproj" -c Release -o $feed "-p:PackageVersion=$version"
if ($LASTEXITCODE) { throw 'Core package build failed.' }
& dotnet pack "$repositoryRoot/src/FluxFlow.Application/FluxFlow.Application.csproj" -c Release -o $feed "-p:PackageVersion=$version" "-p:Version=$version"
if ($LASTEXITCODE) { throw 'Application package build failed.' }
& dotnet pack "$repositoryRoot/src/Plugins/FluxFlow.Plugins.Routing/Runtime/FluxFlow.Components.Routing/FluxFlow.Components.Routing.csproj" -c Release -o $feed "-p:PackageVersion=$version" "-p:Version=$version"
if ($LASTEXITCODE) { throw 'Routing package build failed.' }
Copy-Item -LiteralPath "$PSScriptRoot/routing-consumer/RoutingConsumer.csproj", "$PSScriptRoot/routing-consumer/Program.cs" -Destination $consumer
'<Project />' | Set-Content -LiteralPath "$consumer/Directory.Build.props"
'<Project />' | Set-Content -LiteralPath "$consumer/Directory.Build.targets"
'<Project />' | Set-Content -LiteralPath "$consumer/Directory.Packages.props"
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
@"
<configuration>
  <packageSources><clear /><add key="candidate" value="$escapedFeed" /><add key="public" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="candidate"><package pattern="FluxFlow.*" /></packageSource><packageSource key="public"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath "$consumer/NuGet.Config"
& dotnet restore "$consumer/RoutingConsumer.csproj" "-p:FluxFlowCoreVersion=$version" --packages "$WorkDirectory/packages" --configfile "$consumer/NuGet.Config"
if ($LASTEXITCODE) { throw 'Isolated package restore failed.' }
$assets = Get-Content -LiteralPath "$consumer/obj/project.assets.json" -Raw | ConvertFrom-Json
$flowPackages = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'FluxFlow.*/*' })
if ($flowPackages.Count -ne 3 -or "FluxFlow.Application/$version" -notin $flowPackages -or "FluxFlow.Core/$version" -notin $flowPackages -or "FluxFlow.Components.Routing/$version" -notin $flowPackages) { throw 'Consumer did not resolve exactly the candidate Core, Application and Routing packages.' }
foreach ($framework in @('net8.0','net10.0')) {
    & dotnet run --project "$consumer/RoutingConsumer.csproj" -c Release -f $framework --no-restore "-p:FluxFlowCoreVersion=$version"
    if ($LASTEXITCODE) { throw "Routing consumer failed on $framework." }
}
Write-Output "ROUTING_PACKAGE_ACCEPTANCE_COMPLETE=True"
Write-Output "Evidence=$WorkDirectory"
