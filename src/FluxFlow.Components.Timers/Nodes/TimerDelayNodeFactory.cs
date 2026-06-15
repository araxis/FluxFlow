using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Runtime;
using System.Reflection;

namespace FluxFlow.Components.Timers.Nodes;

internal static class TimerDelayNodeFactory
{
    private static readonly MethodInfo CreateDelayMethod =
        TimerTypedNodeFactory.GetCreateMethod(typeof(TimerDelayNodeFactory), nameof(CreateDelayTyped));

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        TimerComponentOptions componentOptions)
        => TimerTypedNodeFactory.Create(
            context,
            componentOptions,
            TimerOptionsReader.ReadDelaySettings,
            CreateDelayMethod);

    private static RuntimeNode CreateDelayTyped<TInput>(
        RuntimeNodeFactoryContext context,
        TimerDelaySettings settings,
        TimeProvider clock)
    {
        var node = new TimerDelayNode<TInput>(settings, clock);

        return context.CreateNode(node)
            .Input(TimerComponentPorts.Input, node.Input)
            .Output(TimerComponentPorts.Output, node.Output)
            .Output(TimerComponentPorts.Errors, node.Errors)
            .Build();
    }

}
