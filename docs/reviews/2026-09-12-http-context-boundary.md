# HTTP request queue trace boundary

Status: source correction and cross-framework boundary verification complete;
new candidate packaging and overall acceptance remain open. No publication or
external consumer coordination occurred.

## Defect and small correction

The bridge previously created a workflow message inside its queued worker from
request data and correlation only. The worker can inherit the first submitter's
execution context; later requests can therefore acquire the wrong trace. A real
single-request loopback test passed on old code, but distinct queued requests
reproduced the failure in both request/reply and fire-and-forget modes.

Three production files changed:

- `IRequestContext` exposes optional standard `ActivityContext TraceContext`;
  the default is parentless. Existing adapters need implement it only when they
  have an explicit distributed parent to carry.
- `HttpRequestContext` captures the current live server operation's context before
  handing the request to the bridge. It stores the value, not an Activity object.
- `RequestReplyCoordinator` suppresses worker ambient context and creates canonical
  messages with the supplied W3C parent/state in both modes. Existing fire-and-forget
  correlation and Core exchange behavior are retained.

No Core source, runtime owner, native block contract or telemetry SDK dependency
changed. Platform HTTP instrumentation is reused: the platform handles HTTP
trace encoding/decoding, while the bridge owns carrying context across its queue.
See the [platform tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts).
Domain TraceId and exchange correlation remain distinct from distributed tracing.

This is additive optional metadata within the unpublished development train.
The source-declaration baseline changed only manifest row 9 (HTTP adapter, 30 to
31 declarations). Its lightweight scanner does not capture implicit interface
members; the explicit interface addition is reviewed here rather than claiming
the baseline is complete API compatibility analysis. Package READMEs and changelog
describe the behavior and client-adapter responsibility.

## Qualification

| Requirement | Evidence |
| --- | --- |
| Wrong-worker parent regression | `Queued_contexts_preserve_each_request_trace`: four final cases fail against frozen context.1 and pass against corrected source |
| Distinct requests, both modes, missing context, sampling/state | Three requests queued before bridge start; original server activities stopped; exact IDs, parent spans, flags/state, path and restored ambient context asserted |
| Real HTTP wire and queued processing | `Http_transport_preserves_context_through_queued_bridge`: four cases per framework verify caller → outbound process → platform client → server → exchange/process and exact binary bytes |
| Full solution | Fresh nonincremental Release build: zero warnings/errors, 42.02 seconds; 4,861 local passes, zero failures, 81 reports: 76 nonempty and five explicitly filtered integration/acceptance suites |
| Cross-target boundary | Clean source-fixture build and ten cases per target on net8.0/net10.0; four loopback, two initial queued reproducers, four permanent regressions; eight copied production DLLs match current build |
| Identity | 1,491 source inputs stable; three production edits, one permanent test file and baseline update relative to context.1; both Core DLLs unchanged |

The source fixture's duplicate reproducer cases and cross-target reruns are not
additional unique solution tests. Only four permanent cases increase the prior
4,857 count to 4,861. Fresh unfiltered discovery was not performed. The five
filtered suites are the two server-database integration projects, workflow
Acceptance, ContainerIntegration and ExternalSmoke—not integration passes.

Evidence: [full regression](../../artifacts/core-refactoring/http-context-20260912/qualified-20260912/verification.json),
[boundary runs](../../artifacts/core-refactoring/http-context-20260912/boundary-qualified/verification.json),
[final hash/source/reproducer audit](../../artifacts/core-refactoring/http-context-20260912/audit.json),
[assertion and gap review](../../.testagent/http-context-review.md).
The frozen reproducer uses only packages, with all four restored archives and
copied production DLLs matched to context.1. Source-fixture runs are explicitly
project-reference verification, not new package acceptance.

## Remaining limits

The frozen `0.0.0-local.20260912.context.1` feed contains the MQTT correction but
does **not** contain this HTTP correction. Preserve that feed and its qualification;
produce a fresh coherent candidate after the remaining source-affecting acceptance
checks. Do not silently relabel Core benchmarks or earlier consumers as new runs.

This closes the identified HTTP queue-context boundary with real loopback transport.
It does not establish external broker/server-provider integration, exporter/backend
behavior, SDK observer retention/overhead or end-to-end production performance.
The overall refactoring goal remains active.
