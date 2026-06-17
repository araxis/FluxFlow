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
            Summary = "Owns the shared HTTP client referenced by request nodes. The client is established and torn down via the host connect/disconnect API; there is no auto-connect.",
            IconKey = "http-client",
            PreferredNodeName = "httpClient",
            SuggestedEditorWidth = 460,
            Options =
            [
                Text("baseUrl", "Base URL", "Absolute base URL used to resolve relative request URLs."),
                AllowedHosts(),
                Boolean("restrictToBaseUrlOrigin", "Restrict to baseUrl origin", false),
                Boolean("followRedirects", "Follow redirects", true),
                Number("defaultTimeoutMilliseconds", "Default timeout ms", 100000, 1),
                NumberOptional("pooledConnectionLifetimeSeconds", "Pooled connection lifetime s", 1),
                NumberOptional("maxConnectionsPerServer", "Max connections per server", 1)
            ],
            Ports =
            [
                Port("Errors", PortDirection.Output, "FlowError", false)
            ]
        },
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
                Text("client", "Client name", "Name of the http.client resource to use.", true),
                Number("maxResponseBodyBytes", "Max response body bytes", 1048576, 1),
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

    private static OptionDesignMetadata AllowedHosts() => new()
    {
        Name = "allowedHosts",
        Kind = OptionValueKind.Json,
        DisplayName = "Allowed hosts",
        HelperText = "JSON array of host names. Exact match or leading-dot suffix match like \".internal.example\"."
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
