# FlowPorts removal

FlowPorts and CreateStandard are deleted from the production API. There are no
remaining C# references in source, tests, benchmarks or package fixtures.

Plain IFlowNode components register actual Dataflow blocks in the Add callback:

```csharp
workflow.Add("node", node, context =>
{
    context.HasInput("Input", node.Input);
    context.HasOutput("Output", node.Output);
    context.HasEvents(node.Events);
});
```

The optional IFlowComponent convenience now has RegisterPorts(FlowComponent).
Standard node/source/transform bases implement it with those same calls. They no
longer construct or cache a port inventory. No replacement builder, descriptor,
port container or production interface was added. JSON composition's graph view
registers the bound blocks directly instead of creating another inventory.

FlowComponent.HasEvents retains a best-effort event source separately from normal
business outputs. It validates null/duplicate registrations and only accepts
registration during Add. It does not turn diagnostics into reliable business
delivery or introduce a new subscription mechanism.

TerminalInputs remains on the optional component interface for lifecycle only.
It enumerates the native blocks that must stay open while accepted business output
drains. Runtime drain planning and graph lifetime retain block-identity semantics,
including aliases; a signal flag or port name does not imply terminal behavior.
The FlowControlInputs implementation itself and FlowDeliveryBatch are unchanged.
Their simplification is the next separate review, not a completed part of this step.

Custom test/benchmark fixtures now use direct registration callbacks. Trace tests
access actual processing blocks. Existing block identity, borrowed ownership,
missing-block validation, control identity and shutdown tests are retained.
The intermediate test-only migration helper was also removed.

This is a breaking unpublished-preview API change: replace Ports inventories with
the Add callback, or implement RegisterPorts for optional automatic registration.
Custom terminal-control components additionally expose their TerminalInputs.
The reviewed public API baseline reflects the removal and new signatures.

Pre-step source is preserved in `artifacts/remove-flowports-20260915/before.zip`.

Validation: all 2,385 tests passed in seven reports, with no skips. Release build
passed with zero warnings/errors. Fresh package-only direct and JSON consumers
passed on .NET 8.0.31 and .NET 10.0.12. Source/reference scanning found no FlowPorts
references in C# or project files; whitespace checks passed. Evidence is under
`artifacts/remove-flowports-20260915/`: `verification.json`, `regression/`,
`regression.log`, `build-final.log`, `consumer.log` and `consumer/`.
The initial run was interrupted after an unmigrated custom source's terminal
identity stranded its completion; the fixture now preserves that identity and
the full final regression passed. Earlier failed/interrupted attempts remain
separate and are not counted as final qualification.

No commit, push or publication was requested or performed. The one-package Core
baseline remains. Earlier reports describing FlowPorts as pending are superseded.
