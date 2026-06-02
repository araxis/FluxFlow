using FluxFlow.Components.Sources.Timing;

namespace FluxFlow.Components.Sources.Nodes;

internal static class SourceNodeTiming
{
    public static Task DelayInitialAsync(
        int initialDelayMilliseconds,
        ISourceClock clock,
        CancellationToken cancellationToken)
        => DelayAsync(initialDelayMilliseconds, clock, cancellationToken);

    public static Task DelayIntervalAsync(
        int intervalMilliseconds,
        ISourceClock clock,
        CancellationToken cancellationToken)
        => DelayAsync(intervalMilliseconds, clock, cancellationToken);

    private static async Task DelayAsync(
        int milliseconds,
        ISourceClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (milliseconds <= 0)
        {
            return;
        }

        await clock.DelayAsync(
                TimeSpan.FromMilliseconds(milliseconds),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
