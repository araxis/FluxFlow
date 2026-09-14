param(
    [Parameter(Mandatory = $true)] [string] $CollectorPath
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$collector = (Resolve-Path -LiteralPath $CollectorPath).Path
$collectorVersion = & $collector --version
if ($LASTEXITCODE -ne 0) { throw 'Collector version check failed.' }
$work = Join-Path $repo "artifacts/collector-acceptance-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))-$([Guid]::NewGuid().ToString('N').Substring(0,8))"
New-Item -ItemType Directory -Path $work | Out-Null
@{ Version = "$collectorVersion"; Binary = Get-FileHash -LiteralPath $collector -Algorithm SHA256 } | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $work 'collector-identity.json')
function Free-Port {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}
$otlpPort = Free-Port
$hostPort = Free-Port
while ($hostPort -eq $otlpPort) { $hostPort = Free-Port }
$yamlPath = Join-Path $work 'collector.yaml'
$yamlWork = $work.Replace('\', '/').Replace("'", "''")
@"
receivers:
  otlp:
    protocols:
      http:
        endpoint: 127.0.0.1:$otlpPort
exporters:
  file/traces:
    path: '$yamlWork/traces.jsonl'
    flush_interval: 100ms
  file/metrics:
    path: '$yamlWork/metrics.jsonl'
    flush_interval: 100ms
  file/logs:
    path: '$yamlWork/logs.jsonl'
    flush_interval: 100ms
service:
  telemetry:
    metrics:
      level: none
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [file/traces]
    metrics:
      receivers: [otlp]
      exporters: [file/metrics]
    logs:
      receivers: [otlp]
      exporters: [file/logs]
"@ | Set-Content -LiteralPath $yamlPath
& $collector validate --config $yamlPath
if ($LASTEXITCODE -ne 0) { throw 'Collector configuration failed validation.' }
dotnet build (Join-Path $PSScriptRoot 'observability-collector/CollectorHost.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Acceptance host build failed.' }
$collectorProcess = $null
$hostProcess = $null
$client = [Net.Http.HttpClient]::new()
$cancellation = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(45))
$response = $null
try {
    $collectorProcess = Start-Process -FilePath $collector -ArgumentList @('--config', "`"$yamlPath`"") -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $work 'collector.stdout.log') -RedirectStandardError (Join-Path $work 'collector.stderr.log')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if ($collectorProcess.HasExited) { throw 'Collector exited during startup.' }
        try { $tcp = [Net.Sockets.TcpClient]::new(); $tcp.Connect('127.0.0.1', $otlpPort); break }
        catch { if ([DateTime]::UtcNow -gt $deadline) { throw }; Start-Sleep -Milliseconds 100 }
        finally { if ($tcp) { $tcp.Dispose() } }
    } while ($true)
    $dll = Join-Path $PSScriptRoot 'observability-collector/bin/Release/net10.0/CollectorHost.dll'
    $baseUrl = "http://127.0.0.1:$hostPort"
    $hostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @("`"$dll`"", '--urls', $baseUrl) -WindowStyle Hidden -PassThru -Environment @{
        OTEL_EXPORTER_OTLP_ENDPOINT = "http://127.0.0.1:$otlpPort"
        OTEL_EXPORTER_OTLP_PROTOCOL = 'http/protobuf'
        OTEL_EXPORTER_OTLP_HEADERS = ''
        OTEL_EXPORTER_OTLP_TRACES_ENDPOINT = "http://127.0.0.1:$otlpPort/v1/traces"
        OTEL_EXPORTER_OTLP_METRICS_ENDPOINT = "http://127.0.0.1:$otlpPort/v1/metrics"
        OTEL_EXPORTER_OTLP_LOGS_ENDPOINT = "http://127.0.0.1:$otlpPort/v1/logs"
        OTEL_EXPORTER_OTLP_TRACES_HEADERS = ''
        OTEL_EXPORTER_OTLP_METRICS_HEADERS = ''
        OTEL_EXPORTER_OTLP_LOGS_HEADERS = ''
        OTEL_TRACES_SAMPLER = 'always_on'
        OTEL_METRIC_EXPORT_INTERVAL = '60000'
    } -RedirectStandardOutput (Join-Path $work 'host.stdout.log') -RedirectStandardError (Join-Path $work 'host.stderr.log')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if ($hostProcess.HasExited) { throw 'Host exited during startup.' }
        try { $null = $client.GetStringAsync("$baseUrl/clients", $cancellation.Token).GetAwaiter().GetResult(); break }
        catch { if ([DateTime]::UtcNow -gt $deadline) { throw }; Start-Sleep -Milliseconds 100 }
    } while ($true)
    $streamTask = $client.GetAsync("$baseUrl/fluxflow/traces", [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $cancellation.Token)
    do {
        $clients = $client.GetStringAsync("$baseUrl/clients", $cancellation.Token).GetAwaiter().GetResult()
        if ($clients -eq '1') { break }
        Start-Sleep -Milliseconds 100
    } while (!$cancellation.IsCancellationRequested)
    if ($clients -ne '1') { throw 'SSE client did not connect.' }
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "$baseUrl/scenario")
    $null = $request.Headers.TryAddWithoutValidation('traceparent', '00-1234567890abcdef1234567890abcdef-1234567890abcdef-01')
    $scenarioResponse = $client.SendAsync($request, $cancellation.Token).GetAwaiter().GetResult()
    $scenarioResponse.EnsureSuccessStatusCode() | Out-Null
    $scenario = $scenarioResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    $scenario | ConvertTo-Json | Set-Content (Join-Path $work 'scenario.json')
    $response = $streamTask.GetAwaiter().GetResult()
    $response.EnsureSuccessStatusCode() | Out-Null
    if ($response.Content.Headers.ContentType.MediaType -ne 'text/event-stream') { throw 'Invalid SSE content type.' }
    $reader = [IO.StreamReader]::new($response.Content.ReadAsStream())
    $spans = [Collections.Generic.List[object]]::new()
    $eventName = ''
    while ($spans.Count -lt 24) {
        $line = $reader.ReadLineAsync($cancellation.Token).AsTask().GetAwaiter().GetResult()
        if ($null -eq $line) { throw 'SSE stream ended before all branch spans arrived.' }
        if ($line.StartsWith('event: ')) { $eventName = $line.Substring(7) }
        if ($line.StartsWith('data: ')) {
            if ($eventName -ne 'fluxflow.span') { throw 'Unexpected SSE event type.' }
            $spans.Add(($line.Substring(6) | ConvertFrom-Json))
        }
    }
    $spans | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $work 'sse.json')
    $reader.Dispose()
    $response.Dispose()
    $cancellation.Cancel()
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $clients = $client.GetStringAsync("$baseUrl/clients").GetAwaiter().GetResult()
        if ($clients -eq '0') { break }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'SSE subscription survived disconnect.' }
        Start-Sleep -Milliseconds 100
    } while ($true)
    $shutdown = $client.PostAsync("$baseUrl/shutdown", [Net.Http.StringContent]::new('')).GetAwaiter().GetResult()
    $shutdown.EnsureSuccessStatusCode() | Out-Null
    if (!$hostProcess.WaitForExit(15000) -or $hostProcess.ExitCode -ne 0) { throw 'Host did not shut down successfully.' }
    # All SDK providers are now disposed; allow the Collector file buffer to flush.
    Start-Sleep -Milliseconds 500
    Stop-Process -Id $collectorProcess.Id -Force
    $collectorProcess.WaitForExit()
    & (Join-Path $PSScriptRoot 'observability-collector/Verify-Telemetry.ps1') -WorkDirectory $work
    if (!$?) { throw 'Telemetry assertions failed.' }
}
finally {
    if ($response) { $response.Dispose() }
    $cancellation.Dispose()
    $client.Dispose()
    foreach ($process in @($hostProcess, $collectorProcess)) {
        if ($null -ne $process) {
            if (!$process.HasExited) { Stop-Process -Id $process.Id -Force }
            $process.Dispose()
        }
    }
    Write-Host "Evidence: $work"
}
