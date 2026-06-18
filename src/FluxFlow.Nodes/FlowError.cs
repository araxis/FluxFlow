namespace FluxFlow.Nodes;

/// <summary>
/// Uniform error item every node emits on its <c>Errors</c> port. A consumer (a
/// logger, an alerter) can subscribe to any node's errors without knowing the
/// node type. Domain detail goes in <see cref="Context"/>.
/// </summary>
public sealed record FlowError
{
    public DateTimeOffset Timestamp { get; init; }

    public int Code { get; init; }

    public required string Message { get; init; }

    public string? Context { get; init; }

    public Exception? Exception { get; init; }
}
