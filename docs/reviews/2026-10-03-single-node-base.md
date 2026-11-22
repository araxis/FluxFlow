# One optional node base

The previous authoring API asked developers to choose FlowNode, FlowTransformNode or
FlowSource. FlowNode was itself a specialization of FlowTransformNode. The separate
bases are removed, with no forwarding aliases or replacement authoring hierarchy.

FlowNode is an optional helper over native blocks. Components supply ordinary functions:

- CreateInput(Func<FlowMessage, Task<IEnumerable<FlowMessage>>>, flush) constructs the
  native TransformMany input and preserves native returned-result ordering.
- CreateInput(Func<FlowMessage, Task>, flush) supports direct EmitAsync publication,
  including timed Batch output. Errors follow the same explicit publication path.
- RunOnStart(Func<CancellationToken, Task>) configures independent production without
  constructing a processing block. IFlowSource remains the optional lifecycle interface
  used by Application. Its methods forward to StartProductionAsync, StopProducing and
  SourceCompletion; no new interface or lifecycle type is introduced.

The component configures one path during construction, before declaring additional
ports. Its public Input property, if present, exposes InputBlock. Common helpers expose
Output/Events, emission, extra native ports and lifecycle. There are no abstract process,
transform, producer or flush methods. HandlesErrors and OnDisposeAsync remain optional
overrides for error consumption and owned-resource cleanup.

The existing internal processing and source lifetime collaborators remain responsible
for draining, cancellation and fault observation. They are not authoring bases. This
change does not merge distinct source/control termination phases or add an input to a
scheduler. Native IFlowNode/IFlowSource implementations remain fully supported.

## Migration

Replace either removed base with FlowNode. In constructors, call CreateInput with the
former ProcessAsync/TransformAsync method; remove its override modifier. Pass the former
flush method as the optional flush argument. Expose public Input => InputBlock only when
the component has a business input. For producers, call RunOnStart with the former
RunAsync method and implement IFlowSource using the production helpers. Keep public
component options and configuration registration separate from execution.

All bundled components, test fixtures, benchmarks and isolated package consumers follow
this model. Input/Output configuration registrations retain their existing addresses.
Generic helpers must select a concrete component input explicitly rather than assuming
every FlowNode has one. Rebuild downstream binaries and update custom subclasses.

## Verification

Existing regression cases cover processing callbacks and explicit emission, forwarded
and generated error ordering, finite-result enumeration failures, native capacity and
parallelism, tracing, timed batches, producer termination, terminal controls and disposal.
Boundary checks require one optional authoring base, no abstract execution methods and
no universal public Input. A regression test verifies that rejected reconfiguration
leaves the original input/output and completion intact.

Final Release verification passed all 2,102 test executions across 28 project/target
runs with zero warnings. Debug built all 32 projects with zero warnings or errors.
The public API baseline is updated and passing. Isolated Core, Application,
Configuration and Routing package consumers passed on both .NET 8 and .NET 10.
Whitespace validation passed. No commit or publication was performed.
