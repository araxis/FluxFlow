using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Timers.Nodes;

internal static class TimerIntervalNodeFactory
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        => Create(context, new TimerComponentOptions());

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        TimerComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var settings = TimerOptionsReader.ReadIntervalSettings(context.Definition);
        var node = new TimerIntervalNode(settings, componentOptions.Clock);

        return context.CreateNode(node)
            .Output(TimerComponentPorts.Output, node.Output)
            .Output(TimerComponentPorts.Errors, node.Errors)
            .Build();
    }

}
