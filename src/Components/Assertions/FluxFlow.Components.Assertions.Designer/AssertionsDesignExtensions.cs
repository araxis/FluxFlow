using System.Text.Json;
using FluxFlow.Mapping;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Composition;
using Definition = FluxFlow.Components.Assertions.Composition.AssertionsComponentDefinition;
using OptionHints = FluxFlow.Components.Designer.Contracts.OptionDesignMetadataAttributeValues;

namespace FluxFlow.Components.Assertions.Designer;

public static class AssertionsDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddAssertionsDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddDesignMetadata(Definition.Types.Assertion, Describe);
    }

    private static ComponentDesignMetadata Describe(ComponentDesignMetadata metadata)
    {
        var options = new Dictionary<string, Func<OptionDesignMetadata, OptionDesignMetadata>>(StringComparer.Ordinal)
        {
            [Definition.Options.Expression] = option => DescribeOption(option, OptionValueKind.Expression,
                "Expression", "Boolean expression evaluated for each input message.", "Assertions",
                importance: OptionHints.Primary, editor: OptionHints.Expression, syntax: OptionHints.Expression, relatedResource: Definition.Resources.Engine),
            [Definition.Options.ExpressionId] = option => DescribeOption(option, OptionValueKind.Text,
                "Expression ID", "Optional diagnostic identifier emitted with assertion diagnostics.", "Diagnostics",
                editor: OptionHints.Text),
            [Definition.Options.ExpressionName] = option => DescribeOption(option, OptionValueKind.Text,
                "Expression Name", "Optional diagnostic name emitted with assertion diagnostics.", "Diagnostics",
                editor: OptionHints.Text),
            [Definition.Options.InputType] = option => DescribeOption(option, OptionValueKind.Text,
                "Input Type", "Optional semantic type name for the JSON input.", "Type Metadata",
                defaultValue: AssertionOptions.ObjectTypeName, editor: OptionHints.Text),
            [Definition.Options.BoundedCapacity] = option => DescribeOption(option, OptionValueKind.Number,
                "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", "Runtime",
                defaultValue: 128, min: 1, editor: OptionHints.Number),
            [Definition.Options.Description] = option => DescribeOption(option, OptionValueKind.Text,
                "Description", "Description included in assertion results and diagnostics.", "Results",
                defaultValue: AssertionOptions.DefaultDescription, editor: OptionHints.Text),
            [Definition.Options.FailureMessage] = option => DescribeOption(option, OptionValueKind.Text,
                "Failure Message", "Message included when the assertion fails.", "Results",
                defaultValue: AssertionOptions.DefaultFailureMessage, editor: OptionHints.Text)
        };
        var ports = new Dictionary<string, (string Group, int Order, string Summary, bool Primary)>(StringComparer.Ordinal)
        {
            [Definition.Ports.Input] = ("Messages", 0, "Immutable value to evaluate.", true),
            [Definition.Ports.Output] = ("Results", 1, "Typed assertion outcome or workflow error.", true),
            [Definition.Ports.Events] = ("Diagnostics", 2, "Best-effort assertion diagnostics.", false)
        };
        var resources = new Dictionary<string, (string Label, int Order, string Summary, string Picker, string ValueType)>(StringComparer.Ordinal)
        {
            [Definition.Resources.Engine] = ("Engine", 0, "Keyed expression engine used to evaluate assertion expressions.", ResourceDesignMetadataAttributeValues.ExpressionEngine, nameof(IFlowExpressionEngine)),
            [Definition.Resources.ContextFactory] = ("Context Factory", 1, "Optional keyed input context factory for custom expression variables.", ResourceDesignMetadataAttributeValues.ContextFactory, nameof(IFlowMapContextFactory<JsonElement>)),
            [Definition.Resources.Clock] = ("Clock", 2, "Optional keyed clock for deterministic assertion results and diagnostics.", ResourceDesignMetadataAttributeValues.Clock, nameof(TimeProvider))
        };
        return metadata with
        {
            DisplayName = new("Assertion"),
            Category = new("Assertions"),
            Summary = new("Evaluates a JSON value and returns a typed assertion result or error message."),
            IconKey = new("check-circle"),
            PreferredNodeName = new("assert"),
            SuggestedEditorWidth = 420,
            Options = metadata.Options.Select(option => options.TryGetValue(option.Name.Value, out var describe)
                ? describe(option) : option).ToArray(),
            Ports = metadata.Ports.Select(port => ports.TryGetValue(port.Name.Value, out var hint)
                ? port with { Group = new(hint.Group), Order = hint.Order, Summary = new(hint.Summary), IsPrimary = hint.Primary }
                : port).ToArray(),
            Resources = metadata.Resources.Select(resource => resources.TryGetValue(resource.Name.Value, out var hint)
                ? resource with
                {
                    DisplayName = new(hint.Label), Order = hint.Order, Summary = new(hint.Summary),
                    ValueType = new(hint.ValueType),
                    Attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
                    {
                        [new(ResourceDesignMetadataAttributeNames.Ownership)] = new(ResourceDesignMetadataAttributeValues.HostOwned),
                        [new(ResourceDesignMetadataAttributeNames.PickerKind)] = new(hint.Picker),
                        [new(ResourceDesignMetadataAttributeNames.KeyPattern)] = new("Resources.{name}")
                    }
                } : resource).ToArray()
        };
    }

    private static OptionDesignMetadata DescribeOption(OptionDesignMetadata option,
        OptionValueKind kind, string label, string helperText, string section,
        string importance = OptionHints.Advanced, object? defaultValue = null,
        double? min = null, double? max = null, string? editor = null,
        string? syntax = null, string? relatedResource = null,
        IReadOnlyList<OptionChoiceMetadata>? choices = null)
    {
        var attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
        {
            [new(OptionDesignMetadataAttributeNames.Section)] = new(section),
            [new(OptionDesignMetadataAttributeNames.Importance)] = new(importance)
        };
        if (editor is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.Editor), new(editor));
        if (syntax is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.Syntax), new(syntax));
        if (relatedResource is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.RelatedResource), new(relatedResource));
        return option with
        {
            Kind = kind, DisplayName = new(label), HelperText = new(helperText),
            DefaultValue = defaultValue, Min = min, Max = max, Attributes = attributes,
            Choices = choices ?? option.Choices
        };
    }
}
