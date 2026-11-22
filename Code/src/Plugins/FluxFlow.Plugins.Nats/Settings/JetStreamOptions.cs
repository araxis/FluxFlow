namespace FluxFlow.Components.Nats.JetStream;

public enum JetStreamStorage { File = 0, Memory = 1 }
public enum JetStreamDeliverPolicy { All = 0, Last = 1, New = 2, ByStartSequence = 3, ByStartTime = 4, LastPerSubject = 5 }

public sealed record JetStreamOptions
{
    public string Stream { get; init; } = "";
    public bool CreateStream { get; init; }
    public string StreamSubject { get; init; } = "events.>";
    public JetStreamStorage Storage { get; init; } = JetStreamStorage.File;
    public long MaxStreamMessages { get; init; } = 10000;
    public long MaxStreamBytes { get; init; } = 67108864;
    public int MaxStreamAgeSeconds { get; init; } = 86400;
    public NatsComponentOptions Limits { get; init; } = new();

    public void Validate()
    {
        Name(Stream); ArgumentNullException.ThrowIfNull(Limits); Limits.Validate();
        if (!CreateStream) return;
        NatsComponentOptions.ValidateSubject(StreamSubject, true);
        if (!Enum.IsDefined(Storage)) throw new ArgumentException("Unsupported JetStream storage type.");
        if (MaxStreamMessages < 1 || MaxStreamBytes < 1 || MaxStreamAgeSeconds is < 1 or > 31536000)
            throw new ArgumentException("Provisioned streams require finite positive retention limits.");
    }
    public static void Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 255 || name.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '.' or '*' or '>' or '/' or '\\'))
            throw new ArgumentException("Stream and consumer names cannot contain whitespace, dots, wildcards or path separators.");
    }

}

public sealed record JetStreamConsumerOptions
{
    public JetStreamOptions Stream { get; init; } = new();
    public string Consumer { get; init; } = "";
    public bool CreateConsumer { get; init; }
    public string? FilterSubject { get; init; }
    public JetStreamDeliverPolicy DeliverPolicy { get; init; } = JetStreamDeliverPolicy.All;
    public int AckWaitSeconds { get; init; } = 30;
    public int MaxDeliver { get; init; } = 5;
    public int MaxInFlight { get; init; } = 32;
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Stream); Stream.Validate(); JetStreamOptions.Name(Consumer);
        if (MaxInFlight is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(MaxInFlight));
        if (!CreateConsumer) return;
        if (FilterSubject is not null) NatsComponentOptions.ValidateSubject(FilterSubject, true);
        if (DeliverPolicy is not (JetStreamDeliverPolicy.All or JetStreamDeliverPolicy.New))
            throw new ArgumentException("Initial delivery must be All or New.");
        if (AckWaitSeconds is < 1 or > 86400 || MaxDeliver is < 1 or > 10000)
            throw new ArgumentException("Provisioned consumers require finite acknowledgement deadlines and delivery limits.");
    }

}
