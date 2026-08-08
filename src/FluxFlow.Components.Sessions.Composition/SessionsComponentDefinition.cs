namespace FluxFlow.Components.Sessions.Composition;

public static partial class SessionsComponentDefinition
{
    public static class Options
    {
        public const string SessionId = "sessionId";
        public const string SessionName = "sessionName";
        public const string Notes = "notes";
        public const string Tags = "tags";
        public const string BoundedCapacity = "boundedCapacity";
        public const string Mode = "mode";
        public const string StartSequence = "startSequence";
        public const string MaxMessages = "maxMessages";
        public const string FixedIntervalMilliseconds = "fixedIntervalMilliseconds";
        public const string SpeedMultiplier = "speedMultiplier";
        public const string NamePrefix = "namePrefix";
        public const string IncludeActive = "includeActive";
        public const string IncludeCompleted = "includeCompleted";
        public const string Limit = "limit";
        public const string EmitSessionsInResult = "emitSessionsInResult";
    }

    public static class Types { public const string Recorder = "session.record"; public const string Replay = "session.replay"; public const string Query = "session.query"; }
    public static class Ports { public const string Input = "Input"; public const string Output = "Output"; public const string Events = "Events"; }
    public static class Resources { public const string Store = "store"; public const string Clock = "clock"; }
}
