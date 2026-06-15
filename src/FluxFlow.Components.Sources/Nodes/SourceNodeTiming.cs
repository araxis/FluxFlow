namespace FluxFlow.Components.Sources.Nodes;

internal static class SourceNodeTiming
{
    public static Task DelayInitialAsync(
        int initialDelayMilliseconds,
        TimeProvider clock,
        CancellationToken cancellationToken)
        => DelayAsync(initialDelayMilliseconds, clock, cancellationToken);

    public static Task DelayIntervalAsync(
        int intervalMilliseconds,
        TimeProvider clock,
        CancellationToken cancellationToken)
        => DelayAsync(intervalMilliseconds, clock, cancellationToken);

    private static async Task DelayAsync(
        int milliseconds,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (milliseconds <= 0)
        {
            return;
        }

        await Task.Delay(
                TimeSpan.FromMilliseconds(milliseconds),
                clock,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
