# MQTT trace-context boundary

## Finding and correction

The MQTT controller previously passed user properties unchanged, and the trigger
created envelopes under its subscription worker's ambient startup activity. Remote
trace context was therefore lost; missing/invalid context could inherit an unrelated
startup parent. Queue-level tracing alone did not satisfy this transport boundary.

The fix is local to MQTT: one internal mapping helper, one call before transport
publication and one call when the trigger creates its envelope. No Core source,
public API, SDK dependency, service registration or execution mechanism was added.
The platform's [ActivityContext.TryParse](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activitycontext.tryparse?view=net-8.0)
validates W3C parents. The carrier uses application metadata supported by the
[MQTT 5 specification](https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html).

Current W3C context replaces stale reserved `traceparent`/`tracestate` properties;
other properties and immutable content are preserved. With no current W3C activity,
the original publish message is used. The trigger imports valid context and
suppresses unrelated worker context. No baggage is copied; Last Will configuration
is untouched. Application TraceId/acknowledgement identity stays independent.
Hosts still own sampling/exporters and configure the existing Core activity source.

## Verification

[Nine new cases](../../tests/Unit/Components/Mqtt/FluxFlow.Components.Mqtt.Tests/MqttTraceContextTests.cs)
cover outgoing flags, immutability, no-context/hierarchical behavior, valid/invalid/
missing remote context and actual publisher-to-trigger processing span linkage.
The combined case runs real components/controller and queued downstream processing,
simulating only broker forwarding with the existing recording transport. It also
checks exact binary payload bytes. The family passes 130 tests.

[Six reproduced behavior failures](../../artifacts/core-refactoring/mqtt-context-20260911/red-final/red.trx)
establish the defect before enabling the two production call sites.
[Family results](../../artifacts/core-refactoring/mqtt-context-20260911/family/mqtt.trx)
and the [assertion review](../../.testagent/mqtt-context-review.md) record the verified
scope and fixture corrections. Earlier compile/casing/sampler attempts are retained
but excluded from final green evidence. Full-regression results are recorded in
the fresh `qualified-20260912` directory; the interrupted `qualified` directory is
incomplete and must not be counted as a complete gate.

## Full regression qualification — 2026-09-12

The fresh nonincremental solution build passed with zero warnings/errors. The
filtered local regression completed with **4,857 passed/executed tests**, zero
failures. Its 81 result files comprise 76 nonempty suites and five explicitly
excluded suites: durable-input/output server-database integrations, workflow
acceptance, external smoke and container integration. Zero executions in those
five are not integration passes. This is nine more local passes than startup.1's
4,848 checkpoint; fresh unfiltered discovery was not run in this slice.

[Verification](../../artifacts/core-refactoring/mqtt-context-20260911/qualified-20260912/verification.json)
records commands, successful exit codes, exact report hashes, excluded assembly
names and 1,490 unchanged source inputs during validation. The
[source delta](../../artifacts/core-refactoring/mqtt-context-20260911/source-delta.json)
is two MQTT source edits plus one helper and one test file; package README changes
are documented separately. Both rebuilt Core target assemblies match startup.1.

The verifier initially rejected the deliberately empty filtered suites. After
inspecting their assembly names and zero counts, its validation was corrected to
accept exactly those five exclusions while still requiring all 4,857 local passes.
Verification-only mode audited the already completed build/test commands and
unchanged sources; the successful tests were not rerun or their reports rewritten.
Bracketed result filenames are read with literal paths. The interrupted prior run
remains incomplete and excluded from this successful gate.

## Candidate impact

The frozen `0.0.0-local.20260911.startup.1` packages do **not** contain this fix.
Their existing feed and results remain historical. The subsequent
[context.1 candidate](2026-09-12-context-candidate.md) includes the correction and
passes package-only verification, including all nine existing context cases on
both frameworks. Real broker verification remains distinct.
Core measurements are not relabeled as measurements of changed MQTT behavior.

Real broker integration, HTTP platform tracing, observer retention/SDK overhead and
other outstanding cross-cutting checks remain distinct. This change does not make
the entire telemetry feature or overall refactor complete. Nothing was published
or sent to another consumer task.
