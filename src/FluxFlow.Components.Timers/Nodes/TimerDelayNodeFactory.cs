using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Runtime;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FluxFlow.Components.Timers.Nodes;

internal static class TimerDelayNodeFactory
{
    private static readonly MethodInfo CreateDelayMethod = GetMethod(nameof(CreateDelayTyped));

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        TimerComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var settings = TimerOptionsReader.ReadDelaySettings(context.Definition);
        var inputType = componentOptions.ResolveType(settings.InputType);

        try
        {
            var method = CreateDelayMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(null, [context, settings])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static RuntimeNode CreateDelayTyped<TInput>(
        RuntimeNodeFactoryContext context,
        TimerDelaySettings settings)
    {
        var node = new TimerDelayNode<TInput>(settings);

        return context.CreateNode(node)
            .Input(TimerComponentPorts.Input, node.Input)
            .Output(TimerComponentPorts.Output, node.Output)
            .Build();
    }

    private static MethodInfo GetMethod(string name)
        => typeof(TimerDelayNodeFactory).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find timer factory method '{name}'.");
}
