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
- `ComponentType` and `ComponentPortName`: Designer-owned identifiers for
  component types and ports. They do not depend on engine definition types.
- `IComponentDesignMetadataProvider`: package-owned metadata provider contract
  for reusable component packages.
- `ComponentDesignMetadataCatalog`: validates and composes metadata from one or
  more providers.

`ComponentDesignMetadataValidator` reports invalid identifiers, duplicate
options and ports, duplicate primary ports per direction, invalid option
defaults, invalid min/max usage, invalid choices, invalid resources, invalid
attributes, and null-bound metadata collections as validation errors before
metadata is registered.
`ComponentDesignMetadataCatalog` snapshots registered metadata after validation,
including nested choices and attribute maps, so later mutations to
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
    Category = "Samples",
    Summary = "Transforms a sample value.",
    IconKey = "transform",
    PreferredNodeName = "transform",
    SuggestedEditorWidth = 420,
    Options =
    [
        new OptionDesignMetadata
        {
            Name = "expression",
            Kind = OptionValueKind.Expression,
            DisplayName = "Expression",
            IsRequired = true
        }
    ],
    Resources =
    [
        new ResourceDesignMetadata
        {
            Name = "engine",
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
    ]
};

var catalog = new ComponentDesignMetadataCatalog().Add(metadata);
```

## Package Providers

Runtime component packages can ship an `IComponentDesignMetadataProvider` that
returns metadata for their public node type constants. Hosts compose those
providers into a `ComponentDesignMetadataCatalog` to build palettes, editors,
validation views, and generated documentation without duplicating package
descriptors.

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
