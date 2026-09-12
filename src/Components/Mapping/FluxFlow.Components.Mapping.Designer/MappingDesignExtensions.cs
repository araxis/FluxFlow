using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Mapping.Composition;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Mapping.Designer;

public static class MappingDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddMappingDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddDesignMetadata(MappingComponentDefinition.Types.Mapper, Describe);
    }

    private static ComponentDesignMetadata Describe(ComponentDesignMetadata metadata)
    {
        var options = new Dictionary<string, Func<OptionDesignMetadata, OptionDesignMetadata>>(StringComparer.Ordinal)
        {
            [MappingComponentDefinition.Options.Expression] = option => option with
            {
                DisplayName = new("Expression"),
                HelperText = new("Expression evaluated for each input message."),
                Kind = OptionValueKind.Expression,
                Attributes = OptionAttributes("Mapping", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Expression, OptionDesignMetadataAttributeValues.Expression, MappingComponentDefinition.Resources.Engine)
            },
            [MappingComponentDefinition.Options.ExpressionId] = option => option with
            {
                DisplayName = new("Expression ID"),
                HelperText = new("Optional diagnostic identifier emitted with mapper diagnostics."),
                Kind = OptionValueKind.Text,
                Attributes = OptionAttributes("Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text)
            },
            [MappingComponentDefinition.Options.ExpressionName] = option => option with
            {
                DisplayName = new("Expression Name"),
                HelperText = new("Optional diagnostic name emitted with mapper diagnostics."),
                Kind = OptionValueKind.Text,
                Attributes = OptionAttributes("Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text)
            },
            [MappingComponentDefinition.Options.InputType] = option => option with
            {
                DisplayName = new("Input Type"),
                HelperText = new("Optional semantic type name for the JSON input."),
                Kind = OptionValueKind.Text,
                DefaultValue = MapperOptions.ObjectTypeName,
                Attributes = OptionAttributes("Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text)
            },
            [MappingComponentDefinition.Options.OutputType] = option => option with
            {
                DisplayName = new("Output Type"),
                HelperText = new("Optional semantic type name for the mapped JSON value."),
                Kind = OptionValueKind.Text,
                DefaultValue = MapperOptions.ObjectTypeName,
                Attributes = OptionAttributes("Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text)
            },
            [MappingComponentDefinition.Options.BoundedCapacity] = option => option with
            {
                DisplayName = new("Bounded Capacity"),
                HelperText = new("Capacity used for bounded processing and reliable normal-data output."),
                Kind = OptionValueKind.Number,
                DefaultValue = 128,
                Min = 1,
                Attributes = OptionAttributes("Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number)
            }
        };
        var ports = new Dictionary<string, (string Group, int Order, string Summary, bool Primary)>(StringComparer.Ordinal)
        {
            [MappingComponentDefinition.Ports.Input] = ("Values", 0, "Immutable value to map.", true),
            [MappingComponentDefinition.Ports.Output] = ("Results", 1, "Mapped JSON value; mapping failures use the message error case.", true),
            [MappingComponentDefinition.Ports.Events] = ("Diagnostics", 2, "Best-effort mapping diagnostics.", false)
        };
        var resources = new Dictionary<string, (string Label, int Order, string Summary, string Picker, string Key)>(StringComparer.Ordinal)
        {
            [MappingComponentDefinition.Resources.Engine] = ("Engine", 0, "Keyed expression engine service used to evaluate mapper expressions.", ResourceDesignMetadataAttributeValues.ExpressionEngine, "Resources.{name}"),
            [MappingComponentDefinition.Resources.ContextFactory] = ("Context Factory", 1, "Optional keyed mapping context factory for custom expression variables.", ResourceDesignMetadataAttributeValues.ContextFactory, "Resources.{name}"),
            [MappingComponentDefinition.Resources.Clock] = ("Clock", 2, "Optional keyed clock for deterministic mapper diagnostics.", ResourceDesignMetadataAttributeValues.Clock, "Resources.{name}")
        };
        return metadata with
        {
            DisplayName = new("Mapper"),
            Category = new("Mapping"),
            Summary = new("Maps schema-less JSON inputs and carries mapping failures as workflow errors."),
            IconKey = new("map"),
            PreferredNodeName = new("map"),
            SuggestedEditorWidth = 420,
            Options = metadata.Options.Select(option => options.TryGetValue(option.Name.Value, out var describe)
                ? describe(option) : option).ToArray(),
            Ports = metadata.Ports.Select(port => ports.TryGetValue(port.Name.Value, out var hint)
                ? port with
                {
                    Group = new(hint.Group), Order = hint.Order,
                    Summary = new(hint.Summary), IsPrimary = hint.Primary
                } : port).ToArray(),
            Resources = metadata.Resources.Select(resource => resources.TryGetValue(resource.Name.Value, out var hint)
                ? resource with
                {
                    DisplayName = new(hint.Label), Order = hint.Order, Summary = new(hint.Summary),
                    Attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
                    {
                        [new(ResourceDesignMetadataAttributeNames.Ownership)] = new(ResourceDesignMetadataAttributeValues.HostOwned),
                        [new(ResourceDesignMetadataAttributeNames.PickerKind)] = new(hint.Picker),
                        [new(ResourceDesignMetadataAttributeNames.KeyPattern)] = new(hint.Key)
                    }
                } : resource).ToArray()
        };
    }

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> OptionAttributes(
        string section, string importance, string? editor, string? syntax = null, string? relatedResource = null)
    {
        var attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
        {
            [new(OptionDesignMetadataAttributeNames.Section)] = new(section),
            [new(OptionDesignMetadataAttributeNames.Importance)] = new(importance)
        };
        if (editor is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.Editor), new(editor));
        if (syntax is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.Syntax), new(syntax));
        if (relatedResource is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.RelatedResource), new(relatedResource));
        return attributes;
    }
}
