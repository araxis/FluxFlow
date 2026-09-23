# Application Lifetime

FlowApplication is constructed through native DI. BuildApplicationAsync composes it
once and returns it unstarted. StartAsync connects prepared routes and starts sources.
Workflows themselves have no Start/Stop: incoming messages drive their components.

StopAsync stops production and drains accepted processing. DisposeAsync releases
application routing resources; the host provider disposes components and resources.
Dispose is safe after failed or canceled construction. Stop/dispose the application
before the provider. Precanceled Start leaves the application startable.

Completion represents graph termination. FlowError represents a processing error that
can travel through ordinary outputs; it does not automatically terminate the graph.
Fatal faults close admission and propagate through completion/diagnostics.

There are no application revision states, candidate providers, stable revision ports,
ReloadAsync or ApplyAsync APIs in this minimum package. Future live reconfiguration
belongs in a separately designed optional module.
