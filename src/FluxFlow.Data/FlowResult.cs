namespace FluxFlow.Data;

public sealed record FlowResult<T> : IFlowResult
{
    public FlowResult(
        string kind,
        T? value,
        FlowError? error,
        DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Kind = kind.Trim();
        Value = value;
        Error = error;
        Timestamp = timestamp;
    }

    public string Kind { get; }

    public T? Value { get; }

    public FlowError? Error { get; }

    public bool IsError => Error is not null;

    public DateTimeOffset Timestamp { get; }

    public static FlowResult<T> Success(string kind, T? value, DateTimeOffset timestamp)
        => new(kind, value, error: null, timestamp);

    public static FlowResult<T> Failure(
        string kind,
        FlowError error,
        DateTimeOffset timestamp,
        T? value = default)
        => new(kind, value, error ?? throw new ArgumentNullException(nameof(error)), timestamp);
}
