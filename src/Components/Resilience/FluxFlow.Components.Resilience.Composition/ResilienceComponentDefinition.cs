namespace FluxFlow.Components.Resilience.Composition;

public static partial class ResilienceComponentDefinition
{
    public static class Options
    {
        public const string Name = "name";
        public const string Strategy = "strategy";
        public const string InitialDelayMilliseconds = "initialDelayMilliseconds";
        public const string IncrementMilliseconds = "incrementMilliseconds";
        public const string MaximumDelayMilliseconds = "maximumDelayMilliseconds";
        public const string MaximumAttempts = "maximumAttempts";
        public const string MaximumDurationMilliseconds = "maximumDurationMilliseconds";
        public const string JitterFactor = "jitterFactor";
        public const string AttemptTimeoutMilliseconds = "attemptTimeoutMilliseconds";
        public const string Capacity = "capacity";
    }

    public static class Types { public const string Retry = "flow.retry"; }
    public static class Ports
    {
        public const string Input = "Input";
        public const string Ack = "Ack";
        public const string Nak = "Nak";
        public const string Cancel = "Cancel";
        public const string Output = "Output";
        public const string Events = "Events";
    }
    public static class Resources { public const string Clock = "Clock"; public const string Jitter = "Jitter"; }
}
