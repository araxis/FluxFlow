namespace FluxFlow.Core.Mapping;

public interface IFlowExpressionEngine
{
    string Name { get; }

    object? Evaluate(string expression, FlowMapContext context, Type resultType);

    T Evaluate<T>(string expression, FlowMapContext context)
        => (T)Evaluate(expression, context, typeof(T))!;

    /// <summary>
    /// Compiles an expression once for repeated evaluation. The default defers
    /// to <see cref="Evaluate{T}"/> on each call; engines that can pre-parse an
    /// expression should override this so parsing happens once at build time
    /// rather than per message. Callers should compile at build time and reuse
    /// the returned function.
    /// </summary>
    Func<FlowMapContext, T> Compile<T>(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        return context => Evaluate<T>(expression, context);
    }

    /// <summary>Compiles with declared evaluation parameter types; values are supplied per call.</summary>
    Func<FlowMapContext, T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> parameterTypes)
        => Compile<T>(expression);
}
