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
            Type = HttpComponentTypes.Client,
            DisplayName = "HTTP Client",
            Category = "HTTP",
            Summary = "Sends an HTTP request through the host-injected HttpClient: request in, response out (broadcast), failures on the error port. All transport policy (base URL, pooling, redirects, default headers, TLS, any allow-list/SSRF handler) lives on the injected HttpClient.",
            IconKey = "http",
            PreferredNodeName = "httpClient",
            SuggestedEditorWidth = 520,
            Options =
            [
                Text("client", "Client name", "Optional name passed to the host's HttpClient resolver (for example an IHttpClientFactory client name)."),
                Number("maxResponseBodyBytes", "Max response body bytes", 1048576, 1),
                Boolean("treatNonSuccessStatusAsError", "Treat non-success status as error", false),
                Number("boundedCapacity", "Capacity", 128, 1),
                Number("maxDegreeOfParallelism", "Max parallelism", 1, 1),
                NumberOptional("defaultTimeoutMilliseconds", "Default timeout ms", 1)
            ],
            Ports =
            [
                Port(HttpComponentPorts.Input, PortDirection.Input, "HttpRequestInput", true),
                Port(HttpComponentPorts.Output, PortDirection.Output, "HttpResponseOutput", true, 1),
                Port(HttpComponentPorts.Errors, PortDirection.Output, "HttpErrorOutput", false, 2)
            ]
        }
    ];

    private static OptionDesignMetadata Text(
        string name,
        string displayName,
        string? helperText = null,
        bool required = false) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName,
        HelperText = helperText,
        IsRequired = required
    };

    private static OptionDesignMetadata Number(string name, string displayName, object defaultValue, double min) => new()
    {
        Name = name,
        Kind = OptionValueKind.Number,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        Min = min
    };

    private static OptionDesignMetadata NumberOptional(string name, string displayName, double min) => new()
    {
        Name = name,
        Kind = OptionValueKind.Number,
        DisplayName = displayName,
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
