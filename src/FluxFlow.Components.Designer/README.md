# FluxFlow.Components.Designer

Reusable component metadata contracts for FluxFlow.

## Purpose

This package lets component packages describe how a host can present and edit a
component without depending on a specific rendering framework.

## Contracts

- `ComponentDesignMetadata`: component display name, category, summary, icon key,
  preferred node name, suggested editor width, options, resources, ports, and
  attributes.
- `OptionDesignMetadata`: option name, kind, default value, required flag, helper
  text, min/max values, choices, and attributes.
- `ResourceDesignMetadata`: host-owned resource name, display text, order,
  required flag, value type hint, summary, and attributes.
- `PortDesignMetadata`: port name, direction, display name, group, order, summary,
  value type, primary flag, and attributes.
- `ComponentType`, `ComponentCategory`, `ComponentIconKey`,
  `ComponentPreferredNodeName`, `ComponentOptionName`,
  `ComponentOptionChoiceValue`, `ComponentResourceName`, `ComponentPortName`,
  `ComponentPortGroup`, and `ComponentAttributeName`:
  Designer-owned identifiers for component types, palette categories, palette
  icon keys, preferred node names, editable options, option choices,
  host-owned resource slots, ports, port groups, and metadata attribute keys.
  They do not depend on engine definition types.
- `IComponentDesignMetadataProvider`: package-owned metadata provider contract
  for reusable component packages.
- `ComponentDesignMetadataBuilder`: fluent authoring helper over the same
  metadata contracts.
- `ComponentDesignMetadataCatalog`: validates and composes metadata from one or
  more providers.
- `ComponentDesignMetadataServiceCollectionExtensions`: optional host DI helpers
  for registering providers and resolving one validated catalog.

`ComponentDesignMetadataValidator` reports invalid identifiers, duplicate
options and ports, duplicate primary ports per direction, invalid option
kind and port direction values, invalid option defaults, invalid min/max usage,
invalid choices, invalid resource and port order values, invalid resources,
invalid attributes, and null-bound metadata collections as validation errors
before metadata is registered.
`ComponentDesignMetadataCatalog` snapshots registered metadata after validation,
including nested choices and typed attribute maps, so later mutations to
provider-owned collections do not change the catalog.

## Option Kinds

The option kind contract supports:

- text
- number
- boolean
- enum
- multiline text
- JSON
- expression
- duration
- secret

Enum options must provide at least one choice. Choice lists are reserved for
enum options; non-enum options should use their value kind plus optional
constraints such as `Min` and `Max`.
Default values should match the option kind: text-like options use strings,
numbers use numeric values, booleans use `bool`, durations use `TimeSpan`, and
enum defaults use either a choice value string or an enum value whose name
matches a choice. `Min` and `Max` apply only to number and duration options.

## Resource Metadata

Resources describe host-owned dependencies such as keyed clients, stores,
expression engines, or clocks. They are metadata only; this package does not
register, resolve, validate, or dispose those resources.

## Example

```csharp
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;

var metadata = new ComponentDesignMetadata
{
    Type = new ComponentType("sample.transform"),
    DisplayName = "Sample Transform",
    Category = new ComponentCategory("Samples"),
    Summary = "Transforms a sample value.",
    IconKey = new ComponentIconKey("transform"),
    PreferredNodeName = new ComponentPreferredNodeName("transform"),
    SuggestedEditorWidth = 420,
    Options =
    [
        new OptionDesignMetadata
        {
            Name = new ComponentOptionName("expression"),
            Kind = OptionValueKind.Expression,
            DisplayName = "Expression",
            IsRequired = true
        },
        new OptionDesignMetadata
        {
            Name = new ComponentOptionName("mode"),
            Kind = OptionValueKind.Enum,
            DefaultValue = "strict",
            Choices =
            [
                new OptionChoiceMetadata
                {
                    Value = new ComponentOptionChoiceValue("strict"),
                    DisplayName = "Strict"
                },
                new OptionChoiceMetadata
                {
                    Value = new ComponentOptionChoiceValue("relaxed"),
                    DisplayName = "Relaxed"
                }
            ]
        }
    ],
    Resources =
    [
        new ResourceDesignMetadata
        {
            Name = new ComponentResourceName("engine"),
            DisplayName = "Engine",
            Order = 0,
            ValueType = "IExpressionEngine",
            IsRequired = true
        }
    ],
    Ports =
    [
        new PortDesignMetadata
        {
            Name = new ComponentPortName("Input"),
            Direction = PortDirection.Input,
            Order = 0,
            IsPrimary = true
        },
        new PortDesignMetadata
        {
            Name = new ComponentPortName("Output"),
            Direction = PortDirection.Output,
            Order = 0,
            IsPrimary = true
        }
    ],
    Attributes = new Dictionary<ComponentAttributeName, string>
    {
        [new ComponentAttributeName("shape")] = "transform"
    }
};

var catalog = new ComponentDesignMetadataCatalog().Add(metadata);
```

The fluent builder can author the same validated metadata shape with less
boilerplate. Component-level attributes can be added one at a time or as a
range through `AddAttribute` and `AddAttributes`:

```csharp
var built = new ComponentDesignMetadataBuilder("sample.transform")
    .WithDisplay(
        displayName: "Sample Transform",
        category: "Samples",
        summary: "Transforms a sample value.",
        iconKey: "transform",
        preferredNodeName: "transform",
        suggestedEditorWidth: 420)
    .AddOption("expression", OptionValueKind.Expression, isRequired: true)
    .AddResource("engine", order: 0, valueType: "IExpressionEngine", isRequired: true)
    .AddInputPort("Input", order: 0, isPrimary: true)
    .AddOutputPort("Output", order: 0, isPrimary: true)
    .AddAttributes(new Dictionary<string, string>
    {
        ["shape"] = "transform"
    })
    .Build();
```

## Package Providers

Runtime component packages can ship an `IComponentDesignMetadataProvider` that
returns metadata for their public node type constants. Hosts compose those
providers into a `ComponentDesignMetadataCatalog` to build palettes, editors,
validation views, and generated documentation without duplicating package
descriptors.
Providers must return a non-null metadata collection; catalog loading reports a
clear provider error when that contract is violated.
`ComponentDesignMetadataModule` is a small provider helper that validates,
rejects duplicate component types, and snapshots the metadata it receives.
`ComponentDesignMetadataBuilder` is a small authoring helper for providers that
want to build those same contracts fluently before returning them. The builder
validates null fluent option, resource, port, enum-choice, and attribute
arguments immediately, then still runs the same metadata validation path during
`Build()` for blank values, duplicates, invalid directions, and shape errors.

Hosts that use DI can register package-owned providers and one shared catalog:

```csharp
services
    .AddComponentDesignMetadataProvider<MyPackageMetadataProvider>()
    .AddComponentDesignMetadataCatalog();
```

Hosts can layer app-specific behavior, localization, resource pickers, and
rendering hints separately from package-owned metadata.

## Boundaries

This package only defines metadata contracts and catalog helpers. Hosts decide
how metadata is rendered, stored, localized, or combined with their own design
system. The contracts are neutral and do not depend on `FluxFlow.Engine` or
`FluxFlow.Composition`; hosts can map them to either runtime model.

## Composition

This package does not expose standalone workflow nodes or
`FluxFlow.Composition` factories. It composes design metadata for host palettes,
editors, validation views, and generated documentation.
