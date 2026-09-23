# Core boundary and event cleanup - 2026-09-22

Reviewed the three remaining scoped questions: event helpers, Hosting/expression/
diagnostic boundaries, and stale Core documentation. Core remains one project with
48 production C# files. This pass adds no project, production type or public API.

## Event changes

FlowEvent is a component-author helper for creating canonical FlowMessage events;
it is also read by host observers. EmitEvent, native event posting, component bridge
tests and event consumers use it. Deleting the model or its reading helpers would
remove supported behavior, not unused infrastructure.

Removed intermediate work inside the existing implementation:

- Reading a FlowValue now reads its JSON directly, without constructing a temporary
  FlowMessage, generating message/trace IDs or capturing ambient trace headers.
- Canonical and typed message readers share the existing JSON-reading logic and
  retain the original correlation/timestamp behavior.
- Dimension/attribute conversion uses JsonElement directly instead of wrapping each
  serialized object in JsonFlowValue before immediately serializing it again.
- Unsupported values still fall back to invariant text. That text needs no JSON
  wrapper before the enclosing event is serialized.
- A private shared System.Text.Json options instance replaces repeated options
  construction; final event payload ownership and snapshots remain unchanged.

The existing timestamp distinction is intentionally preserved: canonical messages
prefer the payload timestamp and fall back to the envelope; FlowMessage<FlowValue>
projection uses the envelope timestamp. Bare values use payload time or current UTC
and carry no envelope correlation. This pass does not redefine event semantics.
Two new regression cases exercise these boundaries and bare FlowError rejection;
existing encoding tests cover structured values, borrowed JSON, byte arrays, fallback
formatting, empty maps and invalid measurements. No benchmark was run, so no quantified
performance improvement is claimed.

## Retained boundaries and reasons

| Area | Decision | Reason |
| --- | --- | --- |
| Hosting | Keep AddFluxFlow and its BackgroundService adapter in Core | Two small files connect existing compose/start/stop/completion to standard host lifetime. No second provider or alternate runtime is present. Extracting them would move code and add a package, not simplify execution. |
| Expression contracts | Keep IFlowExpressionEngine and FlowMapContext in Core | Configuration and both optional engines share them. Components still receive ordinary delegates. Moving contracts into Configuration would couple engines to setup; a separate contract package would add a project without removing logic. |
| Diagnostics/tracing/metrics | Keep native instrumentation at processing/lifetime boundaries | The original exception, duration and activity parent context must be observed where processing occurs. Host observers own logging/export; no OpenTelemetry SDK or backend dependency is present. Removing instrumentation would lose required observability. |
| Routing/lifetime | Keep runtime coordination | Fanout, backpressure, fault containment, completion and component request/reply remain actual requirements; keyed DI does not implement these runtime behaviors. |

These are pragmatic package decisions for the agreed framework, not a claim that
hosting or expression contracts are mathematically indispensable to bare Dataflow.
No new interface, event manager, catalog, forwarding package or replacement wrapper
is justified by this review. The scoped cleanup can close without another broad
Core redesign; future changes should follow concrete usage or measured problems.

## Documentation and validation

Removed remaining Core README paragraphs describing update hosting, revision buffers,
Core.Runtime and document-builder rejection streams as current features. Historical
reports remain historical. The current reading list includes this decision report.

The source API scanner previously skipped an attributed continuation line in the
typed TryGetFlowEvent signature and absorbed the remaining method bodies. Putting
that unchanged signature on one line, like its other overloads, restores discovery
of three already-existing declarations. The baseline therefore changes from 325 to
328 Core declarations without any actual API additions/removals. The initial full
run failed only this source baseline check; behavioral tests passed. The scanner's
general multiline-attribute limitation is not fixed by this pass.

Validation evidence is under artifacts/cleanup-review-20260920: event-cleanup-focused.log,
event-cleanup-api-check.log, event-cleanup-api.log and event-cleanup-tests-final.log.
The final full Release run passed **1,782 executions**, including the public API
baseline. Isolated Core NuGet consumers passed on .NET 8 and .NET 10; see
event-cleanup-core-package.log. No commit/publication.
