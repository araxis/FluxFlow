# Direct port registration: first migration step

> Superseded by [FlowPorts removal](2026-09-15-flowports-removal.md): the migration
> and deletion described as remaining below have now been implemented.

The user's next target is removing unnecessary node abstractions, beginning with
FlowPorts. This step makes direct construction work without it:

```csharp
var node = new MyNode();
var component = workflow.Add("node", node, context =>
{
    context.HasInput("Input", node.Input);
    context.HasOutput("Output", node.Output);
});
```

The node only implements IFlowNode lifecycle and exposes real Dataflow blocks.
The callback uses the existing FlowComponent registration object; no new builder,
descriptor, port bag or registration interface is introduced. Registration closes
when the callback exits, and ownership transfers only after successful addition.
Links cannot be created against a component whose registration is incomplete.

FlowComponent.Instance now returns IFlowNode instead of IFlowComponent. This is
a breaking source/API change in the unpublished preview; the public baseline was
reviewed and updated. Existing automatic component registration still works.

Two new tests cover native-block routing across workflows, concurrent correlated
replies, normal shutdown/disposal, failed registration, retrying registration and
preventing later changes through a captured context. The package-only consumer
also includes a native-block component registered through the new API.

Validation: Release build has zero warnings/errors; all 2,385 local tests passed
in seven reports. Fresh package-only consumers passed on .NET 8.0.31 and
.NET 10.0.12, including direct native-block registration and JSON construction.
Evidence is in `artifacts/direct-port-registration-20260915/`: `build.log`,
`regression.log`, `regression/`, `consumer.log` and `consumer/`. Whitespace checks
passed. No external service was required.

## Remaining work

FlowPorts is NOT removed yet. The old automatic path, built-in bases, JSON binding
and graph-view adapter still use it. Migrate those consumers to registration before
deleting the type. Terminal-control shutdown ordering must be preserved; ordinary
inputs such as a Gate's Open/Close should not acquire terminal semantics merely
because they are named controls. FlowControlInputs and FlowDeliveryBatch have not
been changed in this step. HasEvents parity also remains part of this migration.

Keep the one-package Core baseline. Do not restore optional modules. No commit,
push or publication is part of this step.
