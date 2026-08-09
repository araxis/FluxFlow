using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Nodes;
using FluxFlow.Components.Http.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Http.Composition;

public static class HttpServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddHttp(
        this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddDesignedComponent(HttpComponents.HttpRequest);
    }

    internal static void ConfigureClient(ComponentRegistrationBuilder component)
    {
        var defaults = HttpClientNodeOptions.Default;
        component.UseProcessing(CompositionProcessingCapabilities.ParallelRelaxedOrder);
        component.WithDisplay(
            displayName: "HTTP Client",
            category: "HTTP",
            summary: "Sends FlowContent HTTP requests through a host-owned HttpClient and emits response or error results.",
            iconKey: "send",
            preferredNodeName: "httpClient",
            suggestedEditorWidth: 420);
        component
            .UseFactory(CreateClientNode)
            .HasInput(HttpComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "HTTP request message.", true)
            .HasOutput(HttpComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "HTTP response or error result.", true)
            .HasEvents(HttpComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort HTTP diagnostics.");
        AddNumberOption(component, HttpComponentDefinition.Options.BoundedCapacity, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaults.BoundedCapacity, 1, "Runtime", OptionDesignMetadataAttributeValues.Advanced);
        AddNumberOption(component, HttpComponentDefinition.Options.MaxResponseBodyBytes, "Max Response Body Bytes", "Maximum response body bytes read before truncating.", defaults.MaxResponseBodyBytes, 1, "Limits", OptionDesignMetadataAttributeValues.Primary);
        component.AddOption<bool>(
            HttpComponentDefinition.Options.TreatNonSuccessStatusAsError,
            OptionValueKind.Boolean,
            displayName: "Treat Non-Success Status As Error",
            helperText: "Return non-2xx HTTP responses as error results instead of response results.",
            defaultValue: defaults.TreatNonSuccessStatusAsError,
            section: "Response",
            importance: OptionDesignMetadataAttributeValues.Advanced);
        AddNumberOption(component, HttpComponentDefinition.Options.MaxDegreeOfParallelism, "Max Degree Of Parallelism", "Maximum concurrent HTTP sends handled by the node.", defaults.MaxDegreeOfParallelism, 1, "Runtime", OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<int?>(
            HttpComponentDefinition.Options.DefaultTimeoutMilliseconds,
            OptionValueKind.Number,
            displayName: "Default Timeout Milliseconds",
            helperText: "Optional per-request timeout used when the input message omits one.",
            min: 1,
            section: "Timeouts",
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Number);
        component.AddResource<HttpClient>(
            HttpComponentDefinition.Resources.Client,
            "Client",
            0,
            "Keyed HttpClient used to send request messages.",
            isRequired: true,
            designValueType: nameof(HttpClient),
            ownership: ResourceDesignMetadataAttributeValues.HostOwned,
            pickerKind: ResourceDesignMetadataAttributeValues.Client,
            keyPattern: "http-client:{name}");
        component.AddResource<TimeProvider>(
            HttpComponentDefinition.Resources.Clock,
            "Clock",
            1,
            "Optional keyed clock for deterministic request timeouts and diagnostics.",
            designValueType: nameof(TimeProvider),
            ownership: ResourceDesignMetadataAttributeValues.HostOwned,
            pickerKind: ResourceDesignMetadataAttributeValues.Clock,
            keyPattern: "clock:{name}");
    }

    private static void AddNumberOption(
        ComponentRegistrationBuilder component,
        string name,
        string displayName,
        string helperText,
        object defaultValue,
        double minimum,
        string section,
        string importance)
        => component.AddOption<int>(
            name,
            OptionValueKind.Number,
            displayName,
            helperText,
            defaultValue: defaultValue,
            min: minimum,
            section: section,
            importance: importance,
            editor: OptionDesignMetadataAttributeValues.Number);

    private static HttpClientNode CreateClientNode(
        ComponentActivationContext context)
    {
        var client = context.GetRequiredResource<HttpClient>(
            HttpComponentDefinition.Resources.Client);
        var options = context.BindConfiguration<HttpClientNodeOptions>();
        var clock = context.GetResource<TimeProvider>(
            HttpComponentDefinition.Resources.Clock);
        return new HttpClientNode(client, options, clock);
    }
}
