[CmdletBinding()]
param(
    [string] $WorkDirectory = "",
    [string] $Distribution = "Ubuntu-24.04",
    [string] $LinuxUser = "maf"
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot
if (-not $WorkDirectory) { $WorkDirectory = Join-Path ([IO.Path]::GetTempPath()) ("fluxflow-ros2-consumer-" + [Guid]::NewGuid().ToString('N')) }
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $WorkDirectory) { throw 'Use a fresh consumer directory.' }
$version = '0.0.0-local.' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$feed = Join-Path $WorkDirectory 'feed'
$consumer = Join-Path $WorkDirectory 'consumer'
New-Item -ItemType Directory -Path $feed, $consumer | Out-Null
# The typed host consumes the ROS plugin and its published host contracts from this feed.
$projects = @(
    'src/FluxFlow.Core/FluxFlow.Core.csproj',
    'src/FluxFlow.Application/FluxFlow.Application.csproj',
    'src/FluxFlow.Configuration/FluxFlow.Configuration.csproj',
    'src/Plugins/FluxFlow.Plugins/FluxFlow.Plugins.csproj',
    'src/Plugins/FluxFlow.Plugins.Ros2/FluxFlow.Plugins.Ros2.csproj'
)
foreach ($project in $projects) {
    & dotnet pack (Join-Path $repositoryRoot $project) -c Release -o $feed "-p:PackageVersion=$version" "-p:Version=$version"
    if ($LASTEXITCODE) { throw "Package build failed: $project" }
}
Copy-Item "$PSScriptRoot/ros2-consumer/*" -Destination $consumer
Copy-Item (Join-Path $repositoryRoot 'samples/Studio/Ros2/topic-relay.fluxflow') -Destination $consumer
'<Project />' | Set-Content "$consumer/Directory.Build.props"
'<Project />' | Set-Content "$consumer/Directory.Build.targets"
'<Project />' | Set-Content "$consumer/Directory.Packages.props"
$linuxFeed = (& wsl.exe -d $Distribution -u $LinuxUser --exec wslpath -a $feed).Trim()
if ($LASTEXITCODE) { throw 'Cannot resolve feed path.' }
$linuxConsumer = (& wsl.exe -d $Distribution -u $LinuxUser --exec wslpath -a $consumer).Trim()
if ($LASTEXITCODE) { throw 'Cannot resolve consumer path.' }
$escapedFeed = [System.Security.SecurityElement]::Escape($linuxFeed)
@"
<configuration>
  <packageSources><clear /><add key="candidate" value="$escapedFeed" /><add key="public" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="candidate"><package pattern="FluxFlow.*" /></packageSource><packageSource key="public"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content "$consumer/NuGet.Config"
# Arguments are passed separately; no paths are inserted into shell source.
& wsl.exe -d $Distribution -u $LinuxUser --exec bash "$linuxConsumer/verify.sh" $linuxConsumer $version
if ($LASTEXITCODE) { throw 'ROS 2 package consumer failed.' }
Write-Output "Evidence=$WorkDirectory"
