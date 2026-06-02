namespace FluxFlow.Components.Http.Contracts;

public sealed record HttpErrorOutput
{
    public DateTimeOffset Timestamp { get; init; }
    public required HttpErrorKind Kind { get; init; }
    public required string Message { get; init; }
    public int? StatusCode { get; init; }
    public string? ReasonPhrase { get; init; }
    public string? Method { get; init; }
    public string? Url { get; init; }
    public long ElapsedMilliseconds { get; init; }
}
