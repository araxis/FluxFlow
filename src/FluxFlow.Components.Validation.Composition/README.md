# FluxFlow.Components.Validation.Composition

Optional `json.validate` registration and Designer metadata.

The descriptor exposes one `JsonElement` Input, one
`JsonSchemaValidationResult` Output, and Events. Valid/invalid results and
`FlowError` values share Output and are separated with conditions. There are no
Valid, Invalid, or Errors ports.

Inline schema, schema path/id, selector, type, and runtime options remain flat.
The optional selector and clock are host-owned keyed resources with Designer
picker hints.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit ValidationComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddValidationComponents();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.
