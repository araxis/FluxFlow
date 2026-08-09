# FluxFlow.Fluent.Hosting

Optional hosting bridge for `FluxFlow.Fluent`.

Register a fluent `FlowGraph` with `AddFlowGraph` and it runs as an
`IHostedService`: built and started when the host starts, drained on host stop,
and disposed on shutdown. The factory delegate receives the application
`IServiceProvider`, so its nodes can be resolved from DI.

## Usage

```csharp
using FluxFlow.Fluent;
using FluxFlow.Fluent.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services.AddFlowGraph(sp => Flow
    .From(new TickSource(sp.GetRequiredService<IClock>()))   // nodes resolved from DI
    .Then(new Worker())
    .To(new Sink())
    .Build());

await builder.Build().RunAsync();
```

Call `AddFlowGraph` more than once to host several flows in one application; each
runs as its own hosted service.

## Boundary

`FluxFlow.Fluent.Hosting` owns only the host lifecycle wiring: the
`AddFlowGraph` registration and a hosted service that builds the graph (once),
starts it, stops it on host stop, and disposes it on shutdown.

It does not own node construction, the DSL, or another runtime. The graph comes
from `FluxFlow.Fluent` (`Flow.From(...)...Build()`), already wraps the canonical
definition and `FluxFlowApplication`, and constructs nodes inside the factory
(optionally from DI). For composing nodes without Generic Host integration, use
`FluxFlow.Fluent` directly.
