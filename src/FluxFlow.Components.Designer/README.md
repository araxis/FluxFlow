# FluxFlow.Components.Designer

Reusable component metadata contracts for FluxFlow.

## Purpose

This package lets component packages describe how a host can present and edit a
component without depending on a specific rendering framework.

## Contracts

- `ComponentDesignMetadata`: component display name, category, summary, icon key,
  preferred node name, suggested editor width, options, ports, and attributes.
- `OptionDesignMetadata`: option name, kind, default value, required flag, helper
  text, min/max values, choices, and attributes.
- `PortDesignMetadata`: port name, direction, display name, group, order, summary,
  value type, primary flag, and attributes.
- `IComponentDesignMetadataProvider`: package-owned metadata provider contract
  for reusable component packages.
- `ComponentDesignMetadataCatalog`: validates and composes metadata from one or
  more providers.

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

## Example

```csharp
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

var metadata = new ComponentDesignMetadata
{
    Type = new NodeType("sample.transform"),
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
    Ports =
    [
        new PortDesignMetadata
        {
            Name = new PortName("Input"),
            Direction = PortDirection.Input,
            Order = 0,
            IsPrimary = true
        },
        new PortDesignMetadata
        {
            Name = new PortName("Output"),
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
system.

## Composition

This package does not expose standalone workflow nodes or
`FluxFlow.Composition` factories. It composes design metadata for host palettes,
editors, validation views, and generated documentation; engine-aware definition
types remain part of that metadata contract where they describe node and port
identifiers.
