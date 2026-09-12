# Consumer signal qualification

The qualified local candidate can feed a consumer-owned telemetry SDK with
processing/request traces, correlated component logs, runtime logs, and runtime
measurements while retaining its native diagnostic event stream. No library source
or dependency changed in this checkpoint.

## Executed acceptance

Case: `Consumer_sdk_collects_correlated_runtime_signals` in
`artifacts/core-refactoring/consumer-signals-20260911/Program.cs`.

It constructs a canonical echo node in an optional hosted application and issues
16 distinct requests, each under a separate consumer trace. Consumer-side SDK
registration listens to the Core activity source and the runtime meter and
configures logging plus in-memory export. The exporters are genuine SDK
components, not stand-in listeners. In-memory export is intended for testing,
not deployment; see the [exporter documentation](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.18.0/src/OpenTelemetry.Exporter.InMemory/README.md).

Each run verifies:

- Sixteen request spans parented to their consumer roots and sixteen processing
  spans parented to those request spans.
- Sixteen component log records with matching processing trace/span IDs and
  expected formatted content.
- Sixteen runtime request-completed log records and sixteen native diagnostic
  completion events, independent of SDK metric collection.
- Sixteen exported duration samples in milliseconds; their sum agrees with the
  original diagnostic-event duration sum within floating-point summation tolerance.
- Exactly the bounded kind/level labels for diagnostic measurements and no
  dimensions for system/drop counters. Per-request identities cannot enter those
  label sets.
- Correct replies with distinct exchange IDs, node drain, caller-owned diagnostic
  target drain, and successful trace/metric flush.

The diagnostic target is an ordinary bounded Dataflow ActionBlock. Host signal
disposal can detach best-effort observers without completing their targets; the
consumer explicitly waits for its expected observations, unlinks, completes its
target, and awaits completion. This is not a new subscription mechanism.

## Qualified results

| Item | Result |
|---|---|
| Candidate | 0.0.0-local.20260911.startup.1 |
| Consumer SDK/exporter version | 1.18.0, pinned only in the fixture |
| Frameworks | .NET 8.0.30 and .NET 10.0.11 |
| Repetitions | Two per framework, reversed order in the second pair |
| Requests / processing spans / request spans | 64 / 64 / 64 |
| Correlated component logs / runtime completion logs / completion events | 64 / 64 / 64 |
| Duration measurements | 64 |
| Package identity | Five FluxFlow packages; all ten target DLLs and cached archives match the qualified feed |
| Source/runtime stability | All 1,488 source inputs and 58 fixture runtime files unchanged |
| Builds | Fresh nonincremental solution and consumer builds: zero warnings/errors |

The fixture contains package references only. Its isolated restore cache is local
to the artifact folder. It does not add a telemetry SDK dependency to Core, modules,
or ordinary samples. The first restore required authorized access to the host's
package configuration; that configuration was not edited.

## Reproduction and review

The fixture directory contains `Consumer.csproj`, `NuGet.config`, `Program.cs`,
and `verify.ps1`. Restore with that configuration and its `packages` directory,
then run the verifier from the repository root. It preserves a completed
qualification instead of overwriting it. The `qualified` directory records exact
build/run arguments, logs, four result rows, package/archive/DLL checks, fixture
hashes, source hashes and `verification.json`.

Early fixture compile/assertion corrections are described in
`.testagent/consumer-signals-review.md`; none required a library change. The review
maps every bounded checklist item to a concrete assertion and documents six
pseudo-mutations those assertions would catch. This is not measured coverage or
an actual mutation-tool result.

## Limits

This establishes actual consumer SDK collection and in-memory export for the
specified successful request workload. It does not establish network collector
delivery, backend availability, remote transport propagation, every sampling mode,
SDK exporter failure/slowdown behavior, resource attributes, or measured telemetry
overhead. Earlier raw-listener tests and timed tracing runs retain their separate
scope. This checkpoint adds no throughput or p99 target.

The overall refactoring goal remains active: remaining feature-level assertions,
performance dimensions, available real integrations and the consolidated final
handoff are not waived. The local candidate is unchanged. Nothing was published,
and no external consumer task was contacted.
