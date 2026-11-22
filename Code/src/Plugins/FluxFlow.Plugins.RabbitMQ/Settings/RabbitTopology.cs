namespace FluxFlow.Components.RabbitMQ;

public sealed record RabbitExchange(string Name, string Type = "direct", bool Durable = true, bool AutoDelete = false,
    Dictionary<string, AmqpField>? Arguments = null);
public sealed record RabbitQueue(string Name, bool Durable = true, bool Exclusive = false, bool AutoDelete = false,
    Dictionary<string, AmqpField>? Arguments = null);
public sealed record RabbitBinding(string Queue, string Exchange, string RoutingKey = "",
    Dictionary<string, AmqpField>? Arguments = null);

/// <summary>Declarations are opt-in. Empty topology makes no administrative calls.</summary>
public sealed record RabbitTopology
{
    public const int MaximumDeclarations = 256;
    public List<RabbitExchange> Exchanges { get; init; } = [];
    public List<RabbitQueue> Queues { get; init; } = [];
    public List<RabbitBinding> Bindings { get; init; } = [];
    public void Validate()
    {
        if (Exchanges is null || Queues is null || Bindings is null || Exchanges.Any(item => item is null) ||
            Queues.Any(item => item is null) || Bindings.Any(item => item is null))
            throw new ArgumentException("Topology requires non-null Exchanges, Queues and Bindings arrays without null entries.");
        if (Exchanges.Count + Queues.Count + Bindings.Count > MaximumDeclarations) throw new ArgumentException($"At most {MaximumDeclarations} declarations are supported.");
        foreach (var exchange in Exchanges)
        {
            Name(exchange.Name);
            if (exchange.Type is not ("direct" or "topic" or "fanout" or "headers")) throw new ArgumentException("Unsupported exchange type.");
            ValidateArguments(exchange.Arguments);
        }
        foreach (var queue in Queues) { Name(queue.Name); ValidateArguments(queue.Arguments); }
        foreach (var binding in Bindings)
        {
            Name(binding.Queue); Name(binding.Exchange);
            ArgumentNullException.ThrowIfNull(binding.RoutingKey);
            if (System.Text.Encoding.UTF8.GetByteCount(binding.RoutingKey) > 255) throw new ArgumentException("Routing keys are limited to 255 UTF-8 bytes.");
            ValidateArguments(binding.Arguments);
        }
    }
    public RabbitTopology Snapshot()
    {
        Validate();
        return new()
        {
            Exchanges = Exchanges.Select(item => item with { Arguments = Copy(item.Arguments) }).ToList(),
            Queues = Queues.Select(item => item with { Arguments = Copy(item.Arguments) }).ToList(),
            Bindings = Bindings.Select(item => item with { Arguments = Copy(item.Arguments) }).ToList()
        };
        static Dictionary<string, AmqpField>? Copy(Dictionary<string, AmqpField>? fields) => fields?.ToDictionary(
            item => item.Key, item => item.Value with { Value = item.Value.Value.Clone() });
    }
    private static void ValidateArguments(Dictionary<string, AmqpField>? fields)
    {
        if (fields is null) return;
        foreach (var (name, field) in fields) { Name(name); ArgumentNullException.ThrowIfNull(field); _ = field.Decode(static seconds => seconds); }
    }

    public static void Name(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (System.Text.Encoding.UTF8.GetByteCount(value) > 255) throw new ArgumentException("AMQP names are limited to 255 UTF-8 bytes.");
    }
}
