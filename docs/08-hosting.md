# Hosting

Namespace: `FluxFlow.Engine` (root namespace)

---

## FlowApplicationHost

Owns the full lifecycle of one `ApplicationRuntime`:
build → start → stop → dispose.

```csharp
public sealed class FlowApplicationHost : IAsyncDisposable, IDisposable
```

### Creating a host

Two static factory methods are preferred over calling the constructor directly.

#### From a definition object

```csharp
var host = FlowApplicationHost.Create(
    definition,   // ApplicationDefinition
    registry);    // RuntimeNodeFactoryRegistry
```

#### From IConfiguration

```csharp
// Reads from section "FluxMq:FlowApplication" by default
var host = FlowApplicationHost.Create(
    configuration,  // IConfiguration
    registry);      // RuntimeNodeFactoryRegistry
```

To use a custom section name, call the constructor directly:

```csharp
var loader = new FlowApplicationConfigurationLoader();
var host   = new FlowApplicationHost(
    configuration,
    new ApplicationRuntimeBuilder(registry),
    configurationLoader: loader,
    sectionName: "MyApp:Graph");
```

### Constructor parameters

```csharp
public FlowApplicationHost(
    IConfiguration?              configuration        = null,
    ApplicationRuntimeBuilder    runtimeBuilder,
    FlowApplicationConfigurationLoader? configurationLoader = null,
    string                       sectionName          = "FluxMq:FlowApplication",
    ScenarioRunner?              scenarioRunner       = null,
    ApplicationDefinition?       applicationDefinition = null)
```

| Parameter | Description |
|-----------|-------------|
| `configuration` | Optional `IConfiguration`; required when no `applicationDefinition` is provided |
| `runtimeBuilder` | Required; created from your `RuntimeNodeFactoryRegistry` |
| `configurationLoader` | Optional; defaults to `new FlowApplicationConfigurationLoader()` |
| `sectionName` | Configuration section to load; defaults to `"FluxMq:FlowApplication"` |
| `scenarioRunner` | Optional; defaults to a runner with the built-in `expect.event` step |
| `applicationDefinition` | Optional pre-built definition; skips config loading when provided |

---

### Lifecycle methods

#### Build

```csharp
FlowApplicationHostBuildResult result = host.Build();
```

Builds the graph without starting it. Returns a structured result.
Use this when you want to inspect the graph before starting.

#### StartAsync

```csharp
FlowApplicationHostBuildResult result = await host.StartAsync(ct);
```

Calls `Build()` then `StartAsync()` on the runtime. Returns the build result.
If build fails, `result.IsSuccess` is `false` and `result.Errors` has details.

#### StartBuiltAsync

```csharp
FlowApplicationHostBuildResult result = await host.StartBuiltAsync(ct);
```

Starts a previously built (but not started) runtime.
Calls `Build()` first if the host has not been built yet.

#### StopAsync

```csharp
await host.StopAsync(ct);
```

Calls `Complete()` on the runtime and waits for `Completion`.

#### RunScenarioAsync

```csharp
ScenarioRunResult result = await host.RunScenarioAsync(
    "scenario-name",
    servicesFactory: runtime => ScenarioStepServices.Empty
        .Add<IMyService>(new MyService(runtime)),
    ct);
```

Runs a named test scenario from the definition's `"tests"` section.
The runtime must already be running; throws `InvalidOperationException` otherwise.

---

### State and properties

```csharp
FlowApplicationHostState     State          { get; }
ApplicationDefinition?       Definition     { get; }
ApplicationRuntime?          Runtime        { get; }
FlowApplicationHostBuildResult? LastBuildResult { get; }
Exception?                   LastException  { get; }
```

```csharp
// FlowApplicationHostState
public enum FlowApplicationHostState { Empty, Built, Running, Stopped, Faulted }
```

---

### Error handling

```csharp
var result = await host.StartAsync();

if (!result.IsSuccess)
{
    foreach (var error in result.Errors)
    {
        Console.Error.WriteLine(
            $"[{error.Code}] {error.WorkflowName}.{error.NodeName}: {error.Message}");
    }
    return;
}
```

```csharp
public sealed record FlowApplicationHostBuildError(
    FlowApplicationHostBuildErrorCode Code,
    string                            Message,
    string?                           WorkflowName = null,
    string?                           NodeName     = null)
```

```csharp
public enum FlowApplicationHostBuildErrorCode
{
    InvalidConfiguration,
    ValidationFailed,
    BuildFailed,
    StartFailed
}
```

---

### Scenario default runner

```csharp
// Returns a ScenarioStepRunnerRegistry with only ExpectEventScenarioStepRunner registered.
// Extend it by calling .Register() with your own step runners.
var registry = FlowApplicationHost.CreateDefaultScenarioStepRunnerRegistry();
registry.Register(new MyCustomStepRunner());

var runner = new ScenarioRunner(registry);

var host = new FlowApplicationHost(
    configuration, new ApplicationRuntimeBuilder(nodeRegistry),
    scenarioRunner: runner);
```

---

## FlowApplicationConfigurationLoader

Loads an `ApplicationDefinition` from any `IConfiguration` source.

```csharp
public sealed class FlowApplicationConfigurationLoader
{
    public const string DefaultSectionName = "FluxMq:FlowApplication";

    public ApplicationDefinition Load(
        IConfiguration configuration,
        string sectionName = DefaultSectionName);
}
```

The loader recursively walks the configuration section and reconstructs
the JSON structure expected by `ApplicationDefinitionJson` — handling
`IConfiguration`'s key-path notation, arrays (numeric keys), booleans, numbers, and strings.

### Custom section name

```csharp
var loader     = new FlowApplicationConfigurationLoader();
var definition = loader.Load(config, "FluxFlow:Application");
```

### Exceptions

If the section is missing or cannot be deserialized, `FlowApplicationConfigurationException` is thrown:

```csharp
try
{
    var definition = loader.Load(config, "MyApp:Flow");
}
catch (FlowApplicationConfigurationException ex)
{
    Console.Error.WriteLine($"Configuration error: {ex.Message}");
}
```

---

## Typical host pattern

```csharp
// Composition root -----------------------------------------------------------
var registry = new RuntimeNodeFactoryRegistry();
registry.Register(new NodeType("demo.source"),  DemoSource.Create);
registry.Register(new NodeType("demo.printer"), DemoPrinter.Create);

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

await using var host = FlowApplicationHost.Create(configuration, registry);

// Start ----------------------------------------------------------------------
var result = await host.StartAsync();
if (!result.IsSuccess) { /* log errors */ return; }

// Observe --------------------------------------------------------------------
host.Runtime!.StateChanges.LinkTo(
    new ActionBlock<ApplicationStateChanged>(s =>
        Console.WriteLine($"[{s.Current}]")));

// Wait for natural completion or stop on Ctrl+C ------------------------------
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    await host.Runtime.Completion.WaitAsync(cts.Token);
}
catch (OperationCanceledException)
{
    await host.StopAsync();
}
```

---

## Using with Microsoft.Extensions.Hosting

```csharp
// Program.cs (Worker Service)
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(_ =>
{
    var registry = new RuntimeNodeFactoryRegistry();
    registry.Register(new NodeType("demo.source"), DemoSource.Create);
    return registry;
});

builder.Services.AddSingleton(sp =>
    FlowApplicationHost.Create(
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<RuntimeNodeFactoryRegistry>()));

builder.Services.AddHostedService<FlowEngineWorker>();
```

```csharp
// FlowEngineWorker.cs
public sealed class FlowEngineWorker(FlowApplicationHost host) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var result = await host.StartAsync(stoppingToken);
        if (!result.IsSuccess) return;

        await host.Runtime!.Completion.WaitAsync(stoppingToken)
            .ContinueWith(_ => { }, TaskScheduler.Default);
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await host.StopAsync(ct);
        await base.StopAsync(ct);
    }
}
```
