namespace FluxFlow.Components.Expectations.Composition;

public static partial class ExpectationsComponentDefinition
{
    public static class Options
    {
        public const string Kind = "kind";
        public const string Name = "name";
        public const string Filter = "filter";
        public const string TimeoutMilliseconds = "timeoutMilliseconds";
        public const string MaxObservedEvents = "maxObservedEvents";
        public const string MaxPreviewChars = "maxPreviewChars";
        public const string BoundedCapacity = "boundedCapacity";
    }

    public static class Types { public const string EventExpectation = "event.expect"; }
    public static class Ports { public const string Input = "Input"; public const string Output = "Output"; public const string Events = "Events"; }
    public static class Resources { public const string Clock = "clock"; }
}
