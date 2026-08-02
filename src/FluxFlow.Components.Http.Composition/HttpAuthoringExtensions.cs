using FluxFlow.Components.Http.Contracts;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Http.Composition;

public static class HttpAuthoringExtensions
{
    public static InputOutputComponentHandle<HttpClientRequest, HttpResponseResult> AddHttpRequest(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<HttpRequestComponentBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var component = workflow.AddComponent(
            name,
            HttpComponentDefinition.Types.Client,
            definition =>
            {
                var builder = new HttpRequestComponentBuilder();
                configure(builder);
                builder.Apply(definition);
            });
        return new(component, HttpComponentDefinition.Ports.Input, HttpComponentDefinition.Ports.Output);
    }

    public static WorkflowDefinitionBuilder AddHttpRequest(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<HttpRequestComponentBuilder> configure,
        out InputOutputComponentHandle<HttpClientRequest, HttpResponseResult> request)
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
