# Component package template

A copyable standalone component built on `FluxFlow.Core`. Construct it directly;
no host, component creator, registration callback or service container is required.

The existing template contains a small transform, immutable input/output/options
records, domain error codes and diagnostics. These records belong to the component;
its physical ports carry the canonical `FlowMessage` envelope with an owned `FlowValue`.

`TemplateEnrichNode : FlowNode` does three things:

1. Materializes its local `TemplateInput` using the existing JSON materializer.
2. Validates and enriches that input using explicit options and a clock.
3. Emits `FlowValue.From(output)`, retaining message lineage and headers.

The base owns bounded processing, reliable output and lifetime. Malformed input
produces a domain error and does not stop the next input. An incoming error is
forwarded as the original envelope.

## Direct composition

```csharp
await using var application = new FlowApplication();
var enrich = application.AddWorkflow("main").Add("enrich",
    new TemplateEnrichNode(new TemplateEnrichOptions { Prefix = "demo" }));

application.RepliesFrom(enrich.Output);
await application.StartAsync();

var response = await application.RequestAsync(enrich.Input, enrich.Output,
    FlowMessage.Create(FlowValue.From(new TemplateInput { Id = "one", Value = "order" })),
    TimeSpan.FromSeconds(5));
```

Check `response.IsError` before projecting its successful value with
`response.Value.Deserialize<TemplateOutput>()`. This is a consumer projection,
not another transport or processing queue.

Outside an application, the same node can be constructed with `new`, its
`Input` accepts canonical messages, and its actual `Output` can be linked to a
canonical consumer block. Graph ownership and naming belong to the application;
business dependencies belong to the node constructor.

Portable document bindings and design metadata are optional integration layers.
They are not required to implement or instantiate this component. This template
does not add a new template-adapter capability to the framework.

## Verification

```sh
dotnet test tests/Unit/Samples/FluxFlow.ComponentPackageTemplate.Tests/FluxFlow.ComponentPackageTemplate.Tests.csproj
```

Tests cover enrichment, validation recovery, diagnostics, constructor validation,
malformed JSON shapes, exact upstream-error forwarding and direct application composition.
