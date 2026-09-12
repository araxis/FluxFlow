# Component constructor and configuration migration

Status: bounded migration accounting complete; not full capability or release signoff.

The comparison uses the original captured public component metadata and the
consumed assemblies of `0.0.0-local.20260911.startup.1`. It invokes neither
constructors nor property getters. All 1,488 qualified source inputs remained
unchanged before and after this audit and its focused test runs.

## Results

| Scope | Verified result |
|---|---|
| Captured public component types | All 60 map to 56 current concrete types; four generic timer/retry types consolidate into existing canonical types. |
| Constructor signatures | 58 mappings unchanged; two explicitly reviewed changes below. |
| Captured public port names | None missing. Typed graph-port signatures intentionally become canonical message ports. |
| Original component options/contracts source | 178 files selected; 176 unchanged after line-ending normalization. |
| Changed or removed selected files | Two request/reply files, reviewed below. |
| New files within that selection | None. |
| Fresh focused tests | Eight passed on .NET 10; zero failed or skipped. |

### Constructor changes

`HttpTriggerNode(source, options, clock)` becomes
`HttpTriggerNode(options, clock)`. Submit transport contexts through `Incoming`;
an external source can link to that target using native Dataflow linking. The
caller owns that source and link. `Output`, `Responses`, and `Events` remain
available. This makes construction instance-first without adding a component
creator or a context/service-locator overload.

`RequestReplyCoordinator<TRequest, TResponse>(options, clock)` adds an optional
third `materializeResponse` callback. Reflection confirms that it is optional;
existing default construction remains valid. It customizes response conversion
at the transport edge, not the canonical graph message representation.

The eight focused cases are the two cases in `DirectHttpTriggerGraphTests` and
six in `DirectRequestBridgeTests`. They exercise direct construction, transport
validation/recovery, response conversion, startup gating, and lifecycle handling.
These are in-process tests, not external HTTP server or broker qualification.

### Configuration changes

The selection covers every original C# file beneath component `Options` or
`Contracts` folders, plus component files whose names end in `Options.cs`.
None of these files had pre-existing edits in the original dirty-worktree
manifest, so the captured commit is a valid baseline for this bounded comparison.
This is not a claim about every public record elsewhere in the solution.

The two exceptions are:

- `CorrelatedRequestTrackerOptions.cs` is removed with the redundant tracker and
  its start-result enum. The existing Core exchange primitive owns request
  settlement. Advanced explicit-key consumers use the Core keyed coordinator;
  application consumers use `RequestAsync`, and transport adapters retain
  `IRequestContext`. The operation-by-operation migration is documented in
  `docs/46-code-first-major-migration.md`, under “Retired correlation tracker and
  sweep option.”
- `RequestReplyOptions.cs` removes the unused `SweepInterval` compatibility
  setting and obsolete duplicate-correlation diagnostic constant. Remaining
  diagnostic numbers are not renumbered. Mode, timeout, capacity, defaults, and
  validation remain; comments now specify that the deadline includes graph
  admission and capacity includes owned terminal callbacks.

Unchanged record source preserves declared configuration/data shape; it does not
by itself establish unchanged processing, defaults imposed elsewhere, serialized
wire behavior, or every lifecycle branch.

## Evidence and reproduction

`artifacts/core-refactoring/component-contract-audit-20260911/inventory.ps1`
reproduces the audit. Pass `-OutputDirectory` with a fresh checkpoint directory;
it refuses to overwrite a completed audit. The `verified` directory contains
the independently regenerated authoritative reports:

- `summary.json`: counts and scope.
- `current-components.json`: consumed assembly hashes, constructors, parameter
  optionality, native ports, public properties, and declared methods.
- `component-migration.json`: all 60 mappings and constructor/port changes.
- `options-and-contracts.json`: all 178 paths with normalized hashes and status.
- `new-contract-paths.json`: the empty new-path array.
- `source-before.json`: qualified source identity.

The first audit attempt falsely reported missing ports because projecting a
property from an empty array produced a null entry in the script. The corrected
script projects through the pipeline; a complete independent rerun reports zero
missing names. `diagnostic.log` retains that failed attempt. No runtime code was
changed to satisfy the audit.

Fresh test commands used Release, `--no-restore`, VSTest `--filter`, and
`--logger trx`, against the existing HTTP and request/reply unit test projects.
Filters were respectively `FullyQualifiedName~DirectHttpTriggerGraphTests` and
`FullyQualifiedName~DirectRequestBridgeTests`. Their TRX reports are under
`http-tests` and `bridge-tests` beside the script. SDK: 10.0.400.

## Remaining scope

This closes constructor/port-name accounting and the selected configuration
source review left open in the earlier capability inventory. It does not close
all component operation assertions, remaining performance/telemetry dimensions,
real provider integrations, or the final consolidated handoff. The intended
performance context is ordinary applications; no numeric throughput or latency
SLO has been supplied or inferred. Nothing was published or sent to an external
consumer task.
