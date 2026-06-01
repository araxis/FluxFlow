# FluxFlow Component Package Template

This sample is a copyable component package shape. It is intentionally small:
one transform node, one options model, one module, one registration extension,
diagnostic names, error codes, contracts, and tests.

Template structure:

```text
FluxFlow.ComponentPackageTemplate/
  Contracts/
  Diagnostics/
  Nodes/
  Options/
  TemplateComponentModule.cs
  TemplateComponentRegistrationExtensions.cs
  TemplateComponentTypes.cs
```

The sample node is `template.enrich`:

```text
Input:  TemplateInput
Output: TemplateOutput
```

It reads static configuration from `TemplateEnrichOptions`:

```json
{
  "prefix": "demo",
  "boundedCapacity": 8
}
```

Hosts register the package through a single extension method:

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterTemplateComponents(options =>
        options.UseTimeProvider(TimeProvider.System));
```

Run the template tests from the repository root:

```sh
dotnet test tests/FluxFlow.ComponentPackageTemplate.Tests/FluxFlow.ComponentPackageTemplate.Tests.csproj
```
