using FluxFlow.Components.State.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.State.Nodes;

internal static class StateReducerNodeFactory
{
    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        StateComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = StateOptionsReader.ReadReducerOptions(context.Definition);
        var expressionEngine = componentOptions.ResolveExpressionEngine(options.Engine);

        // Compile the reducer (and optional key) expression once at build time so
        // parsing happens here rather than per message.
        var reducerExpr = expressionEngine.Compile<object?>(options.Reducer);
        var keyExpr = string.IsNullOrWhiteSpace(options.KeyExpression)
            ? null
            : expressionEngine.Compile<string?>(options.KeyExpression!);
        var reducer = new CompiledFlowReducer(reducerExpr, keyExpr);

        var node = new StateReducerNode(options, reducer, expressionEngine.Name, componentOptions.Clock);

        return context.CreateNode(node)
            .Input(StateComponentPorts.Input, node.Input)
            .Output(StateComponentPorts.Output, node.Output)
            .Output(StateComponentPorts.Errors, node.Errors)
            .Build();
    }
}
