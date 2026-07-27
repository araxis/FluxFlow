# FluxFlow.Components.Designer

Reusable component metadata and canonical application editing contracts for
FluxFlow.

## Purpose

This package lets component packages describe how a host can present and edit a
component without depending on a specific rendering framework.
It also projects the canonical flat `ApplicationDefinition` into editor-facing
workflows, links, resource namespaces, and resource references without creating
a second persistence schema.

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
  `ComponentPortGroup`, `ComponentAttributeName`,
  `ComponentAttributeValue`, `ComponentMetadataText`, and
  `ComponentValueTypeHint`:
  Designer-owned value types for component types, palette categories, palette
  icon keys, preferred node names, editable options, option choices, metadata
  display text,
  host-owned resource slots, ports, port groups, metadata attribute keys,
  metadata attribute values, and value type hints. They do not depend on engine
  definition types.
- `IComponentDesignMetadataProvider`: package-owned metadata provider contract
  for reusable component packages.
- `ComponentDesignMetadataBuilder`: fluent authoring helper over the same
  metadata contracts.
- `OptionDesignMetadataFactory` and `ResourceDesignMetadataFactory`: small
  construction helpers for repeated option and host-owned resource shapes;
  providers remain explicit about node-specific names, defaults, order, ports,
  and attributes.
- `ComponentDesignMetadataCatalog`: validates and composes metadata from one or
  more providers.
- `ComponentDesignMetadataServiceCollectionExtensions`: optional host DI helpers
  for registering providers and resolving one validated catalog.
- `ComponentResourcePickerHint` and `ComponentResourcePickerHints`: neutral
  host-side helpers for reading host-owned resource picker hints from metadata
  without resolving resources or rendering UI.
- `ResourceDesignMetadataAttributeNames`,
  `ResourceDesignMetadataAttributeValues`, and
  `ResourceDesignMetadataAttributes`: shared names, values, and helpers for
  describing host-owned resource picker hints.
- `OptionDesignMetadataAttributeNames`,
  `OptionDesignMetadataAttributeValues`, and
  `OptionDesignMetadataAttributes`: shared names, values, and helpers for
  describing option editor, section, importance, syntax, and related-resource
  hints.
- `PortDesignMetadataAttributeNames`, `PortDesignMetadataAttributeValues`, and
  `PortDesignMetadataAttributes`: neutral hints that let a host distinguish a
  payload-independent signal input from a normal typed message port.
- `DesignerApplicationPersistence`: canonical JSON load/save and editor model
  projection over `FluxFlow.Composition.Model.ApplicationDefinition`.
- `DesignerApplicationDocument`, `DesignerWorkflow`, and `DesignerComponent`:
  flat editor-facing application models whose component properties remain raw
  JSON values.
- `DesignerApplicationLink`: canonical source and target addresses, optional
  condition, and the input/output declaration side used by the loaded document.
- `DesignerResourceNamespace`, `DesignerResource`, and
  `DesignerResourceReference`: nested resource catalog and component-reference
  projections using canonical `Resources.*` addresses.

`ComponentDesignMetadataValidator` reports invalid identifiers, duplicate
options and ports, duplicate primary ports per direction, invalid option
kind and port direction values, invalid option defaults, invalid min/max usage,
invalid choices, invalid resource and port order values, invalid resources,
invalid attributes, and null-bound metadata collections as validation errors
before metadata is registered.
`ComponentDesignMetadataCatalog` snapshots registered metadata after validation,
including nested choices and typed attribute maps, so later mutations to
provider-owned collections do not change the catalog.

Providers declare exactly one canonical component type. Catalog lookup is exact
and ordinal, and palettes expose the same canonical identity used by runtime
activation.

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

## Option Metadata

Options can carry host-facing editor hints through typed attributes. Use
`OptionDesignMetadataAttributes.Create(...)` when a provider needs to describe
an option's section, importance, editor kind, syntax, or related resource. These
attributes are metadata only; hosts still choose their forms, grouping,
validation UI, and expression editors.

## Resource Metadata

Resources describe host-owned dependencies such as keyed clients, stores,
expression engines, or clocks. They are metadata only; this package does not
register, resolve, validate, or dispose those resources.

Use `ResourceDesignMetadataAttributes.CreateHostOwned(...)` when a provider
needs to describe a host-owned resource picker. The shared attribute names cover
resource ownership, picker kind, key pattern, related option, and conditional
requiredness. They are only hints for hosts; the host still owns resource
catalogs, keyed registrations, secrets, lifetimes, and disposal.

## Port Metadata

Normal ports are typed message ports. A component with an
`IFlowSignalTarget` input can add `PortDesignMetadataAttributes.CreateSignal()`
to that input's attributes. Hosts may then render or validate the input as a
trace-identity signal whose incoming payload type is irrelevant. This remains
descriptive metadata; the Designer package does not link or deliver signals.
`CreateSignalMap()` provides the same hint using the strongly typed attribute
map required by an already constructed `PortDesignMetadata` record.

Use `ComponentResourcePickerHints.Create(...)` when a host wants an ordered view
of the host-owned picker hints from one component metadata item or a validated
catalog. The helper filters to host-owned resources with picker kinds, preserves
resource order within each component, and parses conditional option names such
as `predicate,engine` into typed option names. It does not enumerate,
validate, resolve, create, or dispose host resources.

## Application Persistence

Load and save require exact canonical component and resource types. Loads return
structured validation diagnostics for unknown identities, while serialization
preserves canonical type names. The catalog projects package-authored metadata into the canonical
host surface by adding the traced `Events` output and the optional semantic
`processing` profile picker. It omits legacy `name`, `boundedCapacity`,
`maxDegreeOfParallelism`, and `ensureOrdered` options from normal editing; raw
provider metadata remains available for compatibility and convention checks.

`DesignerApplicationPersistence` reads and writes the canonical two-section
application document. It delegates JSON shape to `ApplicationDefinitionJson`,
address parsing to `ApplicationAddress`, and semantic link validation to
`ApplicationLinkCompiler`. A host therefore displays the same link diagnostics
that runtime activation uses.

Loaded links retain whether they were declared on an input or output property.
New workflow links created with `DesignerApplicationLink.Create(...)` default
to source-side output declarations. System output links necessarily use the
target input because `System.Events.Output` and `System.Diagnostics.Output`
have no component property on which to persist a declaration. Malformed link
properties remain raw component properties and round-trip unchanged.

```csharp
using FluxFlow.Components.Designer.Persistence;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;

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

Provide an `ApplicationLinkCompiler` configured with the host's expression
engine when conditional links should be compiled during Designer validation.
Without one, the runtime compiler reports its normal missing-condition-engine
diagnostic while persistence still preserves the condition text.

```json
{
  "Resources": {
    "Messaging": {
      "Client1": {
        "Type": "mqtt.client",
        "Broker": "Resources.Messaging.Broker1"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Read": {
        "Type": "mqtt.receive",
        "Client": "Resources.Messaging.Client1",
        "Output": "Validate.Input"
      },
      "Validate": {
        "Type": "validation.json"
      }
    }
  }
}
```

## Example

```csharp
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;

var metadata = new ComponentDesignMetadata
{
    Type = new ComponentType("sample.transform"),
    DisplayName = new ComponentMetadataText("Sample Transform"),
    Category = new ComponentCategory("Samples"),
    Summary = new ComponentMetadataText("Transforms a sample value."),
    IconKey = new ComponentIconKey("transform"),
    PreferredNodeName = new ComponentPreferredNodeName("transform"),
    SuggestedEditorWidth = 420,
    Options =
    [
        new OptionDesignMetadata
        {
            Name = new ComponentOptionName("expression"),
            Kind = OptionValueKind.Expression,
            DisplayName = new ComponentMetadataText("Expression"),
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
                    DisplayName = new ComponentMetadataText("Strict")
                },
                new OptionChoiceMetadata
                {
                    Value = new ComponentOptionChoiceValue("relaxed"),
                    DisplayName = new ComponentMetadataText("Relaxed")
                }
            ]
        }
    ],
    Resources =
    [
        new ResourceDesignMetadata
        {
            Name = new ComponentResourceName("engine"),
            DisplayName = new ComponentMetadataText("Engine"),
            Order = 0,
            ValueType = new ComponentValueTypeHint("IExpressionEngine"),
            IsRequired = true,
            Attributes = ResourceDesignMetadataAttributes.CreateHostOwnedMap(
                ResourceDesignMetadataAttributeValues.ExpressionEngine)
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
    Attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
    {
        [new ComponentAttributeName("shape")] = new ComponentAttributeValue("transform")
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
    .AddOption(
        "label",
        OptionValueKind.Text,
        attributes: OptionDesignMetadataAttributes.Create(
            section: "General",
            importance: OptionDesignMetadataAttributeValues.Primary,
            editor: OptionDesignMetadataAttributeValues.Text))
    .AddResource(
        "engine",
        order: 0,
        valueType: "IExpressionEngine",
        isRequired: true,
        attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
            ResourceDesignMetadataAttributeValues.ExpressionEngine))
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
returns display and editing metadata for their public component type constants.
Hosts compose those providers with the immutable `ComponentCatalog` into a
`ComponentDesignMetadataCatalog` to build palettes, editors, validation views,
and generated documentation without duplicating package descriptors. The
component descriptor remains authoritative for type identity, port types,
cardinality, processing capabilities, and activation.
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
    .AddMappingComponents()
    .AddHttpComponents()
    .AddComponentDesignMetadataCatalog();
```

Package family extensions register exactly one package-owned metadata provider.
Hosts only call `AddComponentDesignMetadataCatalog()` after selecting their
families; they do not construct a provider-only catalog that bypasses component
descriptors.

Hosts can layer app-specific behavior, localization, resource pickers, and
rendering hints separately from package-owned metadata.

## Boundaries

This package defines metadata, editor-facing persistence projections, and
catalog helpers. `FluxFlow.Composition` remains the only JSON, address, and link
validation model. Hosts decide how models are rendered, localized, combined
with their design system, and supplied with expression-engine validation.

The package does not depend on `FluxFlow.Engine`, execute workflows, own or
resolve resources, build service providers, implement hot reload, or reference
transport adapters.

## Composition

This package does not expose standalone workflow nodes or component factories.
It consumes canonical `FluxFlow.Composition` definitions and registries only to
project editable documents and reuse runtime link diagnostics.
