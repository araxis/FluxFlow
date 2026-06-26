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
}
