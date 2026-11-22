using FluxFlow.Core.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Expressions;

public static class ExpressionCompilationExtensions
{
    public static IFlowExpressionEngine GetExpressionEngine(this IServiceProvider services, string? name)
        => (name is null ? services.GetService<IFlowExpressionEngine>()
            : services.GetKeyedService<IFlowExpressionEngine>(name))
            ?? throw new InvalidOperationException(name is null
                ? "No default expression engine is registered."
                : $"Expression engine '{name}' is not registered.");

    public static Func<FlowMapContext, T> CompileExpression<T>(this IFlowExpressionEngine engine,
        string expression, IReadOnlyDictionary<string, Type>? parameterTypes = null)
        => (parameterTypes is null ? engine.Compile<T>(expression) : engine.Compile<T>(expression, parameterTypes))
            ?? throw new InvalidOperationException($"Expression engine '{engine.Name}' returned no compiled expression.");
}
