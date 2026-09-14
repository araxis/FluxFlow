# Incremental simplification

User-authorized sequence: (1) simplify framework execution and component development,
then migrate existing components; (2) implement standard tracing, metrics and logging;
(3) host integration and SSE. Deliver short, independently verified steps.

Keep component deserialization local, use small shared converters only where needed,
and retain a path for System.Text.Json source-generated metadata. Preserve reliable
delivery, bounded capacity, concurrency, request/reply and graceful drain. Existing
diagnostics remain functional; new OpenTelemetry integration is deferred.

## Step 1: processing exceptions become data consistently

FlowTransformNode now converts a processing exception into node.processing_failed,
matching FlowNode, instead of faulting the entire node. Both use one internal error
conversion helper. Incoming error forwarding, native transform ordering and transport
queues remain unchanged. Explicit Fault and stopping cancellation retain their
termination behavior. This is an intentional behavior change, not a claim of purely
behavior-preserving cleanup. Transport failure recovery is outside this step.

Tests cover an in-flight peer surviving an item failure, a subsequent successful
message, preserved trace/correlation/causation, explicit fault shutdown and existing
tracing behavior. Nodes suite: 296 tests on each of net8.0 and net10.0 (592 executions,
not 592 distinct cases). This step does not claim whole-repository qualification.

## Step 2: payload-only one-result processing

Added FlowValueTransformNode, a thin base over the existing FlowTransformNode. Authors
implement ValueTask<FlowValue> ProcessAsync(FlowValue, CancellationToken). The adapter
creates the derived envelope; existing execution handles errors, tracing, ordering
and delivery. It adds no queues of its own, deserializer registry or event bus.
JSON null is FlowValue.Null; an invalid null reference becomes processing error data.

Migrated UppercaseNode and LabelNode in FluxFlow.FluentSample, leaving their local
deserialization intact. Their combined source change is +6/-12 lines (6 fewer); the
new 23-line Core base means this step does not reduce total production source yet.
The sample now uses the existing transform runtime, whose queue structure differs
from FlowNode; no performance improvement is claimed or measured in this step.

Validation: 300 Nodes tests passed on each of net8.0 and net10.0, with no build
warnings. Four new cases cover value identity without reserialization, envelope
relationships, incoming error bypass, invalid conversion/null result recovery,
concurrent ordered output and the lifetime token. The existing Fluent sample ran
successfully: ALPHA/BETA/GAMMA and all six even/odd labels reached their sinks.

Next: review a real component family's one-result processing wrapper, preserving
its domain error codes and diagnostic events before migrating it. Components needing
message metadata or multiple outputs retain envelope-aware paths. Do not expand the
payload-only base into a general component manager to accommodate every shape.

## Step 3: MQTT-local publish deserialization

Simplified MqttPublishMessageMaterializer from 99 to 73 physical lines (+17/-43).
It now deserializes directly from FlowValue with component-local System.Text.Json
options and a private FlowContent converter. Removed the whole-request JSON string,
mutable JsonNode tree, content replacement and content serialization back into that
tree. Text goes directly to UTF-8; structured content uses JSON bytes; canonical
binary content uses the existing FlowContent converter. No global deserializer was
added. Existing injectable materializers and publish events are unchanged.

This step intentionally addresses the local conversion path before migrating the
publish node's execution base: the latter has domain errors and correlated events
that the payload-only base cannot yet express without losing behavior.

Validation: MQTT unit suite 138 passed, composition suite 35 passed on net10.0;
MQTT library built on its supported targets with zero warnings/errors. Eight new
cases cover object/array/number/boolean content, exact binary bytes, optional fields,
case-insensitive fields, missing required fields and invalid binary data. No real
broker test or performance benchmark was run; reduced conversion work is structural
evidence, not a measured throughput claim. Invalid-input diagnostic reason text
comes from System.Text.Json; MQTT error code/category remain unchanged.

Next: simplify the publish execution wrapper while preserving its explicit domain
errors, correlated events and bounded delivery behavior. OpenTelemetry/SSE remain
deferred to phases 2/3.

## Step 4: MQTT publish one-result execution

MqttPublishOperationNode now derives from FlowTransformNode. Removed the result-send
wrapper, redundant input-error bypass and duplicate capacity validation: +3/-19,
16 fewer production lines. The component retains envelope access for domain errors
and correlated diagnostic events; it deliberately does not use the payload-only
base. Materializer injection, error codes and event fields remain intact.

This changes the public base type (callers assigning this node to FlowNode must use
IFlowComponent or FlowTransformNode). It also adopts the existing transform/forwarder
queue layout, increasing buffering stages relative to FlowNode. Capacity remains
bounded, but identical aggregate buffering/backpressure timing and performance are
not claimed. Review that execution layout before broad component migration.

Validation: MQTT 139 and composition 35 passed on net10.0; Nodes 300 passed on each
of net8.0/net10.0, with no build warnings. Added exact upstream-error forwarding
coverage and strengthened existing failure/recovery tests to check domain event
names, correlation and attributes. Existing MQTT trace-context tests passed.
No broker integration run or whole-repository qualification was performed.

Next: examine the single-result runtime's forwarding stage before migrating more
components, preserving reliable fan-out, ordering and bounded admission. Avoid
trading shorter component code for unnecessary runtime queues.

## Step 5: remove the transform forwarding block

Removed the intermediary ActionBlock and LinkTo from FlowTransformNode. A single
asynchronous reader uses native ReceiveAllAsync and awaits each FlowOutput admission
before receiving the next result. It holds at most one result and has no queue of
its own. The existing TransformBlock still owns concurrency and ordered results;
FlowOutput still owns reliable fan-out. No new execution modes or options were added.
This supersedes step 4's extra-forwarder-queue limitation, but is not a claim that
TransformBlock buffering is identical to the original ActionBlock-based FlowNode.

FlowProcessingLifetime now tracks one processing block and joins the result reader
on both success and failure, before resource disposal. Delivery failure faults the
existing lifetime, which stops blocked admission. No independent lifetime algorithm
or custom target-block protocol was introduced. Source text is slightly longer due
to explicit reader/error handling; the removed complexity is a runtime block/queue
and its link, not a net line-count reduction.

Validation: 302 Nodes cases passed on each of net8.0/net10.0; MQTT 139 and composition
35 passed on net10.0. The linear/branched sample ran successfully. New tests pin the
bounded backlog under a slow fan-out consumer, delivery of all messages to both
consumers in order, and shutdown while output/admission are blocked. Builds emitted
no warnings. No performance benchmark or full repository qualification is claimed.

Next: continue small component simplifications using this fixed execution path;
preserve local conversion, domain diagnostics and existing behavior in each step.

## Step 6: remove serialization-family redundancies

The MQTT control node already uses the single-result runtime, so no change was
needed there. Cleaned SerializationPipeline, shared by the six JSON/text/Base64
components, without changing its base class or queue layout. Removed duplicate
upstream-error handling (FlowNode owns it), repeated option validation and the
unused Options property. Rejection diagnostics now read FlowValue.Kind directly,
instead of cloning the JSON value just to inspect its kind. Net change: +3/-13,
10 fewer production lines. Local converters and domain diagnostics remain intact.
Existing-error delivery failures now follow FlowNode's terminal forwarding policy
rather than passing through the generic processing-error conversion path.

Validation: 17 serialization tests and 16 composition tests passed on net10.0;
the library built for net8.0/net10.0 with zero warnings/errors. Existing coverage
includes all converter round-trips, exact bytes, original error-envelope forwarding,
invalid options, rejection/recovery, domain events and fan-out. No new abstractions,
execution modes or telemetry features were introduced.

Next: continue identifying redundant component plumbing; preserve meaningful domain
logic rather than forcing every component onto the payload-only base.

## Step 7: remove three copies of upstream-error forwarding

StorageOperationPipeline, FileSystemOperationPipeline and SessionOperationPipeline
now rely on FlowNode's default upstream-error forwarding. Removed their HandlesErrors
override and identical forwarding branch: 8 fewer lines each, 24 fewer total.
No base types, queues, converters, domain events or session finalization hooks changed.
As in step 6, failed delivery of an existing error follows Core's terminal forwarding
policy rather than producing a replacement processing error.

Validation: Storage 37, FileSystem 51 and Sessions 49 tests passed on net10.0
(137 total). Existing direct-port tests verify exact error-envelope identity across
these families, alongside successful operations and error recovery. All three
libraries built for net8.0/net10.0 with zero warnings/errors. Scope is these local
suites, not external-service or whole-repository qualification.

Next: inspect remaining envelope adapters for removable work, preserving components
that intentionally inspect/recover errors or create new error lineage. Do not remove
HandlesErrors indiscriminately from those components.

## Step 8: simplify the component author's starting point

TemplateEnrichNode now reads its internal model directly with System.Text.Json
through FlowValue.Deserialize and component-local options. Removed InputMaterializer
inheritance and the conversion-result wrapper from the example's development path.
The local catch maps malformed input to the existing domain code, category, message
and target/reason details. Output acceptance still precedes its diagnostic event;
the base, queue layout, clock and upstream-error forwarding are unchanged.

Updated the template README to show the smallest payload-only component first and
explain why the richer template retains envelope access and explicit domain errors.
This improves the concepts an author must learn, rather than merely shortening a
file: node source grew by 5 lines (+16/-11) as error mapping became explicit locally.
No serializer registry, new framework contract or mandatory interface was added.

Validation: all 9 template tests passed on net10.0, including direct Core application
request/reply composition. The template builds on its net8.0 target without warnings
or errors. Existing malformed-input tests now also verify domain details and warning
events. This is not full repository qualification or a performance claim.

Next: review one production component's internal typed-message adapter, identifying
what can become plain internal data while preserving lineage and domain diagnostics.

## Step 9: remove generic message adapters from Storage operations

The four Storage operations now receive (FlowMessage message, TRequest input,
CancellationToken) and return the canonical FlowMessage. StorageOperationPipeline
has only the input type parameter; it no longer builds FlowMessage<TRequest>, receives
FlowMessage<TOutcome>, or converts that envelope back to the wire message. Domain
outcomes become FlowValue locally. StorageNodeSupport.CreateEvent no longer needs
a generic message parameter. Public constructors/ports and execution queues are
unchanged; request materialization remains local to the Storage package.

This removes two intermediate envelope constructions on successful materialized
operations (and the corresponding wrappers for domain failures), without changing
the single derived-result lineage. Outcome serialization now occurs inside each
operation's existing domain-error boundary; an exception during that serialization
is classified by the operation rather than by the outer generic processing catch.
No global mapping framework or new result abstraction was introduced.

Validation: Storage 37 and composition 2 tests passed on net10.0. The library builds
on net8.0/net10.0 without warnings/errors. Existing direct-operation tests now also
verify new result MessageId and preserved custom headers for all four operations;
correlation, trace, causation, exact content bytes and ownership checks remain.
No allocation/throughput benchmark or external-store qualification was performed.

Next: apply the plain-request pattern to another family only after checking its
event and finalization contracts; review remaining public component plumbing.

## Step 10: plain requests for file read/write

FileReadNode and FileWriteNode now receive their plain request alongside the original
canonical FlowMessage. FileSystemOperationPipeline has one input type parameter and
no longer constructs typed input/result envelopes. Each operation creates its
FlowValue result locally. Diagnostic helpers accept the canonical message directly.
Public constructors, ports, queues, path checks, byte limits, cancellation and file
I/O algorithms are unchanged. Watch/enumeration sources were not part of this step.
Result serialization is now inside the operation's existing try/catch; its existing
exception classifier still determines whether an exception is a domain failure.

Validation: all 51 FileSystem tests passed on net10.0, including real temporary-file
read/write round-trips. The direct graph test additionally verifies retained custom
headers and new result identities for both operations. Existing lineage, error
forwarding and exact-byte assertions pass. Library builds on net8.0/net10.0 have
zero warnings/errors. No throughput benchmark or whole-repository qualification.

Next: examine Sessions' analogous adapters, especially finalization, before changing
them. Continue preserving domain contracts instead of adding general-purpose layers.

## Step 11: plain requests for session recording/query

User clarified that access to the original FlowMessage is useful and acceptable;
do not treat hiding the envelope behind a processing context as a mandatory goal.

SessionRecorderNode and SessionQueryNode now receive the original FlowMessage plus
their plain typed request and return a canonical result. SessionOperationPipeline
has only an input type parameter; intermediate typed request/result envelopes are
removed. Outcome serialization is local and inside each operation's domain-error
boundary, as in Storage. Public APIs, sequencing, lazy session creation, cancellation,
diagnostic events and replay behavior were not redesigned.

The existing finalization callback remains attached to OnInputCompletedAsync.
SessionCompleted, store-close error handling and caller ownership of the store
remain unchanged. No new context object, queue or shared deserializer was added.

Validation: all 49 Sessions tests passed on net10.0; builds for net8.0/net10.0 emitted
no warnings/errors. The direct graph test verifies record/query results, retained
custom headers, new result identity, lineage, session finalization and store ownership.
No full repository qualification or performance benchmark was performed.

Next: review the remaining component construction/port plumbing for simplifications
that improve development, without eliminating useful envelope access or domain APIs.

## Step 12: separate Storage parsing from shape metadata

StorageOperationPipeline deserializes TInput locally with System.Text.Json options
and maps malformed input to the existing domain error. Its constructor now accepts
an operation name instead of a materializer instance and a separate rejection-event
name. Removed four public materializer subclasses; their identical shape descriptions
are available as StorageInputShapes metadata and Composition now uses those directly.
Runtime no longer allocates materializer/result wrappers or instantiates input shapes
for request execution. This is package-local logic, not a new shared deserializer.

Source-breaking removal is documented in the Storage README, including replacements
for shape access and direct Materialize callers. No compatibility wrappers added.
Repository and sibling UI C# searches found no remaining callers of the removed
types. External consumers were not qualified; previously built feeds remain older
artifacts and must be rebuilt before consumer adoption.

Validation: 45 Storage cases (including eight new malformed-input cases across all
operations) and 2 composition cases passed on net10.0. Error code/category/details,
correlation and rejection events remain checked. No performance claim is made.

Next: review similar metadata/execution coupling in other component families while
keeping each migration independently reviewable and documenting any public removals.

## Step 13: local FileSystem parsing and independent metadata

Read/write requests deserialize directly in FileSystemOperationPipeline using local
System.Text.Json options. Its constructor takes an operation name rather than a
materializer and event-name pair. Removed FileReadRequestMaterializer and
FileContentWriteRequestMaterializer; Composition uses FileSystemInputShapes.Read/Write
with the same descriptions and required properties. The README documents this
source-breaking change and migration for metadata and direct Materialize callers.

Preserved filesystem.read/write.invalid_shape error codes, FileSystem category,
target/reason details and file.read/write.rejected events. Component APIs, queues,
actual file operations, cancellation, watch and enumeration are unchanged. No shared
deserializer, context abstraction or compatibility wrapper was added.

Validation: 55 FileSystem tests passed on net10.0, including four new null/scalar
rejection cases and existing actual file I/O tests. Runtime and Composition build
for net8.0/net10.0 with zero warnings/errors. No external-consumer qualification or
performance benchmark. Repository and sibling UI C# callers were checked before
removing the types; package-only consumers need a freshly built coordinated feed.

Next: review Sessions' corresponding parsing/metadata classes, keeping session
finalization and replay contracts intact.

## Step 14: local Sessions parsing and independent metadata

SessionOperationPipeline now deserializes recording/query requests locally with
System.Text.Json. Removed two materializer classes; Composition reads the unchanged
descriptions from SessionInputShapes.Record/Query. Component developers no longer
need a materializer subclass to connect request parsing with metadata. No shared
deserializer or compatibility wrapper was introduced.

The pipeline keeps explicit error and rejection-event names because the existing
contracts intentionally differ: sessions.record.invalid_shape versus
session.recorder.rejected. Category, target/reason details, correlation and error
lineage are preserved. Finalization callbacks, store ownership and replay code are
unchanged. The README documents the public source-breaking removals and migration.
Repository and sibling UI C# searches found no remaining references to those types.

Validation: all 53 Sessions tests passed on net10.0, including four new null/scalar
cases checking rejection without creating a session. The new query tests also
consume and verify the existing constructor startup event before checking rejection.
Runtime and Composition build for net8.0/net10.0 with zero warnings/errors.
No full repository or external-consumer qualification or performance benchmark.

Next: review remaining framework/component construction plumbing for a bounded
simplification that improves development. OpenTelemetry and host/SSE remain later
phases; phase 1 is not yet complete.

## Step 15: remove duplicate Storage request guards

The four private Storage operation handlers are called only by
StorageOperationPipeline, which already rejects null/deserialization failures before
invoking them. Removed their unreachable null-request branches, repeated envelope
null guards and unnecessary null-conditional request access. This removes 32 source
lines across Put/Get/Query/Delete without adding an abstraction or changing public
APIs. Each handler now begins with operation-specific normalization and processing.

Field validation, option fallbacks, store-result validation, domain error mapping,
cancellation and diagnostics remain in place. The existing null/scalar tests verify
that malformed requests still receive storage.*.invalid_shape and rejection events.

Validation: all 45 Storage and 2 Storage Composition tests passed on net10.0;
runtime and Composition builds passed on net8.0/net10.0 with zero warnings/errors.
Existing tests cover this removal; no implementation-mirroring tests were added.
No full repository qualification or performance benchmark was performed.

Next: examine the corresponding private request handlers in FileSystem and Sessions
for the same redundant checks, retaining validation at actual external boundaries.

## Step 16: remove duplicate FileSystem and Sessions request guards

Removed unreachable null-request branches and repeated envelope guards from the
four private handlers for file read/write and session record/query. Their only
callers are the local pipelines, which reject null or malformed input before
invoking operation code. FileSystem's private error/event helpers now accept
non-null requests and access them directly; nullable content and fields retain
their existing handling. Net reduction: 33 source lines across four files.

Public APIs, field/content validation, cancellation, domain errors and diagnostics
are preserved. Session finalization, replay and store ownership are unchanged.
No new abstraction or shared deserializer was added.

Validation: 55 FileSystem and 53 Sessions tests passed on net10.0, including existing
null/scalar rejection cases, actual file I/O and session lifecycle tests. Both
runtime/Composition pairs build on net8.0/net10.0 without warnings or errors.
No new tests were needed for these unreachable-branch removals. No full repository
qualification or performance benchmark was performed.

Next: review remaining materializer-based components for a meaningful reduction in
the steps needed to implement an operation, beyond duplicate-guard cleanup.

## Step 17: local Metrics parsing and independent input metadata

MetricsAggregateNode now deserializes MetricSampleInput directly using local
System.Text.Json options. Removed MetricSampleInputMaterializer and its instance
field/construction; Composition uses MetricsInputShapes.Sample with the identical
description. Removed the now-unreachable sample-null branch after successful
parsing. Developers can follow sample conversion and rejection in the node without
opening a materializer subclass and generic base implementation.

The existing metrics.sample.invalid_shape error, Metrics category, target/reason
details and metrics.aggregate.rejected event are preserved. Aggregation, ordering,
group limits, final snapshots and tracing remain unchanged. Typed envelope adapters
remain for a separate bounded step. No new shared serializer mechanism was added.

The README documents the source-breaking public materializer removal and migration.
Repository and sibling UI C# searches found only the migrated runtime/Composition
callers. External package consumers still require coordinated rebuilt artifacts.

Validation: all 53 Metrics tests passed on each of net8.0 and net10.0, including
three malformed-input cases checking error/event contracts and a subsequent valid
sample counted exactly once. Existing aggregation and trace tests passed. Runtime
and Composition builds passed on both targets with zero warnings/errors. No full
repository qualification or performance benchmark was performed.

Next: simplify Metrics' intermediate typed message conversions while retaining the
original envelope, aggregation behavior and final-snapshot trace linkage.

## Step 18: use canonical messages throughout Metrics processing

MetricsAggregateNode keeps its original FlowMessage and local MetricSampleInput
separate. Removed the typed request-envelope conversion and EmitResultAsync adapter;
successes emit message.With(FlowValue.From(snapshot)), and domain failures emit
message.WithError(error). Group-limit and failure helpers take the original envelope.
Net reduction: 9 source lines, with no public API changes.

Completion tracing uses the last accepted canonical input directly, preserving the
existing single correlation link and new result identity. Coalesced mode retains
one original envelope, including its JSON payload, instead of one typed sample
envelope; it is cleared after final emission or disposal. No contributor history
is retained. Memory and throughput effects were not benchmarked. Serialization
remains within the existing operation try/catch boundary, and telemetry code and
error forwarding behavior were not redesigned.

Validation: all 53 Metrics tests passed on each of net8.0/net10.0. Existing trace
tests cover sampled/unsampled operation, final linkage, headers, lineage, listener
failures and no-final-snapshot cases. Aggregation, group-limit and malformed-input
recovery tests also passed. Runtime and Composition build on both frameworks with
zero warnings/errors. No full repository qualification was performed.

Next: review State and Projections for the same unnecessary typed-envelope and
materializer indirection, choosing one bounded component change.

## Step 19: canonical messages throughout Projections processing

EventProjectionNode keeps the original FlowMessage beside its parsed ProjectionEvent.
Removed typed request/result envelopes and EmitResultAsync; snapshots and errors
emit directly. The failure helper receives the event separately for error details.
The final snapshot derives directly from the last matched envelope or creates an
independent canonical message when there were no matches. Public APIs are unchanged.

Filtering, observed/matched counts, rolling rates, preview handling, diagnostics,
ordering and error forwarding are preserved. Input materialization remains for a
separate step. One original envelope, including JSON payload, is retained instead
of one typed event envelope; its existing lifetime was not redesigned. No memory
or throughput benchmark was performed. State's optional public custom-materializer
parameter was identified and left unchanged for a separate contract review.

Validation: all 25 Projections tests passed on net10.0. Added empty/filtered-only
final-snapshot cases and strengthened last-match final lineage/header assertions.
Existing contract immutability, filtering, fan-out and error recovery tests passed.
Runtime and Composition builds passed on net8.0/net10.0 with zero warnings/errors.
No full repository or new telemetry qualification was performed.

Next: move Projections' simple JSON materializer into the node and separate input
shape metadata, following the already established Metrics pattern.

## Step 20: local projection-event parsing in both consumers

Dependency inspection found Expectations also used ProjectionEventMaterializer.
Both EventProjectionNode and EventExpectationNode now deserialize ProjectionEvent
locally with System.Text.Json Web options. Removed the public materializer and
its runtime fields/construction; both Composition packages use the unchanged
description and required-property metadata in ProjectionInputShapes.Event.
No new shared deserializer was introduced. Projections' unreachable post-parsing
null check was removed. Expectations' typed envelopes and timer machinery remain.

Preserved projection.event.invalid_shape, Projections category, target/reason error
details, and each component's existing rejection event. Projections rejects and
continues without counting malformed events; Expectations resolves exactly once
and ignores later input. Constructors and ports are unchanged. Both READMEs
document the public source-breaking class removal. Repository and sibling UI C#
searches found no remaining references; external consumers were not qualified.

Validation: 27 Projections, 18 Expectations and 11 Expectations Composition tests
passed on net10.0. Malformed-input tests now cover null, scalar and invalid timestamp
values, error details and lineage, plus recovery or one-time resolution as appropriate.
Both runtime/Composition pairs build on net8.0/net10.0 with zero warnings/errors.
No full repository qualification or performance benchmark was performed.

Next: review State's typed-envelope plumbing while preserving its public optional
custom materializer contract; avoid mixing that API decision with internal cleanup.

## Step 21: canonical messages through State operations

StateReducerNode<T> passes the original FlowMessage beside the materialized
StateReducerInput<T>. Removed intermediate typed request/result envelopes and the
success/error conversion branch in ProcessAsync. Reduce, Reset, key-limit handling
and diagnostics receive the canonical envelope; request-dependent diagnostics get
the typed request explicitly. Successes use With(FlowValue.From(result)); failures
use WithError. No context object or new abstraction was introduced.

The public optional custom materializer parameter, default materializer, typed state,
expression evaluation, state mutation order and error handling remain supported.
Result serialization stays outside the domain-error catch, preserving its existing
framework-error classification. Public APIs are unchanged.

Validation: all 19 State tests passed on net10.0. A new custom-materializer test
accepts scalar input, rejects a negative value with a custom error, then checks
recovery, version/state, original headers and lineage, and operation diagnostics.
Existing Reduce/Reset/Clear, key-limit and reducer-failure cases passed. Runtime and
Composition build on net8.0/net10.0 with zero warnings/errors. No full repository
qualification or performance benchmark was performed.

Next: review Expectations' typed-message conversions and timer/completion ownership
before choosing a bounded simplification there.

## Step 22: canonical messages through Expectations resolution

EventExpectationNode now keeps the original FlowMessage beside its parsed event.
Removed the typed event-envelope conversion, typed error conversion and redundant
result conversion. ClaimEvaluationFailure takes the parsed event explicitly for
error details; ClaimSuccess creates or derives a canonical message directly.

The lock, resolved flag, timer-release ownership, bounded timer-resolution queue,
completion drain and disposal paths are unchanged. One original envelope including
JSON payload replaces the previously retained typed envelope; retention lifetime
was not redesigned. Public APIs, matching/guard semantics and diagnostics remain.
No new abstraction or telemetry behavior was added.

Validation: 20 Expectations and 11 Expectations Composition tests passed on net10.0.
Expanded timeout tests cover Expect/Guard with and without observed input, verifying
headers, trace/correlation IDs, causation, new output identity and no duplicate on
completion. Existing timeout/completion race, match-wins, malformed-input, fan-out
and bounded event-history tests passed. Runtime and Composition builds passed on
net8.0/net10.0 with zero warnings/errors. No performance or full repository
qualification was performed.

Next: inspect HTTP's request-body conversion for unnecessary JSON reconstruction,
preserving text, binary and JSON body behavior before changing its API.

## Step 23: direct HTTP request deserialization with a local body converter

HttpClientRequestMaterializer deserializes directly from FlowValue using a private
JsonConverter<FlowContent>. Removed whole-request stringification, JsonNode parsing,
body-property mutation and reserialization of normalized content. The small converter
handles text, exact FlowContent and arbitrary JSON; the exact-content branch uses
the existing FlowContent converter without recursively invoking itself. Public
materializer APIs, shape metadata and HTTP-owned error mapping are unchanged.

Missing/null bodies remain absent; text is UTF-8 text/plain; other JSON is compact
UTF-8 application/json; exact bytes and metadata remain intact. The input-object
check and its diagnostic reason are retained. No shared deserializer, new runtime
abstraction or performance claim was introduced.

Validation: all 36 HTTP and 8 HTTP Composition tests passed on net10.0, including
11 added cases for Unicode text, JSON objects/arrays/scalars, absent/null bodies,
case-insensitive request/content properties, headers/timeouts and invalid exact
content. Existing node tests verify actual outbound request bytes and metadata.
Runtime and Composition builds passed on net8.0/net10.0 with zero warnings/errors.
ASP.NET ingress code was untouched; no full repository or performance qualification.

Next: review HTTP's operation-level message conversions for a bounded simplification,
preserving request execution, response limits and cancellation behavior.

## Step 24: remove HTTP's duplicate framework plumbing

Inspection confirmed HttpClientNode already uses canonical messages; no typed
conversion migration was needed. Removed its HandlesErrors override and redundant
private error/null guard so FlowNode owns upstream-error forwarding. Constructor
options now reuse the validation already performed by CreateNodeOptions before
base construction instead of validating the same options twice. The local value
access is annotated non-null because FlowNode only invokes processing for values.

Request execution, concurrency, timeouts, cancellation classification, response
limits, custom materializer support and public APIs remain unchanged. Upstream
errors now use the framework's standard forwarding-failure semantics (terminal
forwarding failure instead of attempting to replace an existing error envelope).

Validation: 36 HTTP and 8 HTTP Composition tests passed on net10.0. Strengthened
the upstream-error test to verify the exact envelope is forwarded and only later
valid input sends an HTTP request. Existing invalid-option cases cover the retained
validation boundary. After the nullable annotation cleanup, runtime and Composition
build on net8.0/net10.0 without warnings/errors. No full repository qualification
or performance benchmark was performed.

Next: review the remaining framework/component structure and summarize the larger
remaining simplification opportunities before selecting the next bounded change.

## Review after step 24: select a structural target

Reviewed direct application/workflow APIs, component-local pipelines, Composition
metadata/activation and remaining custom materializer callers. Findings and ranked
opportunities are in ../reviews/2026-09-12-remaining-simplification.md.

Recommended next implementation: make internal Composition port construction
canonical, retaining kind/cardinality/input shape and the validation needed for
existing public metadata callers. The separate UI still consumes MessageType, so
public removals must be coordinated rather than assumed safe. Component delegation
is a second candidate, contingent on a clearer concrete authoring example without
a new universal base or serializer. Observability remains in its planned phase.

This checkpoint changed documentation only. No runtime edits or new test run;
previous per-step verification remains recorded above. Phase 1 remains incomplete.

## Step 25: canonical Composition metadata construction

Added ComponentPortMetadata.Create(name, optional linkCardinality) for canonical
business/event ports. Migrated all six internal RuntimeComponentRegistrationBuilder
construction sites and the existing shaped-input factory to this path. Component
authors no longer need to supply FlowValue as a selectable type when directly
describing canonical ports. This is an additive migration step, not a LOC reduction.

The existing Type-taking constructors, generic factories, MessageType property,
deconstruction and validation remain unchanged for public consumers, including the
separate UI. CreateSignal still uses the existing control-kind/object marker;
input shape and cardinality remain preserved. System-output metadata still uses its
supplied type because replacing it unconditionally would bypass existing validation.
No UI code or package feeds were changed.

Validation: all 273 Composition tests passed on net10.0. New compatibility cases
compare canonical metadata with the old generic factory, including normalization,
deconstruction, cardinality and signal distinction. Existing registration, shape,
link and activation tests passed. Composition builds on net8.0/net10.0 with zero
warnings/errors. No full repository or external UI consumer qualification.

Next: inspect remaining public metadata factory usage and legacy type-validation
dependencies before any removal; the canonical path is now available internally.

## Follow-up to step 25: verify the compatibility boundary

Source/sample searches confirmed canonical production callers are migrated. The
remaining generic factories are public compatibility paths; system-output metadata,
typed link predicates and compiler/preflight/activation type checks still have live
semantics. Removing them would require a deliberate contract migration. Findings
are appended to ../reviews/2026-09-12-remaining-simplification.md.

Migrated four canonical fixture sites in input-shape/link-compiler tests to the new
factory while preserving typed mismatch and compatibility coverage. All 273
Composition tests passed on net10.0. No production change, API removal or new build
qualification in this follow-up. The compatible metadata migration is complete.

Next: evaluate the second structural candidate from the review: whether one concrete
component's pipeline delegation can be simplified without adding a generic base,
duplicating parsing or altering lifetime behavior. Compare the implementation before
committing to a family-wide migration.

## Delegation comparison: retain the Storage pipeline

Created a complete review-only direct FlowNode candidate for StorageGetNode in
../reviews/StorageGetNode.direct.cs.txt and compared it in
../reviews/2026-09-12-storage-delegation-comparison.md. Current node: 137 lines;
candidate: 161. The 80-line family pipeline remains necessary for other operations.
The candidate removes forwarding but duplicates parsing/rejection/capacity setup,
changes the public base and changes the executing type observed by tracing.

Decision: do not adopt this candidate or add another generic base to compensate.
Production code is unchanged. The prototype was source-reviewed, not compiled or
tested; no behavior-equivalence claim. This closes the proposed comparison rather
than forcing a refactor that fails the agreed simplicity criterion.

Next: audit developer-facing examples/documentation for canonical API consistency,
then qualify the changed packages together before considering phase 1 complete.

## Documentation and coordinated validation checkpoint

Updated root/Composition/MQTT documentation, added docs/45-component-development.md,
and marked superseded migration guides historical. Current examples use canonical
runtime messages while optional typed definition/Engine helpers remain documented.

All 20 selected regression projects passed: 1,303 cases, 1,802 executions across
their configured targets. All ten affected component Composition projects and all
eight samples built without warnings/errors. Fluent and Composition samples ran
successfully. Exact scope and counts are recorded in
../reviews/2026-09-12-documentation-validation-checkpoint.md.

No production code changed in this checkpoint. No full solution, external UI/feed,
live-service or performance qualification was performed. The checkpoint is complete;
phase 1 completion and transition to OpenTelemetry remain an explicit next decision,
not something inferred from passing this selected validation set.

## Transition after the checkpoint

The user's subsequent instruction to proceed starts phase 2 with a bounded
exception-diagnostics step. Progress, exact coverage and validation are recorded in
2026-09-12-observability.md. The selected phase 1 checkpoint remains the baseline;
this does not expand its qualification to the entire solution or external UI.
