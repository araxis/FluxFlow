# FluxFlow.Core

Message contracts live together in `Messaging/`: `FlowMessage`, `FlowValue`,
`JsonFlowValue`, `FlowError`, `FlowContent`, their JSON converters and message
identifiers. Processing blocks remain in `Nodes/`. These are all part of this
project; the existing `FluxFlow.Data` and `FluxFlow.Nodes` namespaces are retained.

Direct C# construction also has a small fluent surface in the same namespace:

```csharp
await using var app = new FlowApplication();
app.Workflow("main", workflow =>
{
    var source = workflow.Add("source", new Source());
    var archive = workflow.Add("archive", new Archive());
    var review = workflow.Add("review", new Review());
    source.Output.To(archive.Input)
        .To(review.Input, when: message => NeedsReview(message));
});
await app.StartAsync();
```

`Workflow` immediately adds/configures a real workflow and returns the same app.
`To` returns the original output for fanout; `From` returns the original input for
fan-in (`sink.Input.From(first.Output).From(second.Output)`). Predicates are normal
synchronous C# delegates, including method groups and captured dependencies. The
delivery and rejection-handler parameters are the existing Core policies. Use
`LinkTo`/`LinkFrom` when you need the resulting FlowLink. No builder, delayed
execution, expression translation or additional package is involved. If a workflow
callback throws, successful additions remain owned by the application; dispose it
normally. Native C# delegates are not portable JSON expressions.

Custom Dataflow components can use `FlowTracing.StartProcessing(message, component)`
for one message, or `FlowTracing.StartProcessing(messages.AsSpan(), component)` for
one aggregate operation. Dispose the returned scope in the same logical async
operation, across the awaited work it measures. The multi-message form starts a
root with links to valid input contexts; it does not choose one input as the parent.
`Failed`, `Canceled` and `Outcome` record operation results. `SuppressContext` clears
an inherited worker activity for non-message commands. On disposal it restores a
still-running saved activity, or clears the context if that activity has stopped.
The built-in processing wrappers already do this; do not wrap them a second time.
These helpers add no SDK/exporter and retain no input collection after starting.

Development preview of the instance-first application kernel. This project now owns the former foundation, keyed-exchange source and shared application addresses. No forwarding package or compatibility assembly is introduced.

Components are directly constructed. `application.AddWorkflow("main").Add("component", instance)` supplies graph naming and ownership. Components expose their actual blocks through IFlowComponent/FlowPorts. LinkTo and LinkFrom share one link representation. Business routes are bounded and reliable by default; unmatched data fails unless fallback/discard is explicit. Borrowed instances retain external disposal ownership.

For standard components, graph routes share the component's existing `FlowOutput<FlowMessage>` dispatcher. There is no second per-output graph queue: the component's output capacity is authoritative. The application constructor's `linkCapacity` bounds only the adapter required for a custom native source; that adapter uses the same output implementation and counts active plus queued work. Standard output capacity retains its waiting-items-plus-one-active contract. Removing the extra graph queue intentionally tightens backpressure for standard components.

Graph predicates run once per emission, including repeated sends of the same message instance. Selected graph targets use native `SendAsync` admission; `RejectWhenFull` uses native immediate admission. The native sender owns postponement, reservation and cancellation, while public source links retain their native filtering and callback contract. Both kinds of links share the same dispatcher and fanout loop. Independent sibling deliveries start before a delivery failure is observed; native offer scheduling can still race cancellation, and fanout is not atomic. Fan-in lifetime coordination remains separate from route selection, and fallback is used only when neither an ordinary route nor a reply route matches.

Optional update hosting also uses Core's `FlowOutput` dispatcher and shared delivery
batch. It supplies capture, revision route selection and post-admission observation
as policies around that dispatcher. Its stable input queues and prepared-output
staging buffers are additional, bounded revision boundaries; they are not present
in an ordinary direct graph. Revision activation, drain barriers and resource
snapshots remain outside Core. Sharing the delivery implementation does not make
the direct and update-enabled queue layouts identical.

An explicitly isolated route attempts immediate admission and reports each rejected
message or condition error without faulting healthy siblings:

```csharp
source.Output.LinkTo(target.Input,
    delivery: FlowDeliveryPolicy.IsolateRejection,
    onRejected: (message, error) => RecordRejected(message, error));
```

`LinkFrom` and `OtherwiseTo` accept the same policy and required handler. Reliable
delivery remains the default; `RejectWhenFull` still faults the graph on rejection.
The isolated route stays linked for later messages, so temporary saturation can
recover. Rejected work is not retried, cloned, acknowledged as processed, or sent to
fallback after failed admission. An isolated condition error skips that route and
is reported; normal fallback selection may still apply. A condition returning false
is not an error and does not implicitly enable discarding unmatched messages.

The handler receives the original envelope and exception (or an admission error
naming both ports). It runs synchronously, must return promptly, and owns any
asynchronous work it starts. Handler exceptions are contained; Core does not add a
reporting queue or retry failed reporting. Actual component lifetime failures still
fault the graph, regardless of route policy. Portable `Delivery` declarations and
update-host business routes use the same policy values. Direct document builders
require an explicit rejection handler for isolated routes; update hosting reports
through its rejection stream.

This development API adds an optional handler parameter to the three link methods
and exposes it on `FlowLink.OnRejected`. Existing source calls retain their default
behavior, but previously compiled callers must rebuild for the changed signatures.

Core provides address lookup, graph validation/lifetime and first-reply exchanges using unique correlations and expected reply ports. JSON, portable fluent definition, protocol implementations and hosting remain optional callers, not Core dependencies.

Processing components share an internal output/lifetime owner. `FlowNode` retains
its action-style `ProcessAsync`, explicit emissions, error conversion, extra outputs
and final-input flush hook. `FlowTransformNode` is a one-result alternative: override
`TransformAsync`, return a message, and let the native transform block order concurrent
results (`ensureOrdered` defaults to true). Expected failures are returned error
messages; unexpected exceptions fault this transform component and cancel active
peers before joining them. Both wrappers expose actual ports and wait for active
work before releasing owned resources. They do not own borrowed client dependencies.
The shared helper creates no scheduler, subscription API or component creator.

Both processing wrappers emit optional `flow.process` activities from the
`FluxFlow.Core` source. New messages capture W3C context in existing immutable
headers; forwarding preserves the same envelope and creation context. Host sampling
and export stay outside Core. See [processing trace context](../../../docs/processing-trace-context.md)
for exact duration, derivation, failure and coverage boundaries.

`FlowSource` has one finalization path for normal production, early stop and faults.
Stopping signals its token immediately; registered cancellation callbacks execute
asynchronously. `Completion` joins both the producer and those callbacks, then all
owned data outputs. Natural production end also signals the stopping token. Owned
resource cleanup runs afterwards, even when a callback fails; callback errors are
retained alongside an earlier production/output fault. This does not join detached
work started by user callbacks. A stopped or disposed source cannot restart.
Concurrent repeated disposal retains the existing first-caller behavior.

Running direct graphs and update-enabled candidates share one internal component-lifetime owner for sequential source startup, idempotent graceful stopping, first-failure propagation and completion observation. Controlled sources stop alongside entry components; a composed completion cannot hide its underlying node's fault or discard its additional work. Core still faults routing before components release reservations. Update hosting additionally owns stable-port drain barriers, revision transactions and resource snapshots. These policies are not interchangeable with ordinary producer-driven completion and do not add another component construction API to Core.

Graph failure stops business routing before component faults release held reservations. If a native source has already started graceful completion, a fault-only native discard link releases its unread output so that completion is not stranded. Discarded messages are failed work, not processed or acknowledged work. This also applies to borrowed components; borrowing changes disposal ownership, not graph lifetime coordination.

The existing ApplicationAddress and ApplicationAddressKind types now belong to Core under `FluxFlow.Core.Addressing`. Update the address namespace import and rebuild consumers. Absolute/local port strings, resource/system addresses, ordinal comparison and validation rules are unchanged. Direct graph and port naming use the same Core segment rules; no configuration or host dependency is needed to resolve an address.

`FlowRequestReply` is the bounded exchange service shared by direct applications and optional host adapters. It accepts a canonical request, an expected reply address and an awaitable sender. Registration precedes admission; a fresh correlation distinguishes concurrent requests even when they share a trace. The configured deadline covers both admission and waiting. Explicit infinite timeouts remain capacity-bounded and can be cancelled or stopped. Replies preserve the canonical envelope; typed host responses are consumer projections, not a second request runtime.

`FlowApplication.RequestAsync` needs no `RepliesFrom` declaration. Use endpoint
objects or exact application addresses, including ports in different workflows:

```csharp
var reply = await app.RequestAsync(
    "workflow1.node1.Input", "workflow2.node2.Output",
    FlowMessage.Create(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("hello", new JsonSerializerOptions(JsonSerializerDefaults.Web))))), TimeSpan.FromSeconds(5));
```

Bind the same request boundary once for repeated calls:

```csharp
var function1 = app.MakeFunction(
    "workflow1.node1.Input", "workflow2.node2.Output");
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var reply = await function1(message, cancellation.Token);
```

`FlowFunction` is a delegate over `FlowMessage`, with an optional cancellation
token. `MakeFunction` also accepts endpoint objects. Creation validates existing
ports and may precede startup; invocation requires a running application.
Each call uses `RequestAsync` with independent correlation. The caller controls
waiting through cancellation; scheduled cancellation reports OperationCanceledException.
Without a token there is no deadline: the call waits for a reply or application
termination. Pending-request capacity remains bounded.
There is no function registry or JSON declaration in this API.

The request call activates reply handling on the selected output before admission. It
does not add a business link or guarantee reachability; configured links still
determine execution. The first matching correlation at that output settles the
request. Ordinary matching links still receive the message; a matched reply
suppresses fallback, while unrelated/late messages can take fallback. Once used
for requests, that output accepts otherwise-unrouted messages for the remainder
of its lifetime, allowing late/duplicate replies to drain without faulting the
application. No per-request tombstone or second pending registry is added.

The former explicit reply policies have been removed from Core, definition
builders, JSON interpretation and Engine revisions. Remove old reply declarations;
requests, including Engine `SendAndReceiveAsync`, use only automatic handling.

If the exchange timer throws or declines deadline scheduling, its coordinator
faults remaining requests and stops accepting new ones. Replies and timeouts
already settled keep their original outcomes. Request failures cancel and join
pending send operations. Synchronous scheduling callers receive the error;
callback scheduling errors are contained and reported by the first timer disposal
attempt, even when all requests have already expired. Timer disposal still runs
and preserves both errors if cleanup also fails. Application stop and fault paths
continue graph shutdown after exchange cleanup fails. Disposal attempts remaining
owned cleanup, preserves earlier failures and does not dispose borrowed components.
Module-specific cleanup paths still require their remaining migration work.

This preview is not release-ready. Shared dispatch and minimal package execution
are verified, while the final capability audit, wider operational/telemetry and
performance qualification, provider prerequisites and migration handoff remain.
See the repository execution ledger for current evidence and limits.

The retained primitive documentation is in `Nodes/README.md`; exchange implementation documentation is in `Coordination/README.md`. Those implementation namespaces do not represent separately shipped packages in the new package train.

The canonical value converter is accessible to external generated JSON contexts. Scalar, array and object shapes are preserved; JSON `null` deserializes as `JsonFlowValue.Null`. This verifies canonical-value serialization, not whole-library trimming or ahead-of-time compilation compatibility.

FlowValue is an abstract immutable-value contract: Json is produced lazily by CreateJson and cached across concurrent readers. JsonFlowValue owns dynamic JSON. Components perform their own deserialization; the materializer interface/result/base classes are removed. HTTP/MQTT requests and FlowError can travel as typed FlowValue objects. Canonical error messages expose the error through the same Value slot, with IsError retaining failure semantics. See docs/20-flow-data-contracts.md for migration and serialization details.
