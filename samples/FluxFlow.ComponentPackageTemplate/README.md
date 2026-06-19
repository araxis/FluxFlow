# FluxFlow Component Package Template

A copyable shape for a **standalone** FluxFlow component node. A node is a self-contained
TPL Dataflow processor over the `FluxFlow.Nodes` kit — it needs **no engine**. Copy this
package, rename the types, and implement `ProcessAsync`. That is the whole job.

Template structure:

```text
FluxFlow.ComponentPackageTemplate/
  Contracts/        TemplateInput, TemplateOutput
  Diagnostics/      TemplateDiagnosticNames
  Nodes/            TemplateEnrichNode : FlowNode<TemplateInput, TemplateOutput>
  Options/          TemplateEnrichOptions
  TemplateErrorCodes.cs
```

The sample node is `template.enrich` — a transform:

```text
Input:  FlowMessage<TemplateInput>   (post here)
Output: FlowMessage<TemplateOutput>  (link consumers here; carries the same correlation id)
Errors: FlowError                    (domain failures; the pump keeps running)
Events: FlowEvent                    (diagnostics)
```

It reads static configuration from `TemplateEnrichOptions` (`Prefix`, `BoundedCapacity`).

There is no registration glue, factory, module, or design-metadata: a node works out of the
box. Construct it and wire it with plain TPL Dataflow:

```csharp
var node = new TemplateEnrichNode(new TemplateEnrichOptions { Prefix = "demo" });
node.Output.LinkTo(nextBlock);                       // link the graph
await node.Input.SendAsync(FlowMessage.Create(input)); // feed it
```

Reading config and composing a graph (new the nodes, `LinkTo`, run a host) is a separate
layer — the optional engine runtime, or just your own startup code.

Run the template tests from the repository root:

```sh
dotnet test tests/FluxFlow.ComponentPackageTemplate.Tests/FluxFlow.ComponentPackageTemplate.Tests.csproj
```
