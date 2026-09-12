namespace FluxFlow.Coordination;

/// <summary>
/// Controls bounded pending-exchange tracking and timeout behavior.
/// </summary>
public sealed class PendingExchangeCoordinatorOptions
{
    /// <summary>
    /// Gets or initializes the timeout used when an exchange does not provide one.
    /// </summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or initializes the maximum number of concurrently pending exchanges.
    /// </summary>
    public int MaxPending { get; init; } = 1024;

    /// <summary>
    /// Gets or initializes the number of recently settled keys retained to classify
    /// duplicate and late feedback without retaining unbounded history.
    /// </summary>
    public int SettledKeyCapacity { get; init; } = 4096;
}
