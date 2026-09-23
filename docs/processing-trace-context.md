# Processing trace context

The Core processing wrappers emit `flow.process` consumer activities from the
`FluxFlow.Core` activity source. A host chooses listeners, sampling and export.
Core adds no telemetry SDK, exporter, logging dependency or subscription system.
These activities describe queued processing, not receipt of a diagnostic event.
Source control actions use the same helper and source, with operation name
`flow.control`. There is no separate telemetry runtime for controls.

## Message context

New canonical and typed messages capture the current W3C activity in the existing
immutable carrier headers. A caller-supplied carrier containing any field declared
by the propagator takes precedence. Headers use the existing ordinal key contract;
use the lowercase standard names. Caller dictionaries are never modified.

Headers now contain FlowValue values. The reserved traceparent/tracestate entries
remain JSON strings; non-string entries are not parsed as tracing context. Other
structured headers are carried unchanged and are not materialized by tracing.

`WithValue` create new messages. When inheriting headers, they capture
the current W3C activity, replacing the prior creation context. If the new context
has no trace state, stale `tracestate` is removed. Explicit headers with a
propagator fields retain that context. Without a current W3C activity, existing headers
are retained; no trace context is invented. Hierarchical activities are not copied.

Internal typed projections and JSON deserialization preserve message identity and the
original carrier. Ordinary forwarding and fan-out keep the same envelope reference
and creation context. No per-hop clone, mutable activity field or auxiliary message
registry is introduced. The existing business `TraceId`, `MessageId` and
`CausationId` keep their meanings; business `TraceId` is not an activity trace ID.
Baggage is injected/extracted by the default platform propagator and restored on
single-message processing activities. Request activities use the chosen parent's
baggage: the active W3C caller when present, otherwise the message carrier. Aggregate
processing does not arbitrarily merge baggage from multiple inputs. With no activity,
forwarding/derivation retains the existing carrier.

Trace carrier injection and extraction use the platform
`DistributedContextPropagator.CreateDefaultPropagator()` (available on both target
frameworks), rather than assigning activity IDs to headers or reading the trace
fields directly. Its getter/setter callbacks adapt the immutable FlowValue header
dictionary to a string carrier. `FlowTracing` contains no protocol field names or
field-name allowlist: `Propagator.Fields` identifies existing explicit carriers and
the fields to clear before replacing context; Inject decides which fields to write,
and the extraction callbacks read whatever names the propagator requests. Clearing
all owned fields also removes stale baggage without touching business headers.
This supersedes the earlier trace-only filter. The propagator is a fixed default instance, not
`DistributedContextPropagator.Current`; host-selectable propagation formats are
not introduced by this change. The returned parent string is converted to an
ActivityContext using the platform `ActivityContext.TryParse` API. Clearing stale
context, preserving explicitly supplied headers and selecting the correct queued
message remain FluxFlow responsibilities. No OpenTelemetry SDK dependency is added.

## Processing boundary

For one input, its valid creation context is both the activity parent and a link.
Missing or invalid context starts a new root. An unrelated ambient activity captured
by a background block is not treated as the message's parent. Processing restores
the previous ambient value when it exits. This also applies with no listener or
when sampling returns no activity: derived messages retain the input context rather
than accidentally capturing the worker's background context.

Unchanged messages processed by multiple nodes therefore produce sibling spans.
When a node creates a new message inside its processing activity, that new message
carries the processing context for downstream consumers. This is not a fabricated
hop-by-hop parent chain for an unchanged envelope.

`FlowNode` measures its delegate, awaited work and emissions made within that
delegate, including error-message emission. `FlowTransformNode` measures its
transform delegate; its separate result-forwarding block is outside that duration.
Neither duration includes input queue residence or all downstream work.

Existing outcomes are preserved: action-node exceptions become error data;
unexpected transform exceptions fault the component; requested cancellation keeps
the existing lifetime contract. Processing exceptions set error status and
`error.type`, without exception text or payload. Requested cancellation has an
unset error status and `flow.processing.outcome=canceled`. An incoming error message
still bypasses the normal handler and is tagged as error data, not automatically
classified as an unhandled exception.

Full-data samples also include component type, business message/trace IDs and the
input error flag. Those identities are span attributes, not metric dimensions.
Propagation-only samples omit these auxiliary tags. With no listener and no ambient
activity, processing creates no activity; untraced message creation adds no headers.
These are implementation properties, not a quantified overhead guarantee.

## Independent-message fan-in

Multiple upstream outputs can feed the same input without combining messages.
Each processing invocation retains only its own message's causal parent/link,
including when two invocations overlap across awaits. Shared trace IDs do not mean
shared span IDs; independent traces must not become related just because they use
the same component. Derived messages capture their own invocation's activity.
Off/None sampling retains the original carrier instead of the pump's ambient one.

This routing case is distinct from a join or batch producing one result from
multiple messages. The latter needs links for its contributing contexts; the
single-input helper cannot infer those contributors. Custom components can use the
public `FlowTracing.StartProcessing(ReadOnlySpan<FlowMessage>, object)` overload.
It starts an aggregate root with a link for each valid input context. Missing or
malformed carriers are omitted, not replaced with worker context. Full-data samples
record component type and input count, not an arbitrary input's business identity.
No source messages or input collection are retained after starting the scope.
See the [messaging correlation guidance](https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/#consumer-spans).

The built-in join uses ordinary input scopes for key selection/pending admission
and an aggregate scope for actual matched-result construction and bounded output
admission. The result carries the aggregate context while its business lineage
remains left-sided. Timeout emission uses the expired input's context, including
timer and completion-flush commands. Non-message commands suppress worker context.
Completion flush uses the existing lifetime token: graceful input completion does
not cancel it, but forced owner failure interrupts blocked emission. A result span
ends at bounded output admission, not after all downstream delivery. Consequently
an admitted result may have a completed span while the component is still draining.
Owner-aborted blocked emission records cancellation; output admission decline
records failure. The original owner fault remains the component's fault surface.
With disabled or failed activity creation, result derivation retains the source
carrier. Matching, FIFO, deadlines and reliable output policy are unchanged.

Scopes expose `Failed`, `Canceled` and `Outcome`; dispose them in the same logical
async operation where they started. They are not transferable message fields.
Do not add another scope around built-in wrappers that already own processing.
Join qualification is not proof for every aggregate component.

## Incremental metric snapshots

The optional metric aggregation component uses its existing processing wrapper for
each input update. Such a span represents that update and any immediate output, not
every historical contributor to the resulting snapshot. Coalesced completion emits
one separate root processing operation with at most one link to the last accepted
input. Invalid/rejected samples do not replace that input; a group-limit sample does
contribute to global totals and can supply final business lineage.

This is deliberately bounded correlation, not a complete provenance graph. No list
of historical messages or contexts is retained. The existing last-input field is
used only in coalesced mode and released after final emission or disposal. It retains
the existing materialized sample, not extra unused fields from the original payload;
one null-payload canonical envelope supplies correlation at final flush. This
does not impose a new memory bound on the component's existing rate-window data.

Final snapshot serialization and bounded output admission run inside the operation;
downstream consumption is outside it. Full samples record the snapshot outcome;
owned cancellation is canceled, while output decline or an unexpected exception is
failed. Existing original-owner failure semantics remain unchanged. With no listener,
sampling None or a failed activity start, final output retains the accepted input's
carrier, never the completion worker's ambient context. Business IDs and last-input
causation are unchanged. Per-sample, empty and rejected-only streams have no final
operation. Host sampling and export remain outside the component.

## Source control actions

Controls declared with `FlowSource.AddControlInput` restore the incoming message's
context for the actual awaited action, including actions accepted after production
ends. Their duration ends when the action returns or throws, not when the source's
entire lifetime ends. Full-data samples add `flow.control.port` to the existing
component/message attributes. Independent control queues have independent activities;
one control's completion does not end a concurrent sibling's activity.

Both value and error messages reach the control action unchanged. Graceful completion
drains accepted controls; forced owner failure cancels them. An action exception
sets error status without changing the original owner failure. Cancellation requested
by the control lifetime uses the canceled outcome; an unrelated cancellation exception
is still a failure. Sampling and callback containment follow the ordinary processing
helper. With no activity, derived messages retain their input carrier, not the pump's
ambient context. No producer-loop lifetime span or transport acknowledgement span is
implied by this boundary.

Sampling flags retain the installed runtime's behavior. For a recorded parent and
`PropagationData`, .NET 8 inherits the recorded flag while .NET 10 does not; both omit
full-data tags. Core does not override that platform decision. See the
[runtime sampling change](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/activity-sampling).

## Request exchanges

The shared `FlowRequestReply` emits `flow.request` internal activities from the
same Core source. A span starts after bounded exchange registration succeeds and
ends only after reply waiting and admission cleanup finish. An early reply does
not stop the span while its send callback is still active. Invalid arguments,
pre-cancellation, capacity rejection and an already-stopped coordinator start no
span. This is an in-process exchange operation, not a transport client/server span.

A current W3C caller activity is the parent. Without one, the request message's
valid creation context is the fallback parent; otherwise the request starts a root.
The original creation context is also a link when present. Distinct exchanges can
share a trace while retaining distinct spans and exchange correlations.

When a request activity is created, the already-required exchange envelope carries
its `traceparent` and optional `tracestate` for downstream queued processing. Stale
trace state is removed if the new context has none. The original message remains
unchanged; payload/error references and business message/trace/causation IDs retain
their previous semantics. No listener or sampling `None` means no new activity and
the original header reference is retained. The actual caller context is restored
before returning, including callback-failure and terminal paths.

Full-data samples add reply address, business message/trace IDs, the input error
flag, and `flow.request.outcome` (`received`, `timed_out`, `failed`, `canceled`).
Timeout/failure sets error status and exception type without payload or exception
text. Cancellation keeps unset status. A returned error message is successful
exchange delivery, not automatically an unhandled operation failure. Propagation
samples omit auxiliary tags. No request metric or exporter was added.

This follows the platform separation between library instrumentation and host
collection. See [library instrumentation guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)
and [span-kind definitions](https://opentelemetry.io/docs/specs/otel/trace/api/).

## Host ownership and current limits

Enable the `FluxFlow.Core` source in the host's tracing configuration. Code and
client instrumentation executed inside the delegate can use `Activity.Current`.
Exporter setup, log collection, sampling policy and baggage/security policy belong
to the host. Local carrier parsing does not assert a remote parent; transport
adapters still need explicit extraction/injection and remote sampling semantics.

Sampler/start/stop callback exceptions are contained so they do not change component
outcomes. Callbacks remain synchronous: a slow listener can delay processing, and a
throwing listener can prevent later listeners from observing a callback. Recovery
from listener-discovery failure during source initialization is not qualified.

This is partial framework instrumentation. Custom components, source operations,
fan-in links, custom request paths and transport/durability boundaries
still require qualification. FlowNode includes forwarding an already-error input
in its admission operation: successful forwarding remains non-failing, owned
cancellation records canceled, and declined/failed admission records failure.
It preserves the exact input envelope and rethrows forwarding faults rather than
attempting replacement error data. Components that deliberately handle error inputs
retain their existing processing/recovery contract. FlowTransformNode's separate
native result forwarder is not part of the transform operation; its delivery-stage
instrumentation remains separate work. Engine no longer creates
activities around diagnostic receipt; logs and diagnostic-source callbacks retain
the actual current processing or caller context. The complete telemetry and
release gates remain open; see the execution ledger.
