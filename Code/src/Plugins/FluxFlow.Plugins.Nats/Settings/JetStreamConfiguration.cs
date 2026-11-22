using FluxFlow.Components.Nats.JetStream;
using FluxFlow.Configuration.Application;

namespace FluxFlow.Configuration.Components.Nats;

public sealed record JetStreamConfiguration : NatsCommandConfiguration
{
    public string Stream { get; init; } = "";
    public bool CreateStream { get; init; }
    public string StreamSubject { get; init; } = "events.>";
    public JetStreamStorage Storage { get; init; } = JetStreamStorage.File;
    public long MaxStreamMessages { get; init; } = 10000;
    public long MaxStreamBytes { get; init; } = 67108864;
    public int MaxStreamAgeSeconds { get; init; } = 86400;
    public string Consumer { get; init; } = "";
    public bool CreateConsumer { get; init; }
    public string? FilterSubject { get; init; }
    public JetStreamDeliverPolicy DeliverPolicy { get; init; } = JetStreamDeliverPolicy.All;
    public int AckWaitSeconds { get; init; } = 30;
    public int MaxDeliver { get; init; } = 5;
    public int MaxInFlight { get; init; } = 32;
    public JetStreamOptions StreamOptions()
    {
        var options = new JetStreamOptions { Stream = Stream, CreateStream = CreateStream, StreamSubject = StreamSubject, Storage = Storage,
            MaxStreamMessages = MaxStreamMessages, MaxStreamBytes = MaxStreamBytes, MaxStreamAgeSeconds = MaxStreamAgeSeconds, Limits = Options() };
        options.Validate(); return options;
    }
    public JetStreamConsumerOptions ConsumerOptions()
    {
        var options = new JetStreamConsumerOptions { Stream = StreamOptions(), Consumer = Consumer, CreateConsumer = CreateConsumer,
            FilterSubject = FilterSubject, DeliverPolicy = DeliverPolicy, AckWaitSeconds = AckWaitSeconds, MaxDeliver = MaxDeliver, MaxInFlight = MaxInFlight };
        options.Validate(); return options;
    }
}

public sealed record JetStreamSettlementConfiguration : ComponentConfiguration
{
    public string Consumer { get; init; } = "";
    public int Capacity { get; init; } = 128;
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Consumer);
        if (Capacity is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(Capacity));
    }
}
