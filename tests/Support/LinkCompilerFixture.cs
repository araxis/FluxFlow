using FluxFlow.Composition;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Testing;

// Supplies isolated port surfaces to compiler tests. No factories or runtime registration.
internal sealed class LinkCompilerFixture(
    IReadOnlyDictionary<string, ComponentInstance> surfaces,
    IFlowExpressionEngine? conditionEngine = null,
    IEnumerable<ApplicationSystemOutputMetadata>? systemOutputs = null,
    Func<string, IFlowExpressionEngine?>? resolveConditionEngine = null)
{
    public ApplicationLinkCompilationResult Compile(ApplicationDefinition definition) => Compile(new ApplicationSetup(definition));
    public ApplicationLinkCompilationResult Compile(ApplicationSetup setup)
    {
        var instances = new Dictionary<string, ComponentInstance>(StringComparer.Ordinal);
        foreach (var (workflow, definition) in setup.Definition.Workflows)
            foreach (var (name, component) in definition.Components)
                if (surfaces.TryGetValue(component.Type, out var surface)) instances.Add($"{workflow}.{name}", surface);
        return new ApplicationLinkCompiler(instances, conditionEngine, systemOutputs, resolveConditionEngine).Compile(setup);
    }

    public static IReadOnlyDictionary<string, ComponentInstance> Surfaces(IEnumerable<KeyValuePair<string, ComponentInstance>> values)
        => values.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    public static KeyValuePair<string, ComponentInstance> Surface(string type,
        IEnumerable<ComponentPortMetadata>? inputs = null, IEnumerable<ComponentPortMetadata>? outputs = null)
    {
        var instance = new ComponentInstance(new SurfaceNode());
        // Test deliberately malformed/type-specific surfaces independently of canonical factory binding.
        typeof(ComponentInstance).GetProperty(nameof(ComponentInstance.InputMetadata))!.SetValue(instance,
            (inputs ?? []).ToDictionary(p => p.Name, StringComparer.Ordinal));
        typeof(ComponentInstance).GetProperty(nameof(ComponentInstance.OutputMetadata))!.SetValue(instance,
            (outputs ?? []).ToDictionary(p => p.Name, StringComparer.Ordinal));
        return new(type, instance);
    }

    private sealed class SurfaceNode : IFlowNode
    {
        public Task Completion => Task.CompletedTask;
        public void Complete() { }
        public void Fault(Exception error) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
