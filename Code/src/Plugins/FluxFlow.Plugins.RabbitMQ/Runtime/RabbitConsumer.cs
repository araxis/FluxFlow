using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FluxFlow.Components.RabbitMQ;

public sealed class RabbitConsumer : FlowNode, IFlowSource
{
    private readonly RabbitConnection _connection;
    private readonly RabbitConsumerOptions _options;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly Dictionary<string, ulong> _pending = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Exception> _failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IChannel? _channel;
    private string? _consumerTag;
    private bool _accepting;
    private readonly CancellationTokenRegistration _stopFeedback;
    public ITargetBlock<FlowMessage> Input { get; }
    public ITargetBlock<FlowMessage> ACK { get; }
    public ITargetBlock<FlowMessage> NACK { get; }
    public Task ProductionCompletion => Lifetime.ProductionCompletion;

    public RabbitConsumer(RabbitConnection connection, RabbitConsumerOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection); options.Validate();
        _connection = connection;
        _options = options with { Topology = options.Topology.Snapshot() };
        Output = new(static message => message, new() { BoundedCapacity = options.Capacity });
        Events = new(static message => message);
        Lifetime = new(this, produce: ReceiveAsync);
        var input = new ActionBlock<FlowMessage>(message => message.ProcessMessageAsync(this,
            value => SettleAsync(AmqpJson.Read<AmqpSettlement>(value.Value.Json), Stopping), Output, Stopping),
            new() { BoundedCapacity = options.Capacity, MaxDegreeOfParallelism = 1 });
        Input = input;
        ACK = CreateFeedback(nameof(ACK), "Ack");
        NACK = CreateFeedback(nameof(NACK), options.RequeueOnNack ? "Requeue" : "Reject");
        Lifetime.HasTerminalInput(ACK);
        Lifetime.HasTerminalInput(NACK);
        _stopFeedback = Stopping.Register(() => { ACK.Complete(); NACK.Complete(); });
        Lifetime.HasInput(input); Lifetime.HasOutput(Output); Lifetime.HasEvents(Events); Lifetime.Observe();
    }

    private ActionBlock<FlowMessage> CreateFeedback(string port, string action) => new(
        message => FlowBlockExtensions.ProcessControlAsync(message, this, port,
            (value, token) => SettleFeedbackAsync(value, action, token), Lifetime.ControlStopping, Fault),
        new() { BoundedCapacity = _options.Capacity, MaxDegreeOfParallelism = 1 });

    private async Task SettleFeedbackAsync(FlowMessage message, string action, CancellationToken token)
    {
        if (Stopping.IsCancellationRequested) return;
        AmqpSettlement command;
        try
        {
            var feedback = AmqpJson.Read<DeliveryFeedback>(message.Value.Json);
            ArgumentException.ThrowIfNullOrWhiteSpace(feedback.SettlementToken);
            command = new() { SettlementToken = feedback.SettlementToken, Action = action };
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or ArgumentException)
        {
            RejectFeedback(message, "Expected feedback containing a nonempty SettlementToken.");
            return;
        }

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(token, Stopping);
        try
        {
            if (!await TrySettleAsync(command, stopping.Token).ConfigureAwait(false))
                RejectFeedback(message, "The delivery is unavailable, already settled or belongs to another consumer, channel or run.");
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested) { }
    }

    private void RejectFeedback(FlowMessage message, string reason) => EmitEvent(message.WithValue(new FlowEvent
    {
        Name = "amqp.settlement_rejected", Kind = FlowEventKind.Diagnostic,
        Level = FlowEventLevel.Warning, Message = reason, Details = message.Value
    }));

    private sealed record DeliveryFeedback(string SettlementToken);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Lifetime.StartAsync(cancellationToken).ConfigureAwait(false);
            await _ready.Task.WaitAsync(Stopping).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Lifetime.StopProduction();
            await ProductionCompletion.ConfigureAwait(false);
            throw;
        }
    }
    public void StopProduction() => Lifetime.StopProduction();

    private async Task ReceiveAsync(CancellationToken token)
    {
        Exception? failure = null;
        try
        {
            _channel = await _connection.OpenChannelAsync(false, token).ConfigureAwait(false);
            _channel.ChannelShutdownAsync += ChannelClosed;
            _channel.CallbackExceptionAsync += CallbackFailed;
            await _options.Topology.DeclareAsync(_channel, token).ConfigureAwait(false);
            await _channel.BasicQosAsync(0, _options.Prefetch, false, token).ConfigureAwait(false);
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += Delivery;
            consumer.UnregisteredAsync += (_, _) =>
            {
                if (!token.IsCancellationRequested && _channel?.IsOpen == true)
                    _failed.TrySetResult(new IOException("The broker cancelled the AMQP consumer."));
                return Task.CompletedTask;
            };
            lock (_gate) _accepting = true;
            _consumerTag = await _channel.BasicConsumeAsync(_options.Queue, false, consumer, token).ConfigureAwait(false);
            _ready.TrySetResult();
            throw await _failed.Task.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { failure = error; _ready.TrySetException(error); _ = _ready.Task.Exception; throw; }
        finally
        {
            _ready.TrySetCanceled(token);
            try { await CloseAsync().ConfigureAwait(false); }
            catch (Exception cleanup) when (failure is not null)
            { throw new AggregateException("AMQP consumer execution and cleanup failed.", failure, cleanup); }
        }
    }

    private async Task CloseAsync()
    {
        lock (_gate) { _accepting = false; _pending.Clear(); }
        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_channel is { } channel)
            {
                Exception? cancelFailure = null;
                try
                {
                    if (channel.IsOpen && _consumerTag is not null)
                    {
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await channel.BasicCancelAsync(_consumerTag, cancellationToken: deadline.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception error) { cancelFailure = error; throw; }
                finally
                {
                    channel.ChannelShutdownAsync -= ChannelClosed;
                    channel.CallbackExceptionAsync -= CallbackFailed;
                    try { await channel.DisposeAsync().ConfigureAwait(false); _channel = null; }
                    catch (Exception cleanup) when (cancelFailure is not null)
                    { throw new AggregateException("AMQP cancellation and channel cleanup failed.", cancelFailure, cleanup); }
                }
            }
        }
        finally { _commands.Release(); }
    }

    private Task ChannelClosed(object sender, ShutdownEventArgs args)
    {
        lock (_gate) _pending.Clear();
        // Connection loss can be recovered by the official client. Channel protocol errors cannot.
        if (!Stopping.IsCancellationRequested && (!_connection.AutomaticRecovery || args.ReplyCode is >= 400 and < 500))
            _failed.TrySetResult(new IOException($"AMQP channel closed: {args.ReplyCode} {args.ReplyText}"));
        return Task.CompletedTask;
    }
    private Task CallbackFailed(object sender, CallbackExceptionEventArgs args)
    {
        _failed.TrySetResult(args.Exception);
        return Task.CompletedTask;
    }
    private async Task Delivery(object sender, BasicDeliverEventArgs delivery)
    {
        if (Stopping.IsCancellationRequested) return;
        using var activity = FlowTracing.StartReceiving(this);
        try
        {
            if (delivery.Body.Length > _options.MaxMessageBytes) throw new ArgumentException("AMQP delivery exceeds MaxMessageBytes.");
            // Copy borrowed native memory, including headers, before returning from the callback.
            var body = Convert.ToBase64String(delivery.Body.Span);
            var properties = AmqpProperties.FromNative(delivery.BasicProperties);
            var handle = Guid.NewGuid().ToString("N");
            // A broker can deliver the next message before our settlement send resumes.
            // Finish updating the pending set before admitting that replacement delivery.
            await _commands.WaitAsync(Stopping).ConfigureAwait(false);
            try
            {
                lock (_gate)
                {
                    if (!_accepting || Stopping.IsCancellationRequested) return;
                    if (_pending.Count >= _options.Prefetch) throw new InvalidOperationException("AMQP pending-delivery limit exceeded.");
                    _pending.Add(handle, delivery.DeliveryTag);
                }
            }
            finally { _commands.Release(); }
            await EmitAsync(FlowMessage.Create(AmqpJson.Value(new AmqpDelivery(handle, delivery.Exchange, delivery.RoutingKey,
                body, properties, delivery.Redelivered, delivery.ConsumerTag))), Stopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested) { activity.Canceled(); }
        catch (Exception error) { activity.Failed(error); _failed.TrySetResult(error); throw; }
    }

    public async Task SettleAsync(AmqpSettlement command, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SettlementToken);
        if (command.Action is not ("Ack" or "Reject" or "Requeue")) throw new ArgumentException("Settlement Action must be Ack, Reject or Requeue.");
        if (!await TrySettleAsync(command, token).ConfigureAwait(false))
            throw new InvalidOperationException("The delivery is unavailable, already settled or belongs to a previous channel/run.");
    }

    private async Task<bool> TrySettleAsync(AmqpSettlement command, CancellationToken token)
    {
        await _commands.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ulong tag;
            lock (_gate)
            {
                if (!_accepting || Stopping.IsCancellationRequested || !_pending.TryGetValue(command.SettlementToken, out tag))
                    return false;
            }
            if (command.Action == "Ack") await _channel!.BasicAckAsync(tag, false, token).ConfigureAwait(false);
            else await _channel!.BasicRejectAsync(tag, command.Action == "Requeue", token).ConfigureAwait(false);
            lock (_gate) _pending.Remove(command.SettlementToken);
            return true;
        }
        finally { _commands.Release(); }
    }

    protected override ValueTask OnDisposeAsync()
    {
        _stopFeedback.Dispose();
        return ValueTask.CompletedTask;
    }
}
