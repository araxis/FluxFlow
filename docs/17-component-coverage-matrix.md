# Component Coverage Matrix

Current distribution update: the independent `FluxFlow.UI` project now owns all
20 Designer packages; the FluxFlow runtime manifest contains 61 packages. The
dated inventory below describes the earlier combined distribution. Component
execution capabilities remain in FluxFlow. See the
[extraction report](reviews/2026-09-12-ui-extraction.md) for current validation.

Current checkpoint: 2026-09-11. This is a migration/evidence inventory, not a
declaration that the entire refactoring or every capability is complete.

The captured baseline contained 62 packages and 60 public concrete node types.
The current manifest contains 81 packages and reflection finds 56 public concrete
node types in component packages. All 60 baseline node types have a current
counterpart; four generic retry/timer variants were consolidated. Type presence
does not prove unchanged options, errors, timing, persistence or protocol behavior.

## Core and optional packages

| Package/group | Current responsibility and dependency boundary |
| --- | --- |
| `FluxFlow.Core` | Canonical transport, component ports/lifetime, named graph and exchange primitives. No outward project references; payload JSON is not workflow-document definition. |
| `FluxFlow.Json` | Portable documents, parsing and trusted document construction; references Core and Mapping. |
| `FluxFlow.Hosting` | Optional Generic Host integration for direct Core applications. |
| `FluxFlow.Composition` | Optional document contracts, resource bindings and portable code definition; references Core, Json and Mapping. |
| `FluxFlow.Mapping` | Evaluator-neutral contracts; no outward project references. |
| `FluxFlow.Resilience` | Transport-neutral retry policy; no outward project references. |
| Expression adapters | Optional implementations over Mapping, with host-owned native configuration. |
| `FluxFlow.Engine` | Optional hosting/update operations, stable ports and revision policy; not required by direct Core execution. |
| Durable input/output and providers | Optional packages over Engine; provider state and acknowledgement stay outside Core. |
| Health checks | Optional Engine health integration. |
| Designer support and family metadata | Optional editor projections/presentation; not required by ordinary family composition. |

The former `FluxFlow.Nodes` and `FluxFlow.Coordination` package responsibilities
are consolidated into Core. Existing namespaces such as `FluxFlow.Nodes` do not
imply those retired package/runtime boundaries still exist.

## Component families

Counts below are public concrete node types, including generic local-processing
conveniences. Every listed type implements `IFlowComponent`, has a public
constructor, and exposes canonical `FlowMessage` standard block ports. Reflection
did not invoke constructors or property getters; it cannot prove startup cost or
discover the contents of an instance's custom port map.

Each of these 19 families has separate runtime, optional Composition and optional
Designer packages. Current declared transitive project references show **zero
Designer dependencies in all 19 Composition packages**.

| Family | Public node types | Retained capability area to verify |
| --- | ---: | --- |
| Assertions | 2 | Local typed/schema-less results, materialization, errors and diagnostics verified in seven-family qualification |
| Expectations | 1 | Projection-event expectation windows and outcomes |
| File system | 4 | Read/write/enumerate/watch, cancellation and ownership |
| HTTP | 1 | Requests, responses, content and transport context |
| Mapping | 2 | Typed/schema-less mapping and evaluator failures |
| Metrics | 1 | Explicit sample-to-snapshot processing; host exports remain external |
| MQTT | 4 | Publishing, subscriptions, control and client events |
| Observability | 6 | Counter/logger/metrics processing with local typed conveniences |
| Payloads | 1 | Declared content, exact binary, limits and error recovery verified in seven-family qualification |
| Projections | 1 | Filtering, rates, snapshot ownership and final completion verified in seven-family qualification |
| Resilience | 1 | Attempt identity, retry, acknowledgement and terminal results |
| Routing | 6 | Correlation, join and window behavior over canonical ports |
| Serialization | 6 | All converters in a direct graph, exact bytes, error identity and binding ownership verified in seven-family qualification |
| Sessions | 3 | Record/query/replay, sequence, content, recovery and borrowed store lifetime verified in seven-family qualification |
| Sources | 3 | Generated and sequence sources, stop and ownership |
| State | 2 | Typed/schema-less reduction, reset/clear, key bounds and recovery verified in seven-family qualification |
| Storage | 4 | Put/get/delete/query, exact content, error/lineage, drain and host-owned store verified; see persistence qualification below |
| Timers | 5 | Interval/schedule/delay/debounce/throttle and bounded lifecycle |
| Validation | 1 | Domain validation results, selected values, shape/selector failures and recovery verified in seven-family qualification |

Additional component surfaces are the optional inbound HTTP trigger and the
protocol request/reply coordinator (one public node type each). Concrete transport
and storage provider packages supply backend integration; their existence or
successful restore is not a substitute for real provider conformance.

## Explicit type consolidations

| Previous public type | Current type | Verification inspected in this audit |
| --- | --- | --- |
| `FlowRetryNode<T>` | `FlowRetryNode` | Retry input/feedback identity and native-port tests; retry suite: 35 passed |
| `TimerDelayNode<T>` | `TimerDelayNode` | Direct graph binary/error identity and faulted scheduled-work tests |
| `TimerDebounceNode<T>` | `TimerDebounceNode` | Direct stop flushes only latest input; pending timer disposal |
| `TimerThrottleNode<T>` | `TimerThrottleNode` | Direct bounded admission/cancellation and canonical chain identity |

Timer suite: 77 passed. These are breaking API consolidations, not evidence that
retry or timer functionality was deleted. Generic mapper/join/state and similar
conveniences that remain public use canonical transport; generic arity alone
does not establish a second execution runtime.

## Evidence and remaining gates

The [seven-family behavior qualification](reviews/2026-09-11-component-behavior-qualification.md)
records 263 fresh passes across 14 runtime/Composition suites and all 92 runtime
source comparisons (82 unchanged, ten reviewed changes). Its assertion map defines
the verified scope of the seven rows above. This does not convert remaining family
rows or external/performance requirements into passes.

The [remaining-family qualification](reviews/2026-09-11-remaining-family-qualification.md)
adds 882 fresh net10.0 passes across 27 runtime/Composition/adapter suites,
218 baseline runtime paths and current replacement checks for all 19 removed
paths in that selection. Together with the storage/persistence review, all 19
ordinary families and the additional HTTP/request bridge now have local behavior
dispositions. Each report states its inspected assertions and external limits;
transport options and in-process test brokers do not prove external connectivity.

The [current migration audit](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/docs/reviews/2026-09-11-capability-migration-audit.md)
links the 81-package dependency inventory, all 60 old-to-current type rows,
constructor/standard-port metadata and test-project/report associations.

A report associated through a project reference proves that the suite references
the package, not that it tests every capability. Individual behavior, persistence,
transport and performance requirements remain separately accountable.

The current qualified local candidate is `0.0.0-local.20260911.startup.1`: 81
packages and symbols, current readmes and matched binaries, with full-package and
minimal consumers on both supported frameworks. Its source-qualified local
regression has 4,848 passes; discovery found 5,058 cases, not all locally runnable.
The original timer/retry checkpoint above remains historical evidence. Do not
infer new net8.0 source-test executions from package builds.

Follow-up evidence includes constructor/configuration accounting for all captured
types, package-only in-flight updates, and consumer SDK signal collection on both
frameworks. The latest persistence qualification adds 766 passed cases across nine
durability/storage projects on net10.0, with real embedded/file-provider execution.
See `docs/reviews/2026-09-11-persistence-migration-qualification.md` for the exact
capability assertions, 53-file provider source comparison and external limits.

The acceptance case uses a supplied MQTT message and in-process HTTP handler.
Real broker/socket tests remain a separate gate; the configured container daemon
was unavailable in this audit. No license was accepted, no external consumer was
contacted, and nothing was published. Full feature parity, broader telemetry and
the agreed performance matrix are not yet signed off.
