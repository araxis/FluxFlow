using FluxFlow.Mapping;

namespace FluxFlow.Components.Mapping.Contracts;

/// <summary>
/// Adapts a strongly-typed <see cref="IFlowMapContextFactory{TInput}"/> (from
/// FluxFlow.Mapping) to the node's <see cref="IMappingContextFactory"/> seam, so a
/// caller can hand <see cref="Nodes.FlowMapperNode{TInput,TOutput}"/> a typed context
/// factory directly. The payload is expected to be a <typeparamref name="TInput"/>.
/// </summary>
public sealed class TypedMappingContextFactory<TInput> : IMappingContextFactory
{
    private readonly IFlowMapContextFactory<TInput> _inner;

    public TypedMappingContextFactory(IFlowMapContextFactory<TInput> inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public FlowMapContext Create(object? input, MappingNodeContext context)
        => input is TInput typedInput
            ? _inner.Create(typedInput)
            : throw new InvalidOperationException(
                $"Mapping context expected input type '{typeof(TInput).Name}'.");
}
