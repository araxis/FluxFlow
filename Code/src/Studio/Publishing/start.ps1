#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$runnerDir = Join-Path $root 'runner'
$runId = [guid]::NewGuid().ToString()
$logs = [IO.Directory]::CreateDirectory((Join-Path $root "logs/$runId")).FullName
$stdout = Join-Path $logs 'runner.log'
$stderr = Join-Path $logs 'runner-error.log'
$store = Join-Path $logs 'plugins'
$previousStore = $env:FLUXFLOW_PLUGIN_DIRECTORY
$process = $null
$runUri = $null
$headers = @{'X-FluxFlow-Run' = '1'}
try {
    $env:FLUXFLOW_PLUGIN_DIRECTORY = $store
    $packages = Join-Path $root 'packages'
    if (Test-Path -LiteralPath $packages) {
        foreach ($package in Get-ChildItem -LiteralPath $packages -Directory) {
            & (Join-Path $runnerDir 'fluxflow-plugin.ps1') Install -Source $package.FullName -Directory $store
        }
    }
    $options = @{
        FilePath = 'dotnet'
        ArgumentList = @('FluxFlow.Runner.dll','--urls','http://127.0.0.1:0')
        WorkingDirectory = $runnerDir
        RedirectStandardOutput = $stdout
        RedirectStandardError = $stderr
        PassThru = $true
    }
    if ($IsWindows) { $options.WindowStyle = 'Hidden' }
    $process = Start-Process @options
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        if ($process.HasExited) { throw "Runner exited. See $stdout and $stderr." }
        $listening = Select-String -LiteralPath $stdout -Pattern 'Now listening on: (http://127\.0\.0\.1:\d+)' | Select-Object -First 1
        if ($listening) { break }
        if ([DateTime]::UtcNow -gt $deadline) { throw "Runner startup timed out. See $stdout and $stderr." }
        Start-Sleep -Milliseconds 100
    } while ($true)
    $base = $listening.Matches[0].Groups[1].Value
    $runUri = "$base/api/runs/$runId"
    $body = @{
        Configuration = [IO.File]::ReadAllText((Join-Path $root 'application.json'))
        Environment = Get-Content -LiteralPath (Join-Path $root 'environment.json') -Raw | ConvertFrom-Json
        Instances = @()
    } | ConvertTo-Json -Compress
    $status = Invoke-RestMethod $runUri -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $body
    while ($status.State -in 'Starting','Preparing') {
        Start-Sleep -Milliseconds 150
        $status = Invoke-RestMethod $runUri -Headers $headers
    }
    if ($status.State -ne 'Running') { throw "Application $($status.State). $($status.Error)" }
    Write-Host "Running $runUri (PID $($process.Id)). Press Enter to stop."
    # Status polling keeps the Runner's two-minute inactivity lease alive.
    $stop = [Threading.Tasks.Task]::Run([Func[string]][Console]::ReadLine)
    while (!$stop.Wait([TimeSpan]::FromSeconds(30))) {
        $status = Invoke-RestMethod $runUri -Headers $headers
        if ($status.State -ne 'Running') { throw "Application $($status.State). $($status.Error)" }
    }
} finally {
    try {
        if ($runUri -and !$process.HasExited) {
            Invoke-RestMethod $runUri -Method Delete -Headers $headers | Out-Null
        }
    } finally {
        if ($process) {
            if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
            $process.Dispose()
        }
        $env:FLUXFLOW_PLUGIN_DIRECTORY = $previousStore
        if ((Test-Path -LiteralPath $store) -and [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($store)) -eq $logs) {
            Remove-Item -LiteralPath $store -Recurse -Force
        }
    }
}
