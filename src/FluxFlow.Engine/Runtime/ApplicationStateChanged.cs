namespace FluxFlow.Engine.Runtime;

public sealed record ApplicationStateChanged(
    ApplicationState Previous,
    ApplicationState Current,
    Exception? Exception = null)
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
