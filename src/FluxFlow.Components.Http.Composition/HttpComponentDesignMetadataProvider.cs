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
                summary: "Sends HTTP request messages through a host-owned HttpClient and emits response messages.",
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
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: Defaults.BoundedCapacity,
                min: 1)
            .AddOption(
                "maxResponseBodyBytes",
                OptionValueKind.Number,
                displayName: "Max Response Body Bytes",
                helperText: "Maximum response body bytes read before truncating.",
                defaultValue: Defaults.MaxResponseBodyBytes,
                min: 1)
            .AddOption(
                "treatNonSuccessStatusAsError",
                OptionValueKind.Boolean,
                displayName: "Treat Non-Success Status As Error",
                helperText: "Emit non-2xx HTTP responses through Errors instead of Output.",
                defaultValue: Defaults.TreatNonSuccessStatusAsError)
            .AddOption(
                "maxDegreeOfParallelism",
                OptionValueKind.Number,
                displayName: "Max Degree Of Parallelism",
                helperText: "Maximum concurrent HTTP sends handled by the node.",
                defaultValue: Defaults.MaxDegreeOfParallelism,
                min: 1)
            .AddOption(
                "defaultTimeoutMilliseconds",
                OptionValueKind.Number,
                displayName: "Default Timeout Milliseconds",
                helperText: "Optional per-request timeout used when the input message omits one.",
                min: 1);

    private static void AddClientResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                HttpCompositionResourceNames.Client,
                displayName: "Client",
                order: 0,
                summary: "Keyed HttpClient used to send request messages.",
                valueType: nameof(HttpClient),
                isRequired: true)
            .AddResource(
                HttpCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic request timeouts and diagnostics.",
                valueType: nameof(TimeProvider));

    private static void AddClientPorts(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                HttpCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "HTTP request message.",
                valueType: nameof(HttpRequestInput),
                isPrimary: true)
            .AddOutputPort(
                HttpCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "HTTP response message.",
                valueType: nameof(HttpResponseOutput),
                isPrimary: true);
}
