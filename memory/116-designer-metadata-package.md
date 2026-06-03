# Designer Metadata Package

Date: 2026-06-03

`FluxFlow.Components.Designer` now provides neutral metadata contracts for
component presentation and editing.

## Decision

Create a small contracts package instead of a runtime node package.

Hosts can use this package to discover component display metadata, option
metadata, and port layout hints without coupling the component package to any
specific renderer or host application.

## Scope

- Adds `ComponentDesignMetadata` for component display name, category, summary,
  icon key, preferred node name, suggested editor width, options, ports, and
  attributes.
- Adds `OptionDesignMetadata`, `OptionChoiceMetadata`, and `OptionValueKind`
  for text, number, boolean, enum, multiline text, JSON, expression, duration,
  and secret options.
- Adds `PortDesignMetadata` and `PortDirection` for port ordering, grouping,
  primary-port hints, value type hints, and direction.
- Adds `IComponentDesignMetadataProvider`,
  `ComponentDesignMetadataModule`, and `ComponentDesignMetadataCatalog`.
- Adds validation for duplicate component types, duplicate option names,
  duplicate choice values, duplicate ports by direction/name, invalid min/max,
  empty optional text, empty attributes, and default identifier structs.

## Boundary

- No runtime nodes.
- No transport-specific contracts.
- No renderer dependency.
- No host application storage or dashboard dependency.

## Package

- Package: `FluxFlow.Components.Designer`
- Version: `0.1.0-alpha.1`
- Public additions:
  - `ComponentDesignMetadata`
  - `OptionDesignMetadata`
  - `OptionChoiceMetadata`
  - `OptionValueKind`
  - `PortDesignMetadata`
  - `PortDirection`
  - `IComponentDesignMetadataProvider`
  - `ComponentDesignMetadataModule`
  - `ComponentDesignMetadataCatalog`
  - `ComponentDesignMetadataValidator`
  - `DesignerMetadataValidationError`

## Verification

- Focused Designer tests passed: 7 tests.
- Full solution build passed in Release with 0 warnings.
- Full solution tests passed in Release.
- Package pack passed and produced
  `FluxFlow.Components.Designer.0.1.0-alpha.1.nupkg`.
- Release commit: `c81cfec`.
- Release tag: `components-designer-v0.1.0-alpha.1`.
- Release workflow run: `26875082488`, success.
- Main verification run: `26875074734`, success.
- Fresh public-feed restore/run smoke passed and returned `True:1:Input`.
