using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Runtime;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FluxFlow.Components.Timers.Nodes;

internal static class TimerDebounceNodeFactory
{
    private static readonly MethodInfo CreateDebounceMethod = GetMethod(nameof(CreateDebounceTyped));

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        TimerComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var settings = TimerOptionsReader.ReadDebounceSettings(context.Definition);
        var inputType = componentOptions.ResolveType(settings.InputType);

        try
        {
            var method = CreateDebounceMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(null, [context, settings])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static RuntimeNode CreateDebounceTyped<TInput>(
        RuntimeNodeFactoryContext context,
        TimerDebounceSettings settings)
    {
        var node = new TimerDebounceNode<TInput>(settings);

        return context.CreateNode(node)
            .Input(TimerComponentPorts.Input, node.Input)
            .Output(TimerComponentPorts.Output, node.Output)
            .Build();
    }

    private static MethodInfo GetMethod(string name)
        => typeof(TimerDebounceNodeFactory).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find timer factory method '{name}'.");
}
