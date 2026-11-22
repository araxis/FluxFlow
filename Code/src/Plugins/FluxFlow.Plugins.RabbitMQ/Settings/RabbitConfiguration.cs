using FluxFlow.Components.RabbitMQ;
using Microsoft.Extensions.Configuration;
using FluxFlow.Configuration.Application.Links;

namespace FluxFlow.Configuration.Components.RabbitMQ;

public sealed record RabbitConnectionConfiguration
{
    public static RabbitConnectionConfiguration Read(IConfiguration section)
    {
        foreach (var prohibited in new[] { "Username", "Password", "Credentials" })
            if (section.GetSection(prohibited).Exists()) throw new ArgumentException("AMQP credentials belong on the execution host. Use credential environment-variable references.");
        var config = section.Get<RabbitConnectionConfiguration>() ?? throw new ArgumentException("Missing AMQP connection configuration.");
        config.Validate();
        return config;
    }
    public string Endpoint { get; init; } = "amqp://localhost:5672";
    public string VirtualHost { get; init; } = "/";
    public string? UsernameEnvironmentVariable { get; init; }
    public string? PasswordEnvironmentVariable { get; init; }
    public bool AutomaticRecovery { get; init; } = true;
    public int RecoveryIntervalSeconds { get; init; } = 5;
    public int TimeoutSeconds { get; init; } = 30;
    public void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("amqp" or "amqps") ||
            string.IsNullOrEmpty(uri.Host) || uri.Port == 0 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath is not ("" or "/"))
            throw new ArgumentException("AMQP Endpoint requires amqp(s)://host[:port], without credentials or a virtual-host path.");
        ArgumentException.ThrowIfNullOrWhiteSpace(VirtualHost);
        if (TimeoutSeconds is < 1 or > 600 || RecoveryIntervalSeconds is < 1 or > 600) throw new ArgumentException("AMQP timeouts must be between 1 and 600 seconds.");
        if ((UsernameEnvironmentVariable is null) != (PasswordEnvironmentVariable is null))
            throw new ArgumentException("Provide both credential environment-variable names, or neither for the broker's guest account.");
        foreach (var name in new[] { UsernameEnvironmentVariable, PasswordEnvironmentVariable }.OfType<string>())
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['=', '\0']) >= 0) throw new ArgumentException("Invalid credential environment-variable name.");
    }

}

public sealed record RabbitComponentConfiguration : ComponentConfiguration
{
    public OneOrManyPortReferences ACK { get; init; } = OneOrManyPortReferences.Empty;
    public OneOrManyPortReferences NACK { get; init; } = OneOrManyPortReferences.Empty;
    public bool RequeueOnNack { get; set; }
    public string Connection { get; set; } = "";
    public string Queue { get; set; } = "";
    public ushort Prefetch { get; set; } = 32;
    public int Capacity { get; set; } = 128;
    public int MaxMessageBytes { get; set; } = 1048576;
    public int TimeoutSeconds { get; set; } = 30;
    public RabbitComponentConfiguration() { }
    public RabbitComponentConfiguration(RabbitTopology topology) => Topology = topology;
    public RabbitTopology Topology { get; private set; } = new();
    public static RabbitComponentConfiguration Read(IConfigurationSection section)
    {
        var config = section.ReadRequiredConfiguration<RabbitComponentConfiguration>();
        config.Topology = RabbitTopologyConfiguration.Read(section.GetSection("Topology"));
        return config;
    }
    public void Validate(bool consumer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Connection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxMessageBytes);
        if (TimeoutSeconds is < 1 or > 600) throw new ArgumentException("AMQP TimeoutSeconds must be between 1 and 600.");
        Topology.Validate();
        if (consumer) ConsumerOptions().Validate();
    }
    public RabbitConsumerOptions ConsumerOptions() => new() { Queue = Queue, Prefetch = Prefetch, Capacity = Capacity, MaxMessageBytes = MaxMessageBytes, RequeueOnNack = RequeueOnNack, Topology = Topology };
}

public sealed record RabbitSettlementConfiguration : ComponentConfiguration
{
    public string Consumer { get; set; } = "";
    public int Capacity { get; set; } = 128;
}
