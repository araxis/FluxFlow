namespace FluxFlow.Components.Timers.Composition;

public static partial class TimersComponentDefinition
{
    public static class Options
    {
        public const string Name = "name";
        public const string Interval = "interval";
        public const string InitialDelay = "initialDelay";
        public const string EmitImmediately = "emitImmediately";
        public const string MaxTicks = "maxTicks";
        public const string BoundedCapacity = "boundedCapacity";
        public const string Cron = "cron";
        public const string Delay = "delay";
        public const string EmitFirstImmediately = "emitFirstImmediately";
        public const string QuietPeriod = "quietPeriod";
    }

    public static class Types
    {
        public const string Interval = "timer.interval";
        public const string Schedule = "timer.schedule";
        public const string Delay = "timer.delay";
        public const string Throttle = "timer.throttle";
        public const string Debounce = "timer.debounce";
    }

    public static class Ports { public const string Input = "Input"; public const string Output = "Output"; }
    public static class Resources { public const string Clock = "clock"; }
}
