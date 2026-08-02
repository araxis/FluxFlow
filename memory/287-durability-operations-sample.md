# Durability Operations Sample

Date: 2026-08-02

## Decision

The next durability step is an executable host example, not a new runtime
feature. `FluxFlow.DurabilityOperationsSample` proves that the existing public
boundaries compose into one understandable operational workflow:

1. enqueue one string into durable input before host startup;
2. observe that pending state explicitly;
3. start a normal Generic Host and FluxFlow application;
4. transform the value in one typed component;
5. durably capture and deliver the component output;
6. await semantic completion without sleeps or status polling; and
7. explicitly observe final input and output state.

The sample is non-server, deterministic, self-cleaning, and source-generated-
JSON based. It uses separate SQL-file databases in one unique temporary
directory only so it can run without infrastructure. The public durable and
status contracts remain provider-neutral.

## Ownership Boundary

FluxFlow owns event emission at its existing semantic capture and dispatcher
boundaries. The sample host owns one `MeterListener`, one `ActivityListener`,
their filters, aggregation, output, and disposal. A production host can attach
an OpenTelemetry-compatible bridge to the same BCL sources, but FluxFlow does
not select a telemetry SDK, exporter, sampling policy, retention policy,
dashboard, or redaction policy.

Listener callbacks are synchronous, bounded in-memory reducers. They perform
no console, file, database, or network I/O. Required output contains only exact
source/instrument/activity names, bounded outcomes/results, aggregate counts,
and the intentional transformed value. It omits addresses, message/trace/lease
identity, headers, exception data, database paths, connection data, and raw
duration values.

Status is a separate on-demand persisted view. The sample makes exactly three
calls: input after enqueue/before startup, input after terminal delivery, and
output after terminal delivery. It adds no timer, gauge callback, hosted status
reader, health check, cache, or polling loop.

## Lifecycle And Guarantees

The host is built once, started once, stopped once, and disposed before its
exact temporary directory is deleted. One bounded timeout covers the scenario.
Completion comes from the delivery handler and applied semantic input/output
measurements; the expected activities and metrics must also stop/arrive before
the sample accepts success.

The destination example deduplicates by `DurableOutputKey` in memory. This is a
teaching aid, not an exactly-once claim. Delivery remains at-least-once, and a
real destination must persist or otherwise enforce idempotency if duplicate
side effects matter.

## Test Boundary

Release coverage reuses the existing bounded child-process owner and current
prebuilt configuration. One exact-output fact freezes all ten normalized output
lines and requires empty stderr plus exit code zero. One source-shape fact
protects host-owned BCL listeners, exact source names, three explicit status
calls, final cleanup, and the absence of delay polling, timers, automatic hosted
status readers, exporters, and server setup.

The existing dynamic sample inventory and documented-command tests prove the
new project is in `FluxFlow.sln`, listed in `docs/README.md`, and addressed by a
valid run command. No new test project, process runner, test package, global
parallelism setting, or production test hook is added.

## Verification Evidence

- The mandatory source/test pairing analyzer for this round retained the
  established 759 production sources, 311 test sources, 528 paired files, and
  231 unpaired files. This is a static filename/content heuristic, not runtime
  coverage.
- Restore and Release build of the sample passed across nine projects with zero
  errors or warnings. Direct execution passed twice with identical ten-line
  output and removed its temporary data each time.
- Independent focused release facts passed 2/2. The complete
  `SampleDocumentationTests` group passed 6/6, covering dynamic solution/docs
  inventory, documented project paths, existing non-server samples, exact
  repeated operations output, source boundaries, and prebuilt process flags.
- After final goal and memory edits, the combined sample/documentation-boundary
  filter passed 20/20, confirming the final inventories, links, and commands.
- Focused format verification passed for the sample project and touched release
  test file. A whole release-project format scan also reported 52 pre-existing
  unrelated style findings; none is in the touched file and none was rewritten.
- `Microsoft.Extensions.Hosting` is the sample's only direct package. It is
  centrally aligned at 10.0.7 with the repository's other Extensions packages;
  vulnerability inspection found no vulnerable direct or transitive package
  under the configured sources. Production projects and shipped package
  dependencies/versions are unchanged.
- The serialized Release solution build passed 134 targets with zero errors or
  warnings. Release governance passed 125/125. Two consecutive serialized full
  Release suites each passed 2,488/2,488 tests across 66 projects without
  warnings.
- Initial full aggregate attempts under machine load timed out in two unrelated
  process/source timing tests after 2,486 successes. Every observed failure
  passed together in isolation; build servers were cleared before the two
  authoritative all-green serialized passes. No unrelated timing test was
  changed.

## Recommended Next Step

Do not add an exporter or automatic health system to FluxFlow next. First use
this sample as the canonical operations reference and gather concrete host
requirements. If exporter-specific guidance is needed, add a separate optional
host example that consumes the same BCL sources. If readiness policy is needed,
add a host-owned adapter over existing status stores with explicit thresholds
and cadence; keep all database polling outside the engine.
