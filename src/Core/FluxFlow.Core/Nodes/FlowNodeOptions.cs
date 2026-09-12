namespace FluxFlow.Nodes;

/// <summary>
/// Shape of a node's bounded input and reliable output pumps.
/// </summary>
public sealed record FlowNodeOptions
{
    private int _inputCapacity = 128;
    private int _outputCapacity = 128;
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

    public int OutputCapacity
    {
        get => _outputCapacity;
        init
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(OutputCapacity),
                    "OutputCapacity must be greater than zero.");

            _outputCapacity = value;
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
