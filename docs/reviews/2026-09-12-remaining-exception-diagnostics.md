# Remaining exception diagnostics review

Checkpoint update after observability step 28: the straightforward families below,
workflow Observability selectors and selected materializer consumers are now covered.
See the observability plan for tests and scope. Delay/Throttle/Debounce processing,
queued Delay/Debounce context, request/reply response conversion/callbacks and HTTP
trigger reply conversion have also been addressed. The original inventory below
is retained as historical reasoning, not the current completion ledger.

Remaining Routing review findings: Window catches input failures outside its gate
but queues error emission; reporting belongs at that input catch. Correlation
catches inside its gate and schedules timers in finally, so reporting must avoid
observer callbacks under the gate and account for failures during final scheduling.
Join already establishes single-input and aggregate processing Activities, with
some inner catches rethrowing to command handlers. Do not report both boundaries or
arbitrarily assign an aggregate failure to one input without documenting ownership.

Follow-up through steps 29–31: Window input, Correlation outside-gate reporting
(including final-scheduling failure), and message-backed Join command handlers are
implemented and tested. Join diagnostic ownership is the handling command Activity;
inner aggregate/timeout emission catches only mark and rethrow. Source-less command
handling remains a lifecycle/context gap. These updates supersede the proposed
treatments above without claiming all Routing diagnostics are complete.

Resilience review: the inspected retry coordinator catches primarily preserve
cancellation, terminal faults and disposal failures; FlowRetryNode also propagates
output termination. These are not automatically per-message FlowError conversions.
Source/lifecycle failure reporting and standard runtime metrics/exporter integration
remain open design/implementation work. Keep Sessions deferred as requested.

Source review after observability step 11. This is a coverage inventory and next-step
selection, not a claim of complete OpenTelemetry support or new runtime qualification.
Scanned catch sites under src/Components and inspected the representative boundaries
listed below. A catch count is not a count of missing reports: many catches rethrow,
wrap for an outer boundary, provide intentional fallbacks or preserve terminal faults.

## Straightforward per-message gaps

| Family | Observed boundary | Recommended treatment |
| --- | --- | --- |
| Serialization | SerializationPipeline catches SerializationFailureException; its six converter nodes share the pipeline. The wrapper already retains InnerException. | One report in the existing pipeline catch; avoid reporting again in converter wrappers. |
| Mapping | FlowMapperNode catches input conversion, context creation, expression and output conversion failures. | Report at the current domain-error boundary; preserve its existing catch scope. |
| Assertions | AssertionNode catches input/evaluation exceptions; a false predicate produces a normal result. | Report evaluation exceptions only; do not turn failed assertions into exceptions. |
| Validation | JsonSchemaValidatorNode routes selector/evaluation exceptions to Failure. An invalid schema result is normal domain data. | One report in Failure, without changing ordinary invalid-input outcomes or schema construction. |
| Payloads | PayloadInspectNode catches classified/general failures; lower helpers also probe formats and use fallbacks. | Report at the handled operation boundary. Do not instrument every unsuccessful format probe. |

These are confirmed local gaps, not completed work. Serialization is the next bounded
step because one existing boundary serves JsonParse, JsonStringify, TextEncode,
TextDecode, Base64Encode and Base64Decode. Test representative real converter failures,
preserved inner exceptions, domain codes and subsequent successful conversion.

## Boundaries requiring separate context and lifetime review

| Area | Why a mechanical reporting call is insufficient |
| --- | --- |
| Workflow Observability components | FlowCounter, FlowLogger and FlowMetrics catch predicate/selector failures. Logger/Metrics can collect several selector exceptions for one outcome. Decide how to report those original failures without duplicate reports or an invented aggregate model. These components are distinct from the optional ILogger adapter. |
| Timers | Failures occur during input processing and deferred timer emission. An originating message can exist after its original Activity ended. Clock fallback and teardown catches are not automatically new workflow failures. |
| Routing | Correlation/Join/Window mix command processing, queued emissions, timers and shutdown. Join already creates processing Activities and marks some failures; check original exceptions and context at the actual handling boundary. |
| RequestReply | Response conversion, request timeout, reply callbacks and terminal coordinator faults have different ownership. SafeFail/SafeAcknowledge deliberately contain failures when the caller may have disappeared. |
| Resilience | Retry coordinator catches include cancellation, terminal fault capture and disposal. Do not label every retry or shutdown event a processing failure. |
| Sources and completion | Generated/sequence sources, file watching/enumeration, MQTT background connection/trigger handling, session replay/finalization and final aggregate snapshots require lifecycle-specific treatment. Do not fabricate a processing message ID. |

The current hook marks Activity.Current failed and publishes synchronously. Therefore
its use in deferred/background paths needs an appropriate active processing context;
the existence of an old envelope alone does not establish that context.

## Cross-cutting gaps and exclusions

- FlowValueMaterializationResult currently carries a value or FlowError, not the
  original exception. JsonFlowValueMaterializer and content materializers catch and
  convert exceptions before the node sees them. State, HTTP, MQTT and other consumers
  cannot recover details already discarded. Keep this as a separate, narrow design
  decision; do not put Exception into the serialized workflow contract or introduce
  a universal deserializer.
- Faulted Completion, rethrown wrapper exceptions and explicit shutdown cancellation
  already have ownership rules. Preserve those rules and avoid duplicate logging.
- Normal negative outcomes (assertion false, schema invalid, group limit, timeout
  result without a caught exception) must not acquire synthetic exceptions.
- Composition setup, external adapters and fallback/cleanup catches were surfaced
  by the search; they are not all individually qualified by this review.
- The logging adapter and processing diagnostic event are implemented, but standard
  runtime Meter instruments, complete distributed tracing qualification and exporter
  integration remain unfinished. Host/SSE remains phase 3.

## Decision

Continue with Serialization's shared pipeline, then the other straightforward
per-message boundaries in short steps. Reassess the materializer/context gaps before
instrumenting deferred work. Keep the user's Sessions concern recorded and deferred;
it does not alter the agreed sequence. No production/test code changed in this review,
and no tests or builds were rerun for this documentation-only checkpoint.
