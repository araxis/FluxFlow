# FluxFlow.Components.Designer

Reusable component presentation metadata and canonical application-editing
contracts for FluxFlow. The package depends on `FluxFlow.Composition`, but not
on `FluxFlow.Engine` or a rendering framework.

## Registration

Use `AddFluxFlowComponents()` when a tool needs component catalogs without
hosting an application. Family extensions target the returned
`FluxFlowRegistrationBuilder`:

```csharp
var services = new ServiceCollection();

services
    .AddFluxFlowComponents()
    .AddSources()
    .AddMapping()
    .AddTimers();

using var provider = services.BuildServiceProvider();
var runtimeCatalog = provider.GetRequiredService<ComponentCatalog>();
var designCatalog = provider.GetRequiredService<ComponentDesignMetadataCatalog>();
```

`ComponentCatalog` remains owned by Composition. Every designed
`AddComponent(...)` registration automatically contributes to the immutable
`ComponentDesignMetadataCatalog`; there is no terminal catalog-registration
call and no second component registry. A chain containing only
`AddRuntimeComponent(...)` registers only the runtime catalog.

## Flat Designed Components

`AddComponent(...)` creates the runtime descriptor and presentation metadata
from one authoritative, flat callback. The type appears once, the callback runs
immediately, and no `Build`, `Commit`, nested builder, reflection, or scanning is
involved.

There is no parallel metadata builder or option/resource metadata factory.
Registration authors use this callback; standalone tooling constructs the
immutable metadata records directly and passes them to
`ComponentDesignMetadataCatalog`.

```csharp
builder.AddComponent("sample.transform", component =>
{
    component.UseFactory(CreateTransformAsync);
    component.UseProcessing(CompositionProcessingCapabilities.Sequential);

    component.WithDisplay(
        displayName: "Sample Transform",
        category: "Samples",
        summary: "Transforms a sample value.",
        iconKey: "transform",
        preferredNodeName: "transform",
        suggestedEditorWidth: 420);

    component.AddInput<JsonElement>(
        "Input",
        displayName: "Input",
        group: "Values",
        order: 0,
        summary: "Value to transform.",
        isPrimary: true);

    component.AddOutput<JsonElement>(
        "Output",
        displayName: "Output",
        group: "Results",
        order: 0,
        summary: "Transformed value.",
        isPrimary: true);

    component.AddOption<string>(
        "expression",
        kind: OptionValueKind.Expression,
        displayName: "Expression",
        helperText: "Expression evaluated for each input.",
        isRequired: true,
        section: "Mapping",
        editor: OptionDesignMetadataAttributeValues.Expression);

    component.AddOption<string>(
        "mode",
        kind: OptionValueKind.Enum,
        displayName: "Mode",
        defaultValue: "strict");
    component.AddOptionChoice("mode", "strict", displayName: "Strict");
    component.AddOptionChoice("mode", "relaxed", displayName: "Relaxed");

    component.AddResource<IExpressionEngine>(
        "engine",
        displayName: "Engine",
        order: 0,
        summary: "Host-owned expression engine.",
        isRequired: true,
        ownership: ResourceDesignMetadataAttributeValues.HostOwned,
        pickerKind: ResourceDesignMetadataAttributeValues.ExpressionEngine);

    component.SetOptionAttribute("expression", "relatedResource", "engine");
    component.SetPortAttribute("Input", PortDirection.Input, "accepts", "json");
    component.AddAttribute("shape", "transform");
});
```

Root-level methods cover display information, ports, options, resources,
choices, and attributes. References to unknown options, ports, or resources
fail during registration. Duplicate names, invalid ranges, missing
factories, reserved `Events` outputs, and invalid metadata also fail immediately.

Equivalent repeated built-in family registration is idempotent. A semantically
different runtime descriptor or design registration for an existing type throws a clear
conflict exception; registration never uses last-write-wins behavior.

Runtime-only components belong to `FluxFlow.Composition` and use the distinct
`AddRuntimeComponent(...)` API, so Composition-only consumers do not need this
package.

## Metadata Contracts

- `ComponentDesignMetadata` describes display name, category, summary, icon,
  preferred node name, suggested width, processing capabilities, options,
  resources, ports, and attributes.
- `OptionDesignMetadata` describes value kind, default, requiredness, helper
  text, numeric/duration range, choices, and editor attributes.
- `ResourceDesignMetadata` describes a host-owned resource slot, order, value
  type hint, requiredness, summary, and picker attributes.
- `PortDesignMetadata` describes direction, display name, grouping, order,
  message/value types, cardinality, primary status, and attributes.
- `ComponentDesignMetadataCatalog` is a read-only, ordered snapshot. Its public
  constructor accepts metadata directly for standalone tooling; normal DI
  registration builds it automatically from designed components.

`ComponentDesignMetadataValidator` validates identifiers, null collections,
duplicates, option kinds/defaults/ranges/choices, port directions and primary
ports, resource/port ordering, and attributes. Designed registration adds the
canonical processing hints and traced `Events` output, validates the complete
metadata, and snapshots nested collections before changing DI. Direct catalog
construction applies the same finalization, so later source mutations cannot
change the catalog.

## Option And Resource Hints

Option kinds are text, number, boolean, enum, multiline text, JSON, expression,
duration, and secret. Enum options require choices. `Min` and `Max` apply to
number and duration options.

Option attributes describe sections, importance, editor kind, syntax, and
related resources. Resource attributes describe ownership, picker kind, key
patterns, related options, and conditional requiredness. These are neutral
host hints: the host still chooses controls, supplies keyed resources, owns
secrets, and controls service lifetimes.

`ComponentResourcePickerHints.Create(...)` returns an ordered neutral view of
host-owned picker hints. It does not enumerate, resolve, create, validate, or
dispose host resources.

## Canonical Application Persistence

`DesignerApplicationPersistence` loads and saves the same flat
`ApplicationDefinition` used by Composition. It delegates JSON shape, address
resolution, and link grammar to Composition rather than maintaining a second
schema or parser.

```csharp
var persistence = new DesignerApplicationPersistence(componentCatalog, metadataCatalog);
var loaded = persistence.Load(json);

var link = DesignerApplicationLink.Create(
    ApplicationAddress.WorkflowPort("Orders", "Read", "Output"),
    ApplicationAddress.WorkflowPort("Orders", "Validate", "Input"));

var edited = loaded.Document with
{
    Links = [.. loaded.Document.Links, link]
};

var savedJson = persistence.Serialize(edited, writeIndented: true);
```

Loads require exact canonical component and resource identities and return
structured diagnostics for unknown values. Malformed component properties stay
raw and round-trip unchanged. Conditional link validation can use an explicitly
configured `ApplicationLinkCompiler`; there is no service discovery fallback.

## Package Extensions

Component packages expose one normal extension over
`FluxFlowRegistrationBuilder` and register every designed component with
`AddComponent(...)`:

```csharp
public static FluxFlowRegistrationBuilder AddSampleTransforms(
    this FluxFlowRegistrationBuilder builder)
    => builder.AddComponent("sample.transform", component =>
    {
        component.UseFactory(CreateTransformAsync);
        component.WithDisplay(displayName: "Sample Transform", category: "Samples");
        component.AddInput<JsonElement>("Input", displayName: "Input");
        component.AddOutput<JsonElement>("Output", displayName: "Output");
    });
```

Packages remain explicit and Engine-independent. The Designer package does not
execute workflows, own resources, render UI, scan assemblies, or provide global
registries.
