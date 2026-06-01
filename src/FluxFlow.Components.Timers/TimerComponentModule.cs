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
                TimerComponentTypes.Delay,
                context => TimerDelayNodeFactory.Create(context, options)),
            new FlowNodeRegistration(
                TimerComponentTypes.Interval,
                TimerIntervalNode.Create),
            new FlowNodeRegistration(
                TimerComponentTypes.Schedule,
                TimerScheduleNode.Create),
            new FlowNodeRegistration(
                TimerComponentTypes.Throttle,
                context => TimerThrottleNodeFactory.Create(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
