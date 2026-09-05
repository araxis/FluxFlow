namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionReadRequest
{
    private string _sessionId = string.Empty;

    public required string SessionId
    {
        get => _sessionId;
        init => _sessionId = SessionContractNormalization.NormalizeRequired(value);
    }

    public long? StartSequence { get; init; }
    public int? MaxMessages { get; init; }
}
