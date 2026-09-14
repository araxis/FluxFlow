# Lazy FlowValue and component-owned conversion — step 78

FlowValue is now an abstract class with one public Json property and one protected
CreateJson method. JSON is created once on first access through thread-safe Lazy,
validated against Undefined and cloned into owned immutable storage. No From,
FromJson, Deserialize, Kind, ToJsonElement or record-copy API remains on FlowValue.
JsonFlowValue is the concrete dynamic representation and owns its input snapshot
immediately; JsonFlowValue.Null represents JSON null.

## Component and message behavior

Removed IFlowValueMaterializer, FlowValueMaterializationResult,
FlowValueMaterializers and JsonFlowValueMaterializer, including their injectable
constructor parameters. Component-local conversion uses System.Text.Json directly;
HTTP and MQTT keep small internal content parsers rather than a shared materializer
protocol. FlowContent.FromJson is the retained narrow converter for exact bytes,
plain text and JSON content. Serializer options are cached in production classes.
Join's optional JSON fields are handled by Join's own output serialization policy.

HttpClientRequest, MqttPublishMessage and FlowError now derive from FlowValue.
HTTP/MQTT keep typed request identity without serialization; their JSON fallback
uses component-owned generated metadata. FlowContentJsonConverter is public so
external/component generated metadata can use its converter. All these value
classes require immutable state; existing collection snapshots are preserved.
Class inheritance prevents record-with copies from reusing another instance's
lazy cache. Other component models can remain internal CLR types and use dynamic
JsonFlowValue at their boundary; not every existing DTO was converted into a subclass.

The canonical non-generic FlowMessage stores one payload. On failure, Value and
Error reference the same FlowError. IsError remains explicit, so an ordinary
FlowError value does not automatically become failure control flow. TryGetValue
still returns only the successful case. Original exceptions remain in diagnostics.
Existing persisted message JSON keeps its value/error slots and discriminator;
no durable-record format migration is required. The generic message/adapters and
Create<T> overload remain; use a FlowValue-typed variable/cast when calling Create
for a canonical message. This step does not claim to remove every generic adapter.

Typed forwarding and generic JsonElement adapters preserve their existing payload
identity. Event attributes/details use the value's chosen JSON representation.
Serialization is no longer imposed on a typed value until JSON is needed.
No throughput or allocation speedup claim is made without a numerical benchmark.

## Failure containment

Component execution still catches conversion failures, emits FlowError and retains
the original exception for diagnostics. The Serialization pipeline never reads
failing JSON again while reporting that failure, preventing duplicate diagnostics.
Engine outputs with capture configured prepare the portable JSON before capture:
a conversion failure is reported with its original exception and converted into
an error message which is captured and delivered. Subsequent input continues.
Outputs without capture do not request JSON; actual store/capture failures retain
the existing fail-closed behavior. Host accesses outside these execution boundaries
remain the host's responsibility. Lazy caches the original conversion exception.

## Qualification

Final Release solution regression: **4,630 passed, zero failed/skipped, 76 reports**.
The final build/test run has no compiler warnings/errors. This includes normal
public API baseline validation, all 533 Engine cases and 202 release conventions.
The API baseline was explicitly reviewed and updated for these breaking changes.
New cases cover concurrent one-time conversion, owned dynamic JSON, typed forwarding,
portable error representation, generated request metadata, JSON failures followed
by healthy input, event projection, and optional output-capture behavior.
Obsolete materializer-protocol and record-copy assertions were removed/replaced;
component shape, content, rejection, diagnostics and lineage checks were migrated.
The total is three higher than step 77, not a count of only newly added tests.

Final candidate **0.0.0-local.20260914.value.2** contains **63 packages**.
Independent package consumers passed on **net8.0 and net10.0**, verifying all
45 registrations across 19 families, a typed-value workflow, lazy JSON concurrency,
external generated JSON metadata and error roundtrip. Their dependency assets
contain 43 FluxFlow packages and zero project references. Comprehensive package-only
acceptance also passed on both frameworks, including JSON/C# Engine construction,
resources, health and SQLFile separate-process restart/recovery/idempotency; those
fixtures resolve 10 FluxFlow packages and zero project references. Every resolved
FluxFlow dependency uses the final candidate version.

The maintained acceptance helper remains net8.0; the net10 run uses an artifact-only
retargeted fixture/helper. The expected nonpackable HttpTrigger sample pack warning
is not a library failure. The local regression retains the configured exclusions
for Acceptance, ContainerIntegration, ExternalSmoke and TSql.IntegrationTests.
No new external-service, whole-framework trimming/AOT or performance conformance
is claimed. No package publication or external consumer adoption occurred.

Evidence: artifacts/lazy-flowvalue-20260914/verification.json contains test reports,
resolved dependency identity and SHA-256 for all final packages. See
regression-capture-final.log and its TRX directory, api-baseline-reviewed.log,
pack-final.log, families-final-net8/net10.log and acceptance-final-net8/net10.log.
Earlier migration attempts and the superseded value.1 candidate remain recorded;
they are not the final qualification. Focused runs are not added to the total.

## Change accounting and documentation

Counts compare this step's captured C# source with the result, excluding generated
code, project files, docs and API baseline data. Renames count as removed/added paths.

| Area | Changed paths | Lines removed | Lines added | Net |
| --- | ---: | ---: | ---: | ---: |
| src | 81 | 714 | 526 | -188 |
| tests | 181 | 1605 | 1824 | +219 |
| samples | 10 | 29 | 37 | +8 |
| benchmarks | 6 | 13 | 19 | +6 |
| eng | 3 | 10 | 12 | +2 |

Current usage and migration are documented in docs/20-flow-data-contracts.md,
Core/package READMEs and the component authoring/migration guides. Samples,
benchmarks and package acceptance use the current API. Historical execution
records are preserved and marked/superseded rather than treated as current APIs.

This value/conversion refactoring is complete. Further DTO specialization is an
optional measured development choice; a new global serializer or type registry
is not required. FluxMQ-NG adoption and release/version selection remain separate.
The inherited modified tree was preserved; no commit, push or FluxFlow.UI edit
was performed.
