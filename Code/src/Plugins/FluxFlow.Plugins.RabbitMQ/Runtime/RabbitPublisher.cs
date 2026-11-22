using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using RabbitMQ.Client;

namespace FluxFlow.Components.RabbitMQ;

public sealed class RabbitPublisher : FlowNode
{
    private IChannel? _channel;
    public ITargetBlock<FlowMessage> Input { get; }
    public RabbitPublisher(RabbitConnection connection, RabbitTopology? topology = null,
        int capacity = 128, int maxMessageBytes = 1048576, int timeoutSeconds = 30)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageBytes);
        if (timeoutSeconds is < 1 or > 600) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        topology = (topology ?? new()).Snapshot();
        Output = new(static value => value, new() { BoundedCapacity = capacity });
        Events = new(static value => value);
        Lifetime = new(this, flush: CloseAsync);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, async value =>
        {
            var request = AmqpJson.Read<AmqpPublish>(value.Value.Json);
            ArgumentNullException.ThrowIfNull(request.Exchange);
            ArgumentNullException.ThrowIfNull(request.RoutingKey);
            var bytes = Convert.FromBase64String(request.Body);
            if (bytes.Length > maxMessageBytes) throw new ArgumentException("AMQP message exceeds MaxMessageBytes.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Stopping);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            // A broker channel error is permanent even when connection recovery is enabled.
            // Reopen for this new request; never retry the publication that failed.
            if (_channel is { IsOpen: false }) await CloseAsync().ConfigureAwait(false);
            if (_channel is null)
            {
                _channel = await connection.OpenChannelAsync(true, timeout.Token).ConfigureAwait(false);
                try { await topology.DeclareAsync(_channel, timeout.Token).ConfigureAwait(false); }
                catch (Exception original)
                {
                    try { await CloseAsync().ConfigureAwait(false); }
                    catch (Exception cleanup) { throw new AggregateException("AMQP publisher startup and cleanup failed.", original, cleanup); }
                    throw;
                }
            }
            // Native tracking waits for confirms and faults mandatory returns. Never retry an ambiguous publish.
            await _channel.BasicPublishAsync(request.Exchange, request.RoutingKey, request.Mandatory,
                request.Properties.ToNative(), bytes, timeout.Token).ConfigureAwait(false);
            return new[] { value.WithValue(AmqpJson.Value(new { Status = "Confirmed", request.Properties.MessageId })) }.AsEnumerable();
        }, Output, Stopping), new() { BoundedCapacity = capacity, MaxDegreeOfParallelism = 1 });
        input.LinkTo(Output); Input = input;
        Lifetime.HasInput(input); Lifetime.HasOutput(Output); Lifetime.HasEvents(Events); Lifetime.Observe();
    }
    private async ValueTask CloseAsync()
    {
        if (_channel is { } channel) { _channel = null; await channel.DisposeAsync().ConfigureAwait(false); }
    }
    protected override ValueTask OnDisposeAsync() => CloseAsync();
}
