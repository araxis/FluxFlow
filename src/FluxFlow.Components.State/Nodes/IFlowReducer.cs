using FluxFlow.Mapping;

namespace FluxFlow.Components.State.Nodes;

/// <summary>
/// Holds the compiled reducer expression together with an optional compiled key
/// selector. Compilation happens once at build time in the factory; the node
/// evaluates the prepared expressions per message.
/// </summary>
internal interface IFlowReducer
{
    object? Reduce(FlowMapContext context);

    /// <summary>
    /// Resolves the key from the configured key expression, or <see langword="null"/>
    /// when no key expression was configured.
    /// </summary>
    string? ResolveKey(FlowMapContext context);
}
