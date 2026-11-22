# Review before extending component setup — 2026-09-23

## Follow-up: confirmed defects corrected

The user approved fixing all three findings. Configuration now matches all registered
port properties case-insensitively while retaining canonical address spelling, rejects
ambiguous port names transactionally, registers named Events outputs in all six bundled
adapters, and preserves exact engine keys in Mapper/Router as links already did.
Core, component implementations and public signatures did not change.

The 32 regression cases cover actual Gate control behavior, Router named/unmatched
delivery, ambiguous registration, all six adapters' event delivery in both declaration
directions with stamped headers and shutdown, and exact custom engine keys/no fallback
across Mapper, Router and links. Before the fix, 25 failed and seven passed per target;
afterward Configuration passes all 269 cases per target. The uppercase built-in mapper
fixture now uses its registered `jsonata` key; exact custom-key behavior has separate
coverage. Explicit engine values are no longer trimmed or lowercased. Current guides
document this migration; configuration property-name casing is a separate rule.

Final validation after fixes: **1,972 passed, zero failed** across .NET 8/10, with
no build warnings in the full test log. The isolated Configuration package consumer
also passed on .NET 8.0.31 and .NET 10.0.12. Evidence is under
`artifacts/pre-setup-review-20260923/`: `regression-before.log`,
`configuration-after.log`, `full-after.log` and `package-after.log`.
Public API signatures are unchanged; the suite includes the API baseline check.
No commit or publication was performed.

The original review below records the pre-fix findings and validation. Its recommendation
to fix these three issues is now fulfilled; the lifetime limitation and nonblocking
follow-ups remain unchanged.

## Decision

The current architecture follows the agreed direction: configuration sections register
deferred factories and native keyed ports; one provider supplies identity and ownership;
components do not know their configuration format or expression engine. No return to
catalogs, intermediate document models, activation contexts or address subclasses is
justified by this review.

Do not expand setup adapters until the three confirmed configuration defects below
are corrected. These need local fixes and regression coverage, not another architectural
migration. This review changes no production code, public API or existing tests.

## Confirmed findings

### 1. P1 — Named port properties can silently lose their links when casing changes

Location: `src/FluxFlow.Configuration/ApplicationConfiguration.cs:81-89`.

Discovery first compares registered port names using ordinal equality. It retries
case-insensitively only for Input, Output and Events. Consequently Gate's `Open` and
`Close` properties are bound successfully by configuration but their lowercase forms
are skipped during link registration. The same code affects Router's Unmatched and
named route outputs.

Independent Add-time reproduction, with a registered Scheduler `main.clock` and Gate:

| Gate property | Value | Unkeyed LinkFactory registrations | Error |
| --- | --- | --- | --- |
| Open | clock.Output | 1 | None |
| open | clock.Output | 0 | None |
| Close | clock.Output | 1 | None |
| close | clock.Output | 0 | None |

Impact: a valid-looking configuration silently omits control links. A Gate can remain
closed and discard messages. This is a property-name matching defect, not a request to
make address values or native DI service keys case-insensitive.

Smallest correction: match section property names to all declared ports consistently
with configuration casing rules, then use the registered port's canonical spelling to
construct its address. Reject ambiguous port declarations instead of selecting one.
Cover Open/Close, Unmatched, named routes, both link directions and ambiguous names.

### 2. P2 — Five adapters expose Events configuration but register no routable Events port

Locations:

- `Scheduling/SchedulerRegistrationExtensions.cs:54`
- `Routing/GateRegistrationExtensions.cs:29`
- `Routing/RouterRegistrationExtensions.cs:58`
- `Mapping/MapperRegistrationExtensions.cs:50`
- `Sources/SourceRegistrationExtensions.cs:37`

All paths above are under `src/FluxFlow.Configuration`. Their DTOs expose
`OneOrManyLinks Events`, but they call the unnamed AddEventPort overload. That overload
registers an observation bridge at the component address; it does not register an
ISourceBlock at `workflow.component.Events`.

Reproduction: Scheduler with `Events = "gate.Input"` rejects Add with
`Component 'main.clock' has no data port 'Events'.` Batch with the same event-link
declaration registers one link successfully because its adapter uses the named overload.

Smallest correction: use the existing named Events registration in those adapters and
test event linking and shutdown. Preserve the existing observation/error semantics;
neither a new event abstraction nor removal of the bridge is required to fix this mismatch.

### 3. P2 — Mapper and Router alter explicit expression-engine keys

Locations: `Mapping/MapperRegistrationExtensions.cs:28` and
`Routing/RouterRegistrationExtensions.cs:36`, under `src/FluxFlow.Configuration`.

Both adapters trim and lowercase Engine before native keyed resolution. Conditional
links preserve the configured key and resolve it exactly. There is no shared contract
requiring custom engines to register lowercase keys.

Reproduction: register IFlowExpressionEngine under `MyEngine`. A conditional link with
Engine `MyEngine` builds successfully. A Mapper with the identical Engine value fails
Build because it requests the unregistered key `myengine`. Router uses the same faulty
normalization, established by code inspection.

Smallest correction: preserve explicit DI keys consistently in adapters and links.
Keep known engine/file-extension validation separate from service-key identity.
Test a mixed-case custom key across Mapper, Router and conditional links, plus exact
failure for an unregistered key. Do not introduce an engine enum or registry.

## Responsibility and behavior review

| Area | Assessment |
| --- | --- |
| Core/package boundary | Core has no configuration DTOs, IConfiguration traversal or engine implementation dependency. Engines and adapter DTOs stay outside components. |
| Registration and construction | Registration is synchronous and deferred. The staged IServiceCollection protects the caller against partial registration. No production BuildServiceProvider path or replacement lookup provider was found. |
| Application lifetime | Build composes once; Start connects routes and starts sources; Stop drains accepted work. Workflows do not have an independent Start/Stop protocol. |
| Ports and identity | Native keyed singleton ports resolve from their owning component. Address construction and relative resolution remain centralized. Discovery names are not a second cache of port instances. |
| Links | Both declaration directions share one parser and DI link identity. Mixed string/object lists, conditions, exact engine keys, duplicate declarations and invalid addresses have existing tests. |
| Routing | False conditions filter; no matching routes drains normally. Thrown conditions still fault execution. Router's Unmatched output is a distinct component behavior. |
| Fanout and backpressure | FlowOutput contains substantial code for bounded fanout and native source protocol handling. Replacing it with a plain native block solely to shorten code would change guarantees. |
| Coordination | FlowRequestReply/PendingRequests retain deadline, reply-address/correlation matching, cancellation and bounded pending capacity. Retry policy remains in the Polly component. |
| Component behavior | Reviewed Gate/Router, Mapper, Source/Scheduler, Batch, Reducer, ChangeFilter, Deduplicate, Retry and RateLimit behavior against their current responsibilities and test suite. |
| Messages and expressions | Message lineage and lazy owned JSON remain distinct from component mapping. Engine adapters compile at Build; expression context is shared. No need for a central payload deserializer was established. |
| Cleanup | Searches found no production references to the removed definition tree, activation context, DocumentLinks, processing profiles, generic pending exchanges or per-link delivery policy. |

Runtime synchronization is still necessary for concurrent Dataflow processing, request
deadlines, shutdown and lazy provider resolution. Its presence is not evidence of a
configuration-registration design defect.

## Lifetime limitation verified, not counted as a new defect

The existing contract requires stopping/draining Application before disposing the
provider. This is documented by BuildApplicationAsync and `docs/08-runtime-states.md`.
The normal probe followed Application.DisposeAsync then provider.DisposeAsync and
completed successfully on both targets.

A separate negative probe intentionally omitted Application stop/dispose. Provider
disposal was still pending after two seconds: it reaches an event bridge before its
component, and the bridge awaits component completion. Thus provider disposal alone
must not be advertised as application shutdown. DI remains the sole owner of component
and resource disposal. Any later proposal to remove the stop-before-provider contract
needs an explicit lifetime change; adding another owner is not a cleanup fix.

## Follow-ups that do not block setup

- System.Events.Output and System.Diagnostics.Output remain recognized address shapes,
  but this branch does not automatically register those system sources. Accepted address
  syntax does not imply a working system stream. Revisit this API surface when designing
  application observation; do not promise these endpoints in setup examples.
- AGENTS.md and historical reports contain superseded requirements. The current docs
  entry point and newest decisions distinguish these, but consolidating active guidance
  would reduce the risk of reintroducing removed concepts.
- FlowApplication and FlowOutput remain the main concentration of runtime logic. There
  is no evidence here that splitting them into more context/builder/manager classes
  would improve their responsibilities. Fix the confirmed defects before further redesign.

## Evidence and limits

- Full Release solution run: **1,908 passed, zero failed**, across 28 target test runs
  (.NET 8 and .NET 10). Core: 244 per target; Configuration: 237 per target.
- Full run log: `artifacts/cleanup-review-20260920/pre-setup-full.log`.
- Independent diagnostic console: `artifacts/pre-setup-review-20260923/ReviewProbe.csproj`.
  Logs: `probe-net8.log` and `probe-net10.log` in that directory. It is outside the
  solution and references the current Configuration project. Its deliberate negative
  lifetime probe uses a bounded wait so the diagnostic process can exit.
- Existing tests pass despite the three findings; they do not cover these input cases.
- Earlier isolated package-consumer checks belong to the delivery-policy removal step;
  packaging was not rerun in this review because no production/package change was made.
- This was a source, dependency, documentation and executable-regression review, not
  a new throughput benchmark, long-running soak test or deployment/broker integration test.
  Passing tests do not prove absence of every concurrency fault or performance issue.

Next sequence: fix named-port discovery, align Events registration, align engine-key
handling, add focused regressions, rerun validation, then continue the remaining setup
adapters. No additional framework layer is needed for these corrections.
