# Real Collector acceptance

This .NET 10 host is a synthetic acceptance workload, separate from production
components and samples. It exercises native bounded fanout, four concurrent
operations per branch, one processing exception followed by successful messages,
and a final processing operation during hosted-service shutdown.

Obtain a portable Windows Collector from the
[official distribution](https://opentelemetry.io/docs/collector/install/binary/windows/),
verify its published SHA-256 digest, and run:

```powershell
pwsh -NoProfile -File eng/verify-observability-collector.ps1 `
  -CollectorPath artifacts/collector-tools/0.160.0/otelcol.exe
```

Verified with the core Collector 0.160.0, which includes the OTLP receiver and
[file exporter](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.160.0/exporter/fileexporter/README.md).
The script does not download or install anything automatically. It requires the
repository SDK/runtime and dependencies, builds this host, and starts temporary
hidden processes bound to loopback. The acceptance routes deliberately have no
authentication and must only be used in this isolated local check.

The separate PowerShell HTTP client supplies a W3C parent, reads 24 SSE spans,
disconnects, checks subscription cleanup, and requests graceful host shutdown.
The Collector receives real OTLP HTTP/protobuf for traces, metrics and logs and
writes JSON lines. Assertions compare parent/span/message identities, overlapping
execution, full exception details in both traces and logs, SSE equivalence,
resource identity, metric totals/dimensions, and the shutdown operation.

Evidence is retained in a uniquely named `artifacts/collector-acceptance-*`
directory with configuration, Collector binary identity, SDK diagnostic logs,
received signals and a successful verification record with hashes. The script
stops the temporary Collector after host exit and a file-buffer flush interval;
it does not claim graceful Collector shutdown or durability across Collector
failure. Both temporary processes are cleaned up on failure as well.

The short-lived workload intentionally relies on host shutdown for the final
metric collection and pending log batch. It caught a lazy OTLP log HTTP-client
creation failure during service-provider disposal. The adapter now flushes logs
at its existing hosted-service stop boundary, after flow services stop and before
DI disposal, with a five-second bound. No component telemetry code is required.

This is source-host acceptance on net10, not a package-only or Datadog deployment
test. Separate package consumers cover net8/net10. Slow-client backpressure is
covered by the SSE adapter tests; this live check covers framing and disconnect.
