using RabbitMQ.Client;

namespace FluxFlow.Components.RabbitMQ;

internal static class RabbitDeclarations
{
    private static Dictionary<string, object?>? NativeArguments(Dictionary<string, AmqpField>? fields)
    {
        if (fields is null) return null;
        return fields.ToDictionary(item => item.Key, item => item.Value.ToNative());
    }
    internal static async Task DeclareAsync(this RabbitTopology topology, IChannel channel, CancellationToken token)
    {
        topology.Validate();
        foreach (var exchange in topology.Exchanges)
            await channel.ExchangeDeclareAsync(exchange.Name, exchange.Type, exchange.Durable, exchange.AutoDelete, arguments: NativeArguments(exchange.Arguments), cancellationToken: token).ConfigureAwait(false);
        foreach (var queue in topology.Queues)
            await channel.QueueDeclareAsync(queue.Name, queue.Durable, queue.Exclusive, queue.AutoDelete, arguments: NativeArguments(queue.Arguments), cancellationToken: token).ConfigureAwait(false);
        foreach (var binding in topology.Bindings)
            await channel.QueueBindAsync(binding.Queue, binding.Exchange, binding.RoutingKey, arguments: NativeArguments(binding.Arguments), cancellationToken: token).ConfigureAwait(false);
    }
}
