namespace FluxFlow.Components.RabbitMQ;

public sealed record RabbitConsumerOptions
{
    public string Queue { get; init; } = "";
    public ushort Prefetch { get; init; } = 32;
    public int Capacity { get; init; } = 128;
    public int MaxMessageBytes { get; init; } = 1048576;
    public bool RequeueOnNack { get; init; }
    public RabbitTopology Topology { get; init; } = new();
    public void Validate()
    {
        RabbitTopology.Name(Queue);
        if (Prefetch == 0) throw new ArgumentException("Prefetch must be finite and positive.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxMessageBytes);
        Topology.Validate();
    }
}
