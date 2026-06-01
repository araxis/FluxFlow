namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionReadRequest
{
    public required string SessionId { get; init; }
    public long? StartSequence { get; init; }
    public int? MaxMessages { get; init; }
}
