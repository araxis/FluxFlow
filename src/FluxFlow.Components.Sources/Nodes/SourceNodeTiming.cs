namespace FluxFlow.Components.Sources.Nodes;

internal static class SourceNodeTiming
{
    public static Task DelayInitialAsync(
        int initialDelayMilliseconds,
        CancellationToken cancellationToken)
        => DelayAsync(initialDelayMilliseconds, cancellationToken);

    public static Task DelayIntervalAsync(
        int intervalMilliseconds,
        CancellationToken cancellationToken)
        => DelayAsync(intervalMilliseconds, cancellationToken);

    private static Task DelayAsync(
        int milliseconds,
        CancellationToken cancellationToken)
        => milliseconds <= 0
            ? Task.CompletedTask
            : Task.Delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
}
