using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Runtime;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FluxFlow.Components.Timers.Nodes;

internal static class TimerThrottleNodeFactory
{
    private static readonly MethodInfo CreateThrottleMethod = GetMethod(nameof(CreateThrottleTyped));

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        TimerComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var settings = TimerOptionsReader.ReadThrottleSettings(context.Definition);
        var inputType = componentOptions.ResolveType(settings.InputType);

        try
        {
            var method = CreateThrottleMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(null, [context, settings])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static RuntimeNode CreateThrottleTyped<TInput>(
        RuntimeNodeFactoryContext context,
        TimerThrottleSettings settings)
    {
        var node = new TimerThrottleNode<TInput>(settings);

        return context.CreateNode(node)
            .Input(TimerComponentPorts.Input, node.Input)
            .Output(TimerComponentPorts.Output, node.Output)
            .Build();
    }

    private static MethodInfo GetMethod(string name)
        => typeof(TimerThrottleNodeFactory).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find timer factory method '{name}'.");
}
