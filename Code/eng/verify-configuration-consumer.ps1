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
# Pack the same family artifacts the consumer references, plus their shared dependencies.
$projects = @(
    'FluxFlow.Core','FluxFlow.Application','FluxFlow.Configuration',
    'Environments/FluxFlow.Environments','Plugins/FluxFlow.Plugins',
    'Plugins/FluxFlow.Plugins.Mqtt/Settings',
    'Plugins/FluxFlow.Plugins.Mqtt/Runtime',
    'Expressions/FluxFlow.Expressions.CSharp','Expressions/FluxFlow.Expressions.Jsonata'
)
foreach ($family in @('Routing','FlowControl','Transformation','Logging','Sources','Http')) {
    $projects += "Plugins/FluxFlow.Plugins.$family"
}
foreach ($project in $projects) {
    & dotnet pack "$repositoryRoot/src/$project" -c Release --artifacts-path "$WorkDirectory/build" -o $feed "-p:PackageVersion=$version" "-p:Version=$version"
    if ($LASTEXITCODE) { throw "$project package build failed." }
}
Copy-Item -LiteralPath "$PSScriptRoot/configuration-consumer/ConfigurationConsumer.csproj", "$PSScriptRoot/configuration-consumer/Program.cs", "$PSScriptRoot/configuration-consumer/TelemetryCheck.cs" -Destination $consumer
Copy-Item -LiteralPath "$repositoryRoot/samples/Hosting/FluxFlow.Hosting.Telemetry.Sample/appsettings.json" -Destination "$consumer/TelemetrySample.json"
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
$expectedPackages = @(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' | ForEach-Object { $_.Name.Substring(0, $_.Name.Length - ".$version.nupkg".Length) })
if ($flowPackages.Count -ne $expectedPackages.Count) { throw 'Unexpected candidate package count.' }
foreach ($package in $expectedPackages) {
    if ("$package/$version" -notin $flowPackages) { throw "Missing candidate package $package/$version." }
}
foreach ($framework in @('net8.0','net10.0')) {
    & dotnet run --project "$consumer/ConfigurationConsumer.csproj" -c Release -f $framework --no-restore "-p:FluxFlowCoreVersion=$version"
    if ($LASTEXITCODE) { throw "Configuration consumer failed on $framework." }
}
Write-Output "CONFIGURATION_PACKAGE_ACCEPTANCE_COMPLETE=True"
Write-Output "Evidence=$WorkDirectory"
