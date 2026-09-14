param([Parameter(Mandatory = $true)] [string] $WorkDirectory)
$ErrorActionPreference = 'Stop'
$traces = @(Get-Content (Join-Path $WorkDirectory 'traces.jsonl') | ForEach-Object { ($_ | ConvertFrom-Json).resourceSpans.scopeSpans.spans })
$metrics = @(Get-Content (Join-Path $WorkDirectory 'metrics.jsonl') | ForEach-Object { ($_ | ConvertFrom-Json).resourceMetrics.scopeMetrics.metrics })
$logs = @(Get-Content (Join-Path $WorkDirectory 'logs.jsonl') | ForEach-Object { ($_ | ConvertFrom-Json).resourceLogs.scopeLogs.logRecords })
Write-Host "Collector received $($traces.Count) spans, $($metrics.Count) metrics, $($logs.Count) logs."
if (!$traces.Count -or !$metrics.Count -or !$logs.Count) { throw 'Collector did not receive all three signals.' }

function Attribute($item, [string] $key) {
    return ($item.attributes | Where-Object key -eq $key).value.stringValue
}
function Check([bool] $condition, [string] $message) {
    if (!$condition) { throw $message }
}
$scenario = Get-Content (Join-Path $WorkDirectory 'scenario.json') -Raw | ConvertFrom-Json
$sse = @(Get-Content (Join-Path $WorkDirectory 'sse.json') -Raw | ConvertFrom-Json)
$root = @($traces | Where-Object { $_.name -eq 'POST /scenario' })
Check ($root.Count -eq 1 -and $root[0].traceId -eq '1234567890abcdef1234567890abcdef' -and $root[0].parentSpanId -eq '1234567890abcdef') 'HTTP ingress lost the supplied W3C parent.'
$flow = @($traces | Where-Object name -eq 'flow.process')
$branches = @($flow | Where-Object { (Attribute $_ 'flow.component.type') -in @('LeftBranch', 'RightBranch') })
Check ($branches.Count -eq 24 -and $scenario.results -eq 24 -and $scenario.errors -eq 1) 'Fanout did not process all 24 branch operations.'
foreach ($span in $branches) {
    Check ($span.traceId -eq $root[0].traceId -and $span.parentSpanId -eq $root[0].spanId) 'A branch lost its ingress parent.'
}
$pairs = @($branches | Group-Object { Attribute $_ 'flow.message_id' })
Check ($pairs.Count -eq 12 -and @($pairs | Where-Object Count -ne 2).Count -eq 0) 'Fanout did not preserve the same message identity across siblings.'
foreach ($branch in @('LeftBranch', 'RightBranch')) {
    $ordered = @($branches | Where-Object { (Attribute $_ 'flow.component.type') -eq $branch } | Sort-Object { [long]$_.startTimeUnixNano })
    Check ($ordered.Count -eq 12 -and [long]$ordered[1].startTimeUnixNano -lt [long]$ordered[0].endTimeUnixNano) 'Exported spans do not show overlapping branch execution.'
}
$failed = @($branches | Where-Object { $_.status.code -eq 2 })
Check ($failed.Count -eq 1) 'Expected exactly one failed processing span.'
$failureEvent = $failed[0].events | Where-Object name -eq 'exception'
Check ((Attribute $failureEvent 'exception.stacktrace') -like '*collector-inner-detail*Branch.TransformAsync*') 'Span exception stack or inner detail was lost.'
$failureLog = @($logs | Where-Object { (Attribute $_ 'exception.message') -eq 'collector-acceptance-failure' })
Check ($failureLog.Count -eq 1) 'Expected exactly one processing exception log.'
Check ($failureLog[0].traceId -eq $failed[0].traceId -and $failureLog[0].spanId -eq $failed[0].spanId) 'Exception log is not correlated with the failed span.'
Check ((Attribute $failureLog[0] 'exception.stacktrace') -eq (Attribute $failureEvent 'exception.stacktrace')) 'Log and trace do not preserve the same full exception detail.'
Check ($sse.Count -eq 24 -and @($sse.spanId | Sort-Object -Unique).Count -eq 24) 'SSE did not deliver 24 unique processing spans.'
foreach ($span in $sse) {
    $match = @($branches | Where-Object spanId -eq $span.spanId)
    Check ($match.Count -eq 1 -and $match[0].traceId -eq $span.traceId -and $match[0].parentSpanId -eq $span.parentSpanId) 'SSE and Collector span identity differ.'
}
$sseFailure = $sse | Where-Object spanId -eq $failed[0].spanId
Check (($sseFailure.events | Where-Object name -eq 'exception').tags.'exception.stacktrace' -like '*collector-inner-detail*') 'SSE lost exception detail.'
$shutdown = @($flow | Where-Object { (Attribute $_ 'flow.component.type') -eq 'ShutdownProbe+ShutdownNode' })
Check ($shutdown.Count -eq 1) 'Shutdown processing was not exported.'
$completed = ($metrics | Where-Object name -eq 'fluxflow.processing.completed').sum.dataPoints
$active = ($metrics | Where-Object name -eq 'fluxflow.processing.active').sum.dataPoints
$exceptions = ($metrics | Where-Object name -eq 'fluxflow.exceptions').sum.dataPoints
$duration = ($metrics | Where-Object name -eq 'fluxflow.processing.duration').histogram.dataPoints
Check (($completed | Measure-Object asInt -Sum).Sum -eq 25) 'Completed metric does not include all 25 processing scopes.'
Check ($active.Count -eq 3 -and @($active | Where-Object { [long]$_.asInt -ne 0 }).Count -eq 0) 'Active processing did not return to zero for every component.'
Check (($exceptions | Measure-Object asInt -Sum).Sum -eq 1) 'Exception metric should be exactly one.'
Check (($duration | Measure-Object count -Sum).Sum -eq 25 -and ($duration | Measure-Object sum -Sum).Sum -gt 0) 'Duration histogram lost processing observations.'
foreach ($point in @($completed) + @($active) + @($exceptions) + @($duration)) {
    Check (@($point.attributes | Where-Object { $_.key -notin @('flow.component.type','flow.operation','error.type') }).Count -eq 0) 'Unexpected high-cardinality metric dimension.'
}
foreach ($signal in @('traces','metrics','logs')) {
    $property = @{ traces = 'resourceSpans'; metrics = 'resourceMetrics'; logs = 'resourceLogs' }[$signal]
    foreach ($line in Get-Content (Join-Path $WorkDirectory "$signal.jsonl")) {
        foreach ($resource in ($line | ConvertFrom-Json).$property) {
            Check ((Attribute $resource.resource 'service.name') -eq 'FluxFlow.CollectorAcceptance') 'Signal resource identity differs.'
        }
    }
}
@{
    Result = 'Passed'
    CompletedUtc = [DateTime]::UtcNow.ToString('O')
    BranchSpans = 24
    ShutdownSpans = 1
    ProcessingExceptionLogs = 1
    SseSpans = 24
    CompletedOperations = 25
    ActiveOperationsAfterDrain = 0
    Exceptions = 1
    Checks = @('W3C ingress parent', 'fanout identity', 'concurrent spans', 'error recovery', 'full exception details', 'log/span correlation', 'SSE identity and disconnect', 'shutdown export', 'metric totals and dimensions', 'resource identity')
    Evidence = @(Get-ChildItem -LiteralPath $WorkDirectory -File | Where-Object { $_.Extension -in @('.jsonl','.json') -and $_.Name -ne 'verification.json' } | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash)
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $WorkDirectory 'verification.json')
Write-Host 'COLLECTOR_ACCEPTANCE_OK'
