using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Routing.Composition;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Routing.Designer;

public static class RoutingDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddRoutingDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(RoutingComponentDefinition.Types.Window, DescribeWindow)
            .AddDesignMetadata(RoutingComponentDefinition.Types.Correlation, DescribeCorrelation)
            .AddDesignMetadata(RoutingComponentDefinition.Types.Join, DescribeJoin);
    }

    private static ComponentDesignMetadata DescribeWindow(ComponentDesignMetadata metadata)
        => Describe(metadata, "Window", "Buffers input messages into count- or time-based windows.", "panel-top", "window", 420,
            new()
            {
                [RoutingComponentDefinition.Options.InputType] = InputType("Input Type"),
                [RoutingComponentDefinition.Options.MaxItems] = Number("Max Items", "Maximum buffered item count; set timeMilliseconds when zero.", 0, 0, "Windowing", "primary"),
                [RoutingComponentDefinition.Options.TimeMilliseconds] = Number("Time Milliseconds", "Maximum window duration in milliseconds; set maxItems when zero.", 0, 0, "Windowing", "primary"),
                [RoutingComponentDefinition.Options.EmitPartialOnCompletion] = Boolean("Emit Partial On Completion", "Emit a partial window when input completes.", true, "Windowing"),
                [RoutingComponentDefinition.Options.BoundedCapacity] = Capacity()
            },
            new()
            {
                [RoutingComponentDefinition.Ports.Input] = ("Messages", 0, "Schema-less JSON value.", true),
                [RoutingComponentDefinition.Ports.Output] = ("Messages", 1, "Count-, time-, or completion-based window; failures use the message error case.", true),
                [RoutingComponentDefinition.Ports.Events] = ("Diagnostics", 2, "Best-effort window diagnostics.", false)
            });

    private static ComponentDesignMetadata DescribeCorrelation(ComponentDesignMetadata metadata)
    {
        var options = MatchingOptions("Match keys and sides using case-sensitive comparisons.",
            "Timeout for pending correlations.", "Maximum pending correlation keys.");
        options[RoutingComponentDefinition.Options.KeyExpression] = Expression("Key Expression", "Diagnostic key expression metadata; key selection uses the keySelector resource.", RoutingComponentDefinition.Resources.KeySelector);
        options[RoutingComponentDefinition.Options.SideExpression] = Expression("Side Expression", "Diagnostic side expression metadata; side selection uses the sideSelector resource.", RoutingComponentDefinition.Resources.SideSelector);
        options[RoutingComponentDefinition.Options.InputType] = InputType("Input Type");
        options[RoutingComponentDefinition.Options.RequestSide] = Text("Request Side", "Side label treated as the request side.", "Matching", "request");
        options[RoutingComponentDefinition.Options.ResponseSide] = Text("Response Side", "Side label treated as the response side.", "Matching", "response");
        return Describe(metadata, "Correlation", "Pairs related request and response messages by host-provided key and side selectors.", "link", "correlate", 460,
            options, new()
            {
                [RoutingComponentDefinition.Ports.Input] = ("Messages", 0, "Schema-less JSON request or response value.", true),
                [RoutingComponentDefinition.Ports.Output] = ("Messages", 1, "Match or timeout outcome; failures use the message error case.", true),
                [RoutingComponentDefinition.Ports.Events] = ("Diagnostics", 2, "Best-effort correlation diagnostics.", false)
            });
    }

    private static ComponentDesignMetadata DescribeJoin(ComponentDesignMetadata metadata)
    {
        var options = MatchingOptions("Match keys using case-sensitive comparisons.",
            "Timeout for pending joins.", "Maximum pending join keys.");
        options[RoutingComponentDefinition.Options.LeftKeyExpression] = Expression("Left Key Expression", "Diagnostic left key expression metadata; left keys use the leftKeySelector resource.", RoutingComponentDefinition.Resources.LeftKeySelector);
        options[RoutingComponentDefinition.Options.RightKeyExpression] = Expression("Right Key Expression", "Diagnostic right key expression metadata; right keys use the rightKeySelector resource.", RoutingComponentDefinition.Resources.RightKeySelector);
        options[RoutingComponentDefinition.Options.LeftInputType] = InputType("Left Input Type");
        options[RoutingComponentDefinition.Options.RightInputType] = InputType("Right Input Type");
        return Describe(metadata, "Join", "Joins left and right messages by host-provided key selectors.", "combine", "join", 460,
            options, new()
            {
                [RoutingComponentDefinition.Ports.Left] = ("Messages", 0, "Schema-less JSON left value.", true),
                [RoutingComponentDefinition.Ports.Right] = ("Messages", 1, "Schema-less JSON right value.", false),
                [RoutingComponentDefinition.Ports.Output] = ("Messages", 2, "Match or timeout outcome; failures use the message error case.", true),
                [RoutingComponentDefinition.Ports.Events] = ("Diagnostics", 3, "Best-effort join diagnostics.", false)
            });
    }

    private static ComponentDesignMetadata Describe(ComponentDesignMetadata metadata,
        string label, string summary, string icon, string preferredName, int width,
        Dictionary<string, Func<OptionDesignMetadata, OptionDesignMetadata>> options,
        Dictionary<string, (string Group, int Order, string Summary, bool Primary)> ports)
    {
        var required = metadata.Resources.Where(resource => resource.IsRequired).Select(resource => resource.Name.Value).ToArray();
        var resources = new Dictionary<string, (string Label, int Order, string Summary, string Type, string Picker, string Key)>(StringComparer.Ordinal)
        {
            [RoutingComponentDefinition.Resources.Clock] = ("Clock", required.Length, "Optional keyed clock for deterministic routing timing, timeouts, and diagnostics.", nameof(TimeProvider), "clock", "clock:{name}"),
            [RoutingComponentDefinition.Resources.KeySelector] = ("Key Selector", 0, "Required keyed delegate that selects the correlation key for each input message.", "Func<JsonElement,string?>", "delegate", "delegate:{name}"),
            [RoutingComponentDefinition.Resources.SideSelector] = ("Side Selector", 1, "Required keyed delegate that selects request or response side labels.", "Func<JsonElement,string?>", "delegate", "delegate:{name}"),
            [RoutingComponentDefinition.Resources.LeftKeySelector] = ("Left Key Selector", 0, "Required keyed delegate that selects the join key for left messages.", "Func<JsonElement,string?>", "delegate", "delegate:{name}"),
            [RoutingComponentDefinition.Resources.RightKeySelector] = ("Right Key Selector", 1, "Required keyed delegate that selects the join key for right messages.", "Func<JsonElement,string?>", "delegate", "delegate:{name}")
        };
        var attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>(metadata.Attributes);
        if (required.Length > 0)
            attributes.Add(new("requiredResources"), new(string.Join(",", required)));
        return metadata with
        {
            DisplayName = new(label), Category = new("Routing"), Summary = new(summary),
            IconKey = new(icon), PreferredNodeName = new(preferredName), SuggestedEditorWidth = width,
            Attributes = attributes,
            Options = metadata.Options.Select(option => options.TryGetValue(option.Name.Value, out var describe)
                ? describe(option) : option).ToArray(),
            Ports = metadata.Ports.Select(port => ports.TryGetValue(port.Name.Value, out var hint)
                ? port with { Group = new(hint.Group), Order = hint.Order, Summary = new(hint.Summary), IsPrimary = hint.Primary }
                : port).ToArray(),
            Resources = metadata.Resources.Select(resource => resources.TryGetValue(resource.Name.Value, out var hint)
                ? resource with
                {
                    DisplayName = new(hint.Label), Order = hint.Order, Summary = new(hint.Summary),
                    ValueType = new(hint.Type),
                    Attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
                    {
                        [new(ResourceDesignMetadataAttributeNames.Ownership)] = new(ResourceDesignMetadataAttributeValues.HostOwned),
                        [new(ResourceDesignMetadataAttributeNames.PickerKind)] = new(hint.Picker),
                        [new(ResourceDesignMetadataAttributeNames.KeyPattern)] = new(hint.Key)
                    }
                } : resource).ToArray()
        };
    }

    private static Dictionary<string, Func<OptionDesignMetadata, OptionDesignMetadata>> MatchingOptions(
        string caseHelp, string timeoutHelp, string pendingHelp)
        => new(StringComparer.Ordinal)
        {
            [RoutingComponentDefinition.Options.Engine] = Text("Engine", "Diagnostic engine metadata; composition DI selection uses host-owned selector resources.", "Diagnostics"),
            [RoutingComponentDefinition.Options.ExpressionId] = Text("Expression ID", "Optional diagnostic identifier emitted with routing diagnostics.", "Diagnostics"),
            [RoutingComponentDefinition.Options.ExpressionName] = Text("Expression Name", "Optional diagnostic name emitted with routing diagnostics.", "Diagnostics"),
            [RoutingComponentDefinition.Options.CaseSensitive] = Boolean("Case Sensitive", caseHelp, true, "Matching"),
            [RoutingComponentDefinition.Options.TimeoutMilliseconds] = Number("Timeout Milliseconds", timeoutHelp, 30_000),
            [RoutingComponentDefinition.Options.MaxPending] = Number("Max Pending", pendingHelp, 1_024),
            [RoutingComponentDefinition.Options.BoundedCapacity] = Capacity()
        };

    private static Func<OptionDesignMetadata, OptionDesignMetadata> Capacity()
        => Number("Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", 128);

    private static Func<OptionDesignMetadata, OptionDesignMetadata> InputType(string label)
        => Text(label, "Diagnostic input type metadata; CLR type comes from the closed registration.", "Type Metadata", WindowRoutingOptions.ObjectTypeName);

    private static Func<OptionDesignMetadata, OptionDesignMetadata> Text(string label, string help, string section, string? defaultValue = null)
        => option => option with
        {
            DisplayName = new(label), HelperText = new(help), Kind = OptionValueKind.Text,
            DefaultValue = defaultValue, Attributes = OptionAttributes(section, "advanced", "text")
        };

    private static Func<OptionDesignMetadata, OptionDesignMetadata> Number(string label, string help,
        int defaultValue, double min = 1, string section = "Runtime", string importance = "advanced")
        => option => option with
        {
            DisplayName = new(label), HelperText = new(help), Kind = OptionValueKind.Number,
            DefaultValue = defaultValue, Min = min, Attributes = OptionAttributes(section, importance, "number")
        };

    private static Func<OptionDesignMetadata, OptionDesignMetadata> Boolean(string label, string help, bool defaultValue, string section)
        => option => option with
        {
            DisplayName = new(label), HelperText = new(help), Kind = OptionValueKind.Boolean,
            DefaultValue = defaultValue, Attributes = OptionAttributes(section, "advanced")
        };

    private static Func<OptionDesignMetadata, OptionDesignMetadata> Expression(string label, string help, string resource)
        => option => option with
        {
            DisplayName = new(label), HelperText = new(help), Kind = OptionValueKind.Expression,
            Attributes = OptionAttributes("Selection", "advanced", "expression", resource)
        };

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> OptionAttributes(
        string section, string importance, string? editor = null, string? resource = null)
    {
        var attributes = new Dictionary<ComponentAttributeName, ComponentAttributeValue>
        {
            [new(OptionDesignMetadataAttributeNames.Section)] = new(section),
            [new(OptionDesignMetadataAttributeNames.Importance)] = new(importance)
        };
        if (editor is not null) attributes.Add(new(OptionDesignMetadataAttributeNames.Editor), new(editor));
        if (resource is not null)
        {
            attributes.Add(new(OptionDesignMetadataAttributeNames.Syntax), new("expression"));
            attributes.Add(new(OptionDesignMetadataAttributeNames.RelatedResource), new(resource));
        }
        return attributes;
    }
}
