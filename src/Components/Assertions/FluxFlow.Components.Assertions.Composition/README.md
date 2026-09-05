# FluxFlow.Components.Assertions.Composition

Optional registration and Designer metadata for `data.assert`.

`AssertionsComponentDefinition` is the package-owned source of canonical type,
option, resource, port, and Designer presentation declarations.

The registration uses `JsonAssertionNode`: one `JsonElement` Input, one
`AssertionResult<JsonElement>` Output, and Events. Passed/failed results and
in-band errors are routed with link conditions; there are no Passed, Failed, or
Errors ports.

The expression engine is a required host-owned keyed resource. Context factory
and clock resources are optional. Option metadata groups expression, diagnostic,
type, result, branch, and runtime hints without owning those resources.

## DI Registration

This optional application-integration adapter uses one designed component
registration. Its `ComponentDescriptor` owns the canonical type, options,
resources, processing capabilities, and typed ports; Designer metadata adds
presentation hints without redefining that structure:

```csharp
services.AddFluxFlowComponents().AddAssertions();
```

The resulting `ComponentCatalog` and `ComponentDesignMetadataCatalog` are built
once from DI registrations. Standalone runtime nodes remain usable without this
package, and referenced external resources remain host-owned.

## Code-first authoring

`AssertionsComponents.Assertion` is the typed contract used by both generic `AddComponent` and `AddAssertion`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor. Normal
code-first hosting therefore calls only `AddFluxFlow(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.
