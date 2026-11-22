# FluxFlow.Components.Routing

Optional routing components, depending only on FluxFlow.Core. Core does not depend
on this module. Targets .NET 8 and .NET 10.

Router accepts RouterOptions with a Routes list of Predicate<FlowMessage>, optional
native Execution options and OutputCapacity. The list is copied at construction.
Outputs[i] corresponds to Routes[i]; assign names outside the component. Every
matching route receives the exact input message. Unmatched receives a normal message
only when no route matches. Inherited Output receives errors, including existing error
messages that bypass predicates unchanged. False predicates are not processing errors.
All predicates are evaluated before sending so predicate failure produces one error
without partial routing. Publishing is not transactional: a side-output failure after
other publications cannot undo those publications. Sends await local broadcast
admission only; slow or declining consumers can miss messages. Execution defaults to
serial processing; parallel side emissions are not ordered by EnsureOrdered.

```csharp
var router = new Router(new RouterOptions
{
    Routes = [message => message.Value.Json.GetInt32() > 0,
              message => message.Value.Json.GetInt32() % 2 == 0]
});
// router.Outputs[0], router.Outputs[1], router.Unmatched, router.Output (errors)
```

Predicates receive the original message, including headers, identity and timestamp.
Setup can compile a condition once and call it with FlowMapContext.ForMessage(message),
the same context used by Mapper and conditional links. Router has no expression-engine
dependency. Existing value-only callbacks must now read message.Value.

```csharp
var gate = new Gate(); // closed by default
// Gate implements behavior only. Its block properties can be linked directly.
```

Gate exposes Input, Open, Close, Output and the inherited Events stream.
It has no application registration or string port names.
Open/Close are ordinary native Dataflow inputs; their payload is ignored. Input
forwards the exact message while open and discards it while closed, including error
messages. No replay queue is kept for dropped messages. Constructor initiallyOpen
can change the starting state. IsOpen reports the currently processed state.

SendAsync confirms admission, not execution. Different input blocks have no shared
ordering: await observation of IsOpen when sequential command/data coordination is
required. Simultaneous Open/Close commands are resolved by execution order. Gate
checks state when processing each Input message; an already accepted output is not
retracted by Close. Gate applies native broadcast capacity and execution options
when constructing its blocks. FlowLifetime closes and drains the declared inputs
and outputs; FlowNode provides emission helpers and lifecycle forwarding.

Application registration is outside Gate. FluxFlow.Configuration supplies AddGate
and AddRouter to register factories, native ports and configured links. Standalone
callers can connect the blocks directly. The component package has no configuration
or application dependency.
