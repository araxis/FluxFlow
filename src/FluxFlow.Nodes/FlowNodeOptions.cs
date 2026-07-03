namespace FluxFlow.Nodes;

/// <summary>
/// Shape of a node's input pump. Inputs are a bounded buffer (so a node applies
/// backpressure on its own intake); outputs are broadcast (no backpressure).
/// </summary>
public sealed record FlowNodeOptions
{
    private int _inputCapacity = 128;
    private int _maxDegreeOfParallelism = 1;

    public int InputCapacity
    {
        get => _inputCapacity;
        init
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(InputCapacity),
                    "InputCapacity must be greater than zero.");

            _inputCapacity = value;
        }
    }

    public int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        init
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(MaxDegreeOfParallelism),
                    "MaxDegreeOfParallelism must be greater than zero.");

            _maxDegreeOfParallelism = value;
        }
    }

    /// <summary>
    /// Clock used for node-owned timestamps (currently the safety-net error stamp when
    /// <c>ProcessAsync</c> throws). Defaults to <see cref="TimeProvider.System"/>; pass a
    /// FakeTimeProvider for deterministic error timestamps in tests.
    /// </summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}
