[CmdletBinding()]
param([string] $WorkDirectory = "")
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot
if (-not $WorkDirectory) { $WorkDirectory = Join-Path ([IO.Path]::GetTempPath()) ("fluxflow-configuration-consumer-" + [Guid]::NewGuid().ToString('N')) }
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $WorkDirectory) { throw 'Use a fresh consumer directory.' }
$version = '0.0.0-local.' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$feed = Join-Path $WorkDirectory 'feed'
$consumer = Join-Path $WorkDirectory 'consumer'
New-Item -ItemType Directory -Path $feed, $consumer | Out-Null
& dotnet pack "$repositoryRoot/src/FluxFlow.Core/FluxFlow.Core.csproj" -c Release -o $feed "-p:PackageVersion=$version"
if ($LASTEXITCODE) { throw 'Core package build failed.' }
& dotnet pack "$repositoryRoot/src/Components/FluxFlow.Components.Scheduler/FluxFlow.Components.Scheduler.csproj" -c Release -o $feed "-p:PackageVersion=$version" "-p:Version=$version"
if ($LASTEXITCODE) { throw 'Scheduler package build failed.' }
& dotnet pack "$repositoryRoot/src/Components/FluxFlow.Components.Routing/FluxFlow.Components.Routing.csproj" -c Release -o $feed "-p:PackageVersion=$version" "-p:Version=$version"
if ($LASTEXITCODE) { throw 'Routing package build failed.' }
& dotnet pack "$repositoryRoot/src/Components/FluxFlow.Components.Source/FluxFlow.Components.Source.csproj" -c Release -o $feed "-p:PackageVersion=$version" "-p:Version=$version"
if ($LASTEXITCODE) { throw 'Source package build failed.' }
& dotnet pack "$repositoryRoot/src/Components/FluxFlow.Components.Mapping/FluxFlow.Components.Mapping.csproj" -c Release -o $feed "-p:PackageVersion=$version" "-p:Version=$version"
if ($LASTEXITCODE) { throw 'Mapping package build failed.' }
& dotnet pack "$repositoryRoot/src/Components/FluxFlow.Components.Batch/FluxFlow.Components.Batch.csproj" -c Release -o $feed "-p:PackageVersion=$version" "-p:Version=$version"
if ($LASTEXITCODE) { throw 'Batch package build failed.' }
& dotnet pack "$repositoryRoot/src/FluxFlow.Configuration/FluxFlow.Configuration.csproj" -c Release -o $feed "-p:PackageVersion=$version" "-p:Version=$version"
if ($LASTEXITCODE) { throw "Configuration package build failed." }
foreach ($engine in @('CSharp','Jsonata')) {
    & dotnet pack "$repositoryRoot/src/Expressions/FluxFlow.Expressions.$engine/FluxFlow.Expressions.$engine.csproj" -c Release -o $feed "-p:PackageVersion=$version" "-p:Version=$version"
    if ($LASTEXITCODE) { throw "$engine expression package build failed." }
}
Copy-Item -LiteralPath "$PSScriptRoot/configuration-consumer/ConfigurationConsumer.csproj", "$PSScriptRoot/configuration-consumer/Program.cs" -Destination $consumer
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
& dotnet restore "$consumer/ConfigurationConsumer.csproj" "-p:FluxFlowCoreVersion=$version" --packages "$WorkDirectory/packages" --configfile "$consumer/NuGet.Config"
if ($LASTEXITCODE) { throw 'Isolated package restore failed.' }
$assets = Get-Content -LiteralPath "$consumer/obj/project.assets.json" -Raw | ConvertFrom-Json
$flowPackages = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'FluxFlow.*/*' })
$expectedPackages = @('FluxFlow.Core','FluxFlow.Configuration','FluxFlow.Components.Batch','FluxFlow.Components.Scheduler','FluxFlow.Components.Routing','FluxFlow.Components.Source','FluxFlow.Components.Mapping','FluxFlow.Expressions.CSharp','FluxFlow.Expressions.Jsonata')
if ($flowPackages.Count -ne $expectedPackages.Count) { throw 'Unexpected candidate package count.' }
foreach ($package in $expectedPackages) {
    if ("$package/$version" -notin $flowPackages) { throw "Missing candidate package $package/$version." }
}
foreach ($framework in @('net8.0','net10.0')) {
    & dotnet run --project "$consumer/ConfigurationConsumer.csproj" -c Release -f $framework --no-restore "-p:FluxFlowCoreVersion=$version"
    if ($LASTEXITCODE) { throw "Scheduler consumer failed on $framework." }
}
Write-Output "CONFIGURATION_PACKAGE_ACCEPTANCE_COMPLETE=True"
Write-Output "Evidence=$WorkDirectory"
