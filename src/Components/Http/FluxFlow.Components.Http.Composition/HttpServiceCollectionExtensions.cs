using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Materialization;
using FluxFlow.Components.Http.Nodes;
using FluxFlow.Components.Http.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Http.Composition;

public static class HttpServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddHttp(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(HttpComponents.HttpRequest);
    }

    internal static void ConfigureClient(RuntimeComponentRegistrationBuilder component)
    {
        component.UseProcessing(CompositionProcessingCapabilities.ParallelRelaxedOrder);
        component.AddOption<int>(HttpComponentDefinition.Options.BoundedCapacity);
        component.AddOption<int>(HttpComponentDefinition.Options.MaxResponseBodyBytes);
        component.AddOption<bool>(HttpComponentDefinition.Options.TreatNonSuccessStatusAsError);
        component.AddOption<int>(HttpComponentDefinition.Options.MaxDegreeOfParallelism);
        component.AddOption<int?>(HttpComponentDefinition.Options.DefaultTimeoutMilliseconds);
        component.AddResource<HttpClient>(HttpComponentDefinition.Resources.Client, isRequired: true);
        component.AddResource<TimeProvider>(HttpComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateClientNode)
            .HasStandardPorts(new HttpClientRequestMaterializer().InputShape);
    }

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
