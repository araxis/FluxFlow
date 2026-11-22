# Application Lifetime

FlowApplication is constructed through native DI. BuildApplicationAsync composes it
once and returns it unstarted. StartAsync connects prepared routes and starts sources.
Incoming messages drive components. After application startup, independently linked
workflows support StopAsync and StartAsync with unchanged configuration. Stop drains
and disposes the run's DI scope; restart constructs fresh blocks in a new scope.
See [workflow lifetime](../Code/src/FluxFlow.Application/README.md#workflow-stop-and-restart).

Start requires a successful Build. An attempt before or during Build is rejected
without starting components or preventing a subsequent valid Build/Start. If Stop
arrives while a source is starting, later sources are not started. A fault during
the last source's startup is reported by Start as well as by completion.

StopAsync stops production and drains accepted processing. DisposeAsync releases
application routing resources and workflow DI scopes; the root provider retains shared resources.
Dispose is safe after failed or canceled construction. Stop/dispose the application
before the provider. Precanceled Start leaves the application startable.

Build and Start run the same cleanup path after failure. If cleanup only observes
the original failure again, the initiating exception is rethrown intact. Independent
cleanup errors are combined with it once by exception identity, including when the
original failure is an aggregate. Provider-owned disposal retains standard DI behavior.

Completion represents graph termination. FlowError represents a processing error that
can travel through ordinary outputs; it does not automatically terminate the graph.
Fatal faults close admission and propagate through completion/diagnostics.

There are no application revision states, candidate providers, stable revision ports,
ReloadAsync or ApplyAsync APIs in this minimum package. Future live reconfiguration
belongs in a separately designed optional module.
