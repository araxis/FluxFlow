# HTTP Trigger in Studio Run

HTTP Trigger now participates in the normal companion execution lifecycle. The HTTP
plugin registers the existing trigger configuration reader and maps its routes onto
a Kestrel listener in the run's own service provider. No second workflow container
or component-specific dependency in Studio is introduced.

## Hosting and compatibility

- The plugin contract has an optional `ConfigureWorkflowHost(IHostBuilder,
  IConfiguration)` method with a default implementation. Existing plugin packages
  can run without implementing it. SDK version: `1.0.0-alpha.6`.
- HTTP package version `1.0.0-alpha.2` starts a listener only when a workflow contains
  `http.trigger`. Each application instance uses `http://127.0.0.1:0`, allowing the
  operating system to choose an available port.
- The original `Microsoft.Hosting.Lifetime` log entry `Now listening on: ...` reports
  the address. Append the saved Route. The address is not written into configuration.
- Workflow registration reads the original application document separately from
  ASP.NET Core host keys such as `applicationName` and `urls`. The native host owns
  configuration disposal; failed host construction disposes the document directly.
- Listener startup precedes workflow startup; shutdown reverses that order. The
  existing Cancel and Drain modes, reply correlation, admission limits and timeout
  implementation remain unchanged. Stop closes the listener and rerun starts a new
  host and workflow instance.
- Public endpoints, fixed ports, TLS and authentication remain available through the
  existing standalone ASP.NET Core hosting API. Studio's listener is loopback only.

## Verification

All checks passed on .NET 8, 10 and 11:

- 192 runner, HTTP Client, HTTP Trigger, companion, execution log and trace tests.
  New runner coverage includes concurrent correlated replies, 202 acknowledgements,
  504 timeouts, pending Cancel and Drain, two simultaneous instances, listener
  closure, failed second-instance startup, retained observations and clean reruns.
- 183 existing ASP.NET Core hosting tests, including request parsing, cancellation,
  response correlation and late replies. The test project now also targets .NET 11.
- 531 focused shared Studio editor, palette and port-contract tests, including HTTP
  mode switching, method editing, Save/Cancel, unknown saved properties and Undo/Redo.
- Six real RabbitMQ log-contract tests covering normal shutdown and unexpected
  disconnect. Original messages, severity, categories and complete exception details
  survive capture and transport, with containers removed after each trial.
- Published package consumers loaded HTTP from an isolated store alongside the
  previously installed Transformation package. Two runs per framework verified
  replies, timeouts, pending Stop cancellation, closed listeners, unchanged
  configuration and retained observations. No HTTP project reference was present.

The HTTP package and published consumers are under
`%TEMP%/fluxflow-http-trigger-review/`.
The user's running Studio, tabs and plugin store were not replaced.
All 63 protected Core/WFENG files match the pre-existing hash baseline.

## Repeatable checks

Run `HttpTriggerRunTests` in `FluxFlow.Environments.Tests` for all three target
frameworks. Run `FluxFlow.Hosting.AspNetCore.Tests` for native HTTP semantics.

For package loading, install the HTTP package and an existing Transformation package
into a temporary store using `Code/eng/fluxflow-plugin.ps1`. Build or publish
`Code/eng/http-trigger-plugin-consumer/HttpTriggerPluginConsumer.csproj` for each
framework and pass that store directory to the executable. Success prints
`HTTP_TRIGGER_PLUGIN_TRIAL_PASSED` with the actual runtime version.

## Follow-up review

Reviewed host configuration ownership, plugin host registration, startup failure,
reply correlation, timeout handling and shutdown order. No runtime correction or
additional abstraction was needed.

Added a normal-run regression using a local downstream HTTP server. A reply arriving
after the first request's timeout cannot complete an already admitted second request,
even with `MaxPendingRequests` set to one. A subsequent accepted request completes
successfully while Stop waits in Drain mode. The saved request remains unchanged.

The new regression and all existing HTTP Trigger runner tests passed on .NET 8/10/11.
The 192 runner/logging checks, 183 native HTTP hosting checks and 531 focused shared
Studio checks also passed. Published package consumers again passed on all three
runtimes with the previously installed Transformation package. Existing saved values,
Undo/Redo and plugin compatibility require no migration. Only regression coverage
and this report changed during the follow-up review.
