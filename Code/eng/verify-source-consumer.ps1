[CmdletBinding()]
param([string] $WorkDirectory = "")
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot
if (-not $WorkDirectory) { $WorkDirectory = Join-Path ([IO.Path]::GetTempPath()) ("fluxflow-source-consumer-" + [Guid]::NewGuid().ToString('N')) }
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $WorkDirectory) { throw 'Use a fresh consumer directory.' }
$version = '0.0.0-local.' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$feed = Join-Path $WorkDirectory 'feed'
$consumer = Join-Path $WorkDirectory 'consumer'
New-Item -ItemType Directory -Path $feed, $consumer | Out-Null
& dotnet pack "$repositoryRoot/src/FluxFlow.Core/FluxFlow.Core.csproj" -c Release -o $feed "-p:PackageVersion=$version"
if ($LASTEXITCODE) { throw 'Core package build failed.' }
& dotnet pack "$repositoryRoot/src/Plugins/FluxFlow.Plugins.Sources/Runtime/FluxFlow.Components.Source/FluxFlow.Components.Source.csproj" -c Release -o $feed "-p:PackageVersion=$version" "-p:Version=$version"
if ($LASTEXITCODE) { throw 'Source package build failed.' }
Copy-Item -LiteralPath "$PSScriptRoot/source-consumer/SourceConsumer.csproj", "$PSScriptRoot/source-consumer/Program.cs" -Destination $consumer
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
& dotnet restore "$consumer/SourceConsumer.csproj" "-p:FluxFlowCoreVersion=$version" --packages "$WorkDirectory/packages" --configfile "$consumer/NuGet.Config"
if ($LASTEXITCODE) { throw 'Isolated package restore failed.' }
$assets = Get-Content -LiteralPath "$consumer/obj/project.assets.json" -Raw | ConvertFrom-Json
if (@($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'Cronos/*' -or $_ -like 'FluxFlow.Components.Scheduler/*' }).Count) { throw 'Source must not depend on scheduling packages.' }
$flowPackages = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'FluxFlow.*/*' })
if ($flowPackages.Count -ne 2 -or "FluxFlow.Core/$version" -notin $flowPackages -or "FluxFlow.Components.Source/$version" -notin $flowPackages) { throw 'Consumer did not resolve exactly the candidate Core and Source packages.' }
foreach ($framework in @('net8.0','net10.0')) {
    & dotnet run --project "$consumer/SourceConsumer.csproj" -c Release -f $framework --no-restore "-p:FluxFlowCoreVersion=$version"
    if ($LASTEXITCODE) { throw "Source consumer failed on $framework." }
}
Write-Output "SOURCE_PACKAGE_ACCEPTANCE_COMPLETE=True"
Write-Output "Evidence=$WorkDirectory"
