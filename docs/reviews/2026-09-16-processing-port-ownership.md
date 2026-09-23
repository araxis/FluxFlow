# Processing ports and lifetime responsibilities

FlowProcessingLifetime previously constructed and exposed the main output and
event stream, and constructed additional outputs using its own capacity setting.
Component wrappers retrieved their ports from this shutdown helper. This coupled
port construction policy to lifecycle coordination.

FlowNode and FlowTransformNode now construct, retain and expose their own
FlowOutput and BroadcastBlock. FlowNode also constructs additional outputs using
its existing capacity setting. FlowProcessingLifetime receives existing
IDataflowBlock references and coordinates their completion, faults, cancellation,
draining and disposal. It has no port factory, port properties, capacity setting
or message-type dependency. The same block references are shared; no extra queues
or forwarding layer were introduced.

This change clarifies ownership rather than reducing line count. Common shutdown
logic remains shared; the source's distinct production lifecycle remains unchanged.
Port naming and application registration remain external. Public/protected
component APIs and Gate implementation are unchanged. The source declaration
baseline changed because it also tracks public declarations inside internal types.

## Validation

- Release solution build: zero warnings and errors.
- Full solution regression: 2,409 passed in nine TRX reports, no failures/skips.
- Fresh Core-only package consumer: passes .NET 8 and .NET 10.
- Fresh Routing plus Core package consumer: passes .NET 8 and .NET 10.
- Existing coverage exercises output draining, errors, cancellation, ordered
  transforms, additional outputs, terminal controls and Gate commands.

Evidence: `artifacts/port-ownership-20260916/`. No commit, push or publication.
