# FluxFlow.Components.Timers

Standalone timer sources and operators over canonical `FlowMessage` ports.

| Node | Contract |
|------|----------|
| `TimerIntervalNode` | source of `FlowValue` containing `TimerIntervalTick` |
| `TimerScheduleNode` | source of `FlowValue` containing `TimerScheduleTick` |
| `TimerDelayNode` | forward each message after its delay |
| `TimerThrottleNode` | forward messages at the configured interval |
| `TimerDebounceNode` | forward the latest message after its quiet period |

All five nodes implement `IFlowComponent` and expose their real `Ports`. Sources
have Output/Events only; operators inherit Input/Output/Events and lifecycle from
`FlowNode`. Construct an instance and add it directly:

```csharp
await using var app = new FlowApplication();
var workflow = app.AddWorkflow("main");
var ticks = workflow.Add("ticks", new TimerIntervalNode(
    new TimerIntervalSettings { Interval = TimeSpan.FromSeconds(1) }));
var delay = workflow.Add("delay", new TimerDelayNode(
    new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(100) }));
ticks.Output.LinkTo(delay.Input);
// Link delay.Output to the application's consumer before starting.
```

The generic operator classes are removed in the development major. Use
`FlowMessage.Create(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))` for structured or binary data. Timing
operators forward the exact original message: no clone, new message ID or new
causation edge. Incoming errors bypass timing and retain the same identity.
An operation failure still produces a derived error and a diagnostic event.

Admission and output are bounded. Operator input capacity includes the currently
executing delegate; delay and debounce also own bounded scheduling/emission queues
for their timing semantics. Completion drains accepted work; debounce flushes its
latest pending value. Fault/disposal cancels pending waits and disposal releases
owned timers and joins scheduling work. Events remain best-effort observations.

Nodes use an optional caller-owned `TimeProvider`, expose Events, and do not
require Engine or Composition.

## Composition

Install `FluxFlow.Components.Timers.Composition` for optional runtime registration and definition. This runtime package remains free of Composition, Designer, and Engine dependencies.
