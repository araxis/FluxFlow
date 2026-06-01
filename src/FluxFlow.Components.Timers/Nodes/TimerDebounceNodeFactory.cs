using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Runtime;
using System.Reflection;

namespace FluxFlow.Components.Timers.Nodes;

internal static class TimerDebounceNodeFactory
{
    private static readonly MethodInfo CreateDebounceMethod =
        TimerTypedNodeFactory.GetCreateMethod(typeof(TimerDebounceNodeFactory), nameof(CreateDebounceTyped));

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        TimerComponentOptions componentOptions)
        => TimerTypedNodeFactory.Create(
            context,
            componentOptions,
            TimerOptionsReader.ReadDebounceSettings,
            CreateDebounceMethod);

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

}
