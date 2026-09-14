# Observability: incremental implementation

Current checkpoint: steps 34–71 completed locally, including Gate source validation. Steps 53–55 replace named
function resources with CallFunction, defer/remove Constants, and support workflow
connections in direct Composition applications. Revision-host replacement remains
deferred. Earlier next-step notes are historical; see the latest ledger entries.
The earlier package-consumer and local Collector evidence belongs to steps 34–48.

Step 42 additionally removes the explicit reply-registration prerequisite from
request APIs and adds direct address-based requests.
Step 43 removes the obsolete declarations and configuration path entirely; the
earlier note retaining optional declarations is superseded.
Step 44 adds C# MakeFunction delegates over the existing request API; JSON remains deferred.
Step 45 removes the definition timeout; callers control waiting through cancellation.
Step 46 retires the Fluent chain API in favor of direct application/workflow links.
Step 47 improves the existing flat JSON format; no structural redesign is adopted.
Step 48 adds three small Core extensions for idiomatic C# construction.
External telemetry backend integration is outside the current scope, not the next
required step. The separate Sessions concern remains deferred. See
docs/46-observability-host.md for usage and scope.

## User clarification â€” 2026-09-13

The user clarified that Datadog was a dictation misunderstanding, not a requested
integration. Prometheus, Elasticsearch and other external logging/telemetry systems
are possible consumer choices, but choosing or connecting them is not important
for the current work. The framework boundary is standard OpenTelemetry output and
live SSE, already verified locally. Do not treat vendor deployment as unfinished
framework work or automatically pursue it. Earlier Datadog next-step notes are
superseded by this clarification.

## Open user concern: revisit Sessions

The user has an unresolved concern about Sessions and explicitly asked to record
that it must be revisited. Details of the concern have not yet been provided.
The user subsequently clarified that the normal agreed refactoring plan must not
change: this note is a deferred revisit, not a pause or a replacement workstream.
Continue the approved exception diagnostics work while preserving the current
Sessions refactoring/design. Do not treat passing diagnostics tests as resolving
this concern. Discuss it with the user before a further Sessions design change.

Following the documentation/validation checkpoint and the user's instruction to
continue, phase 2 starts with one shared diagnostic hook. Full OpenTelemetry logs,
traces and metrics remain the goal; host/SSE integration remains phase 3.

## Step 1: original processing exceptions

FlowNode and FlowTransformNode report exceptions caught at their shared processing
boundary through DiagnosticListener. Existing FlowError conversion, forwarding,
cancellation and terminal-fault rules remain in place. Individual components need
no instrumentation code for exceptions reaching this boundary.

Contract in src/Core/FluxFlow.Core/Nodes/FlowDiagnostics.cs:

- Listener name: FlowDiagnostics.ListenerName (`FluxFlow.Core`).
- Event name: FlowDiagnostics.ProcessingExceptionEvent (`FluxFlow.Processing.Exception`).
- Payload: FlowProcessingException, containing the original Exception object,
  executing component type, message/correlation/business trace identifiers and
  current processing Activity when available.

Hosts discover the listener through DiagnosticListener.AllListeners and subscribe
with an event-name filter. Delivery is synchronous during the catch, after the
Activity is marked failed and before normal error conversion. A subscriber can
log the original exception or enrich the active Activity. It must finish Activity
enrichment during the callback; the Activity is stopped as processing exits.
The payload's TraceId is the message's business trace identifier; use Activity
for distributed trace/span identifiers when available.

The event works independently of Activity sampling. No diagnostic payload is
allocated when the event is disabled. Core does not queue or retain exceptions;
subscription lifetime, retention and export belong to the host/adapter. Workflow
value/header payloads are omitted. Exceptions from diagnostic callbacks are caught
to protect processing; delivery to every observer is not guaranteed if one throws.

Scope: exceptions already caught and converted inside a component do not reach
this hook. Source, control and lifecycle paths are not comprehensively covered.
This step adds no ILogger adapter, Meter instruments, SDK/exporter configuration,
Datadog integration or SSE endpoint.

## Verification

Release tests passed on both net8.0 and net10.0:

- Nodes: 310 per target, including eight new diagnostic scenarios.
- Core: 144 per target.
- Metrics components: 53 per target.

Total: 1,014 passing executions. New tests verify original exception identity,
inner exception/stack preservation, identifiers, tracing enabled/disabled,
throwing observers, continued processing and upstream error forwarding.

## Step 2: optional standard logging adapter

Added FluxFlow.Observability under src/Adapters/Observability, registered in the
solution and package manifest. Core and individual components are unchanged.
The package depends on Core and Microsoft.Extensions.Logging.Abstractions only.

The host calls `FlowDiagnosticLogging.Subscribe(loggerFactory)` once per process
and owns the returned IDisposable. The adapter discovers existing/future listeners,
logs Error-level ProcessingException events with the original exception and
structured component/message/correlation/business trace fields, and unsubscribes
on disposal without disposing the host's factory. In-flight callbacks may finish.
Logging happens inside the existing processing Activity and is also available
without tracing. No additional Activity, exception serialization or queue is added.
Logger failures are contained without retrying or interrupting other observers.

Release verification: six adapter cases passed on each of net8.0 and net10.0,
covering real node failures, exception/field preservation, optional ambient tracing,
disabled/throwing loggers, continuation, discovery timing, concurrent delivery and
disposal. All 310 existing node cases also passed on each target: 632 executions
for this step. Earlier Core/Metrics counts above belong to step 1.
Local pack succeeded. Inspected the archive in artifacts/observability-step2:
both target DLLs, README and expected Core/logging-abstractions dependencies are
present. This is an unpublished development package, not a qualified consumer feed.

Host usage and coverage limits are in the package README. This is a logging bridge,
not an OpenTelemetry SDK/exporter installation or a full solution qualification.

## Next bounded step

Continue component-local exception coverage one family at a time. Metrics and fuller
distributed tracing qualification follow separately; host/SSE remains phase 3.

## Step 3: Storage-local exception coverage

The user chose to retain DiagnosticListener and the disposable logging adapter for
now, with the architecture open to later review. No Dataflow telemetry path was added.

FlowDiagnostics.ProcessingException is now public for shared family handling paths.
It marks the current processing Activity failed, adds error.type only for full-data
sampling, and delivers the original exception through the existing listener. Call
once when handling an exception locally; leave exceptions rethrown to FlowNode's
boundary for that boundary to report. Diagnostic callbacks remain contained.

Storage reports at two shared locations: input deserialization in
StorageOperationPipeline and domain error creation in
StorageNodeSupport.CreateProcessingError. Four operation call sites now provide
the original message and component to that existing error creation path. There is
no logger, subscription or Activity creation code in the operations.

Existing storage.*_failed and storage.*.invalid_shape FlowErrors, transient flags,
lineage and workflow events remain unchanged. Operation failures identify the
concrete Storage node; parsing failures identify the executing family pipeline.
Wrapped domain exceptions retain their inner exception rather than flattening it.
Normal not-found outcomes, successful processing and forwarded errors do not report
new exceptions. Cancellation catch ordering is unchanged.

Verification: Storage 53 cases on net10.0 (eight new scenarios across all four
operations with/without tracing), Nodes 310 per target, logging adapter six per
target on net8.0/net10.0: 685 passing executions. Storage built on both targets with
zero warnings/errors. New scenarios verify original exception identity/stack/inner
exception, message identifiers, domain error codes, Activity failure, malformed
inputs, exactly-once reporting and continued processing. Existing sampling tests
caught an unconditional error.type tag; the final implementation preserves their
full-data-only rule. No live store, full solution or package-feed qualification.

Next candidate: FileSystem's shared local error handling. Other component families,
control/source/lifecycle coverage and complete OpenTelemetry remain unfinished.

## Step 4: FileSystem read/write exception coverage

Added three calls to the existing FlowDiagnostics.ProcessingException hook: the
shared FileSystemOperationPipeline parser and the classified catch boundaries of
FileReadNode and FileWriteNode. Unlike Storage, read/write have separate failure
classifiers and no shared error factory; no new abstraction was introduced to hide
these calls. No logger, subscription or Activity construction was added to nodes.

Caught original exceptions now reach the existing diagnostics/logging route before
conversion. Domain FlowError codes, details, workflow events, cancellation ordering
and forwarding behavior remain unchanged. Unclassified exceptions still reach the
FlowNode boundary, which reports them there. Operation failures identify the concrete
node; parsing failures identify the executing pipeline.

Validation: all 63 FileSystem tests passed on net10.0 (55 existing plus eight new
scenarios). New tests use real temporary files to verify missing reads, CreateNew
write collisions, malformed input, exception stack/type and identifiers, failed
Activity status, tracing disabled, throwing observers, continued successful IO,
unchanged existing file contents, and no repeated report for forwarded errors.
Both net8.0 and net10.0 package builds passed with zero warnings/errors. No Core or
logging adapter changes in this step; no new full-solution or package qualification.

DirectoryEnumerateNode and FileWatchNode use source/lifecycle handling and remain
outside this message-processing step. Next candidate: Sessions' local error handling.

## Step 5: Sessions recorder/query exception coverage

The user authorized proceeding with diagnostics while explicitly keeping a Sessions
concern open for later discussion (recorded at the top of this document). This step
does not redesign Sessions or resolve that concern.

Added three calls to the existing processing-exception hook: SessionRecorderNode's
operation catch, SessionQueryNode's operation catch, and the shared
SessionOperationPipeline deserialization catch. Kept the error factory unchanged
because SessionReplayNode also uses it without an incoming processing message.
No new abstractions, logger dependencies or subscriptions inside components.

Existing domain errors, message lineage, recorder sequence behavior, workflow events,
cancellation and completion remain intact. Replay source failures and recorder
finalization failures are outside this message-processing step; no synthetic message
or trace identity was created for them.

Validation: 57 Sessions tests passed on net10.0 (53 existing plus four new scenarios
covering recorder/query with tracing enabled/disabled). New scenarios verify original
exception type/message/stack, identifiers, failed Activity, domain and malformed-input
error codes, one report per handled failure, successful continuation, recorder
sequence after failure, completion and no new report for forwarded errors. Package
builds passed on net8.0/net10.0 with zero warnings/errors. No Core or adapter changes,
full-solution run or package-feed qualification in this step.

Next observability candidate: Metrics component-local error handling. The Sessions
design concern remains open until the user explains it and it is addressed separately.

## Step 6: Metrics processing exception coverage

Continued the agreed incremental plan after the user clarified that the deferred
Sessions concern must not alter it. Added three existing diagnostic-hook calls in
MetricsAggregateNode: deserialization rejection, known MetricsOperationException,
and the general processing catch before it wraps the original exception for the
domain error. No new base class, logger dependency, subscription or state model.

Preserved FlowError classification, aggregation, grouping and final snapshot
behavior. A group-limit outcome has no exception and does not manufacture one for
logging. Completion/final-snapshot exception coverage remains a separate lifecycle
step; this change covers per-message processing only.

Validation: all 57 Metrics tests passed on each of net8.0 and net10.0 (114 executions;
53 existing plus four new cases per target). New cases verify malformed input, known
validation failures, original general exception identity/inner exception/stack before
wrapping, identifiers and Activity failure, tracing disabled, throwing observers,
continued aggregation, and no exception report for group-limit or forwarded errors.
Existing aggregate tracing tests passed. No Core/adapter changes or full-solution
qualification in this step.

Next candidate: Projections' local processing error handling. Sessions remains a
deferred user concern, not a change to the planned sequence.

## Step 7: Projections processing exception coverage

Added two calls to the existing diagnostic hook in EventProjectionNode: the input
deserialization catch and the local projection-processing catch. No new abstraction,
logging dependency or subscription. Existing domain FlowErrors, filtering, counters,
snapshot creation, forwarding and completion behavior are preserved.

Validation: all 31 Projections tests passed on net10.0 (27 existing plus four new
cases). New cases exercise malformed input and a real rate-window underflow at
DateTimeOffset.MinValue, tracing enabled/disabled, throwing observers, exception
type/stack and message identifiers, failed Activity status, one report per handled
exception, filtering, continued snapshots and no report for forwarded errors.
Existing behavior is explicit in the test: counters can advance before a subsequent
calculation fails; this step does not change transactional aggregation semantics.
Both net8.0 and net10.0 package builds passed with zero warnings/errors.

Final-snapshot lifecycle failures remain outside this per-message step. No Core or
adapter implementation changed; no full-solution or feed qualification was run.
Next candidate: Expectations' local input/processing error handling. Sessions remains
an open deferred concern as requested.

## Step 8: Expectations processing exception coverage

Added existing diagnostic-hook calls after a malformed-input rejection wins the
resolution and after ClaimEvaluationFailure returns a claimed failure. Both calls
run outside the node gate. If another outcome already won, no exception report is
created for the ignored failure. No timer, matching, completion, error-code or
upstream error handling changes; no new abstraction or logging dependency.

Validation: all 28 Expectations tests passed on net10.0 (20 existing plus eight new
cases). New cases verify malformed rejection identifiers/type/stack and Activity
status, tracing disabled, throwing observers, callback execution outside the gate,
one report despite later invalid/matching input and timeout, and no synthetic
exception report for timeout, match, upstream error or completion outcomes.
The evaluation-catch placement was reviewed; the new behavioral tests exercise the
malformed-input catch, not an injected summary/filter evaluation exception.
Both net8.0 and net10.0 package builds passed with zero warnings/errors. No Core or
adapter implementation change, full-solution or package qualification in this step.

Next candidate: State's local processing error handling. Sessions remains deferred
for the user's separate concern, without changing the normal plan.

## Step 9: State processing exception coverage

Added existing diagnostic-hook calls at StateReducerNode's two local processing
catches. Known StateOperationException wrappers are reported once with the original
reducer/key-selector exception preserved as InnerException. General exceptions are
reported before domain wrapping. Error codes, stored state/version, result
serialization location and materializer extension points remain unchanged.

The materializer returns FlowError-only rejection results, with no original exception
available at the node boundary. This remains an explicit coverage gap; no synthetic
exception or new materializer contract was introduced. An unthrown exception object
used for the null-input domain outcome is also not reported as a caught exception.

Validation: all 23 State tests passed on net10.0 (19 existing plus four new cases).
New cases cover reducer and key-selector failures with tracing enabled/disabled,
original exception identity through InnerException, stack/identifiers/Activity status,
throwing observers, unchanged prior state and version on expression failure,
successful continuation, materializer rejection and upstream error forwarding.
Both net8.0 and net10.0 package builds passed with zero warnings/errors. No Core,
adapter, full-solution or package-feed qualification changes in this step.

Next candidate: HTTP's local exception handling. The materializer exception-detail
gap and the separate Sessions concern remain open.

## Step 10: HTTP handled exception coverage

Added one diagnostic-hook call inside CompleteFailure's existing non-null exception
branch. All existing callers benefit without repeated instrumentation in request
construction, sending, timeout/cancellation classification and response reading.
Original caught exceptions (or existing input-domain wrappers retaining their inner
exceptions) are delivered through the same listener. Non-success HTTP statuses,
nonpositive-timeout validation and FlowError-only materializer rejection do not
manufacture exceptions. Stopping cancellation still rethrows through its existing
path; error classification and request/resource ownership remain unchanged.

Validation: all 45 HTTP tests passed on net10.0 (36 existing plus nine new cases).
In-process handlers exercise network/send/body-read failures and request timeouts
with tracing enabled/disabled, original identity where HttpClient preserves it,
exception stack and identifiers, failed Activity status, throwing observers,
continued requests and forwarding without repeated reporting. Non-exception status,
timeout-validation and materializer errors are checked for zero exception reports.
No real HTTP endpoint was contacted. Both net8.0/net10.0 package builds passed with
zero warnings/errors. No Core/adapter implementation changes or full-solution/feed
qualification in this step. Materializer exception details remain an explicit gap.

Next candidate: MQTT's local operation error handling. Sessions remains a deferred
user concern; the planned incremental sequence is unchanged.

## Step 11: MQTT operation exception coverage

Added diagnostic reporting at MqttPublishOperationNode and MqttControlNode's existing
MqttClientOperationException catches. The shared MqttClientResultFactory previously
received the original exception but retained only its serialized classification.
It now passes the exception into a new three-argument MqttClientOperationException
constructor. The existing two-argument constructor remains and delegates with null
InnerException. FlowError codes/details/transience remain unchanged; the original
exception is retained outside workflow data through the wrapper's InnerException.

Validation: all 144 MQTT tests passed on net10.0 (139 existing plus five new cases).
New cases cover publish/control paths with tracing enabled/disabled, original
transport exception identity/stack in the wrapper, unchanged FlowError identity and
transience, message identifiers, failed Activity, throwing observers, successful
subsequent publishes, rejection and forwarding without additional reports, and the
existing exception constructor. Both net8.0/net10.0 builds passed without warnings.
Tests used the existing in-process transport; no live broker was contacted.

Materializer exception-detail gaps, background reconnect and trigger/source lifecycle
diagnostics remain unfinished. No connection/cancellation behavior, Core or logging
adapter implementation changes; no full-solution/feed qualification.

Next: review the remaining component catch boundaries and coverage gaps before
choosing the next bounded change. Sessions remains an open deferred user concern.

## Step 12: remaining-boundary review

Completed a source review of remaining catch boundaries and recorded the findings in
../reviews/2026-09-12-remaining-exception-diagnostics.md. Confirmed straightforward
local gaps in Serialization, Mapping, Assertions, Validation and Payloads. Separated
deferred/lifecycle ownership and materializer exception-loss issues from those cases.
No claim that every catch should emit a processing diagnostic or that OTel is complete.

Selected next step: SerializationPipeline's existing classified-error catch, shared
by six converters. Preserve converter-local wrapping and avoid duplicate reports.
No production code changed and no tests/builds were rerun for this review checkpoint.
The normal incremental plan continues; the Sessions revisit remains deferred.

## Step 13: Serialization shared failure reporting

Added one existing diagnostic-hook call in SerializationPipeline's
SerializationFailureException catch, shared by all six converter nodes. Converter
helpers keep their current wrapping and fallback behavior; errors are reported only
at the pipeline handling boundary. No new abstraction, dependency or API change.

Validation: all 23 Serialization tests passed on net10.0 (17 existing plus six new
cases). New cases exercise JSON parse and Base64 decode failures with original inner
exceptions, text input-size rejection with no fabricated inner exception, tracing
enabled/disabled, failed Activity, message identifiers, throwing observers,
successful subsequent conversions and forwarding without duplicate reports.
Text decoding retains its existing replacement fallback for invalid bytes; it was
not changed to throw to accommodate a test. Both net8.0/net10.0 package builds passed
with zero warnings/errors. No full-solution, feed or new Core/adapter qualification.

FlowError-only materializer rejection still has the previously recorded exception
detail gap. Next step: Mapping's local error boundary, following the reviewed order.

## Step 14: Mapping local failure reporting

Added one existing diagnostic-hook call at FlowMapperNode's current catch boundary.
The catch still covers input conversion, context creation, mapping and result
conversion. Domain FlowError codes/details, output lineage, fanout and forwarding
remain unchanged. No new abstraction, logger dependency or API change.

Validation: all 14 Mapping tests passed on net10.0 (12 existing plus two new cases).
New cases exercise malformed input and expression failures with tracing enabled or
disabled, original exception identity/inner exception/stack, message identifiers,
failed Activity, throwing observers, correct subsequent mapping and forwarding
without duplicate reports. Both net8.0/net10.0 package builds passed with zero
warnings/errors. No Core or adapter implementation changes, full-solution run or
package-feed qualification in this step.

Next step: Assertions' evaluation-error boundary; a false assertion remains an
ordinary result. Sessions remains a deferred user concern.

## Step 15: Assertions evaluation failure reporting

Added one existing diagnostic-hook call in AssertionNode's input/evaluation catch.
A predicate returning false still produces a normal AssertionResult with Passed=false;
it does not manufacture an exception report. Domain error codes, details, lineage,
workflow events and forwarding behavior remain unchanged. No new abstraction,
dependency or API change.

Validation: all 13 Assertions tests passed on net10.0 (11 existing plus two new
cases). New cases cover input conversion and predicate exceptions with tracing
enabled/disabled, original exception identity/inner exception/stack, identifiers,
failed Activity, throwing observers, later true and false results, and forwarding
without duplicate reports. Both net8.0/net10.0 package builds passed with zero
warnings/errors. No Core/adapter change or full-solution/feed qualification.

Next step: Validation's selector/evaluation failure path. Ordinary schema-invalid
results remain distinct from exceptions. The Sessions revisit stays deferred.

## Step 16: Validation shared failure reporting

Added one existing diagnostic-hook call in JsonSchemaValidatorNode.Failure, shared
by selector and schema-evaluation catches. Ordinary schema-invalid inputs still
produce JsonSchemaValidationResult with IsValid=false and issues, not exception
reports. Existing domain codes, selected values, error details and upstream error
lineage remain unchanged. No new abstraction, dependency or API change.

Validation: all 12 Validation tests passed on net10.0 (10 existing plus two new cases).
New cases cover selector exception identity/inner exception/stack, identifiers and
failed Activity with tracing enabled/disabled, throwing observers, continued valid
and invalid schema results, and forwarding without duplicate reports. The shared
evaluation-error path was source-reviewed; these new tests inject selector failures,
not failures inside the schema library. Both net8.0/net10.0 package builds passed
with zero warnings/errors. No Core/adapter implementation changes, full-solution or
package-feed qualification in this step.

Next step: Payloads' handled inspection failures, preserving intentional format
probes and fallbacks. Sessions remains an open deferred user concern.

## Step 17: Payloads inspection failure reporting

Added existing diagnostic-hook calls at PayloadInspectNode's classified and general
inspection catches. Inner JSON/XML exceptions remain in their existing wrapper.
Format probes, decoded-preview fallback, input materialization and forwarding/error
lineage are unchanged. No new abstraction, dependency or API change.

Validation: all 21 Payloads tests passed on net10.0 (17 existing plus four new cases).
New cases cover JSON/XML failures with tracing enabled/disabled, inner exception
type/stack, identifiers, failed Activity, throwing observers, later successful
inspection and forwarding without duplicate reports. Invalid Base64 detection and
invalid UTF-8 decoded-preview fallbacks explicitly produce normal results and no
exception reports. The general inspection catch was source-reviewed; new injected
failures exercise the classified parser path. Both net8.0/net10.0 builds passed with
zero warnings/errors. No Core/adapter changes or full-solution/feed qualification.

This completes the five straightforward families selected in the remaining-boundary
review. Remaining work includes workflow Observability's multi-selector failures,
materializer exception loss and deferred/lifecycle context. Next: inspect the
Observability component failure paths and choose the smallest treatment that retains
multiple original selector exceptions without duplicate reports or a new model.
The Sessions concern remains deferred; full OpenTelemetry is not yet complete.

## Step 18: workflow logger selector failures

Reviewed the workflow Observability components. FlowLoggerNode can collect multiple
selector exceptions for one error outcome. Selected the existing diagnostic event
once per caught selector failure, keeping the existing single FlowError with failed
selector names. Added two call sites: selector catch and general processing catch.
No aggregate exception, new diagnostic model or logger dependency was introduced.

Reports share the originating message ID; the diagnostic payload itself does not
add selector names (those remain in FlowError details). Each caught failure is
reported once at its catch boundary, even if later rendering encounters a separate
failure. Distinct failures may therefore produce multiple reports for one message.
No duplicate report is added for the combined selector-error outcome or forwarding.

Validation: all 27 workflow Observability tests passed on net10.0 (25 existing plus
two new cases). New cases cover two simultaneous selector failures, exact exception
identity and stacks, identifiers, failed Activity with tracing enabled/disabled,
throwing observers, one workflow error, existing failed-selector names, general
input conversion failure, successful continuation/sequence and no forwarding report.
Both net8.0/net10.0 builds passed with zero warnings/errors. No Core/adapter changes,
full-solution or package qualification.

Next step: FlowMetricsNode's size-selector and FlowCounterNode's predicate failures.
These workflow components are separate from standard runtime Meter instrumentation,
which remains unfinished. Sessions remains a deferred user concern.

## Step 19: workflow Metrics and Counter failure reporting

Added existing diagnostic-hook calls to FlowMetricsNode's size-selection catch and
general processing catch, and FlowCounterNode's input/predicate catch. No new model,
dependency or API. Counter false predicates remain normal rejected results; Metrics
continues counting an observation when size selection fails without adding a size.
Existing codes, totals, snapshots, forwarding and lifetime behavior are preserved.

Validation: all 31 workflow Observability tests passed on net10.0 (27 previous plus
four new cases). New cases cover selector/predicate and input-conversion failures
with tracing enabled/disabled, original exception identity/inner exception/stack,
message identifiers, failed Activity, throwing observers, subsequent valid values,
unchanged counts/size totals/averages, false predicate rejection and no repeated
report for forwarded errors. Both net8.0/net10.0 builds passed without warnings.
No Core/adapter implementation changes or full-solution/feed qualification.

Next: review the materializer exception-detail gap before changing contracts, using
the existing component-local conversion and optional-materializer requirements.
Deferred/lifecycle context and standard runtime Meter/export integration remain
unfinished; the Sessions concern stays recorded without changing the plan.

## Step 20: materializer exception-detail review

Confirmed that conversion catches discard the original exception before the node
receives a failed FlowValueMaterializationResult. Recorded the evidence, alternatives
and recommendation in `docs/reviews/2026-09-12-materializer-exception-details.md`.

Recommend an optional, System.Text.Json-ignored exception on the existing local
result, preserving Failure(error) and adding an overload. The consuming node would
report once through the existing hook; workflow envelopes and component-local
deserialization remain unchanged. Ordinary rejections acquire no synthetic exception.

This step changes documentation only. No runtime/API changes or tests/builds.
Next implementation slice: result plus JSON base materializer and State consumer,
with compatibility, exception identity, serialization and continuation checks.
Other materializers/consumers follow in small steps. Sessions remains deferred.

## Step 21: preserve materialization exceptions through State

Added an optional JsonIgnore Exception property and Failure(error, exception)
overload to the existing local result. Failure(error) and the materializer interface
remain unchanged. The JSON base materializer retains its caught exception; State
reports it once at the rejection boundary through the existing diagnostic hook.
FlowError, FlowValue and FlowMessage contracts are unchanged. Primitive content,
HTTP/MQTT custom conversion and other consumers have not yet been migrated.

Validation: 25 State tests passed on net10.0; all 312 Core node tests passed on each
of net8.0/net10.0 (649 total executions). New checks cover exception identity,
inner exception/stack, JSON exclusion metadata, unchanged serialized FlowError,
old factory/custom-materializer compatibility, ordinary rejection without an
exception, tracing enabled/disabled, throwing observers, forwarding without a
duplicate and successful continuation without state advancement on rejection.
Existing State malformed-input expectations now include its newly retained JSON
exception. State package builds passed for both targets with zero warnings/errors.
No full-solution or package-feed qualification.

Next: preserve primitive content conversion exceptions and report them in the
Serialization and Payloads consumers. Full observability and the deferred Sessions
concern remain open.

## Step 22: content materialization diagnostics

The existing content materializer now retains its caught exception in the local
result. SerializationPipeline and PayloadInspectNode report that exception once
at their rejection boundaries. No new API, wrapper or deserializer was added.
Existing FlowError codes/details and forwarding behavior remain unchanged.

Validation: 29 Serialization and 23 Payloads tests passed on net10.0; 313 Core node
tests passed on each target framework (678 total executions). New cases exercise
malformed content through JsonParse, TextDecode, Base64Encode and PayloadInspect,
with tracing enabled/disabled, JSON/inner Base64 exception stacks, error details,
message identifiers, failed Activity, throwing observers, successful continuation
and no duplicate forwarding report. Core checks cover content rejection plus
successful string, arbitrary JSON and exact-byte materialization. Existing format
probe and encoding-fallback tests remain passing. Both component packages built
for net8.0/net10.0 with zero warnings/errors. No full-solution/feed qualification.

Next: HTTP request materialization, then MQTT publish/control in a separate slice.
HTTP trigger reply materialization belongs to the still-open request/reply context
review. Runtime metrics/exporter integration, lifecycle diagnostics, host/SSE and
the user's Sessions concern remain deferred as already recorded.

## Step 23: HTTP request materialization diagnostics

HttpClientRequestMaterializer retains its caught exception in the existing local
result. HttpClientNode reports it once at the materialization rejection boundary.
No new API, deserializer or abstraction. Existing HTTP error codes/details and
ordinary custom-materializer rejections are preserved; rejected input is never sent.

Validation: all 49 HTTP tests passed on net10.0 (45 previous plus four new cases).
New cases cover non-object input and malformed exact body content, tracing on/off,
exact exception identity, JSON/inner Base64 stacks, unchanged error object and
lineage, identifiers, failed Activity, throwing observers, custom Failure(error)
compatibility, zero sends for rejected/forwarded input, successful continuation and
no duplicate reports. The old nonexception test now covers status/timeout outcomes;
its malformed-shape case moved into the conversion-exception coverage. Existing
request body conversion, binary content, network and timeout tests remain passing.
Both net8.0/net10.0 package builds passed with zero warnings/errors. No Core changes,
full-solution or package-feed qualification in this step.

Next: MQTT publish/control materialization. HTTP trigger reply conversion remains
part of the separate request/reply context review. Other deferred work, including
the user's Sessions concern, is unchanged.

## Step 24: MQTT materialization diagnostics

MQTT publish materialization retains its caught exception. Publish/control nodes
report retained conversion exceptions once at their rejection boundary; control
uses the already-updated JSON base materializer. Error codes, forwarding and
controller calls are unchanged. Rejected conversion never reaches publish.

All 148 MQTT tests passed on net10.0; both package targets built without warnings
or errors. Four new cases cover malformed exact content for publish/control with
tracing enabled/disabled, JSON/inner Base64 stacks, identifiers, failed Activity,
throwing observers, no rejected publish, no forwarding duplicate and later success.
Existing scalar-input assertions now include the newly available conversion report.
The control test was corrected to use its existing case-sensitive Operation/Publish
discriminator. No wire-contract change, Core change or full-solution qualification.

The user authorized continuing all small steps without requesting each checkpoint.
Continue context review and safe local boundaries autonomously; preserve the
previously deferred Sessions and host/SSE scope.

## Step 25: timer input-processing diagnostics

Delay, Throttle and Debounce report caught input-processing exceptions through the
existing hook. Debounce reports after releasing its gate. Shutdown cancellation,
clock fallbacks, domain errors, scheduling and forwarding remain unchanged.
All 83 Timer tests passed on net10.0, including six new tracing on/off cases for
original clock exceptions, inner/stack details, identifiers, failed Activity,
throwing observers, forwarding and successful continuation. Both target builds
passed without warnings/errors. Deferred emission handling follows separately.

## Step 26: queued timer emission context

Delay and Debounce emission workers now use existing FlowTracing.StartProcessing
with the queued message, then report caught emission failures. This isolates them
from ambient worker Activities and creates no new API. Cancellation/fallback and
emission ownership are unchanged.

All 87 Timer tests passed on net10.0. Four new deferred failure cases and expanded
input cases verify the explicit message trace parent, unrelated worker isolation,
tracing on/off, retained exceptions, continuation and no forwarding duplicate.
Both target builds passed without warnings/errors. Source timers/lifecycle failures
remain separate; this does not claim complete tracing/exporter qualification.

## Step 27: request/reply response processing

The coordinator uses existing StartProcessing with the correlated response around
materialization and caller notification. Its default conversion catch and reply
callback catch report original exceptions once. SafeFail/SafeAcknowledge, admission,
timeouts, cancellation and terminal faults retain their existing ownership.
The default materializer is now a private instance method; no public API change.

All 52 request/reply tests passed on net10.0. Four new cases cover conversion and
callback failures with tracing on/off, original callback exception identity/inner
stack, response IDs/trace parent, worker isolation, throwing observers, notification
counts and later successful callers. Both targets built without warnings/errors.
Custom materializers execute in the same response scope; HTTP's retained reply
conversion exception is the next small consumer update.

## Step 28: HTTP trigger reply conversion

HttpTriggerNode reports its retained conversion exception in the coordinator's
response-processing scope. Existing caller failure, rejection event, codes and
later valid replies remain unchanged. No host setup or SSE work was introduced.
All 28 HTTP ASP.NET Core tests passed on net10.0. The direct graph test now covers
tracing on/off, response IDs/parent, original JSON stack, throwing observers,
exactly one report for rejection and none for valid replies. Both targets built
without warnings/errors.

## Step 29: Window input diagnostics

Window reports its caught input-processing exception before queuing the existing
error emission. No change to window contents, output ordering or cancellation.
All 100 Routing tests passed on each of net8.0/net10.0. Two new tracing on/off
cases cover malformed input, JSON stack, identifiers, observer containment,
forwarding without duplicate reports and a subsequent correctly formed window.

## Step 30: Correlation diagnostics outside its gate

Correlation retains the caught exception locally and reports in an outer finally,
after its lock is released. Existing timer scheduling remains inside the gate and
retains its original exception behavior. A separate scheduling failure still reaches
the node boundary; it no longer hides the already-caught selector exception from
diagnostics. No new public API or shared abstraction.

All 105 Routing tests passed on each target. Four selector cases check key/side
failures with tracing on/off, original inner exception, successful matching and
observer access to the released gate. Another case checks selector plus scheduling
failure: two distinct reports, no duplicates, and later successful matching.

## Step 31: Join message-command diagnostics

Join reports at its existing command catches when command.Source is present.
The full caught wrapper/exception is retained, while existing domain-error details
remain unchanged. Inner emission catches continue to mark their own Activities and
rethrow; they do not also report. The diagnostic belongs to the handling command's
Activity. Source-less completion/timer commands retain their existing output/error
treatment and are not assigned fabricated message IDs.

All 109 Routing tests passed on each of net8.0/net10.0 (218 final-suite executions).
Four new cases cover left/right selector failures, original inner exception/stack,
tracing on/off, malformed-input general catches, throwing observers, forwarding
without duplicate reports and later successful matches. Existing join aggregate
tracing tests also passed. Both package targets built with zero warnings/errors.
The new injected failures exercise input/selector command handling, not aggregate
output-failure injection. No full-solution/exporter qualification.

Resilience follow-up review confirms that NAK, timeout, stopped and exhausted
outcomes are not caught processing exceptions. Inspected failures in attempt
observation and output emission preserve RecordFatalError/terminal ownership.
No Resilience runtime change is warranted by this per-message diagnostic pass.
Remaining work is lifecycle/source diagnostics and their ownership/context,
including source-less Routing commands and aggregate emission failures without
a message-backed handler. Full runtime metrics/exporter integration remains open;
Sessions and host/SSE retain their agreed deferred scope.

## Step 32: first recorded source lifecycle fault

Added an in-process LifecycleException event/payload carrying the original exception,
component type and operation name, with no workflow message IDs or Activity. The
hook suppresses inherited Activity context during synchronous notification and
restores it afterward; it creates no artificial span. Observer failures are contained.

FlowSource reports once after its first-failure record succeeds and releases its
lifetime gate. Existing fault/completion behavior and cancellation remain unchanged.
Reporting at this shared boundary avoids repeated instrumentation in rethrowing
Generated/Sequence source catches. The operation label is "source"; it deliberately
does not guess whether the fault originated in startup, production, output or a
control action. Later faults do not replace or duplicate that first report.

The existing logging subscription handles LifecycleException as EventId 2 with
ComponentType and Operation, preserving the original exception. Logger category
remains FluxFlow.Processing for compatibility; lifecycle entries have no fabricated
MessageId/BusinessTraceId fields. This additive diagnostic API is outside workflow
serialization and introduces no new service, registry or source-base abstraction.

Validation: 316 Core node tests and 9 adapter tests passed on each target, plus
39 Sources tests on net10.0 (689 executions). New cases verify original exception
identity/inner stack, unchanged Completion fault, duplicate suppression, requested
stop/success without reports, throwing observers/providers, disabled logging,
unsubscribe/factory ownership and ambient Activity isolation/restoration.
Both adapter target builds passed with zero warnings/errors.

Scope: only failures reaching FlowSource.RecordFailure are covered. Cancellation
callback failures accumulated directly during shutdown, additional later failures,
component-specific catches that never reach this boundary, standalone lifecycle
owners and disposal failures still require their own ownership review. No full
solution/exporter qualification. Runtime metrics, Sessions and host/SSE scope remain
as previously recorded.

## Step 33: source stop and disposal-hook failures

Added existing lifecycle-hook calls at the cancellation-callback failure boundary
in RunLifetimeAsync (source.stop) and the OnDisposeAsync override boundary
(source.dispose). The stop report preserves the caught aggregate and all callback
exceptions before existing Completion aggregation; the disposal report rethrows
the same exception and preserves the existing finally cleanup. Neither re-reports
the earlier production fault. No API, lifecycle state or exception-policy change.

Validation: 320 Core tests and 9 logging-adapter tests passed on each target, plus
39 Sources tests on net10.0 (697 executions). Four new Core cases cover callback
and disposal failures with/without an earlier source fault, exact exception identity,
inner/stack details, unchanged Completion, repeat disposal, throwing observers,
ambient Activity isolation and no duplicate reports. Existing source shutdown tests
continue to verify output draining, callback failures and disposal ordering.
Both Core target builds passed with zero warnings/errors.

Scope remains narrow: source.dispose covers the component's OnDisposeAsync hook,
not every output/control cleanup failure in the nested finally blocks. Failures
owned by other runtime classes still need separate review. No full-solution or
exporter qualification; the agreed Sessions and host/SSE deferrals remain unchanged.

## Step 34: remaining lifecycle ownership

Reviewed shared source output/control cleanup: output disposal can rethrow the
already-owned dispatch failure; control disposal releases its cancellation source.
No extra reports were added to those replay paths. Shared FlowProcessingLifetime
now receives its owning component (internal constructor only), reports its first
terminal fault and stop-callback/disposal-hook failures through lifecycle diagnostics.
The disposal cancellation report preserves the existing exception and cleanup order.

Join reports its first terminal fault and source-less command catches as lifecycle
events. Retry reports at its existing deduplicated failure list, after releasing
the gate. RequestReply reports its deduplicated final failure list before existing
Completion failure propagation. Normal retry NAK/timeout/exhaustion, caller fallback
notifications and shutdown cancellation retain their existing classifications.
Tests verify original lifecycle exceptions reach SDK logs once for these owners.
No synthetic workflow identifiers or spans are added.

## Step 35: standard runtime metrics and exception span events

Added one BCL Meter, FluxFlow.Core, at the existing processing scope:
fluxflow.processing.completed, fluxflow.processing.active,
fluxflow.processing.duration (seconds), and fluxflow.exceptions. Metrics remain
independent of Activity sampling; dimensions omit message IDs and payloads.
Scopes represent execution work (including controls, queued work and aggregates),
not unique messages. Callback exceptions are contained.

The existing shared exception hook adds standard exception Activity events with
type/message/stack for full-data sampling. It retains original ILogger exceptions
and does not serialize exceptions into workflow envelopes. Core still has no SDK
dependency. New tests cover overlapping scopes, tag cardinality, sampling-off
metrics and throwing metric callbacks.
Lifecycle exception measurements also run after inherited Activity suppression,
preventing unrelated trace context from reaching metric listeners/exemplars. The
direct host-activity regression check passed on both targets.

## Step 36: optional OpenTelemetry SDK host adapter

FluxFlow.Observability.OpenTelemetry targets net8.0/net10.0. AddFluxFlowTelemetry
registers the existing ActivitySource/Meter, logging SDK pipeline and host-owned
diagnostic subscription. Resources and exporters remain host-configurable through
the standard OpenTelemetryBuilder. Official OpenTelemetry 1.18.0 packages were
verified against NuGet. In-memory SDK tests check all signals, original exception
logs, span exception events and parent context with sampling on/off. Actual OTLP
HTTP/protobuf exporters were exercised through a capturing HTTP transport for
/v1/traces, /v1/metrics and /v1/logs.

## Step 37: ASP.NET Core host/SSE integration

FluxFlow.Observability.AspNetCore targets net10.0 because it uses native
TypedResults.ServerSentEvents. The explicit package-target convention was updated.
AddFluxFlowTraceStream/MapFluxFlowTraceStream expose completed-span JSON snapshots.
Each client has a bounded queue; overflow disconnects the slow client without
blocking other clients or processing. Cancellation/host disposal removes clients.
No replay, durable storage or workflow payloads; tag values are represented as text.
Live clients request full Activity data; the listener requests none without clients.

The HttpTriggerSample demonstrates standard telemetry registration, official
ASP.NET Core/HttpClient instrumentation and an opt-in OTLP collector endpoint.
Its SSE endpoint is restricted to direct loopback clients. The library endpoint
returns RouteHandlerBuilder so real hosts can apply their authorization policy.
Host docs include a runnable example and deployment configuration.

## Step 38: verification and packaging checkpoint

Final relevant suite counts (repeated intermediate runs are not added):

| Suite | Targets | Executions |
| --- | --- | ---: |
| Core nodes | net8.0/net10.0, 324 each | 648 |
| Existing logging adapter | net8.0/net10.0, 9 each | 18 |
| OpenTelemetry SDK integration | net8.0/net10.0, 6 each | 12 |
| SSE integration | net10.0 | 2 |
| Routing | net8.0/net10.0, 109 each | 218 |
| Resilience | net10.0 | 35 |
| RequestReply | net10.0 | 52 |
| HTTP | net10.0 | 49 |
| HTTP ASP.NET Core | net10.0 | 28 |
| MQTT | net10.0 | 148 |
| Timers | net10.0 | 87 |
| Sources | net10.0 | 39 |
| Manifest/metadata/test layout | net10.0 | 8 |
| Total | | 1,344 |

All listed executions passed. Sample build and adapter builds/pack passed without
warnings/errors. A real loopback sample request returned HTTP 200 and the expected
greeting; the SSE endpoint returned HTTP 200, text/event-stream and a flow.request
span with a valid trace ID. The temporary sample process was stopped afterward.

Three unpublished adapter packages were produced under
artifacts/observability-integration and inspected for nuspec, README and expected
target DLLs. New projects are in the solution and release manifest; changelog and
public API docs are updated. Existing observability metadata was brought into
the same packaging conventions.

This completes the local implementation/integration sequence. It does not claim a
full-solution release/feed qualification, benchmark result or live external
Collector/Datadog deployment. Custom resources/third-party clients remain host-owned;
not every catch is an independent telemetry event. Sessions remains an explicit
deferred concern. No commit, publication, remote deployment or UI change was made.

### Step 39 â€” fresh package-only observability consumer

Added `eng/observability-consumer` and `eng/verify-observability-consumer.ps1`.
The script packs Core and all three adapters with a unique local version, uses
an isolated cache and source mapping restricting FluxFlow to that feed, and
executes a consumer with no project references on net8.0 and net10.0.

Verified the saved script end-to-end with candidate
`0.0.0-local.observability-20260912203014-b5b346b1`. Both targets passed on
installed runtimes 8.0.31 and 10.0.12. Each checked error-then-success processing,
SDK traces/metrics/logs and original exception identity. The net10 consumer also
registered the packaged SSE endpoint and started/stopped a loopback host.
The verification record and four package SHA-256 hashes are under
`artifacts/observability-20260912203014-b5b346b1/verification.json`.

These are two additional consumer executions, separate from the previous 1,344
targeted tests. No runtime changes were necessary. The initial direct restore
command treated its URL as a local path; the saved script uses a NuGet config
and passed fresh restore/build/run. The previous full release qualification is
not renewed by this focused check. SSE requires net10; generic release scripts
defaulting to net8 are not an appropriate qualification target for that package.
External Collector/Datadog deployment and Sessions remain unperformed/deferred.

### Step 40 â€” real local Collector and shutdown log correction

The user authorized the next end-to-end Collector validation. Docker/Collector
were absent; downloaded the official portable core Collector 0.160.0 under
artifacts, checked the archive against GitHub's published SHA-256 digest
`cacb952f9b7ef3b08830425aa8e5efca153d69b7b8d3019e6c38329cdcfc2894`, and ran
temporary hidden loopback processes. No Windows service was installed.

Added a dedicated net10 acceptance host and repeatable script. Initial harness
iteration incorrectly used repeated one-shot receives on live concurrent outputs;
corrected the harness to keep bounded BufferBlock links attached. No runtime
delivery change was needed.

The real run then found a production integration issue: with no prior log batch,
the SDK's lazy OTLP log HTTP client accessed a disposed IServiceProvider during
host disposal. Traces/metrics arrived but logs did not. Added a bounded five-second
LoggerProvider flush to the adapter's existing StopAsync, while DI services still
exist. Observer/exporter exceptions remain contained. Component code is unchanged.
Added a batch-log regression that asserts delivery after Host.StopAsync without
manual test flush or host disposal. SDK suite: 7 passed per net8/net10 (14);
SSE suite: 2 passed on net10. Builds had no warnings/errors.

Final real run: `artifacts/collector-acceptance-20260912204224-506e6d0e`.
Collector received 51 total spans, 4 metric instruments and 111 total logs;
HTTP polling accounts for variable non-flow counts. Assertions verified 24 branch
spans across 12 shared message identities, overlapping execution on both branches,
one processing exception log matched to its failed span, identical full stack/inner
detail, 24 matching SSE spans and disconnect cleanup, one shutdown processing span,
25 completed/duration observations, zero active work per component, one exception,
bounded metric dimensions and common service identity. Evidence hashes are in
verification.json. The host stopped gracefully. The temporary Collector was stopped
after its file buffer flushed; Collector crash durability/graceful stop is not claimed.

The integration fixture uses source references on net10; package-only acceptance
passed again after the fix on net8/net10 with candidate
`0.0.0-local.observability-20260912204255-27e4027d` (verification.json under
the matching artifacts directory). No full-solution, benchmark, Datadog, remote
deployment, publication or Sessions qualification is claimed.

### Step 41 â€” integrated review and closure

User authorized whole-change validation, a minimal usage review and an honest
size/structure report. Complete Release solution build passed with zero warnings
and errors. Configured local regression: 4,613 executions in 71 nonempty reports,
initially 4,612 passed and one stale public source API baseline failure. Reviewed
candidate declarations before updating the baseline, documented intentional
breaking changes, and reran its two tests with acceptance disabled: both passed.
No runtime changes were required. Separate in-process workflow acceptance passed
(one test); a direct application/workflow/two-transform example verified invalid
input followed by a successful reply with telemetry confined to shared/host code.

Evidence: `artifacts/integrated-review-20260913`. See
[integrated review](../reviews/2026-09-13-integrated-simplification-review.md) for
counts, code, migration review and exclusions. Size comparisons include untracked
source and identify the baseline as already refactored. Production C# is 4,722
lines smaller in this repository; 5,130 of that is UI extraction, so excluding UI
production is 408 lines larger after added capabilities. Do not present raw Git
deletions as eliminated runtime complexity. External backends remain outside scope;
Sessions remains deferred. No new general refactor or external adoption was started.

### Step 42 â€” requests without a reply declaration

The user explicitly likes RequestAsync as a function-like boundary and requested
removal of its RepliesFrom prerequisite plus address-based input/output selection.
Added `FlowApplication.RequestAsync(string inputAddress, string replyAddress, ...)`
delegating to the same endpoint overload and exchange coordinator. Input and reply
may belong to different workflows. Topology validation still rejects foreign ports;
address lookup still rejects missing ports. Existing links determine reachability.

An accepted exchange activates the selected output's reply handling before sending.
One per-output flag retains drain behavior for otherwise-unrouted late/duplicate
messages for that output's lifetime; there is no second registry, timer or per-request
tombstone. Ordinary links still receive responses. Automatic handling suppresses
fallback only for a response that settles an exchange; unrelated/late messages may
follow fallback. Existing explicit reply configuration remains optional with its
previous stronger policy semantics. No declaration is required in ordinary usage.
Engine SendAndReceiveAsync now follows the same automatic behavior across revisions.

Core request and tracing tests now run without RepliesFrom calls. Added address
selection across workflows, invalid addresses, and ordinary-link/fallback regressions.
Core: 147 passed on each net8/net10; Engine: 538 passed on net10; normal public API
baseline verification: 2 passed after documenting/reviewing the additive overload.
Whole solution Release build passed with zero warnings/errors. After moving activation
inside accepted exchange admission, Core's 294 executions and the 76 relevant Engine
port/policy tests passed again; these overlap the suite totals and are not extra
unique tests. The full-solution build predates only that final placement adjustment;
the final affected projects compiled in the targeted test runs.

Updated the current usage example, Core README, migration notes and changelog.
Previous archived review evidence is unchanged. External backends and Sessions
remain outside this change; no publication or consumer adoption was performed.

### Step 43 â€” remove obsolete reply policies; record flow-function concept

User explicitly rejected retaining unused reply configuration. Removed Core and
definition-builder RepliesFrom APIs, topology reply declarations, JSON reserved
policy parsing/filtering, compiled ReplyOutputs, Engine builder flags, runtime-plan
fields and revision-policy propagation. No compatibility aliases remain. Existing
RequestAsync address/object overloads and automatic activation are unchanged.
DiscardUnmatched remains a separate meaningful output policy. Migration notes
describe deletion of old declarations and the automatic fallback behavior.

Migrated caller tests to automatic requests and replaced tests of removed policy
switching with request/revision coverage. Ordinary links still receive responses;
late/duplicate messages follow fallback if configured, or drain at request-used
outputs without routes. Source/test scan found no remaining RepliesFrom,
ReplyOutputs, replyOutputs or named replies configuration references in C#.
Historical documents/evidence are retained as historical, not current instructions.

Full Release build passed with zero warnings/errors. Integrated local regression
(API snapshot checked separately): 4,604 executions, initially 4,601 passed and
three test-migration failures. Removed one obsolete case asserting that the deleted
policy name remains reserved. Fixed the two fallback cases to drain their BufferBlock
before awaiting completion. No runtime fix was needed for these test failures.
Final Composition suite passed 271; complete Engine suite passed 537; JSON suite
passed 100; normal API baseline verification passed 2 after reviewed removals; the
separate in-process acceptance passed 1. Reruns overlap the initial execution count.
No unresolved failures remain in this scope. Evidence lives under
`artifacts/reply-policy-removal-20260913`; initial failed reports are preserved.

The user proposed a reusable named input/output/timeout boundary for future API
and JSON design. Recorded [FlowFunction design direction](2026-09-13-flow-functions.md).
This is not an implemented MakeFunction API or a new configuration schema. No
generic message-type machinery, execution engine or function registry was added.

### Step 44 â€” reusable C# request functions

Added FlowApplication.MakeFunction overloads for exact addresses and endpoint
objects, returning a FlowFunction delegate with an optional per-call cancellation
token. Creation validates ports and timeout without starting or admitting work.
Invocation delegates directly to RequestAsync; application ownership, fresh
correlation, deadlines, first replies and error-message behavior remain unchanged.
No wrapper class, registry, generic payload API or JSON configuration was added.

Three new Core tests cover reuse and concurrent cross-workflow calls, lifecycle,
error replies, fake-clock timeout, cancellation and creation validation. Core
passed 150 tests on each net8/net10 (300 executions). Reviewed the additive public
source API snapshot and normal baseline verification passed 2 tests. Full solution
Release build succeeded; the initial build retried one temporary test-assembly
file lock while the baseline test was running. Updated the Core README, changelog
and flow-function design note to show the actual callable syntax.

### Step 45 â€” caller-owned function waiting policy

Removed timeout from both MakeFunction overloads. The delegate uses the existing
RequestAsync coordinator with infinite timeout and the caller's cancellation token.
There is no implicit deadline; pending capacity and application termination still
apply. Direct RequestAsync's explicit timeout overloads remain unchanged.
Updated tests exercise caller-scheduled cancellation, explicit cancellation, and
an unbounded call remaining pending after advancing a fake clock by one day, then
successfully receiving its reply. Core passed 150 tests each on net8/net10; normal
public API baseline verification passed 2. Full Release build passed with zero
warnings/errors. Updated current examples and design notes; Step 44's bound-timeout
design is superseded. JSON remains deferred.

### Step 46 â€” remove the redundant Fluent construction API

User authorized retiring FlowGraph and the Then/Branch/Tap/FlowSegment layer.
Removed FluxFlow.Fluent and its builder-specific tests. Moved independent Generic
Host support to FluxFlow.Hosting/AddFlowApplication; moved selected native event
observation to FlowEventStream in FluxFlow.Observability. Both now use the direct
application independently. Migrated the sample to FluxFlow.DirectSample and the
package-consumer fixture to direct Core links. No substitute builder was added.

Core passed 150 tests per framework; Hosting 5 per framework; Observability 16 per
framework, including preserved event/error behavior and observer lifetime tests.
Full Release build and the source-reference consumer scenarios passed. The public
API snapshot was reviewed and verified normally. See the complete scope, migration
notes and release-check evidence in ../reviews/2026-09-13-fluent-removal.md.
Next: return to portable JSON definition review; Sessions remains deferred.

### Step 47 â€” improve flat JSON authoring without restructuring

Preserved written member order, accepted structural case variants, made Resources
optional on input, and added exact root-relative resource paths. Component/MQTT
resource lookup and store adapters use the common behavior; no resource search
across groups and no new graph/document containers. Added regression coverage for
order, casing, actual link execution and resource references. JSON 113, Composition
272, MQTT Composition 37, Engine 537, Core 300 executions, Storage Composition 2,
Sessions 57 and normal API baseline 2 passed. Full Release solution build passed.
See 2026-09-13-flat-json-improvements.md for the contract and precise scope.

### Step 48 â€” minimal direct C# fluent extensions

Added FlowExtensions in Core: Workflow adds/configures the real workflow and returns
its application; To returns the source output for repeated fanout; From returns
the target input for repeated fan-in. Both link extensions forward native C#
predicates, delivery policy and rejection callback to the existing link methods.
There is no builder, graph wrapper, factory requirement, expression translator or
new package. LinkTo/LinkFrom still return FlowLink. Configuration is immediate;
successful additions remain application-owned if a callback throws.

Two real execution tests cover chained fanout with captured predicates across
workflows, input-side fan-in, explicit discard and frozen topology after stop.
Core passed 152 on each net8/net10. Full Release solution build passed with zero
warnings/errors. Reviewed additive public API snapshot and normal verification
passed 2. The updated DirectSample ran successfully (ALPHA/BETA/GAMMA and all six
parity labels). JSON schema and execution are unchanged by this step.

### Step 49 â€” solution cleanup and local regression

Audited source/test/sample project membership and ProjectReference targets: no
orphaned project, broken target or duplicate project reference was found. Active
Core, Json, Composition and Engine responsibilities remain in use; no additional
project was removed in this step. Historical Fluent removal remains step 46.

Removed three unused imports left by the component-resource and event-test
refactors. Corrected the component-template sample link and the migration guide's
hosting instruction. Marked the earlier release train and migration-tool inputs
as historical so obsolete package/split plans do not masquerade as current work.
Historical evidence and the existing uncommitted changes were preserved.

Configured local solution regression passed 4,609 executions in 76 TRX reports,
with zero failures (framework targets count separately), under
`artifacts/cleanup-20260913`. This ran before the import/document-only cleanup;
the final Release solution build then passed with zero warnings and errors.
Acceptance, container integration, external smoke and TSql integration were
excluded by the repository's local regression filter. No package release or
external consumer qualification is claimed.

Next design discussion: represent named reusable functions in flat JSON using
the existing C# MakeFunction input/output contract, with cancellation/deadlines
remaining caller-owned. Do not introduce a new graph or builder for this purpose.

### Step 50 â€” named functions in direct applications and JSON

Implemented Resources.Functions at application and workflow level with optional
groups. FunctionDefinition contains only Input/Output; workflow definitions can
carry Resources without wrapping their flat components. Serialization retains
written group/member order and normalizes structural casing. Invalid leaves,
duplicate properties and invalid endpoint direction/existence fail clearly.

Core AddFunction/GetFunction register and retrieve existing MakeFunction delegates;
FlowWorkflow supplies its location for relative lookup. Registration is before
startup, endpoints already exist, and waiting remains caller-owned. Shared
ApplicationAddress code handles categorized resource references and workflow
resource addresses; port references reuse ResolvePort. Relative lookup checks
local then application; explicit application/cross-workflow paths remain available.
There is no private visibility, extra execution engine or new builder.

Both FlowJson.BuildAsync and Composition BuildApplicationAsync bind the definitions
after components/links exist. Composition export retains workflow resources, and
connection-specific resource visitors skip function leaves. Revision-aware function
hosting was not designed in this step: Engine explicitly rejects it rather than
silently ignoring the declaration. Constants and workflow connection activation
are still separate work; existing application connection syntax is unchanged.

Validation: final Release build passed with zero warnings/errors. Configured local
regression passed 4,628 executions in 76 reports with zero failures, under
artifacts/functions-20260913. This includes reviewed API baseline verification,
concurrent named-function calls, address lookup/shadowing, cancellation, frozen
registration, JSON round trips, invalid-port cleanup, Composition execution/export
and explicit revision-host rejection. Acceptance, container integration, external
smoke and TSql integration remain excluded by the normal local filter.

Next: design Constants references and the distinction between groups and object
values before implementing constants. Hot reload and optional private access
remain deferred. See 2026-09-13-flow-functions.md for executable API usage and scope.

### Step 51 â€” remove Group and address ordinary resource JSON

The user rejected a distinct Group concept across all resource sections. Removed
ResourceGroupDefinition, ResourceGroupBuilder and IResourceDefinitionContainerBuilder.
ResourceDefinition now owns JSON data, including object/array/scalar/null values.
Connection and function leaf contracts remain; nested objects have no grouping
metadata or special $value wrapper. Existing connection locations remain supported.

C# declarations accept dotted resource paths and materialize JSON objects. Migrated
typed MQTT extensions, sample and existing builder tests; duplicate/ancestor-leaf
collisions are checked before committing definitions. Connection enumeration is
shared by MQTT, processing profiles and revision planning instead of three separate
group traversals. Constants and Functions sections are not mistaken for connections.

ApplicationAddress.ResolveResourcePaths supports explicit, workflow-relative and
object-relative addresses. Existing ResolveResource and categorized function lookup
delegate to it. TryReadJsonValue reads exact object members or numeric array indexes;
ApplicationDefinition.GetResourceValue exposes whole values or nested properties
without activation or whole-document serialization. Structural section keywords are
normalized, user data names remain ordinal, and cross-workflow access stays public.
Arbitrary IConfiguration nulls are no longer guessed to be empty resource groups.

Validation: Release build passed with zero warnings/errors. Configured local
regression passed 4,631 executions in 76 TRX reports under artifacts/addressing-20260913.
After the final ResolveResource delegation, Core 157 per net8/net10, Json 123,
Composition 273 and MQTT Composition 37 passed again. The source API snapshot needed
refresh for the final declaration form; reviewed/updated it and normal verification
passed 2. Repeated focused tests overlap the full-run totals. External/container/
acceptance/TSql integration suites remained excluded by the normal local filter.

Next is Constants consumption: placeholder syntax/build substitution and runtime
access. The former group-vs-object question is resolved by using ordinary JSON.

### Step 52 â€” consume Constants at build time and runtime

Implemented whole-value ${...} constant references for direct FlowJson/Composition
construction. Preparation creates a separate execution definition, preserving the
source document's placeholders/order. Resolution uses ApplicationAddress, retains
JSON types (including objects, arrays and null), and reports missing/invalid paths
before activating components. Composition resource registration and preflight see
the prepared configuration. Constants themselves remain literal data; inserted
values are not recursively interpreted. $${...} escapes a literal placeholder;
embedded string interpolation and chained aliases are not added.

Core and workflow AddConstant/GetConstant use the shared resource path rules and
FlowValue. Definitions load whole constant objects once; C# registration uses
dotted paths before startup. Duplicates and non-object path crossings fail without
changing the previous value. Local lookup, application qualification and public
cross-workflow access are covered. Conversion remains consumer-owned.

Validation: Release solution build passed with zero warnings/errors. Configured
local regression passed 4,639 executions in 76 TRX reports, zero failures, under
artifacts/constants-20260913. Coverage includes typed substitution, source round-trip
preservation, literal escaping, scoped runtime reads, null/object/array values,
invalid-reference locations, registration freeze, Composition resource references
and normal public API baseline verification. Focused reruns overlap this total.
External/container/acceptance/TSql integration suites were excluded as usual.

Revision-aware hosting/hot reload remains deferred; Engine rejects nonempty named
constant definitions as it does named functions. See 2026-09-13-constants.md for the
implemented syntax and explicit limits. Connections/workflow activation and any
future hot-reload behavior remain separate decisions.
See 2026-09-13-resource-object-addressing.md for examples, migration and limits.

### Step 53 — replace named function resources with CallFunction

Supersedes the named-function registry from step 50. Removed FunctionDefinition,
DocumentFunctions, AddFunction/GetFunction and the special resource parsing tree.
Resources.Functions now gives an explicit migration error. C# MakeFunction and
RequestAsync remain available without registration.

CallFunction is an ordinary FlowTransformNode with RequestTo, ReplyFrom and
Timeout. It uses RequestAsync for each input and emits its reply through Output,
restoring the outer correlation and recording reply causation. Shared processing
handles incoming errors, target errors, timeout exceptions and diagnostics. No
additional serializer, queue, subscriber or request scheduler was introduced.

Direct FlowJson and Composition construction include the component. Addresses
resolve with the existing workflow-relative/full rules. Call dependencies use the
existing cycle detector and completion coordinator, preventing premature target
completion while callers remain active. Engine revision-host execution and hot
reload remain separate work; its catalog does not receive the direct-only binding.

Updated current resource/function docs, migration guidance and the source API
snapshot. Tests cover nested concurrent calls, timeout followed by recovery,
target exceptions, incoming errors, pending-request shutdown, ordinary producer
completion, invalid addresses, cycles and direct JSON/Composition execution.

Validation: full Release build passed with zero warnings/errors. The configured
local regression passed 4,644 executions in 76 TRX reports with zero failures;
evidence: artifacts/call-function-20260913 and artifacts/call-function-regression.log.
The normal release suite includes source API baseline verification. Focused runs
overlap the total; net8/net10 executions are counted separately. External service,
container, acceptance and TSql integration suites remain excluded by the normal
local filter. Diff whitespace checks passed. No packages were published.


### Step 54 — defer Constants and remove the experiment

The user considers the constant design insufficiently mature for the framework.
Removed DocumentConstants, the Core application/workflow constant APIs/store,
the now-unused categorized resource resolver, constant section parsing and
activation exceptions, and the Engine's constant-specific guard. General
resource JSON/property addressing and connection lookup remain.

Direct JSON and Composition builds no longer expand or unescape strings and no
longer copy/serialize/reparse the full definition for preparation. Replaced the
experimental tests with configuration pass-through coverage, retaining the
independent tests of scoped resource/property/array addressing. The design note
is now explicitly deferred; step 52 describes historical work, not current support.

Naming remains CallFunction for the component that executes a call per input.
The existing MakeFunction helper constructs a callable delegate; no rename or
duplicate alias was added for this tentative naming suggestion.

Validation: Release solution build passed with zero warnings/errors. Local regression
passed 4,638 executions across 76 TRX reports with zero failures, including the
normal public API snapshot check. Evidence: artifacts/constants-removal-20260913
and artifacts/constants-removal-regression.log. The lower count reflects deletion
of the retired feature tests and addition of literal configuration coverage;
net8/net10 executions remain counted separately. External/container/acceptance/TSql
integration suites remain excluded. Source searches found no remaining Constants
feature or categorized resolver references; whitespace checks passed.

### Step 55 — workflow connections in direct Composition applications

Removed the direct-build rejection of workflow connections. Added a full-document
ResourceInstanceDefinition.Enumerate overload returning full addresses; built-in
processing profiles and MQTT registration use it. Registrars still run once per
registration identity and the existing application resource scope owns disposal.

WorkflowDefinitionBuilder.AddResource reuses ResourceContainerState with a workflow
address context, including typed contracts, handles and export. Component lookup
uses ResolveResourcePaths with its workflow and retains host-owned key fallback.
MQTT broker/subscription references resolve against the resource index with type
validation; retry/credential/certificate lookup uses the same workflow path rules.
No private visibility, workflow service provider or new resource lifecycle exists.

Tests cover local/application fallback, explicit cross-workflow paths, shared
instances and one disposal after components, construction failure cleanup, typed
C# handles and round-trip export, and MQTT configuration binding without a
network connection. An existing certificate test caught a missing continue in
the edited string-reference branch; corrected it and reran MQTT successfully.
Custom registrars that only enumerate application.Resources must opt into the
new full-definition overload. Engine revision hosting remains explicitly deferred.

The first full regression exposed an incorrect expectation in the new typed MQTT
resource test: ToDefinition intentionally rejects activation contracts. Corrected
the test to assert that rejection and inspect setup.Definition; the separate raw
resource test verifies portable export. No export restriction was relaxed.

Final validation: Release solution build passed with zero warnings/errors. The final local regression passed 4644 of 4644 executions in 76 TRX reports, zero failures, including normal source API baseline verification. Evidence: artifacts/workflow-connections-final-20260913 and artifacts/workflow-connections-final-regression.log. Earlier failed/focused attempts remain separate and overlap the final total. External/container/acceptance/TSql integration tests were excluded as usual; no broker connection or publication was performed. Whitespace checks passed.


### Step 56 — remove catalog and activation forwarding

Removed ComponentCatalog. Composition, Engine, component tests, support helpers
and benchmarks now consume a standard IReadOnlyDictionary keyed by component type.
CreateComponentMap in the existing registration extensions builds an ordered,
read-only snapshot, preserving repeated-identity and conflicting-registration checks.
The direct construction loop now invokes the selected factory directly;
ComponentActivation.CreateAsync was removed. Physical-port validation remains.

This does not remove ComponentDescriptor or registration builders. Descriptor still
wraps factory execution and holds metadata used by preflight/link checks. Continue
simplifying these responsibilities; do not claim the whole path is bare delegates.
See 2026-09-13-component-loading.md. The dictionary test explicitly checks exact
keys; definition and registration name validation remain. Initial full regression
caught two diagnostic wording changes; restored the existing null/conflict wording.

Final validation: Release solution build passed with zero warnings/errors. Final
local regression passed 4,644 executions in 76 TRX reports, zero failures, including
the normal public API baseline verification. Evidence: artifacts/component-map-final-20260913
and artifacts/component-map-final-regression.log. The first failed attempt and focused
runs are separate overlapping evidence. External/container/acceptance/TSql integration
suites remain excluded. No publication or external consumer qualification was performed.
Source searches confirm the removed class/forwarder have no C# consumers; whitespace
checks passed. This is a completed incremental step, not completion of the broader
Descriptor and registration-builder simplification.


### Step 57 — flatten component registration builders

Removed AdvancedFluxFlowRegistrationBuilder and RuntimeComponentInstanceBindingBuilder.
The direct registration API is AddFluxFlowComponents().AddComponent(type, configure).
UseInstanceFactory now returns the registration builder itself. Removed unused
protected/virtual extension hooks, configuration copying and the duplicate internal
ComponentDescriptor constructor; descriptor registration identity stays internal.
Migrated source consumers, tests, package-consumer scripts and current documentation.

The typed port binding builder and ComponentDescriptor still exist. Runtime port
mapping, validation, event attachment and cleanup are preserved. Continue the bounded
descriptor/factory simplification in 2026-09-13-component-loading.md; do not claim
all builders or descriptors have been eliminated.

Removed the obsolete copy-hook theory (two framework executions); snapshot and
duplicate-declaration coverage remain. Initial regression passed 4641/4642 with
one reflection test still counting all methods on the merged registration builder;
updated it to inspect the five port declarations. No runtime assertion was relaxed.

Final validation: Release solution build passed with zero warnings/errors. Final
local regression passed 4,642 executions across 76 TRX reports with zero failures,
including normal public API baseline verification. Evidence:
artifacts/component-builders-final-20260913 and
artifacts/component-builders-final-regression.log. The initial failed run is separate
and overlaps these totals. External/container/acceptance/TSql integration tests
remain excluded; no publication or external package qualification was performed.
Whitespace checks passed.


### Step 58 — delete the outer registration builder

Deleted FluxFlowRegistrationBuilder. AddComponent overloads, all 19 package
registration extensions and AddFluxFlowUpdates now accept/return IServiceCollection.
No replacement wrapper was added. Direct AddComponent initializes its map and
returns the same service collection; a focused test verifies the no-prerequisite
path. AddFluxFlowComponents only initializes an empty map when needed.
AddFluxFlowMetrics replaces the package AddMetrics name to avoid the .NET DI
extension collision. Workflow AddMetrics is unchanged. Migrated consumers and docs.

ComponentDescriptor and the two remaining runtime/typed binding builders are still
next targets, not accepted as the final architecture. See component-loading plan.
Initial regression had one stale documentation failure; corrected the package
registration example. All runtime suites passed. Focused registration tests passed.

Final validation: solution Release build passed with zero warnings/errors. Final
local regression passed 4,642/4,642 executions in 76 TRX reports, including normal
public API baseline verification. Evidence: artifacts/direct-registration-final-20260913
and artifacts/direct-registration-final-regression.log. The initial run and 12 focused
registration tests overlap the final total. External/container/acceptance/TSql
integration suites remain excluded. No publication was performed. Whitespace
checks passed; removed builder references remain only in its absence test.


### Step 59 — remove the port-binding class hierarchy

Deleted ComponentBinding and ComponentInputBinding, ComponentOutputBinding and
ComponentEventsBinding. Private delegates perform the actual binding. Registration
snapshots sort delegates in input/signal/output/events order once; activation calls
them once each instead of scanning the bindings for each role. Stable ordering
within each role and original selector-based registration identity remain.
No public API is added; ComponentDescriptor and the two public builders remain
explicit removal targets. Existing tests cover order, native port identity, deferred
execution, null/throwing selectors, snapshot isolation and owned cleanup.

Validation: Release build passed with zero warnings/errors. The full local run
completed 4,642 executions across 76 TRX reports: 4,641 passed and the sole failure
was the source-declaration baseline (it also scans protected declarations inside
internal types). Reviewed the removed internal hierarchy, refreshed the baseline,
then reran baseline verification normally with acceptance disabled; it passed.
No production code changed after the full run. Do not describe the original TRX
folder as an all-green run. Evidence: artifacts/port-binding-20260913 and
artifacts/port-binding-regression.log. External/container/acceptance/TSql integration
suites remain excluded. No publication was performed. Whitespace checks passed.


### Step 60 — bypass typed binding builders for standard components

Removed StandardComponentBindingExtensions/HasStandardPorts. The new direct
UseStandardFactory(factory, optionalInputShape) path registers native standard ports
without creating RuntimeComponentBindingBuilder<TNode>. Migrated all 33 standard
family registrations, their existing tests, documentation and load benchmark.
Synchronous/asynchronous and additional-owner factories preserve activation and
cleanup. The same standard-port shape validation, deferred construction, metadata
snapshots and original selector identity apply. Custom ports retain UseFactory.
Existing standard-component tests now exercise the direct path, including invalid
port shapes and cleanup failures. This removes the typed builder from the common
path; it does not remove the main registration builder or ComponentDescriptor.

Final validation: Release solution build passed with zero warnings/errors. Full
local regression passed 4,642/4,642 executions across 76 TRX reports, including
normal public API baseline verification. Evidence: artifacts/standard-factory-20260913
and artifacts/standard-factory-regression.log. External/container/acceptance/TSql
integration suites remain excluded. No publication was performed. Whitespace
checks passed. The removed HasStandardPorts API has no live C# callers.


### Step 61 — remove hidden descriptor activation

ComponentDescriptor now stores the original factory delegate without decorating it.
Moved lifecycle initialization to ComponentInstance.ActivateAsync and physical-port
checks to a private instance method; deleted ComponentActivation. Direct Composition,
Engine, and factory tests now invoke explicit activation. Existing processing setup,
owned-node protection, repeated-instance protection, event attachment, null-result
policy and cleanup/aggregate errors are preserved. A descriptor test asserts the
factory delegate stays unchanged. The public Factory property now means raw factory;
full initialization requires the explicit activation method (documented migration).
No replacement helper class was added. Metadata descriptors/builders remain targets.

Validation: Release solution build passed with zero warnings/errors. Initial full
regression had one timeout in the direct-node logging test (outside Composition);
all 18 focused logging executions passed unchanged on both targets. The final full
local regression then passed 4,642/4,642 executions in 76 TRX reports, including
normal API baseline verification. Evidence: artifacts/instance-activation-final-20260913
and artifacts/instance-activation-final-regression.log. Initial/focused runs overlap
that total; no timeout or behavioral assertion was weakened. External/container/
acceptance/TSql integration tests remain excluded. No publication was performed.
Whitespace checks passed. ComponentActivation has no remaining C# definition/callers.


### Step 62 — eliminate the nested component descriptor

Deleted ComponentDescriptor. ComponentContract now directly holds immutable port,
option/resource metadata and the original factory. Removed contract.Descriptor;
typed handle/options contracts inherit the same data rather than retaining a nested
object. The registration map, direct/Engine activation and link checks all consume
ComponentContract. ApplicationSetup.ComponentContracts replaces ComponentDescriptors.
Updated registration identity tests to assert the actual typed contract is retained.
The immutable fields are shared from the temporary declaration when a typed contract
is constructed; that temporary object is not retained. No JSON restructuring or
replacement descriptor/catalog class. The two remaining builders are still targets.

Final validation: Release solution build passed with zero warnings/errors. Full
local regression passed 4,642/4,642 executions in 76 TRX reports, including normal
public API baseline verification. Evidence: artifacts/contract-flatten-20260913 and
artifacts/contract-flatten-regression.log. External/container/acceptance/TSql
integration suites remain excluded. No publication was performed. Whitespace
checks passed. The old descriptor type remains only in the explicit absence test
and historical documentation; no production declaration remains.


### Step 63 — direct Mapper registration without either builder

ComponentContract.CreateStandard accepts a synchronous standard node factory,
typed options/handle functions and passive metadata directly. It constructs native
ports without either registration builder. Mapper now uses it; ConfigureMapper is
deleted and metadata sits with the typed contract. Internal Mapper deserialization
and resource resolution remain unchanged. Other families are not yet migrated;
async/additional-owner factory support is not added speculatively. The remaining
builders cannot be deleted until their consumers migrate.

Extended existing standard-port tests to exercise the direct path for native
identity/events, shaped/unshaped metadata without activation, invalid ports and
cleanup failure aggregation. This adds 12 test executions to the full local suite.

Final validation: Release solution build passed with zero warnings/errors. Full
local regression passed 4,654/4,654 executions in 76 TRX reports, including normal
API baseline verification. Evidence: artifacts/direct-mapper-20260913 and
artifacts/direct-mapper-regression.log. External/container/acceptance/TSql integration
suites remain excluded. No publication was performed. Whitespace checks passed.


### Step 64 — migrate fourteen more direct registrations

Used the existing ComponentContract.CreateStandard overload for Assertion,
EventExpectation, FileRead, FileWrite, HttpClient, MetricAggregation, observability
Counter/Logger/Metrics, PayloadInspection, Projection, Window, StateReducer and
JsonSchemaValidator. Deleted their Configure callbacks; metadata is declared with
the contract. Original order, required flags, shapes, processing capabilities and
factory option/resource resolution are preserved. No new framework API or test
exceptions were introduced. There are 15 direct component registrations, including
Mapper, and 29 remaining callbacks (18 standard + 11 custom). Initial inspection
counted MQTT's two async factories and Correlation among candidates; those require
separate handling and were excluded from this synchronous batch.

Validation: Release solution build passed with zero warnings/errors. The full local
run completed 4,654 executions across 76 TRX reports: 4,653 passed; the sole failure
was the source-declaration snapshot after contract initializers changed. Reviewed
and updated the snapshot, then normal acceptance-disabled verification passed.
No production behavior changed after the full run (only blank-line cleanup).
Evidence: artifacts/direct-families-20260913, artifacts/direct-families-regression.log
and artifacts/direct-families-baseline-verification.log. Do not describe the original
TRX folder as an all-green run. External/container/acceptance/TSql integration tests
remain excluded. No publication was performed. Whitespace checks passed.


### Step 65 — remove duplicate option/resource metadata

Deleted ComponentOptions, ComponentResources, ComponentOptionMetadata,
ComponentResourceMetadata and ComponentConfigurationPreflight. Removed corresponding
contract fields, constructor parameters, registration methods, equality comparisons
and declarations throughout the component packages. Application/workflow resources,
typed options, factory binding and native port checks remain. Validation now belongs
to the factory/component; malformed optional resource keys are rejected on access.
Configuration failures during build release previously created components/resources.
Updated authoring guidance and replaced schema-only assertions with resource access
and initialization-failure tests. Port metadata/InputShape/builders are still pending.

Validation: Release solution build passed, zero warnings/errors. All 4,656 executions
passed across 76 TRX reports. Evidence: artifacts/remove-config-metadata-20260913 and
artifacts/remove-config-metadata-regression.log. Reviewed API snapshot refreshed before
normal full-suite verification. External/container/acceptance/TSql integration suites
remain excluded. No publication performed.


### Step 66 — remove descriptive input schemas

Removed InputShape from ComponentContract, ComponentPortMetadata and both registration
builders; removed ValidateInputShape/CreateFlowValueInput and shape overloads. All
component registrations now declare native ports without repeating their payload
schema. Removed FlowValueShape/FlowValueShapeValidationResult and the five standalone
FileSystem/Storage/Sessions/Metrics/Projection input-shape helper files, which no
longer had production consumers. IFlowValueMaterializer now requires only Materialize;
JsonFlowValueMaterializer constructors take error code/category only. Actual conversion,
domain validation, FlowError and original exception diagnostics remain unchanged.

Retained port names, block selectors, kinds, link cardinality, processing capabilities,
factory ownership and unique instance addressing. The remaining port registration
records still serve link compilation and native-block validation; this step does not
claim to remove all registration builders. Updated current authoring/migration docs.
The old shape inspection tests were removed; canonical link compilation, deferred
factory/selector execution and block ownership tests remain. Reduced duplicate
shaped/unshaped cases and updated the expected number of registration overloads.

Validation: Release solution build passed with zero warnings/errors. The full local
run produced 76 TRX reports and 4,636 executions: 4,634 passed, two API surface tests
still expected the removed HasInput overload. Corrected those expectations and reran
the entire Composition suite: all 275 passed. No production changes followed that
full run. Reviewed and refreshed the API baseline before full regression, which
verified it normally. Evidence: artifacts/remove-input-shape-20260913,
artifacts/remove-input-shape-regression.log and artifacts/remove-input-shape-focused.log.
Do not describe the original TRX folder as all green. External/container/acceptance/
TSql integration suites remain excluded. No publication or consumer migration.


### Step 67 — direct registration for remaining synchronous standard factories

Migrated Serialization's six components, Timers Delay/Throttle/Debounce, and Routing
Correlation to the existing ComponentContract.CreateStandard API. Deleted ten
Configure callbacks and made their existing factories internal for direct declarations.
No new framework API. Serialization's helper no longer takes unused input/output
CLR type parameters; Storage's helper likewise keeps only its actual options type.
Storage remains on the builder path because all four factories are asynchronous and
own additional resources. MQTT and Sessions likewise retain their current ownership.

There are now 25 direct standard registrations and 19 remaining callbacks: eight
async/additional-owner standard factories and eleven custom-port registrations.
The remaining builders are not yet deleted. Next: migrate the eight standard factories
with their existing async/disposal behavior, then remove custom-port builder dependencies.

Validation: Release solution build passed with zero warnings/errors. All 4,636 local
executions passed in 76 TRX reports, including normal source API baseline verification
following the reviewed update. Evidence: artifacts/direct-standard-remainder-20260913
and artifacts/direct-standard-remainder-regression.log. External/container/acceptance/
TSql integration suites remain excluded. Whitespace checks passed. No publication.


### Step 68 — finish built-in direct factory migration

All 44 official component registrations now use CreateStandard or CreateNative;
no component package refers to either runtime registration builder. Removed the
eight remaining standard callbacks (MQTT, Sessions, Storage) and eleven custom-port
callbacks (sources, timers, file-system, retry, join, session replay, MQTT).
CreateStandard now supports asynchronous nodes and asynchronous additional-owner
activations. CreateNative reads declared names from the node's native Ports,
including controls and events; no second queues or reflective discovery. Standard
shape validation and native adaptation share one activation/cleanup implementation.
Factory identity, completion and additional-resource disposal remain explicit.

Deleted all four UseStandardFactory overloads and AddStandardPorts from the old
builder. Tests/benchmark callers moved to direct standard registration. Async tests
cover deferred construction, native identity, explicit completion, disposal exactly
once, and preservation of node/resource cleanup failures. Removed duplicate tests
of the now-deleted standard builder path. Updated the family convention test to
recognize CreateNative and current documentation/examples.

Important remaining work: RuntimeComponentRegistrationBuilder and
RuntimeComponentBindingBuilder still expose the low-level custom/instance callback
API through AddComponent(type, configure) and ComponentContract.Create. Tests and
benchmark fixtures still use it, including selected/aliased and multiple event ports.
Do not claim that these types have been deleted. Next migrate those uses to direct
contracts/native ports or explicit ComponentInstance factories without removing
aliasing, multiple events, cardinality, control lifetime, or failure-cleanup coverage;
then delete the remaining builder API and obsolete identity/binding machinery.
User has authorized this remainder; no additional approval question is needed.

Validation: final Release solution build passed with zero warnings/errors. Final
full local regression passed 4,625/4,625 executions in 76 TRX reports, including
normal API baseline verification after review/update. Evidence:
artifacts/direct-native-final-20260913, artifacts/direct-native-final-build.log and
artifacts/direct-native-final-regression.log. Earlier direct-native-20260913 run had
one convention-test failure for the newly introduced CreateNative name; it was
corrected before the final run. External/container/acceptance/TSql integration tests
remain excluded. No consumer project migration or publication. Whitespace clean.


### Step 69 — delete remaining mutable registration builders

Deleted RuntimeComponentRegistrationBuilder and RuntimeComponentBindingBuilder,
AddComponent(type, configure), mutable factory-mode state, ComponentFactoryMode,
ComponentBindingIdentity and the delegate/binding comparison machinery. IServiceCollection
now registers an existing ComponentContract only; repeated use of that object remains
idempotent and distinct contracts with the same type fail without changing registrations.
Typed ComponentContract.Create decorates an existing registration with handles/options.

Native component declarations remain CreateStandard/CreateNative. The selected-block
case uses CreateBound and an immutable ComponentPortBinding list: each name, direction,
selector and cardinality is declared once. This is a fixed value, not a replacement
mutable builder or registry. It preserves aliases, controls, multiple named event
sources, normal outputs named Events, selector ordering, lazy activation and native
block identity. Binding lists are snapshotted; duplicate names fail before factories run.
Already assembled ComponentInstance factories use the existing contract constructor.
Sync/async nodes and additional owners retain initialization-failure cleanup and
original exception details.

Migrated tests, samples, benchmarks and package-consumer fixture source. Reworked
builder-only tests into direct registration/binding tests, preserving ownership,
control draining, aliases, named events, cardinality, snapshot and null-selector checks.
Removed obsolete tests of mutable configuration and multiple factory modes. Current
README/authoring/API examples and migration guidance now show fixed direct declarations.
No compatibility builder has been retained in production or test helpers. Searches
found no old builder/factory-callback usages in src/samples/benchmarks/eng C# source.
Package consumer fixture source was migrated; fresh package-only qualification was not run.

Initial local regression: 4,608 passed and two convention tests failed (one stale
port-reflection assertion and one sample/fixture source expectation). Corrected the
assertions, migrated package fixture source, and all 24 focused convention tests and
249 Composition tests passed. Final regression evidence follows below.


Final step 69 validation: Release solution build completed with zero warnings and
zero errors. The second local solution run produced 76 TRX reports: 4,608 passed
and two failed out of 4,610 executions. Both failures were stale package-consumer
fixture source assertions (old HasInput/HasOutput/AddComponent syntax), not runtime
failures. Updated those assertions and reran the complete Release test project:
167 passed, zero failed. The focused Composition suite passed all 249 tests and
the focused convention suite passed all 24 tests. All runtime tests passed in both
solution runs; neither full-run TRX directory is presented as an all-green run.
The normal public API baseline check passed without acceptance enabled.

Evidence: artifacts/remove-builders-final-build.log,
artifacts/remove-builders-final-20260913,
artifacts/remove-builders-release-final.log,
artifacts/remove-builders-focused-final.log and
artifacts/remove-builders-conventions.log.
Acceptance, container, external-smoke and TSql integration suites were excluded.
No package publication or fresh package-only consumer qualification was performed.
Whitespace checks are clean.


### Step 70 — registration closeout

Corrected the current-status headers and transfer continuation note. Fresh local
candidate 0.0.0-local.20260913.registration.2 contains 63 packages; target assemblies
and archive hashes are recorded in verification.json (ASP.NET Core telemetry targets
net10.0 only). An isolated package-only net8.0 consumer passed JSON, direct/code-first
construction, resources, health, durability and separate-process restart recovery.
No project references or external consumer edits were used. The fixture needed a
missing Definition namespace import.

The first consolidated regression found a real shutdown race: a cleanup fault could
replace the initiating RequestReply fault. A deterministic reentrant timer-failure
case reproduces it. Publishing the initiating block fault before cancellation and
cleanup fixes it; all 52 RequestReply tests passed. Repacked with a new candidate
version after the fix. Final consolidated regression: 76 reports, 4,610 passed,
zero failed. External/container/acceptance-category/TSql suites remain excluded.

Evidence under artifacts/registration-closeout-20260913/: verification.json,
regression-final/, bridge-repro.log, bridge-fixed.log and acceptance-net8-verified.log.
The initial package restore's vulnerability lookup could not reach NuGet; the second
pack used NuGetAudit=false, so this is not vulnerability-audit evidence. Pack emits
an expected non-packable sample warning. Dependency restoration for the isolated
consumer succeeded with network access; later retries reused those downloaded
archives. Nothing was published. Next: an Open/Close Gate that drops closed-state data.


### Step 71 — Gate component using direct registration

Added GateNode to Routing and flow.gate to AddRouting via one CreateBound factory.
The user selected Open/Close signal inputs and dropping values while closed.
InitiallyOpen defaults to false; BoundedCapacity defaults to 128. The original
message is forwarded while open; existing error envelopes bypass closed-state
filtering under the normal FlowNode policy. No payload conversion, retained queue,
Gate builder/descriptor or new project was introduced. Direct C# uses new GateNode.

FlowNode now supports terminal controls through the existing FlowControlInputs:
shared instrumentation, bounded admission, output drain before control completion,
and joining/cancellation on fault/disposal. Nodes without controls keep their
existing completion task. Tests cover closed dropping, original envelopes, errors,
initial state, invalid capacity, real backpressure/fault release, native ports,
input completion, feedback to Close in FlowApplication, configuration activation,
and completion/fault with an active asynchronous control.

Release build: zero warnings/errors. Routing: 115 passing tests per target (net8.0
and net10.0); Core Nodes: 324 per target. Hosted configuration tests passed. The
first whole-solution run found two stale README/registration-matrix conventions;
updated them for contract terminology and the direct Gate registration. Reviewed
and accepted additive public API changes; the normal baseline check then passed.
Final whole-solution local regression: 4,630 passed, zero failed, 76 TRX reports.
This includes the focused suites; do not add their counts again.

Evidence under artifacts/registration-closeout-20260913/: gate-solution-build.log,
gate-runtime-verified.log, gate-core.log, gate-composition.log,
gate-conventions-complete.log, gate-regression-final/ and gate-tests.json.
The same external/container/acceptance-category/TSql exclusions apply. The verified
step-70 package candidate predates Gate; Gate validation is source-suite execution,
not a claim that Gate is included in that candidate. No publication or external
consumer changes. Detailed review: docs/reviews/2026-09-13-registration-closeout-and-gate.md.


### Step 72 — Declare native ports in the factory context

Added AddComponent(type, factory) with context.HasInput/HasOutput/HasEvents.
Gate now constructs its node and declares all five ports in the same callback;
RoutingComponents.Gate and its CreateBound declaration are removed. Registration
stores a type/factory pair, not a new ComponentContract or definition builder.

Composition and Engine now construct instances, validate native port declarations,
and compile links using each actual instance's ports. Port names may differ between
instances of the same type. Invalid declarations/links dispose newly created nodes;
shared owned nodes are rejected without disposing the original application. Engine
plans no longer carry a contract catalog, and the redundant built-component wrapper
was removed. Legacy typed/static-inspection APIs remain supported separately.

Validation: Release solution build passed with zero warnings/errors. Final configured
source regression: 4,640 passed, zero failed, 76 TRX reports. This includes direct
registration, custom names, conflicting registrations, failure cleanup, shared-node
ownership, Gate hosting, Engine, and normal public API baseline verification.
The public API baseline was reviewed/updated. Documentation examples now lead with
the direct callback. Evidence: artifacts/context-registration-20260913/build-final.log,
regression-final/ and verification.json. Acceptance, container, external-smoke and
TSql integration suites remain excluded; no new package candidate was qualified.

Remaining: migrate the 44 older typed registrations and their async/additional-owner
cases before removing ComponentContract/CreateBound globally. This step removes
those requirements from the new runtime registration path; it does not claim global
contract deletion or new external consumer acceptance.


### Step 73 — Async and additional ownership in context registration

Migration preparation: the direct AddComponent callback can now asynchronously
return IFlowNode. Context.SetLifetime supplies an optional explicit completion task
and additional disposal callback, reusing ComponentInstance lifetime handling.
Both callback shapes share activation/validation. Registration stays lazy and
idempotent, and sync/async type conflicts are rejected before DI mutation.
No new contract, builder or activation wrapper is required in the direct callback.

Six new tests cover awaited construction, laziness/idempotency, successful ownership
cleanup, cleanup after invalid declarations, preservation of node/resource cleanup
failures, sync/async registration conflicts, and explicit completion. Factory-local
resources remain factory-owned until a node is returned; failed constructors must
clean up what they acquired. Borrowed host resources must not be disposed here.

Release build: zero warnings/errors. Final local source regression: 4,646 passed,
zero failed, 76 reports. Public API baseline updated and normal verification passed.
Evidence: artifacts/async-context-registration-20260913/build.log, regression/,
verification.json. Same external/acceptance/container/TSql exclusions; no package
qualification, external consumer edits or publication.

The 44 typed registrations are still present. Next migration must reconcile their
code-first definition registrations with DI without introducing duplicate type
registrations, and preserve HTTP's parallel processing capability. This step
completes async/additional ownership prerequisites, not the family migration itself.


### Step 74 — Routing migrated off component contracts

Deleted RoutingComponents and its Window/Correlation/Join ComponentContract
properties. AddRouting registers the node factories directly; each declares its
native Input/Output/Events or Left/Right/Output/Events on context. Typed AddWindow,
AddCorrelation and AddJoin remain, including configured capture overloads and typed
port handles. Their callbacks are identical to JSON/DI callbacks, not a second
factory implementation. Gate remains on its already migrated direct path.

ApplicationSetup carries an immutable ComponentFactories dictionary. The existing
WorkflowDefinitionBuilder can attach a factory with configuration/handle creation,
without an additional registration class. Core construction and Engine merge this
with DI, accepting identical delegates and rejecting conflicts. Definition failure
does not commit the factory/component. Setup export and the portable-only build
path reject in-process callbacks. Engine revision comparison detects changed factory
delegates for each workflow; unchanged delegates retain the existing revision.

Release build: zero warnings/errors. Full configured source regression: 4,651
passed, zero failed, 76 reports. Added coverage for typed Window execution both
with and without family DI, failed definition rollback, factory snapshot immutability,
DI conflicts before activation, and revision callback changes. Existing hosted
Window/Correlation/Join/Gate tests passed. Reviewed the public API removal/additions
and updated the baseline; its normal check passed in the final suite.
Evidence: artifacts/routing-context-migration-20260914/build3.log, regression/,
verification.json. Same external/container/acceptance/TSql exclusions. No new
package qualification, publication or external consumer modifications.

41 legacy typed registrations remain. Next migrate remaining component families
onto this same shared definition/DI factory path, preserving async resource ownership
and HTTP parallel-processing capability. ComponentContract/CreateBound are not yet
globally removed. RoutingComponents source references must migrate to the retained
AddWindow/AddCorrelation/AddJoin methods; standalone node usage is unchanged.


### Step 75 — Assertions, Payloads and Validation migrated

Removed AssertionsComponents, PayloadsComponents and ValidationComponents and their
three ComponentContract properties. AddAssertions/AddPayloads/AddValidation now
register callbacks that construct nodes and call context.HasInput/HasOutput/HasEvents.
Typed AddAssertion/AddPayloadInspection/AddJsonSchemaValidator retain existing
options, optional configuration behavior and fluent capture overloads; they embed
those same callbacks. No additional framework API or registration layer was added.
Expression engine, optional context factory, schema selector and clock remain
host-owned; node algorithms and result/error envelopes are unchanged.

Registration tests now inspect activated native ports rather than static metadata.
Six new execution cases verify typed definitions with and without family DI across
all three families. Existing hosted assertion and validation behavioral tests pass.
Updated package READMEs and migration guide, including removal of obsolete UI hints.
The first full run found an old README contract-wording convention; updated that
convention for all four migrated families (including Routing), then reran the suite.

Final Release build: zero warnings/errors. Full configured local source regression:
4,657 passed, zero failed, 76 reports. Reviewed API removals and updated the baseline;
its normal verification passed in the final run. Evidence:
artifacts/component-context-migration-20260914/build-verified.log,
regression-final/ and verification.json. Same external/container/acceptance/TSql
exclusions apply. No package qualification, external consumer changes or publication.

38 legacy typed registrations remain. Continue family migration using the existing
shared definition/DI callback path. Async owned-resource families and HTTP parallel
processing still require preservation of their specific behavior. Global removal
of ComponentContract/CreateBound remains unfinished.


### Step 76 — All remaining 38 builtin registrations migrated

All 45 builtin registrations across 19 families now use direct context factories.
Removed the remaining 15 contract containers; retained 44 typed AddX methods and
55 capture overloads. Typed definitions and DI share callbacks. Async Sessions/
Storage ownership and HTTP parallel relaxed-order processing remain covered.
No builtin legacy contract/activation usage remains under src/Components.
Legacy custom-template APIs elsewhere remain a separate cleanup boundary.

Release build: zero warnings/errors. Final configured local regression: 4,688
passed, zero failed, 76 reports. Added 29 resource ownership cases and two HTTP
profile cases. Fixed a test-only Gate live-output subscription race. Reviewed API
baseline and updated all family registration documentation. Across 34 captured
production C# files, 3,478 -> 3,622 lines: this is structural simplification, not a
net line reduction.

Fresh candidate 0.0.0-local.20260914.context.1 produced 63 packages. Independent
package-only consumers passed on net8.0 and net10.0 (45 factories, typed
Serialization roundtrip); existing comprehensive package-only acceptance passed
on net8.0 including SQLFile restart. No external services, publication or external
consumer adoption. Local regression exclusions remain Acceptance,
ContainerIntegration, ExternalSmoke and TSql.IntegrationTests.
Evidence: artifacts/all-context-migration-20260914/verification.json and logs.
See docs/reviews/2026-09-14-builtin-context-registration-complete.md for scope.
Next: audit and remove remaining legacy custom-template APIs after migrating their
necessary samples/tests. There are zero builtin registrations left to migrate.

### Step 77 — Obsolete registration API cleanup complete

Removed all component contract/template APIs, activation wrappers, contract maps
and the empty AddFluxFlowComponents bootstrap. Custom samples, tests, benchmarks
and package acceptance now use direct context factories. Actual instance ports
are the sole compiler input. Resource contracts, typed AddX methods, async
ownership, processing profiles and runtime behavior remain supported.

Production C#: 846 removed / 162 added, net -684 lines across 18 changed paths.
Final local regression: 4,627 passed, zero failed/skipped, 76 reports. The 61-case
reduction from step 76 removes/replaces obsolete API-only assertions; retained
runtime scenarios were migrated. An unfiltered attempt encountered missing SQL
and container prerequisites and is separately recorded, not claimed as passing.

Final candidate 0.0.0-local.20260914.cleanup.2 contains 63 packages. All-family
package consumers and comprehensive SQLFile restart acceptance pass on both
net8.0 and net10.0. Public API baseline, current documentation and examples are
updated. Evidence: artifacts/legacy-cleanup-20260914/verification.json.
See docs/reviews/2026-09-14-registration-cleanup.md for details and limitations.
No internal registration cleanup remains. External consumer adoption and release
publication remain separate, unperformed work.

### Step 78 — Lazy FlowValue and local conversion complete

FlowValue is now an abstract class exposing Json/CreateJson with a thread-safe
lazy owned representation; JsonFlowValue handles dynamic JSON. Removed the old
From/Deserialize/Kind helpers and all framework materializer/result/base APIs.
HTTP/MQTT requests and FlowError inherit FlowValue. Components own conversion and
generated metadata; canonical FlowMessage has one payload with explicit IsError.
Persisted envelope shape and generic adapters remain. Capture prepares JSON and
converts a payload conversion failure to portable error data; actual store failure
remains fail-closed. Non-captured typed forwarding does not force JSON.

Final Release regression: 4,630 passed, zero failed/skipped, 76 reports. Candidate
0.0.0-local.20260914.value.2 contains 63 packages. All-family independent consumers
and comprehensive SQLFile restart acceptance pass on net8.0/net10.0. Source API
baseline, samples and current documentation are updated. No publication or external
consumer adoption. See docs/reviews/2026-09-14-lazy-flow-value.md and
artifacts/lazy-flowvalue-20260914/verification.json for evidence and scope limits.

