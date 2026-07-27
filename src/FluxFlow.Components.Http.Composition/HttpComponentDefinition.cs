using System.Net.Http;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;

namespace FluxFlow.Components.Http.Composition;

public static partial class HttpComponentDefinition
{
    private static readonly HttpClientNodeOptions Defaults = HttpClientNodeOptions.Default;

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        => [CreateClientMetadata()];

    private static ComponentDesignMetadata CreateClientMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(HttpComponentDefinition.Types.Client)
            .WithDisplay(
                displayName: "HTTP Client",
                category: "HTTP",
                summary: "Sends FlowContent HTTP requests through a host-owned HttpClient and emits response or error results.",
                iconKey: "send",
                preferredNodeName: "httpClient",
                suggestedEditorWidth: 420);

        AddClientOptions(builder);
        AddClientResources(builder);
        AddClientPorts(builder);

        return builder.Build();
    }

    private static void AddClientOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(OptionDesignMetadataFactory.BoundedCapacity(Defaults.BoundedCapacity))
            .AddOption(
                Options.MaxResponseBodyBytes,
                OptionValueKind.Number,
                displayName: "Max Response Body Bytes",
                helperText: "Maximum response body bytes read before truncating.",
                defaultValue: Defaults.MaxResponseBodyBytes,
                min: 1,
                attributes: OptionAttributes(
                    "Limits",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.TreatNonSuccessStatusAsError,
                OptionValueKind.Boolean,
                displayName: "Treat Non-Success Status As Error",
                helperText: "Return non-2xx HTTP responses as error results instead of response results.",
                defaultValue: Defaults.TreatNonSuccessStatusAsError,
                attributes: OptionAttributes(
                    "Response",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.MaxDegreeOfParallelism,
                OptionValueKind.Number,
                displayName: "Max Degree Of Parallelism",
                helperText: "Maximum concurrent HTTP sends handled by the node.",
                defaultValue: Defaults.MaxDegreeOfParallelism,
                min: 1,
                attributes: OptionAttributes(
                    "Runtime",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.DefaultTimeoutMilliseconds,
                OptionValueKind.Number,
                displayName: "Default Timeout Milliseconds",
                helperText: "Optional per-request timeout used when the input message omits one.",
                min: 1,
                attributes: OptionAttributes(
                    "Timeouts",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number));

    private static void AddClientResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(ResourceDesignMetadataFactory.HostOwned(
                HttpComponentDefinition.Resources.Client,
                ResourceDesignMetadataAttributeValues.Client,
                "Client",
                0,
                "Keyed HttpClient used to send request messages.",
                nameof(HttpClient),
                isRequired: true,
                keyPattern: "http-client:{name}"))
            .AddResource(ResourceDesignMetadataFactory.Clock(
                HttpComponentDefinition.Resources.Clock,
                1,
                "Optional keyed clock for deterministic request timeouts and diagnostics."));

    private static IReadOnlyDictionary<string, string> OptionAttributes(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(
            section: section,
            importance: importance,
            editor: editor);

    private static void AddClientPorts(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                HttpComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: "HTTP request message.",
                valueType: nameof(HttpClientRequest),
                isPrimary: true)
            .AddOutputPort(
                HttpComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: "HTTP response or error result.",
                valueType: nameof(HttpResponseResult),
                isPrimary: true);


    public static class Options
    {
        public const string BoundedCapacity = "boundedCapacity";
        public const string MaxResponseBodyBytes = "maxResponseBodyBytes";
        public const string TreatNonSuccessStatusAsError = "treatNonSuccessStatusAsError";
        public const string MaxDegreeOfParallelism = "maxDegreeOfParallelism";
        public const string DefaultTimeoutMilliseconds = "defaultTimeoutMilliseconds";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Client =>
            [
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<int>(Options.MaxResponseBodyBytes),
                ComponentOptions.Metadata<bool>(Options.TreatNonSuccessStatusAsError),
                ComponentOptions.Metadata<int>(Options.MaxDegreeOfParallelism),
                ComponentOptions.Metadata<int?>(Options.DefaultTimeoutMilliseconds)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Client =>
            [
                ComponentResources.Metadata<HttpClient>(Resources.Client, isRequired: true),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Client = "http.request";
    }

    public static class Ports
    {
        public const string Input = "Input";
    
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Client = "client";
    
        public const string Clock = "clock";
    }
}
