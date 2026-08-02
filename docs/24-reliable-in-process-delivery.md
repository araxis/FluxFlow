# Reliable In-Process Delivery

FluxFlow normal data outputs use bounded in-process queues. An accepted data
message is offered once to every subscriber that was active when the output
accepted the message. A slow subscriber eventually fills the configured queue
and backpressures the producer instead of silently replacing the message.

This contract applies to normal workflow data. Events, observations, telemetry,
and diagnostics remain best-effort so an observer cannot stall workflow work.

## Acceptance and delivery

An asynchronous output send succeeds when the bounded output queue accepts
ownership. Acceptance does not mean that downstream business processing has
finished. The guarantee ends when a downstream input accepts the message.

The output captures its current subscriber snapshot at acceptance:

- subscribers linked later receive future messages only;
- explicitly disposing a link releases that subscriber from pending delivery;
- an unexpected target rejection faults the output instead of being hidden;
- messages accepted with no subscribers are discarded as live-stream data and
  are not retained for replay.

Cancellation before acceptance leaves the message with the caller. Once a
message is accepted, caller cancellation does not retract it. Normal completion
stops new acceptance, drains accepted messages, finishes in-flight delivery,
and then propagates completion to links that requested it.

Ordering is the output queue's acceptance order. Concurrent node processing can
naturally change production order when its configured parallelism is greater
than one.

## Capacity configuration

FluxFlow keeps three capacity scopes explicit:

| Scope | Configuration | Meaning |
| --- | --- | --- |
| Component instance | Component DSL `BoundedCapacity` or a family-specific name such as MQTT `MaximumPendingMessages` | Capacity owned by that component's bounded work and reliable normal-data output. |
| Custom standalone node | `FlowNodeOptions.InputCapacity`, `FlowNodeOptions.OutputCapacity`, or `FlowSourceOptions.OutputCapacity` | Capacity owned by the constructed node instance. |
| Engine stable ports | `FluxFlowApplicationOptions.InputCapacity` and `OutputCapacity` | Capacity of the stable addressable application-port layer across revisions. |

The component C# DSL writes the same canonical property used by JSON binding:

```csharp
var application = new ApplicationDefinitionBuilder();
application.AddWorkflow("Orders", out var workflow);

workflow
    .AddGeneratedSource(
        "Source",
        options =>
        {
            options.SetItems(new[] { "alpha", "beta" });
            options.BoundedCapacity = 256;
        },
        out var source)
    .AddDebounce(
        "Debounce",
        options =>
        {
            options.QuietPeriod = TimeSpan.FromMilliseconds(100);
            options.BoundedCapacity = 64;
        },
        out var debounce)
    .Connect(source.Output, debounce.Input);
```

This produces the canonical component property `boundedCapacity`; loading the
same property from JSON follows the identical binding path.

Engine stable-port capacity is configured separately during registration:

```csharp
services.AddFluxFlow(application.Build(), options =>
{
    options.InputCapacity = 256;
    options.OutputCapacity = 512;
});
```

The Engine values do not override the component values. The low-level
`Flow.From(...).Then(...)` API likewise does not mutate capacities: callers
construct each source and node with its own options before adding it to a graph.

## Reliability boundary

This is an in-process delivery contract. It does not provide persistence,
replay, crash recovery, retries, dead-letter queues, distributed execution, or
exactly-once processing. A process or machine failure can still lose accepted
in-memory messages.

Hosts that need persistence before an external input reaches this boundary can
opt into `FluxFlow.Engine.DurableInput` and provide an `IDurableInputStore`.
Local hosts can use the separate `FluxFlow.Engine.DurableInput.SqlFile` provider;
shared or distributed deployments can supply another store implementation.
Durable acceptance then means the store accepted the entry; delivery means the
current stable Engine input returned `Accepted` in the default
`EngineAccepted` mode. The dispatcher preserves the original `MessageId`, but a
crash between Engine acceptance and store settlement can redeliver it. Hosts
may instead explicitly select `WorkflowCompleted`, provide one exact
lease-scoped completion source, and use a store that supports exact lease
renewal. That mode dispatches one durable entry at a time and settles only the
explicit completion result. It remains at-least-once: a crash after workflow
side effects but before settlement can still redeliver the entry. No internal
queue, link, output, revision, checkpoint, or business transaction is
persisted. See [Optional Durable Inputs](25-durable-inputs.md) and
[Durable Input Workflow-Completion Acknowledgement](33-durable-input-workflow-completion.md).

Providers may separately implement `IDurableInputDeadLetterStore` for bounded
inspection and explicit replay. That operation re-enters the same at-least-once
delivery protocol; it does not make in-process links durable or provide
exactly-once side effects.

Hosts can independently persist selected application outputs through
`FluxFlow.Engine.DurableOutput`, then opt into its serial leased dispatcher for
at-least-once delivery through one host-owned handler. Capture happens before
normal live output dispatch; external delivery can still repeat when a process
fails after the side effect but before completion commits. Long-running
handlers renew their exact current unexpired lease through a simple configured
heartbeat; short handlers perform no renewal call. Unlimited retry is
the default; a positive attempt limit can move final failures to a durable
dead-letter state for bounded inspection and explicit generation-protected
replay. These operations do not make in-process links durable. See
[Optional Durable Output Capture](27-durable-output-capture.md) and
[Optional Durable Output Delivery](29-durable-output-delivery.md), plus
[Durable Output Dead-Letter Operations](30-durable-output-dead-letter-operations.md)
and [Durable Output Lease Renewal](37-durable-output-lease-renewal.md).

Backend storage settings, Engine stable-port capacity, and component-owned
input/output capacities remain separate configuration concerns. The canonical
workflow JSON does not expose a delivery-mode switch: normal data is reliable,
while diagnostic streams are explicitly best-effort.

Engine stable ports retain their established per-link isolation policy. A full
or failed stable-port target produces an observable rejection/diagnostic while
healthy sibling routes continue; it does not turn the component-level output
contract into a global application-wide head-of-line dependency.
