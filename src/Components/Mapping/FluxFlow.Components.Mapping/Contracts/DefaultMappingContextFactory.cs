using FluxFlow.Mapping;

namespace FluxFlow.Components.Mapping.Contracts;

/// <summary>
/// The default <see cref="IMappingContextFactory"/>: exposes the message payload as
/// the <c>input</c> and <c>value</c> variables on the produced <see cref="FlowMapContext"/>.
/// Used by <see cref="Nodes.FlowMapperNode{TInput,TOutput}"/> when the caller does not
/// supply its own factory.
/// </summary>
public sealed class DefaultMappingContextFactory : IMappingContextFactory
{
    public static DefaultMappingContextFactory Instance { get; } = new();

    public FlowMapContext Create(object? input, MappingNodeContext context)
        => new()
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = input,
                ["value"] = input
            }
        };
}
