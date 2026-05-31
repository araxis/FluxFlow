# Scenarios

Namespace: `FluxFlow.Engine.Scenarios`

Scenarios let you write **deterministic, repeatable tests** against a running
`ApplicationRuntime` by describing what actions to take and what events to expect.

---

## Concepts

| Type | Role |
|------|------|
| `ScenarioDefinition` | Named list of ordered steps (stored in JSON under `"tests"`) |
| `ScenarioStepDefinition` | One step: type + configuration |
| `ScenarioRunner` | Executes a scenario against a live event stream |
| `IScenarioStepRunner` | Implements one step type |
| `ScenarioStepRunnerRegistry` | Maps step-type strings to runners |
| `ScenarioStepServices` | Immutable service bag passed to runners |
| `ScenarioEventJournal` | Ordered list of `FlowEvent` collected during the run |
| `ScenarioRunResult` | Final result: status + per-step results |

---

## Defining a scenario in JSON

```json
{
  "tests": {
    "publish-and-receive": {
      "steps": {
        "send": {
          "type": "mqtt.publish",
          "configuration": {
            "connection": "broker",
            "topic":      "sensors/temp",
            "payload":    "{ \"value\": 42 }",
            "qos":        1
          }
        },
        "receive": {
          "type": "expect.event",
          "configuration": {
            "type":           "mqtt.message.received",
            "topic":          "sensors/temp",
            "timeoutSeconds": 5
          }
        }
      }
    }
  }
}
```

Steps execute **in definition order**. If any step fails, the run stops.

---

## Built-in step runners

### `expect.event`

Waits for a `FlowEvent` in the journal that matches the configured criteria.

```json
{
  "type": "expect.event",
  "configuration": {
    "type":           "mqtt.message.received",
    "topic":          "sensors/temp",
    "timeoutSeconds": 10,
    "status":         "success",
    "subject":        "optional-subject"
  }
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `type` | Yes | `FlowEvent.Type` to match |
| `topic` | No | `FlowEvent.Topic` exact match |
| `status` | No | `FlowEvent.Status` match |
| `subject` | No | `FlowEvent.Subject` match |
| `timeoutSeconds` | No | Seconds to wait before failing (default: 30) |

`ExpectEventScenarioStepRunner` is registered by default via
`FlowApplicationHost.CreateDefaultScenarioStepRunnerRegistry()`.

---

## ScenarioRunner

```csharp
public sealed class ScenarioRunner(ScenarioStepRunnerRegistry registry)
{
    // Without services (uses ScenarioStepServices.Empty)
    public Task<ScenarioRunResult> RunAsync(
        string name, ScenarioDefinition scenario,
        ISourceBlock<FlowEvent> events,
        CancellationToken ct = default);

    // With services (pass protocol-specific helpers)
    public Task<ScenarioRunResult> RunAsync(
        string name, ScenarioDefinition scenario,
        ISourceBlock<FlowEvent> events,
        ScenarioStepServices services,
        CancellationToken ct = default);
}
```

Typically you use `FlowApplicationHost.RunScenarioAsync` which wires everything
for you. Call `ScenarioRunner` directly for advanced use or unit tests.

---

## ScenarioRunResult

```csharp
public sealed record ScenarioRunResult
{
    public string                          Name        { get; init; }
    public ScenarioRunStatus               Status      { get; init; }
    public DateTimeOffset                  StartedAt   { get; init; }
    public DateTimeOffset                  FinishedAt  { get; init; }
    public IReadOnlyList<ScenarioStepResult> Steps    { get; init; }
}
```

```csharp
// ScenarioRunStatus
public enum ScenarioRunStatus { Passed, Failed, Canceled }

// ScenarioStepRunStatus
public enum ScenarioStepRunStatus { Passed, Failed, Canceled, Skipped }
```

---

## ScenarioStepResult

```csharp
public sealed record ScenarioStepResult
{
    public string              Name                { get; init; }
    public string              Type                { get; init; }
    public ScenarioStepRunStatus Status            { get; init; }
    public DateTimeOffset      StartedAt           { get; init; }
    public DateTimeOffset      FinishedAt          { get; init; }
    public string?             Message             { get; init; }
    public int?                MatchedEventIndex   { get; init; }
    public int                 NextEventOffset     { get; init; }
    public bool                IsSuccess           => Status == ScenarioStepRunStatus.Passed;
}
```

---

## ScenarioStepServices

An immutable, type-keyed service container. Passed to every `IScenarioStepRunner`.

```csharp
public sealed class ScenarioStepServices
{
    public static ScenarioStepServices Empty { get; }

    // Returns a new instance with the service added (immutable)
    public ScenarioStepServices Add<TService>(TService service) where TService : class;

    public bool TryGet<TService>(out TService service) where TService : class;
    public TService GetRequired<TService>() where TService : class;
}
```

Protocol-specific runners use services to access shared state
(e.g. an MQTT client factory) without coupling to the engine core.

---

## Writing a custom step runner

```csharp
public sealed class HttpPostScenarioStepRunner : IScenarioStepRunner
{
    public string Type => "http.post";

    public async Task<ScenarioStepResult> RunAsync(
        ScenarioStepRunContext context,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var cfg = context.Step.Configuration;
        var url = cfg["url"].GetString()!;

        // Retrieve a service registered by the host
        if (!context.Services.TryGet<IHttpClientFactory>(out var factory))
        {
            return Fail(context, started, "IHttpClientFactory not registered");
        }

        try
        {
            using var http   = factory.CreateClient();
            var body         = new StringContent(cfg["body"].GetString()!);
            var response     = await http.PostAsync(url, body, cancellationToken);
            response.EnsureSuccessStatusCode();
            return Pass(context, started);
        }
        catch (Exception ex)
        {
            return Fail(context, started, ex.Message);
        }
    }

    private static ScenarioStepResult Pass(ScenarioStepRunContext ctx, DateTimeOffset started)
        => new()
        {
            Name        = ctx.StepName,
            Type        = Type,
            Status      = ScenarioStepRunStatus.Passed,
            StartedAt   = started,
            FinishedAt  = DateTimeOffset.UtcNow,
            NextEventOffset = ctx.EventOffset
        };

    private static ScenarioStepResult Fail(ScenarioStepRunContext ctx, DateTimeOffset started, string msg)
        => new()
        {
            Name        = ctx.StepName,
            Type        = Type,
            Status      = ScenarioStepRunStatus.Failed,
            StartedAt   = started,
            FinishedAt  = DateTimeOffset.UtcNow,
            Message     = msg,
            NextEventOffset = ctx.EventOffset
        };
}
```

Register it:

```csharp
var stepRegistry = FlowApplicationHost.CreateDefaultScenarioStepRunnerRegistry()
    .Register(new HttpPostScenarioStepRunner());

var scenarioRunner = new ScenarioRunner(stepRegistry);

var host = new FlowApplicationHost(
    configuration: null,
    runtimeBuilder: new ApplicationRuntimeBuilder(nodeRegistry),
    scenarioRunner: scenarioRunner,
    applicationDefinition: definition);
```

---

## ScenarioStepRunContext

Passed to every step runner.

```csharp
public sealed record ScenarioStepRunContext
{
    public string                   ScenarioName         { get; init; }
    public string                   StepName             { get; init; }
    public ScenarioStepDefinition   Step                 { get; init; }
    public ScenarioEventJournal     Events               { get; init; }
    public ScenarioRunLifetime      Lifetime             { get; init; }
    public ScenarioStepServices     Services             { get; init; }
    public int                      EventOffset          { get; init; }
    public IReadOnlySet<int>        ConsumedEventIndexes { get; init; }
}
```

- `Events` — append-only journal of `FlowEvent` items collected since the scenario started.
- `EventOffset` — the event index where this step should begin searching.
- `ConsumedEventIndexes` — events already matched by previous steps (skip these).
- `Lifetime` — owns disposable resources created by setup steps (e.g. MQTT subscriptions).

---

## Running a scenario via FlowApplicationHost

```csharp
// Start the runtime first
await host.StartAsync();

// Run by scenario name (defined under "tests" in the definition)
var result = await host.RunScenarioAsync("publish-and-receive");

Console.WriteLine($"Status:   {result.Status}");
Console.WriteLine($"Duration: {(result.FinishedAt - result.StartedAt).TotalSeconds:F2}s");

foreach (var step in result.Steps)
    Console.WriteLine($"  [{step.Status}] {step.Name}: {step.Message}");
```

---

## Unit-testing with ScenarioRunner

You can drive a scenario against any `ISourceBlock<FlowEvent>` — no full runtime needed.

```csharp
[Fact]
public async Task ExpectEvent_MatchesByType()
{
    var events = new BufferBlock<FlowEvent>();
    var runner = new ScenarioRunner(
        FlowApplicationHost.CreateDefaultScenarioStepRunnerRegistry());

    var scenario = new ScenarioDefinition
    {
        Steps = new Dictionary<string, ScenarioStepDefinition>
        {
            ["expect"] = new()
            {
                Type = ScenarioStepTypes.ExpectEvent,
                Configuration = new Dictionary<string, JsonElement>
                {
                    ["type"]           = JsonDocument.Parse("\"test.done\"").RootElement,
                    ["timeoutSeconds"] = JsonDocument.Parse("2").RootElement
                }
            }
        }
    };

    // Post a matching event before running
    events.Post(new FlowEvent
    {
        Timestamp = DateTimeOffset.UtcNow,
        Type      = "test.done",
        Source    = "test"
    });
    events.Complete();

    var result = await runner.RunAsync("test", scenario, events);

    result.Status.ShouldBe(ScenarioRunStatus.Passed);
}
```
