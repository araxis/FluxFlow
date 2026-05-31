# Package Authoring Helpers

Date: 2026-05-31

## Decision

Add a small registration grouping API for future component packages:

- `FlowNodeRegistration`: delegate-backed `IFlowNodeRegistration`.
- `IFlowNodeModule`: named contract for a component family registration group.
- `FlowNodeModule`: simple module implementation for static registration sets.
- `RuntimeNodeFactoryRegistry.Register(IFlowNodeModule)`.
- `RuntimeNodeFactoryRegistry.RegisterModules(...)`.

## Rationale

The FluxMq migration proved that a component family can grow a large factory
registry quickly. The engine already supported one registration object at a
time, but package authors needed a simple way to expose a whole component
family as one explicit unit.

This helper keeps the package boundary boring:

- No assembly scanning.
- No reflection.
- No hidden global state.
- Dependencies stay in constructors, delegates, or options owned by the package.
- Duplicate node type registration still fails through `RuntimeNodeFactoryRegistry`.
- Range and module registration validate duplicate node types before mutating
  the registry, so a bad group does not leave partial registrations behind.

## Intended Package Shape

Package authors can expose a module directly:

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .Register(new SampleOrderModule(store));
```

Or hide the module behind a package-owned extension:

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterSampleOrderComponents(store);
```

The extension should create and register one module. It should not scan
assemblies or pull dependencies from global state.

## Sample Update

`samples/FluxFlow.SampleApp` now uses `SampleOrderModule` behind
`RegisterSampleOrderComponents`, so the sample mirrors the future component
package convention.
