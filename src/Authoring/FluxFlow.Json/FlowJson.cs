using System.Text.Json;
using FluxFlow.Json.Model;
using FluxFlow.Core;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Json;

/// <summary>Builds the same Core graph from the existing portable document model.</summary>
public static class FlowJson
{
    /// <summary>Serializes the portable application document.</summary>
    public static string Serialize(ApplicationDefinition definition, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(definition, options);
    }

    public static Task<FlowApplication> BuildAsync(string json, ComponentBindings bindings,
        IFlowExpressionEngine? expressions = null, Func<string, IFlowExpressionEngine?>? resolveExpressions = null,
        Action<string, string, FlowMessage, Exception>? onRejected = null)
        => BuildAsync(JsonSerializer.Deserialize<ApplicationDefinition>(json)
            ?? throw new JsonException("An application document is required."), bindings, expressions, resolveExpressions, onRejected);

    /// <summary>Constructs a Core graph. The optional rejection handler receives source address,
    /// target address, original message and error; it is required for IsolateRejection links.
    /// It must return promptly and own any asynchronous work it starts.</summary>
    public static async Task<FlowApplication> BuildAsync(ApplicationDefinition definition, ComponentBindings bindings,
        IFlowExpressionEngine? expressions = null, Func<string, IFlowExpressionEngine?>? resolveExpressions = null,
        Action<string, string, FlowMessage, Exception>? onRejected = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(bindings);
        if (definition.Resources.Count != 0)
            throw new NotSupportedException("Resource declarations require the optional composition layer. Build the document with BuildApplicationAsync(applicationServices), or supply host dependencies through component bindings and leave Resources empty.");
        var constructors = bindings.Snapshot();
        foreach (var component in definition.Workflows.Values.SelectMany(workflow => workflow.Components.Values))
            if (!constructors.ContainsKey(component.Type))
                throw new InvalidOperationException($"Component type '{component.Type}' is not registered.");

        var app = new FlowApplication();
        var components = new Dictionary<string, FlowComponent>(StringComparer.Ordinal);
        try
        {
            foreach (var (workflowName, workflowDefinition) in definition.Workflows)
            {
                var workflow = app.AddWorkflow(workflowName);
                foreach (var (name, componentDefinition) in workflowDefinition.Components)
                {
                    var instance = constructors[componentDefinition.Type](componentDefinition)
                        ?? throw new InvalidOperationException($"Binding '{componentDefinition.Type}' returned no component.");
                    try { components.Add($"{workflowName}.{name}", workflow.Add(name, instance)); }
                    catch (Exception additionError)
                    {
                        if (!FlowTopology.HasOwner(instance))
                        {
                            try { await instance.DisposeAsync().ConfigureAwait(false); }
                            catch (Exception cleanup) { throw new AggregateException("Component addition and cleanup failed.", additionError, cleanup); }
                        }
                        throw;
                    }
                }
            }
            new DocumentLinks(definition, expressions, resolveExpressions, onRejected).Apply(app, components);
            app.Validate();
            return app;
        }
        catch (Exception error)
        {
            try { await app.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Document construction and cleanup failed.", error, cleanup); }
            throw;
        }
    }
}
