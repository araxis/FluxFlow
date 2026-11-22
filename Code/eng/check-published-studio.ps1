# Published-host portion of check-studio-release.ps1. Uses only the existing companion API.
#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Distribution,
    [Parameter(Mandatory)][string] $PluginStore,
    [Parameter(Mandatory)][string] $Output
)
$ErrorActionPreference = 'Stop'
$distributionPath = (Get-Item -LiteralPath $Distribution).FullName
$store = (Get-Item -LiteralPath $PluginStore).FullName
$outputPath = (Get-Item -LiteralPath $Output).FullName
$previousStore = $env:FLUXFLOW_PLUGIN_DIRECTORY
$process = $null
$runs = [Collections.Generic.List[string]]::new()
$base = $null
$failure = $null
$cleanupErrors = [Collections.Generic.List[Exception]]::new()
function Json($value) { ConvertTo-Json -InputObject $value -Depth 100 -Compress }
function Api([string] $path, [string] $method = 'GET', $body = $null) {
    $parameters = @{ Uri = "$base/api/runs$path"; Method = $method; Headers = @{ 'X-FluxFlow-Run' = '1' }; TimeoutSec = 15 }
    if ($null -ne $body) { $parameters.Body = Json $body; $parameters.ContentType = 'application/json' }
    Invoke-RestMethod @parameters
}
function Save-Logs([string] $run, [string] $path) {
    Invoke-WebRequest -Uri "$base/api/runs$run/telemetry/logs" -Headers @{ 'X-FluxFlow-Run' = '1' } -OutFile $path -TimeoutSec 15
    (Get-FileHash -LiteralPath $path).Hash
}
try {
    $env:FLUXFLOW_PLUGIN_DIRECTORY = $store
    $stdout = Join-Path $outputPath 'companion.log'
    $stderr = Join-Path $outputPath 'companion-errors.log'
    $process = Start-Process dotnet -ArgumentList @(
        ('"' + (Join-Path $distributionPath 'FluxFlow.StudioWebHost.dll') + '"'),
        '--urls', 'http://127.0.0.1:0', '--webroot', ('"' + (Join-Path $distributionPath 'site/wwwroot') + '"')) `
        -WorkingDirectory $distributionPath -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        if ($process.HasExited) { throw 'Published companion exited before readiness. See companion logs.' }
        $log = Get-Content -LiteralPath $stdout -Raw
        if ($log -match 'Now listening on: (http://127\.0\.0\.1:\d+)') { $base = $Matches[1]; break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$base) { throw 'Published companion did not become ready within 45 seconds.' }

    $catalog = Api '/plugins'
    $installed = @(Get-ChildItem -LiteralPath (Join-Path $store 'active') -Directory)
    $expected = @($installed | ForEach-Object { (Get-Content -LiteralPath (Join-Path $_.FullName 'fluxflow-plugin.json') -Raw | ConvertFrom-Json).id } | Sort-Object)
    if ((Json @($catalog.Id | Sort-Object)) -cne (Json $expected)) { throw 'Published plugin catalog differs from the installed package set.' }
    $catalog | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $outputPath 'plugin-catalog.json')
    $download = Join-Path $outputPath 'asset-check.bin'
    foreach ($plugin in $catalog) {
        if (!$plugin.Design) { continue }
        foreach ($assembly in @($plugin.Design.Assembly) + @($plugin.Design.Dependencies)) {
            $original = Join-Path $store "active/$($plugin.Id)/lib/net10.0/$assembly.dll"
            if (!(Test-Path -LiteralPath $original)) { throw "Missing .NET 10 compatibility asset: $original" }
            $uri = "$base/api/runs/plugins/$($plugin.Id)/studio?dependency=$([uri]::EscapeDataString($assembly))"
            Invoke-WebRequest -Uri $uri -Headers @{ 'X-FluxFlow-Run' = '1' } -OutFile $download -TimeoutSec 15
            if ((Get-FileHash -LiteralPath $download).Hash -ne (Get-FileHash -LiteralPath $original).Hash) {
                throw "Published design asset changed: $assembly"
            }
        }
    }
    $wwwroot = Join-Path $distributionPath 'site/wwwroot'
    $assets = @((Get-Item -LiteralPath (Join-Path $wwwroot 'index.html')))
    $canvas = @(Get-ChildItem -LiteralPath (Join-Path $wwwroot '_content/Z.Blazor.Diagrams') -File | Where-Object Extension -In '.js','.css')
    if (!$canvas.Count) { throw 'Published canvas assets are missing.' }
    foreach ($asset in $assets + $canvas) {
        $relative = [IO.Path]::GetRelativePath($wwwroot, $asset.FullName).Replace('\','/')
        Invoke-WebRequest -Uri "$base/$relative" -OutFile $download -TimeoutSec 15
        if ((Get-FileHash -LiteralPath $download).Hash -ne (Get-FileHash -LiteralPath $asset.FullName).Hash) { throw "Published asset differs: $relative" }
    }

    $configuration = @{ Workflows = @{ main = @{
        tick = @{ Type = 'scheduler'; Interval = '00:00:00.100'; Output = 'gate.Input' }
        gate = @{ Type = 'gate'; InitiallyOpen = $true; Output = 'format.Input' }
        format = @{ Type = 'mapper'; Engine = 'jsonata'; Expression = '{"MessageTemplate":"Release check","LogLevel":"Information"}'; Output = 'record.Input' }
        record = @{ Type = 'logger' }
    } } }
    $request = @{ Configuration = (Json $configuration); Environment = @{ Resources = @{} }; Instances = @() }
    @{ Format = 'FluxFlow.Studio'; Version = 1; ApplicationSection = ''; ActiveWorkflow = 'main'; Configuration = $configuration;
       Environment = $request.Environment; PortColors = @{}; Links = @{};
       Nodes = @{ 'main.tick' = @{ X = 40; Y = 80 }; 'main.gate' = @{ X = 320; Y = 80 }; 'main.format' = @{ X = 600; Y = 80 }; 'main.record' = @{ X = 880; Y = 80 } }
    } | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $outputPath 'desktop-trial.fluxflow')
    $check = Api '/check' 'POST' $request
    if (!$check.Success) { throw "Environment check failed: $(Json $check)" }
    $previous = $null
    foreach ($attempt in 1..2) {
        $run = '/' + [guid]::NewGuid().ToString()
        $runs.Add($run)
        $started = Api $run 'POST' $request
        if ($started.State -ne 'Running') { throw "Run failed: $(Json $started)" }
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            $status = Api $run
            if ($status.State -ne 'Running') { throw "Run stopped unexpectedly: $(Json $status)" }
            $outputs = @($status.Outputs.Records | Where-Object Port -EQ 'main.gate.Output')
            if ($outputs.Count -ge 2) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($outputs.Count -lt 2) { throw 'Timed out waiting for workflow output.' }
        Api $run 'DELETE' | Out-Null
        $stopped = Api $run
        if ($stopped.State -ne 'Stopped') { throw "Cleanup failed: $(Json $stopped)" }
        $retained = Json $stopped
        $retained | Set-Content -LiteralPath (Join-Path $outputPath "run-$attempt.json")
        $logFile = Join-Path $outputPath "run-$attempt-logs.pb"
        $logHash = Save-Logs $run $logFile
        if (![Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($logFile)).Contains('Release check')) {
            throw 'Original logger message is absent from retained OTLP data.'
        }
        Start-Sleep -Milliseconds 250
        if ((Json (Api $run)) -cne $retained) { throw 'Observations changed after Stop.' }
        if ((Save-Logs $run $download) -cne $logHash) { throw 'Original log bytes changed after Stop.' }
        if ($previous) {
            if ((Json (Api $previous.Id)) -cne $previous.Snapshot) { throw 'Rerun changed the previous run.' }
            if ((Save-Logs $previous.Id $download) -cne $previous.LogHash) { throw 'Rerun changed previous log bytes.' }
            $ids = @($stopped.Outputs.Records.Message.MessageId | Where-Object { $_ })
            if (!$ids.Count -or @($ids | Where-Object { $_ -in $previous.MessageIds }).Count) { throw 'Rerun message identities were missing or reused.' }
        }
        $previous = @{ Id = $run; Snapshot = $retained; LogHash = $logHash; MessageIds = @($stopped.Outputs.Records.Message.MessageId) }
        Write-Output "Published run $attempt passed: output, original logs, Stop retention and isolation."
    }
    Write-Output "Loaded $($catalog.Count) plugins and verified their .NET 10 design assets on the .NET 11 host."
} catch {
    $failure = $_
} finally {
    try {
        if ($process -and !$process.HasExited) {
            if ($base) {
                foreach ($run in $runs) {
                    try { Api $run 'DELETE' | Out-Null }
                    catch { $cleanupErrors.Add($_.Exception) }
                }
            }
            # Workflow Stop is verified above. Terminating this owned test host is not a desktop close test.
            try { $process.Kill($true) }
            catch { if (!$process.HasExited) { $cleanupErrors.Add($_.Exception) } }
        }
        if ($process -and !$process.WaitForExit(10000)) {
            throw 'The temporary companion did not exit.'
        }
    } catch {
        $cleanupErrors.Add($_.Exception)
    } finally {
        try { if ($process) { $process.Dispose() } }
        catch { $cleanupErrors.Add($_.Exception) }
        finally { $env:FLUXFLOW_PLUGIN_DIRECTORY = $previousStore }
    }
}
if ($failure -and !$cleanupErrors.Count) { $PSCmdlet.ThrowTerminatingError($failure) }
if ($failure) { $cleanupErrors.Insert(0, $failure.Exception) }
if ($cleanupErrors.Count) { throw [AggregateException]::new('Published check or cleanup failed.', $cleanupErrors) }
