# Observability package consumer

Run from the repository root:

```powershell
pwsh -NoProfile -File eng/verify-observability-consumer.ps1
```

Requires the repository SDK, .NET 8 and .NET 10 runtimes, the ASP.NET Core 10
runtime, and access to NuGet.org for public dependencies. Nothing is published.
Each run packs Core and all three observability adapters with a unique local
version, restores into a fresh cache, and runs this package-only consumer on both
frameworks. FluxFlow packages are restricted to that local feed by source mapping.
No project references are used.

The consumer processes a failing message followed by a successful one, checks
error recovery and all three telemetry signals, and verifies the original
exception survives into logging. On .NET 10 it additionally maps the packaged
SSE endpoint and starts/stops a temporary loopback host on a free port. SSE
delivery/backpressure tests remain in the adapter integration suite; this smoke
does not replace them or qualify an external Collector/Datadog deployment.

The deliberate processing error appears in console logs; `PACKAGE_CONSUMER_OK`
on each runtime indicates success. The local feed, isolated cache and successful
verification record (including package SHA-256 hashes) remain under `artifacts`.
