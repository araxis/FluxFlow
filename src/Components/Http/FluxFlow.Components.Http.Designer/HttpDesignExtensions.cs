using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Http.Composition;
using FluxFlow.Components.Http.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Http.Designer;

public static class HttpDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddHttpDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddDesignMetadata(HttpComponentDefinition.Types.Client, Describe);
    }

    private static ComponentDesignMetadata Describe(ComponentDesignMetadata metadata)
    {
        var defaults = HttpClientNodeOptions.Default;
        var options = new Dictionary<string, Func<OptionDesignMetadata, OptionDesignMetadata>>(StringComparer.Ordinal)
        {
            [HttpComponentDefinition.Options.BoundedCapacity] = option => option with
            {
                DisplayName = new("Bounded Capacity"),
                HelperText = new("Capacity used for bounded processing and reliable normal-data output."),
                Kind = OptionValueKind.Number,
                DefaultValue = defaults.BoundedCapacity,
                Min = 1,
                Attributes = OptionAttributes("Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number)
            },
            [HttpComponentDefinition.Options.MaxResponseBodyBytes] = option => option with
            {
                DisplayName = new("Max Response Body Bytes"),
                HelperText = new("Maximum response body bytes read before truncating."),
                Kind = OptionValueKind.Number,
                DefaultValue = defaults.MaxResponseBodyBytes,
                Min = 1,
                Attributes = OptionAttributes("Limits", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number)
            },
            [HttpComponentDefinition.Options.TreatNonSuccessStatusAsError] = option => option with
            {
                DisplayName = new("Treat Non-Success Status As Error"),
                HelperText = new("Return non-2xx HTTP responses as error results instead of response results."),
                Kind = OptionValueKind.Boolean,
                DefaultValue = defaults.TreatNonSuccessStatusAsError,
                Attributes = OptionAttributes("Response", OptionDesignMetadataAttributeValues.Advanced, null)
            },
            [HttpComponentDefinition.Options.MaxDegreeOfParallelism] = option => option with
            {
                DisplayName = new("Max Degree Of Parallelism"),
                HelperText = new("Maximum concurrent HTTP sends handled by the node."),
                Kind = OptionValueKind.Number,
                DefaultValue = defaults.MaxDegreeOfParallelism,
                Min = 1,
                Attributes = OptionAttributes("Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number)
            },
            [HttpComponentDefinition.Options.DefaultTimeoutMilliseconds] = option => option with
            {
                DisplayName = new("Default Timeout Milliseconds"),
                HelperText = new("Optional per-request timeout used when the input message omits one."),
                Kind = OptionValueKind.Number,
                Min = 1,
                Attributes = OptionAttributes("Timeouts", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number)
            }
        };
        var ports = new Dictionary<string, (string Group, int Order, string Summary, bool Primary)>(StringComparer.Ordinal)
        {
            [HttpComponentDefinition.Ports.Input] = ("Messages", 0, "HTTP request message.", true),
            [HttpComponentDefinition.Ports.Output] = ("Results", 1, "HTTP response or error result.", true),
            [HttpComponentDefinition.Ports.Events] = ("Diagnostics", 2, "Best-effort HTTP diagnostics.", false)
        };
        var resources = new Dictionary<string, (string Label, int Order, string Summary, string Picker, string Key)>(StringComparer.Ordinal)
        {
            [HttpComponentDefinition.Resources.Client] = ("Client", 0, "Keyed HttpClient used to send request messages.", ResourceDesignMetadataAttributeValues.Client, "http-client:{name}"),
            [HttpComponentDefinition.Resources.Clock] = ("Clock", 1, "Optional keyed clock for deterministic request timeouts and diagnostics.", ResourceDesignMetadataAttributeValues.Clock, "clock:{name}")
        };
        return metadata with
        {
            DisplayName = new("HTTP Client"),
            Category = new("HTTP"),
            Summary = new("Materializes workflow values into HTTP requests and emits canonical response values."),
            IconKey = new("send"),
            PreferredNodeName = new("httpClient"),
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
