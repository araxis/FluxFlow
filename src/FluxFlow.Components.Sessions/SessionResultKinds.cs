namespace FluxFlow.Components.Sessions;

public static class SessionResultKinds
{
    public const string RecordStored = "SessionRecord";
    public const string RecordFailed = "SessionRecordFailed";
    public const string ReplayRecord = "SessionReplayRecord";
    public const string ReplayFailed = "SessionReplayFailed";
    public const string QueryCompleted = "SessionQuery";
    public const string QueryFailed = "SessionQueryFailed";
}
