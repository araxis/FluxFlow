using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Runtime;

public sealed record RuntimeNodeFactoryContext(
    NodeName Name,
    NodeDefinition Definition,
    string? WorkflowName,
    IReadOnlyDictionary<NodeName, RuntimeNode> Resources)
{
    public bool IsResource => WorkflowName is null;

    public NodeAddress Address => WorkflowName is null
        ? new NodeAddress(WellKnownScopes.Resources, Name)
        : new NodeAddress(WorkflowName, Name);

    /// <summary>
    /// Looks up a resource node by name. Used by workflow factories that need
    /// to share resource handles with runtime nodes.
    /// </summary>
    public RuntimeNode GetResource(NodeName resourceName)
    {
        if (!Resources.TryGetValue(resourceName, out var node))
        {
            throw new InvalidOperationException(
                $"Resource '{resourceName}' was not found. Define it under 'resources' before referencing it.");
        }
        return node;
    }

    /// <summary>
    /// Resolves a resource node and returns its <see cref="RuntimeNode.Node"/>
    /// as <typeparamref name="T"/> — the resource's component-defined handle
    /// (e.g. a connection handle exposing a shared client). Throws if the
    /// resource is missing or does not provide <typeparamref name="T"/>.
    /// </summary>
    public T GetResource<T>(NodeName resourceName) where T : class
        => GetResource(resourceName).Node as T
           ?? throw new InvalidOperationException(
               $"Resource '{resourceName}' does not provide '{typeof(T).Name}'.");
}
