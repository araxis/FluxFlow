# Node contracts in Core

Components construct and connect native Dataflow blocks. FlowNode is an optional
base containing emission helpers and forwarding to a supplied FlowLifetime. It
creates no blocks or lifetime coordinator and imposes no abstract processing method.

```csharp
public sealed class PositiveNumbers : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    public PositiveNumbers()
    {
        Output = new BroadcastBlock<FlowMessage>(static message => message,
            new DataflowBlockOptions { BoundedCapacity = 128 });
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(
            message => message.ProcessMessageAsync(this, Select, Output, Stopping),
            new ExecutionDataflowBlockOptions { BoundedCapacity = 128 });
        input.LinkTo(Output);
        Input = input;

        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private IEnumerable<FlowMessage> Select(FlowMessage message)
        => message.Value.Json.GetInt32() > 0 ? [message] : [];
}
```

Return [] to filter, [message] to forward unchanged, or messages derived with
message.WithValue(value). ProcessMessageAsync wraps an ordinary callback with tracing
and error conversion. A returned sequence is materialized before publication so an
enumeration failure produces one error instead of partially returned results.
Native TransformManyBlock controls capacity, parallelism and returned-result ordering.
Result count is not bounded by input/output capacity.

Synchronous transforms return IEnumerable<FlowMessage> directly; asynchronous transforms
return Task<IEnumerable<FlowMessage>>. Both use the same processing/error path. Ordinary
actions are supported alongside Func<FlowMessage, Task> for explicit publication.
Synchronous callbacks execute inline within the processing scope, without Task.Run or
Task.FromResult in component code. The outer helper remains awaitable because publishing
forwarded/generated errors may wait for output admission.

The helper accepts Func<FlowMessage, Task> for explicit publication with EmitAsync.
In that overload, forwarded and generated errors use the same immediate emission path.
Direct sends are not reordered by EnsureOrdered. Incoming errors bypass the callback
unless handlesErrors: true is supplied. Processing failures become FlowError values
with input lineage; cancellation and infrastructure failures retain fault semantics.
The helper constructs no blocks and changes no lifecycle declarations.

## Declaring lifetime

Every component explicitly constructs its blocks and a FlowLifetime. HasInput,
HasOutput, HasEvents and HasTerminalInput accept existing blocks and return those
same blocks. They do not create, name, address or link ports. Extra inputs and outputs
use the same declaration methods as the main blocks. Components can also implement
IFlowNode directly and use this coordinator without inheriting FlowNode.

Call Observe once, at the end of construction, after all declarations and component
fields are initialized. It seals declarations and begins watching completion. This
separate boundary prevents an already canceled/faulted block from closing a lifetime
while other blocks are still being declared. It does not start an independent producer.
Runtime calls before Observe, later declarations and repeated Observe calls reject.

The first HasInput is the main input: its native completion begins draining all
ordinary inputs. Complete closes ordinary input admission directly. After those inputs
drain, the optional flush callback runs, then data outputs finish, and terminal reply
inputs close. A fault cancels work and faults inputs/data outputs; Events drain normally.
Flush is skipped on fault. Native block completion does not acknowledge downstream
receipt; Application coordinates cross-component fan-in separately.

Batch supplies its final flush explicitly:

```csharp
Lifetime = new FlowLifetime(this, flush: OnInputCompletedAsync,
    cancellationToken: execution.CancellationToken);
```

For callbacks returning a finite flush sequence, Output.FlushMessagesAsync accepts either
a synchronous IEnumerable<FlowMessage> result or ValueTask<IEnumerable<FlowMessage>>.
It materializes the sequence before publishing and awaits output admission in both cases.
A component with owned resources overrides OnDisposeAsync or
passes a release callback directly to Lifetime.DisposeAsync. Release runs once after
the coordinator finishes. Borrowed dependencies remain with their owner.

## Sources and replies

A source constructs Output/Events and supplies produce: RunAsync to FlowLifetime.
It declares its blocks and calls Observe during construction, then implements
IFlowSource by forwarding StartAsync, StopProduction and ProductionCompletion to
Lifetime. It has no artificial processing input or separate source base class.

StopProduction cancels production while keeping terminal replies available for
already accepted downstream work. ProductionCompletion covers production and data
outputs. Full Completion also joins terminal inputs and their cancellation callbacks.
Complete closes those terminal inputs too. Sources cannot restart after stopping.
Source stop cancellation callbacks are asynchronous and joined before resource release.

Reply inputs use HasTerminalInput. ProcessControlAsync wraps the awaited control
callback with tracing and owner faulting, without constructing an ActionBlock. It accepts
both synchronous actions and asynchronous functions. Use
Lifetime.ControlStopping for control callbacks; for a source this token remains active
after production stops. Control labels are diagnostic names, not addresses. Gate's
Open/Close commands are ordinary inputs and use HasInput.

## Publication and events

Native BroadcastBlock admission does not guarantee delivery to every target. Full or
declining consumers can miss messages, and late subscribers can receive the latest
value. Consumers choose buffering or stronger delivery components. Published messages
cannot be rolled back after a later processing failure.

FlowEvent is a FlowValue and creates its message representation. Event projection
preserves Int64 values, including nested attributes. Invalid optional timestamps fall
back to envelope time (or current time for a bare event); nonnumeric measurements are
skipped. Projection does not mutate the envelope. Hosts provide logging and telemetry
exporters; Core exposes diagnostics, ActivitySource and Meter without an exporter dependency.

Message fanout shares immutable envelopes and payloads without cloning. Typed FlowValue
subclasses provide lazy JSON; JsonFlowValue snapshots JSON. Components deserialize only
when their operation requires another type.

See eng/application-consumer/Program.cs for explicit multi-output construction and
separate DI registration. eng/core-consumer/Program.cs uses Core without Application
or DI. Both package consumers run on .NET 8 and .NET 10.
