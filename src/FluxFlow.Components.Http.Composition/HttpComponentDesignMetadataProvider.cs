using System.Net.Http;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;

namespace FluxFlow.Components.Http.Composition;

public sealed class HttpComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private static readonly HttpClientNodeOptions Defaults = HttpClientNodeOptions.Default;

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateClientMetadata()];

    private static ComponentDesignMetadata CreateClientMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(HttpCompositionNodeTypes.Client)
            .WithDisplay(
                displayName: "HTTP Client",
                category: "HTTP",
                summary: "Sends FlowContent HTTP requests through a host-owned HttpClient and emits response or error results.",
                iconKey: "send",
                preferredNodeName: "httpClient",
                suggestedEditorWidth: 420)
            .AddAttribute(ComponentDesignMetadataAttributeNames.Aliases, string.Join(',', HttpCompositionNodeTypes.ClientDescriptor.Aliases));

        AddClientOptions(builder);
        AddClientResources(builder);
        AddClientPorts(builder);

        return builder.Build();
    }

    private static void AddClientOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(OptionDesignMetadataFactory.BoundedCapacity(Defaults.BoundedCapacity))
            .AddOption(
                "maxResponseBodyBytes",
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
                "treatNonSuccessStatusAsError",
                OptionValueKind.Boolean,
                displayName: "Treat Non-Success Status As Error",
                helperText: "Return non-2xx HTTP responses as error results instead of response results.",
                defaultValue: Defaults.TreatNonSuccessStatusAsError,
                attributes: OptionAttributes(
                    "Response",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "maxDegreeOfParallelism",
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
                "defaultTimeoutMilliseconds",
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
                HttpCompositionResourceNames.Client,
                ResourceDesignMetadataAttributeValues.Client,
                "Client",
                0,
                "Keyed HttpClient used to send request messages.",
                nameof(HttpClient),
                isRequired: true,
                keyPattern: "http-client:{name}"))
            .AddResource(ResourceDesignMetadataFactory.Clock(
                HttpCompositionResourceNames.Clock,
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
                HttpCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "HTTP request message.",
                valueType: nameof(HttpClientRequest),
                isPrimary: true)
            .AddOutputPort(
                HttpCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "HTTP response or error result.",
                valueType: nameof(HttpResponseResult),
                isPrimary: true);
}
