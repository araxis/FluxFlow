# FluxFlow.Data

Transport-neutral data-boundary contracts for FluxFlow. The package has no
Engine, Composition, Dataflow, DI, or provider dependency.

## Contracts

- `FlowContent`: exact owned immutable bytes plus optional content type and
  encoding.
- `FlowError`: stable processing error code, message, category, transient flag,
  and optional independently owned JSON details.

```csharp
var body = FlowContent.FromBytes(bytes, "application/json", "utf-8");
var error = new FlowError(
    "content.invalid",
    "The content is invalid.",
    "validation",
    details: JsonSerializer.SerializeToElement(new { field = "body" }));
```

`FromBytes` copies caller-owned memory. `FlowError` clones supplied
`JsonElement` details. The package does not provide a universal value tree,
result wrapper, codec catalog, lazy decoding, or dynamic object model.

Use typed CLR records for known component contracts, detached `JsonElement` for
explicit JSON work, and Serialization components for visible content
conversion. See `docs/20-flow-data-contracts.md` in the repository.
