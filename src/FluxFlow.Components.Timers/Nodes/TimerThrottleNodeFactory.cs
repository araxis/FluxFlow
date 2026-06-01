using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Runtime;
using System.Reflection;

namespace FluxFlow.Components.Timers.Nodes;

internal static class TimerThrottleNodeFactory
{
    private static readonly MethodInfo CreateThrottleMethod =
        TimerTypedNodeFactory.GetCreateMethod(typeof(TimerThrottleNodeFactory), nameof(CreateThrottleTyped));

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        TimerComponentOptions componentOptions)
        => TimerTypedNodeFactory.Create(
            context,
            componentOptions,
            TimerOptionsReader.ReadThrottleSettings,
            CreateThrottleMethod);

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

}
