#requires -Version 7.0
[CmdletBinding()]
param(
    # Optional, complete set of previously released packages (including the five basic families and Serial).
    [string] $CompatibilityPackages,
    [string] $ArtifactsRoot = (Join-Path ([IO.Path]::GetTempPath()) 'fluxflow-release-checks')
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'This release check includes Windows desktop tests. Run it on Windows.' }
$code = Split-Path $PSScriptRoot
if ($CompatibilityPackages) { $CompatibilityPackages = [IO.Path]::GetFullPath($CompatibilityPackages) }
$work = Join-Path ([IO.Path]::GetFullPath($ArtifactsRoot)) ([guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($work)
$results = Join-Path $work 'results'
[void][IO.Directory]::CreateDirectory($results)
$steps = [Collections.Generic.List[object]]::new()
$report = [ordered]@{ Status = 'Running'; Framework = 'net11.0'; Steps = $steps; DesktopUI = 'Manual checks required' }
Write-Host "Release check artifacts: $work"

function Step([string] $name, [scriptblock] $action) {
    Write-Host "Checking $name..."
    try {
        & $action *>&1 | Tee-Object -FilePath (Join-Path $work "$name.log") | Out-Host
        $steps.Add(@{ Name = $name; Status = 'Passed' })
    } catch {
        $failure = $_
        $details = "$($failure.Exception.ToString())`n$($failure.ScriptStackTrace)"
        $steps.Add(@{ Name = $name; Status = 'Failed'; Error = $failure.Exception.Message; ErrorDetails = $details })
        try { Add-Content -LiteralPath (Join-Path $work "$name.log") -Value $details }
        catch { throw [AggregateException]::new('Release step and diagnostic logging failed.', @($failure.Exception, $_.Exception)) }
        throw
    }
}
function Invoke-Dotnet([string[]] $arguments) {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($arguments[0]) failed with exit code $LASTEXITCODE." }
}
function Test([string] $name, [string] $project, [string] $framework, [string] $filter = '') {
    $arguments = @('test', (Join-Path $code $project), '-c', 'Release', '-f', $framework,
        '--results-directory', $results, '--logger', "trx;LogFileName=$name.trx",
        '--blame-hang-timeout', '2m')
    if ($filter) { $arguments += @('--filter', $filter) }
    Invoke-Dotnet $arguments
    [xml] $trx = Get-Content -LiteralPath (Join-Path $results "$name.trx") -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    if ([int]$counts.total -lt 1 -or [int]$counts.passed -ne [int]$counts.total) {
        throw "$name did not execute and pass every selected test. See the TRX report."
    }
    Write-Output "$name passed $($counts.passed) tests."
}

$failure = $null
$reportFailure = $null
Push-Location $code
try {
    if ($CompatibilityPackages -and !(Test-Path -LiteralPath $CompatibilityPackages -PathType Container)) {
        throw 'CompatibilityPackages must be an existing folder of plugin packages.'
    }
    Step 'toolchain' { Invoke-Dotnet @('--info') }
    Step 'release-script-tests' { Test 'release-script' 'tests/Unit/FluxFlow.Release.Tests' 'net10.0' 'FullyQualifiedName~StudioReleaseCheckTests' }
    Step 'studio-tests' { Test 'studio' 'tests/Unit/FluxFlow.Studio.Tests' 'net11.0' }
    Step 'desktop-tests' { Test 'desktop' 'tests/Unit/FluxFlow.Studio.Desktop.Tests' 'net11.0-windows10.0.17763.0' }
    Step 'plugin-lifecycle-tests' {
        $classes = @('InstalledPluginTests','MqttPluginTests','RabbitPluginTests','BrokerEnvironmentPluginTests',
            'CompanionTests','ExecutionLogTests','ExecutionTraceTests','HttpClientRunTests')
        $filter = ($classes | ForEach-Object { "FullyQualifiedName~FluxFlow.Environments.Tests.$_." }) -join '|'
        Test 'plugin-lifecycle' 'tests/Integration/FluxFlow.Environments.Tests' 'net11.0' $filter
    }
    $web = Join-Path $work 'web'
    Step 'publish-web' { & (Join-Path $PSScriptRoot 'publish-studio.ps1') -Framework net11.0 -Output $web }
    foreach ($case in 'inventory', 'orders') {
        Step "compiled-$case" {
            $kitArgs = @()
            if ($case -eq 'orders') { $kitArgs = @('--build-kit-output', (Join-Path $work 'build-kit')) }
            & python -X utf8 -u (Join-Path $PSScriptRoot 'test-studio-compiled-app.py') $web --case $case @kitArgs
            if ($LASTEXITCODE -ne 0) { throw "Compiled application $case check failed with exit code $LASTEXITCODE." }
        }
    }
    Step 'publish-desktop' {
        Invoke-Dotnet @('publish', 'src/Studio/FluxFlow.Studio.Desktop', '-c', 'Release', '-f',
            'net11.0-windows10.0.17763.0', '-o', (Join-Path $work 'desktop'))
    }
    $packages = Join-Path $web 'bundled-plugins'
    if ($CompatibilityPackages) {
        $packages = (Get-Item -LiteralPath $CompatibilityPackages).FullName
    } else {
        Step 'pack-serial' { Invoke-Dotnet @('pack', 'src/Plugins/FluxFlow.Plugins.Serial', '-c', 'Release', '-o', $packages) }
    }
    $store = Join-Path $work 'plugin-store'
    Step 'install-plugins' {
        & (Join-Path $PSScriptRoot 'fluxflow-plugin.ps1') Initialize -Source $packages -Directory $store
        foreach ($family in 'Sources','Logging','Routing','Transformation','FlowControl','Serial') {
            if (!(Test-Path -LiteralPath (Join-Path $store "active/FluxFlow.Plugins.$family/fluxflow-plugin.json"))) {
                throw "Package set must include $family."
            }
        }
        $report['PackageInput'] = $packages
        $report['Packages'] = @(Get-ChildItem -LiteralPath $packages -Filter '*.nupkg' -File | ForEach-Object {
            @{ Name = $_.Name; SHA256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
        })
    }
    Step 'plugin-sources' {
        # Use the installed folder so the existing test also exercises the exact compatibility payload.
        $serial = Join-Path $store 'active/FluxFlow.Plugins.Serial'
        $archive = Join-Path $work 'serial-source-test.nupkg'
        [IO.Compression.ZipFile]::CreateFromDirectory($serial, $archive)
        & (Join-Path $PSScriptRoot 'test-plugin-sources.ps1') -Package $archive
    }
    Step 'published-lifecycle' {
        & (Join-Path $PSScriptRoot 'check-published-studio.ps1') -Distribution $web -PluginStore $store -Output $work
    }
    $report.Status = 'Passed'
} catch {
    $failure = $_
    $report.Status = 'Failed'
    $report['Error'] = $_.Exception.Message
    $report['ErrorDetails'] = "$($_.Exception.ToString())`n$($_.ScriptStackTrace)"
} finally {
    try { $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $work 'summary.json') }
    catch { $reportFailure = $_ }
    finally {
        Pop-Location
        Write-Host "Results and publications retained at: $work"
    }
}
if ($failure -and $reportFailure) {
    throw [AggregateException]::new('Release check and summary reporting failed.', @($failure.Exception, $reportFailure.Exception))
}
if ($failure) { $PSCmdlet.ThrowTerminatingError($failure) }
if ($reportFailure) { $PSCmdlet.ThrowTerminatingError($reportFailure) }
Write-Host 'Automated release check passed. Desktop UI checks remain manual; see docs/studio-release-check.md.'
