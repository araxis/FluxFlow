using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Timers;

public static class TimerComponentTypes
{
    public static readonly NodeType Debounce = new("timer.debounce");
    public static readonly NodeType Delay = new("timer.delay");
    public static readonly NodeType Interval = new("timer.interval");
    public static readonly NodeType Schedule = new("timer.schedule");
    public static readonly NodeType Throttle = new("timer.throttle");
}
