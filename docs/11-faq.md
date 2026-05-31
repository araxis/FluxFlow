# FAQ

---

### Why does `ApplicationRuntimeBuilder.Build` fail with `EmptyWorkflow`?

Every workflow must have at least one node. An empty workflow has no entry nodes
and the completion chain cannot be established. Add at least one node or remove
the workflow.

---

### Why is my output port connected but messages are not arriving?

Check:
1. **Port name mismatch** — the JSON link references `"source.Output"` but your factory
   declared `new PortName("output")` (case-sensitive). Fix the casing.
2. **Node never calls `StartAsync`** — the runtime calls `StartAsync` for you, but only
   after `ApplicationRuntime.StartAsync()` is called. Ensure you are awaiting `StartAsync`.
3. **Messages posted before `StartAsync`** — the `OutputPort<T>` wraps in a
   `BroadcastBlock<T>`; messages posted before consumers are linked may be dropped.
   Post messages in `StartAsync` or after startup.

---

### Why are some messages lost when multiple downstream nodes consume the same output?

`OutputPort<T>` uses `BroadcastBlock<T>` with the default clone function `value => value`.
`BroadcastBlock` drops messages if a consumer's input is full
(`BoundedCapacity` reached). Solutions:

- Increase `BoundedCapacity` on downstream `ActionBlock` / `TransformBlock`.
- Use `DataflowBlockOptions { BoundedCapacity = DataflowBlockOptions.Unbounded }` for
  low-volume pipelines.
- Ensure downstream processing is fast enough not to fall behind.

---

### How do I access configuration values from a node factory?

```csharp
public static RuntimeNode Create(RuntimeNodeFactoryContext ctx)
{
    var cfg = ctx.Definition.Configuration;

    // Scalar
    var host = cfg["host"].GetString()!;

    // Optional with default
    var port = cfg.TryGetValue("port", out var p) ? p.GetInt32() : 1883;

    // Array
    var topics = cfg["topics"].EnumerateArray().Select(e => e.GetString()!).ToList();
}
```

`ctx.Definition.Configuration` is `Dictionary<string, JsonElement>`.

---

### How do I share state between node instances?

Use a **resource node** (declared under `"resources"`) that holds the shared state
and expose it as a typed property. Workflow factories access it via
`ctx.GetResource(new NodeName("resourceName"))`.

See [Pattern 4 in Extending](09-extending.md).

---

### How do I pass a service (e.g. ILogger) into a node factory?

Close over it in the factory delegate:

```csharp
ILogger<MyNode> logger = ...; // from your DI container

registry.Register(
    new NodeType("my.node"),
    ctx => MyNode.Create(ctx, logger));
```

---

### Why does `Completion` never complete?

Usually means `Complete()` was never called on an entry node.
`ApplicationRuntime.Complete()` only signals the entry nodes (nodes with no incoming links).
If your entry node's `StartAsync` is supposed to drain and complete automatically
(file source, sequence source), ensure it calls `_buffer.Complete()` and
`_done.SetResult()` at the end.

---

### How do I handle `CancellationToken` in `StartAsync`?

```csharp
public async Task StartAsync(CancellationToken ct = default)
{
    try
    {
        while (!ct.IsCancellationRequested)
        {
            var msg = await _channel.ReadAsync(ct);
            await _output.SendAsync(msg, ct);
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // Expected — cancellation requested
    }
    finally
    {
        _output.Complete();
        _done.SetResult();
    }
}
```

---

### How do I change the configuration section name?

The default is `"FluxMq:FlowApplication"`. This name is inherited from FluxMQ;
change it for your application:

```csharp
var host = new FlowApplicationHost(
    configuration,
    new ApplicationRuntimeBuilder(registry),
    sectionName: "MyApp:FlowGraph");
```

Or load manually:

```csharp
var loader = new FlowApplicationConfigurationLoader();
var definition = loader.Load(config, "MyApp:FlowGraph");
var host = FlowApplicationHost.Create(definition, registry);
```

---

### How do I run a scenario from code (not from FlowApplicationHost)?

```csharp
var runner = new ScenarioRunner(
    FlowApplicationHost.CreateDefaultScenarioStepRunnerRegistry());

var result = await runner.RunAsync(
    "my-scenario",
    definition.Tests["my-scenario"],
    runtime.Events,   // ISourceBlock<FlowEvent>
    services,
    ct);
```

---

### Can I use FluxFlow.Engine in an ASP.NET Core app?

Yes. The library has no MAUI or desktop dependencies.
See [the Worker Service example in Hosting](08-hosting.md#using-with-microsoftextensionshosting).

---

### Can I register the same NodeType twice?

No — `RuntimeNodeFactoryRegistry.Register` throws `InvalidOperationException` if the
type is already registered. Build your registry once at startup.

---

### Is FluxFlow.Engine thread-safe?

- `RuntimeNodeFactoryRegistry` is **not thread-safe for writes**. Register all factories
  before creating a `ApplicationRuntimeBuilder`.
- `ApplicationRuntime` and `Workflow` are thread-safe for `StartAsync`, `Complete`,
  `Fault`, and `StateChanges` subscriptions. The state machine is lock-guarded.
- `ScenarioStepServices` is immutable and safe to share.
- `FlowMapContext` is immutable and safe to share.

---

### Does the engine support hot-reload (changing a graph without stopping it)?

Not currently. `ApplicationRuntimeBuilder.Build` is a cold build. Hot-reload requires
patching the Dataflow graph at runtime (unlinking, creating new nodes, re-linking).
That feature is in the FluxMQ roadmap but has not been extracted to FluxFlow.Engine yet.

---

### Where does FluxMQ end and FluxFlow.Engine begin?

FluxFlow.Engine contains:
- `IFlowNode`, `FlowError`, `FlowEvent`, `FlowNodeId`
- `ApplicationDefinition` and the full JSON definition model
- `ApplicationRuntimeBuilder`, `ApplicationRuntime`, `Workflow`
- `RuntimeNodeFactoryRegistry`, `InputPort<T>`, `OutputPort<T>`
- `IFlowExpressionEngine`, `DynamicExpressoFlowExpressionEngine`, `JsonataFlowExpressionEngine`
- `ScenarioRunner`, `IScenarioStepRunner`, `ScenarioStepServices`
- `FlowApplicationHost`, `FlowApplicationConfigurationLoader`

FluxMQ adds on top:
- MQTT components (`MqttConnectionComponent`, `MqttTriggerComponent`, etc.)
- LiteDB storage and session management
- MQTT scenario step runners
- Blazor visual flow designer
- MAUI desktop shell
