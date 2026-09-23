# Hosting And Observability

FluxFlow.Core provides ordinary hosting through `AddFluxFlow()` in
`FluxFlow.Core.Application`. Register configuration with
`await services.AddFluxFlowApplicationAsync(configuration)` and the component setup
extensions before building the host provider. Host startup composes FlowApplication,
connects routes and starts sources. Shutdown stops and drains processing before the
host provider disposes nodes and resources.

```csharp
services.AddFluxFlow();
```

For manual lifetime control, resolve with `await provider.BuildApplicationAsync()`,
call `StartAsync`, then stop/dispose the application before disposing the provider.
Build returns the same registered singleton on repeated calls. Configuration changes
require rebuilding/restarting the host; this package has no hot reload or revision API.

## Ownership And Cancellation

The host owns one native DI provider. Resource registration adds factories before
provider construction. Components own their native block cleanup; DI owns components.
Cancellation can stop waiting or construction between factories. It is cooperative,
not a forced interruption of a constructor. Failed composition stops/drains partial
construction; the host must still dispose its provider.

## Diagnostics

Component Events carry address-stamped FlowMessage observations. Processing errors use
FlowError and preserve original exceptions through diagnostics. Application Completion
reports fatal graph/lifecycle failures. Automatic error collection and OpenTelemetry/SSE
integration remain separate work; no replacement revision event stream is introduced.
