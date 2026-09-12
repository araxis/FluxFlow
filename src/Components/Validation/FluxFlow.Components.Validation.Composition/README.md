# FluxFlow.Components.Validation.Composition

Runtime registration has no Designer dependency. Visual consumers add
FluxFlow.Components.Validation.Designer and call AddValidationDesign after AddValidation.
Direct constructors and runtime behavior are unchanged.


Optional structural `json.validate` registration.

The descriptor exposes canonical `FlowValue` Input and Output ports, plus Events.
The component materializes input to `JsonElement` and wraps validation results in
the output value. Valid/invalid results and
`FlowError` values share Output and are separated with conditions. There are no
Valid, Invalid, or Errors ports.

Inline schema, schema path/id, selector, type, and runtime options remain flat.
The optional selector and clock are host-owned keyed resources. The optional
presentation package supplies their picker hints.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit ValidationComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddValidation();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`ValidationComponents.JsonSchemaValidator` is the typed contract used by both generic `AddComponent` and `AddJsonSchemaValidator`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.
