namespace FluxFlow.Composition;

public sealed record CompositionProcessingSettings
{
    public CompositionProcessingSettings(
        int bufferCapacity,
        int concurrency,
        bool preserveOrder)
    {
        if (bufferCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferCapacity));
        if (concurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(concurrency));

        BufferCapacity = bufferCapacity;
        Concurrency = concurrency;
        PreserveOrder = preserveOrder;
    }

    public int BufferCapacity { get; }

    public int Concurrency { get; }

    public bool PreserveOrder { get; }
}
