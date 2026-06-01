using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Timers;

public sealed class TimerComponentModule : IFlowNodeModule
{
    public TimerComponentModule(TimerComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                TimerComponentTypes.Interval,
                TimerIntervalNode.Create)
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
