using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Sessions;

public static class SessionsComponentTypes
{
    public static readonly NodeType Recorder = new("session.recorder");
    public static readonly NodeType Replay = new("session.replay");
    public static readonly NodeType Query = new("session.query");
}
