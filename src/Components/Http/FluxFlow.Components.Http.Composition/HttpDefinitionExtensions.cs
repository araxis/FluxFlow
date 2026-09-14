using FluxFlow.Composition;
using FluxFlow.Components.Http.Options;
using FluxFlow.Components.Http.Nodes;
using FluxFlow.Components.Http.Contracts;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Composition.Definition;
using FluxFlow.Data;

namespace FluxFlow.Components.Http.Composition;

public static class HttpDefinitionExtensions
{
    public static InputOutputComponentHandle<FlowValue, FlowValue> AddHttpRequest(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<HttpRequestComponentBuilder> configure)
        => workflow.AddComponent(name, HttpComponentDefinition.Types.Client,
            HttpServiceCollectionExtensions.CreateClientNode,
            definition =>
            {
                var options = new HttpRequestComponentBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, HttpComponentDefinition.Ports.Input, HttpComponentDefinition.Ports.Output, HttpComponentDefinition.Ports.Events));

    public static WorkflowDefinitionBuilder AddHttpRequest(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<HttpRequestComponentBuilder> configure,
        out InputOutputComponentHandle<FlowValue, FlowValue> request)
    {
        request = workflow.AddHttpRequest(name, configure);
        return workflow;
    }
}

public sealed class HttpRequestComponentBuilder
{
    public int? BoundedCapacity { get; set; }
    public int? MaxResponseBodyBytes { get; set; }
    public bool? TreatNonSuccessStatusAsError { get; set; }
    public int? MaxDegreeOfParallelism { get; set; }
    public int? DefaultTimeoutMilliseconds { get; set; }
    public ResourceHandle<HttpClient>? Client { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        if (Client is null)
            throw new InvalidOperationException("HTTP request components require Client.");

        Set(definition, HttpComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        Set(definition, HttpComponentDefinition.Options.MaxResponseBodyBytes, MaxResponseBodyBytes);
        Set(definition, HttpComponentDefinition.Options.TreatNonSuccessStatusAsError, TreatNonSuccessStatusAsError);
        Set(definition, HttpComponentDefinition.Options.MaxDegreeOfParallelism, MaxDegreeOfParallelism);
        Set(definition, HttpComponentDefinition.Options.DefaultTimeoutMilliseconds, DefaultTimeoutMilliseconds);
        definition.UseResource(HttpComponentDefinition.Resources.Client, Client);
        if (Clock is not null)
            definition.UseResource(HttpComponentDefinition.Resources.Clock, Clock);
    }

    private static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}
