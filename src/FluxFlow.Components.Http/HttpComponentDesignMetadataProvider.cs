using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Http;

public sealed class HttpComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        new()
        {
            Type = HttpComponentTypes.Request,
            DisplayName = "HTTP Request",
            Category = "HTTP",
            Summary = "Sends HTTP requests from explicit request inputs.",
            IconKey = "http",
            PreferredNodeName = "httpRequest",
            SuggestedEditorWidth = 520,
            Options =
            [
                Number("defaultTimeoutMilliseconds", "Default timeout ms", 30000, 1),
                Number("maxResponseBodyBytes", "Max response body bytes", 1048576, 1),
                Boolean("followRedirects", "Follow redirects", true),
                Boolean("treatNonSuccessStatusAsError", "Treat non-success status as error", false),
                Number("boundedCapacity", "Capacity", 128, 1)
            ],
            Ports =
            [
                Port(HttpComponentPorts.Input, PortDirection.Input, "HttpRequestInput", true),
                Port(HttpComponentPorts.Output, PortDirection.Output, "HttpResponseOutput", true, 1),
                Port(HttpComponentPorts.Errors, PortDirection.Output, "HttpErrorOutput", false, 2)
            ]
        }
    ];

    private static OptionDesignMetadata Number(string name, string displayName, object defaultValue, double min) => new()
    {
        Name = name,
        Kind = OptionValueKind.Number,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        Min = min
    };

    private static OptionDesignMetadata Boolean(string name, string displayName, bool defaultValue) => new()
    {
        Name = name,
        Kind = OptionValueKind.Boolean,
        DisplayName = displayName,
        DefaultValue = defaultValue
    };

    private static PortDesignMetadata Port(string name, PortDirection direction, string valueType, bool primary, int order = 0) => new()
    {
        Name = new PortName(name),
        Direction = direction,
        ValueType = valueType,
        IsPrimary = primary,
        Order = order
    };
}
