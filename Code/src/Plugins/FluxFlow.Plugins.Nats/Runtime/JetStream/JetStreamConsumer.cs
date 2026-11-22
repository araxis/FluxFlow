using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using NATS.Client.JetStream;

namespace FluxFlow.Components.Nats.JetStream;

public sealed class JetStreamConsumer : FlowNode, IFlowSource
{
    private readonly NatsSession _connection;
    private readonly JetStreamConsumerOptions _options;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _slots;
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<string, (ulong Sequence, INatsJSMsg<byte[]> Message)> _pending = new(StringComparer.Ordinal);
    public ITargetBlock<FlowMessage> Input { get; }
    public Task ProductionCompletion => Lifetime.ProductionCompletion;

    public JetStreamConsumer(NatsSession connection, JetStreamConsumerOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection); options.Validate();
        _connection = connection; _options = options; _slots = new(options.MaxInFlight, options.MaxInFlight);
        Output = new(static value => value, new() { BoundedCapacity = options.Stream.Limits.Capacity });
        Events = new(static value => value); Lifetime = new(this, produce: ReceiveAsync);
        Input = new ActionBlock<FlowMessage>(message => message.ProcessMessageAsync(this,
            async value => { await SettleAsync(JetStreamJson.Read(value), Lifetime.ControlStopping).ConfigureAwait(false); }, Output, Lifetime.ControlStopping),
            new() { BoundedCapacity = options.Stream.Limits.Capacity, MaxDegreeOfParallelism = 1 });
        Lifetime.HasTerminalInput(Input); Lifetime.HasOutput(Output); Lifetime.HasEvents(Events); Lifetime.Observe();
    }
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Lifetime.StartAsync(cancellationToken).ConfigureAwait(false);
            await _ready.Task.WaitAsync(Stopping).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { StopProduction(); await ProductionCompletion.ConfigureAwait(false); throw; }
    }
    public void StopProduction() => Lifetime.StopProduction();

    private async Task ReceiveAsync(CancellationToken token)
    {
        using var active = CancellationTokenSource.CreateLinkedTokenSource(token, _connection.FailureToken);
        try
        {
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(active.Token);
            startup.CancelAfter(TimeSpan.FromSeconds(_options.Stream.Limits.TimeoutSeconds));
            var native = await _connection.OpenAsync(startup.Token).ConfigureAwait(false);
            var consumer = await _options.OpenAsync(new NatsJSContext(native), startup.Token).ConfigureAwait(false);
            _ready.TrySetResult();
            while (true)
            {
                await _slots.WaitAsync(active.Token).ConfigureAwait(false);
                var retained = false;
                try
                {
                    var next = await consumer.NextAsync<byte[]>(opts: new() { Expires = TimeSpan.FromSeconds(1) }, cancellationToken: active.Token).ConfigureAwait(false);
                    if (next is not { } message) continue;
                    active.Token.ThrowIfCancellationRequested(); message.EnsureSuccess();
                    using var activity = FlowTracing.StartReceiving(this);
                    try
                    {
                        var bytes = message.Data ?? [];
                        if (bytes.Length > _options.Stream.Limits.MaxMessageBytes) throw new ArgumentException("JetStream delivery exceeds MaxMessageBytes.");
                        var metadata = message.Metadata ?? throw new InvalidOperationException("JetStream delivery has no metadata.");
                        var payload = new NatsMessage { Subject = message.Subject, DataBase64 = Convert.ToBase64String(bytes),
                            Headers = NatsMessage.CopyHeaders(message.Headers) };
                        var handle = Guid.NewGuid().ToString("N");
                        lock (_gate) _pending.Add(handle, (metadata.Sequence.Stream, message));
                        retained = true;
                        await EmitAsync(FlowMessage.Create(JetStreamJson.Value(new JetStreamDelivery(handle, payload, metadata), JetStreamJson.Default.JetStreamDelivery)), active.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { activity.Canceled(); throw; }
                    catch (Exception error) { activity.Failed(error); throw; }
                }
                finally { if (!retained) _slots.Release(); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            try { _connection.ThrowIfFailed(); } catch (Exception failure) { error = failure; }
            _ready.TrySetException(error); _ = _ready.Task.Exception; ExceptionDispatchInfo.Capture(error).Throw();
        }
        finally { _ready.TrySetCanceled(token); }
    }

    public async Task SettleAsync(JetStreamSettlement command, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SettlementToken);
        if (command.Action is not ("Ack" or "Nak" or "Progress" or "Terminate") || command.DelaySeconds is < 0 or > 86400 ||
            command.DelaySeconds != 0 && command.Action != "Nak") throw new ArgumentException("Use Ack, Nak, Progress or Terminate; only Nak accepts a delay.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, Lifetime.ControlStopping, _connection.FailureToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.Stream.Limits.TimeoutSeconds));
        var acquired = false;
        try
        {
            await _commands.WaitAsync(deadline.Token).ConfigureAwait(false);
            acquired = true;
            _connection.ThrowIfFailed(); deadline.Token.ThrowIfCancellationRequested();
            (ulong Sequence, INatsJSMsg<byte[]> Message) delivery;
            lock (_gate)
                if (!_pending.TryGetValue(command.SettlementToken, out delivery))
                    throw new InvalidOperationException("The delivery is unavailable, already settled or belongs to another consumer/run.");
            var message = delivery.Message;
            if (command.Action == "Ack") await message.AckAsync(new() { DoubleAck = true }, deadline.Token).ConfigureAwait(false);
            else if (command.Action == "Nak") await message.NakAsync(new() { NakDelay = TimeSpan.FromSeconds(command.DelaySeconds), DoubleAck = false }, deadline.Token).ConfigureAwait(false);
            else if (command.Action == "Progress") await message.AckProgressAsync(new() { DoubleAck = false }, deadline.Token).ConfigureAwait(false);
            else await message.AckTerminateAsync(new() { DoubleAck = false }, deadline.Token).ConfigureAwait(false);
            if (command.Action != "Progress")
            {
                lock (_gate)
                {
                    int before = _pending.Count;
                    if (command.Action == "Nak") _pending.Remove(command.SettlementToken);
                    else
                    {
                        // ACK and Terminate settle the stream message, including other local attempts.
                        foreach (var entry in _pending)
                            if (entry.Value.Sequence == delivery.Sequence) _pending.Remove(entry.Key);
                    }
                    if (_pending.Count < before) _slots.Release(before - _pending.Count);
                }
            }
        }
        catch (OperationCanceledException) { _connection.ThrowIfFailed(); throw; }
        finally { if (acquired) _commands.Release(); }
    }
    protected override ValueTask OnDisposeAsync()
    {
        lock (_gate) _pending.Clear();
        return ValueTask.CompletedTask;
    }
}
