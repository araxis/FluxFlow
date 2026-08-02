# Designed Registration And Immutable Catalog Simplification

Date: 2026-07-28

## Outcome

Designed component registration now has one complete public path:

```csharp
services.AddFluxFlowComponents().AddComponent("sample.type", component =>
{
    component.UseFactory(CreateAsync);
    component.AddInput<Message>("Input", displayName: "Input");
    component.AddOutput<Result>("Output", displayName: "Output");
});
```

`AddComponent(...)` builds the runtime descriptor and design metadata from the
same flat callback, finalizes and validates metadata before changing DI, stores
an immutable snapshot, and automatically registers both `ComponentCatalog` and
`ComponentDesignMetadataCatalog`. `AddRuntimeComponent(...)` remains a distinct
Composition-only path and does not register a design catalog.

## Removed Public Surface

- terminal `AddDesignerCatalog()`;
- `IServiceCollection.AddComponentDesignMetadataCatalog()`;
- public `ComponentDesignDeclaration` construction;
- mutable catalog `Add(...)` and `AddRange(...)` plus public declaration factory;
- 19 family `*ComponentDefinition.CreateMetadata()` shims;
- `DescribeInput`, `DescribeOutput`, `DescribeOption`, `DescribeResource`, and
  `SetOptionRange` post-description methods.

The retained registration builder remains one flat callback. Ports, options,
resources, choices, ranges, presentation hints, and custom attributes are
authored at registration without nested callback builders.

## Catalog Contract

`ComponentDesignMetadataCatalog` is a read-only ordered index. Its public
constructor accepts zero or more metadata records for standalone tooling,
rejects null entries and duplicate types, finalizes and deep-snapshots inputs,
preserves source order, exposes one cached read-only `All` list, and supports
exact `TryGet` lookup. Normal DI consumers resolve the automatically built
catalog and do not construct declarations.

Registration-time finalization retains canonical compatibility filtering,
`omittedOptions` diagnostics, the semantic `processing` option/resource, the
traced `Events` output, processing capabilities, exact structural message and
resource hints, required flags, ordering, and nested attributes.

## Scope And Boundaries

- All 19 active composition families and all 44 component registrations use
  the same `AddComponent(...)` signature.
- Family `*ComponentDefinition` types now contain only public constants and
  related names; they no longer build a temporary service collection.
- Composition has no Designer or Engine project dependency.
- Component composition packages have no Engine project dependency.
- No reflection discovery, assembly scanning, new generic hierarchy, nested
  callback DSL, production friend assembly, or compatibility wrapper was added.
- The changes stay within the existing breaking major release train; package
  versions were not advanced again. Changelog and package release notes record
  the additional removals.

## Verification

- Focused verification: 598 tests passed with zero warnings:
  Designer 127, all 19 family suites 305, Composition 105, DesignerHost 22,
  and focused Release matrix/conventions 39.
- Full solution build: 121 projects, zero errors, zero warnings.
- Full serialized solution test command passed with zero failures.
- `dotnet format FluxFlow.sln --no-restore` completed successfully.
- The accepted public API source-declaration baseline passed independently.
- Removed-API, project-boundary, 19-family/44-registration, and whitespace
  searches passed; `git diff --check` is clean.

No staging, commit, push, branch, or pull-request operation was performed.
