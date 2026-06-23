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

    private static ComponentDesignMetadata CreateClientMetadata() => new()
    {
        Type = new ComponentType(HttpCompositionNodeTypes.Client),
        DisplayName = "HTTP Client",
        Category = "HTTP",
        Summary = "Sends HTTP request messages through a host-owned HttpClient and emits response messages.",
        IconKey = "send",
        PreferredNodeName = "httpClient",
        SuggestedEditorWidth = 420,
        Options =
        [
            new OptionDesignMetadata
            {
                Name = "boundedCapacity",
                Kind = OptionValueKind.Number,
                DisplayName = "Bounded Capacity",
                DefaultValue = Defaults.BoundedCapacity,
                Min = 1,
                HelperText = "Maximum queued input messages."
            },
            new OptionDesignMetadata
            {
                Name = "maxResponseBodyBytes",
                Kind = OptionValueKind.Number,
                DisplayName = "Max Response Body Bytes",
                DefaultValue = Defaults.MaxResponseBodyBytes,
                Min = 1,
                HelperText = "Maximum response body bytes read before truncating."
            },
            new OptionDesignMetadata
            {
                Name = "treatNonSuccessStatusAsError",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Treat Non-Success Status As Error",
                DefaultValue = Defaults.TreatNonSuccessStatusAsError,
                HelperText = "Emit non-2xx HTTP responses through Errors instead of Output."
            },
            new OptionDesignMetadata
            {
                Name = "maxDegreeOfParallelism",
                Kind = OptionValueKind.Number,
                DisplayName = "Max Degree Of Parallelism",
                DefaultValue = Defaults.MaxDegreeOfParallelism,
                Min = 1,
                HelperText = "Maximum concurrent HTTP sends handled by the node."
            },
            new OptionDesignMetadata
            {
                Name = "defaultTimeoutMilliseconds",
                Kind = OptionValueKind.Number,
                DisplayName = "Default Timeout Milliseconds",
                Min = 1,
                HelperText = "Optional per-request timeout used when the input message omits one."
            }
        ],
        Ports =
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(HttpCompositionPortNames.Input),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = "HTTP request message.",
                ValueType = nameof(HttpRequestInput),
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(HttpCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Results",
                Order = 1,
                Summary = "HTTP response message.",
                ValueType = nameof(HttpResponseOutput),
                IsPrimary = true
            }
        ]
    };
}
