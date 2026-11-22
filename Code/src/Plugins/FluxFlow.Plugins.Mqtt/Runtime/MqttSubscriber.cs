using System.Buffers;
using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using MQTTnet;
using MQTTnet.Protocol;

namespace FluxFlow.Components.Mqtt;

public sealed partial class MqttSubscriber : FlowNode, IFlowSource
{
    private readonly MqttClientResource _connection;
    // This is the live subscription state, not a second registration or service lookup.
    private readonly Dictionary<string, MqttTopic> _topics = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removed = new(StringComparer.Ordinal);
    private bool _restored;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public ITargetBlock<FlowMessage> Input { get; }

    public MqttSubscriber(MqttSubscriberOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Connection);
        ArgumentNullException.ThrowIfNull(options.Topics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxPendingAcknowledgements);
        if (!Enum.IsDefined(options.AcknowledgementMode)) throw new ArgumentOutOfRangeException(nameof(options.AcknowledgementMode));
        _acknowledgementMode = options.AcknowledgementMode;
        _maxPendingAcknowledgements = options.MaxPendingAcknowledgements;
        foreach (var topic in options.Topics)
        {
            ArgumentNullException.ThrowIfNull(topic);
            topic.Validate();
            ValidateAcknowledgementQoS(topic);
            _topics[topic.Topic] = topic;
        }
        _connection = options.Connection;
        _connection.ClaimSubscriber(this);
        Output = new(static message => message, new() { BoundedCapacity = options.OutputCapacity });
        Events = new(static message => message);
        Lifetime = new FlowLifetime(this, produce: async token =>
        {
            await _connection.ExecuteAsync(async (client, operationToken) =>
            {
                if (client.IsConnected && !_restored) await RestoreAsync(client, true, operationToken).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
        });
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(
            message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), new() { BoundedCapacity = 128 });
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        ACK = new ActionBlock<FlowMessage>(message => FlowBlockExtensions.ProcessControlAsync(
            message, this, nameof(ACK), AcknowledgeAsync, Lifetime.ControlStopping, Fault),
            new ExecutionDataflowBlockOptions { BoundedCapacity = options.OutputCapacity });
        Lifetime.HasTerminalInput(ACK);
        _stopAcknowledgements = Stopping.Register(() =>
        {
            ClearPending("Subscriber stopped.");
            ACK.Complete();
        });
        _connection.StateChanged += OnConnectionStateChanged;
        _connection.RestoreSubscriptions = RestoreAsync;
        _connection.Client.ApplicationMessageReceivedAsync += ReceiveAsync;
        Lifetime.Observe();
    }

    public Task ProductionCompletion => Lifetime.ProductionCompletion;
    public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
    public void StopProduction() => Lifetime.StopProduction();

    private async Task<IEnumerable<FlowMessage>> ProcessAsync(FlowMessage message)
    {
        var command = MqttJson.Read<MqttSubscriptionCommand>(message);
        if (!Enum.IsDefined(command.Command)) throw new ArgumentOutOfRangeException(nameof(command.Command));
        if (command.Topics.Count == 0) throw new ArgumentException("A subscription command requires at least one topic.");
        foreach (var topic in command.Topics)
        {
            ArgumentNullException.ThrowIfNull(topic);
            topic.Validate();
            if (command.Command == MqttSubscriptionOperation.Subscribe) ValidateAcknowledgementQoS(topic);
        }
        var applied = await _connection.ExecuteAsync(async (client, token) =>
        {
            foreach (var topic in command.Topics)
            {
                if (command.Command == MqttSubscriptionOperation.Subscribe)
                {
                    _topics[topic.Topic] = topic;
                    _removed.Remove(topic.Topic);
                }
                else
                {
                    _topics.Remove(topic.Topic);
                    _removed.Add(topic.Topic);
                }
            }
            if (client.IsConnected)
                await ApplyAsync(client, token, command.Command == MqttSubscriptionOperation.Subscribe ? command.Topics : []).ConfigureAwait(false);
            return client.IsConnected;
        }, Stopping).ConfigureAwait(false);
        EmitEvent(message.WithValue(new FlowEvent
        {
            Name = applied ? "mqtt.subscriptions_applied" : "mqtt.subscriptions_pending",
            Kind = FlowEventKind.Domain,
            Details = command
        }));
        return [];
    }

    private async Task RestoreAsync(IMqttClient client, bool sessionPresent, CancellationToken cancellationToken)
    {
        if (Stopping.IsCancellationRequested) return;
        if (!sessionPresent) _removed.Clear();
        await ApplyAsync(client, cancellationToken).ConfigureAwait(false);
        _restored = true;
        EmitEvent(new FlowEvent { Name = "mqtt.subscriptions_applied", Kind = FlowEventKind.Domain });
    }

    private async Task ApplyAsync(IMqttClient client, CancellationToken cancellationToken, IReadOnlyList<MqttTopic>? changedTopics = null)
    {
        if (_removed.Count > 0)
        {
            var options = new MqttClientUnsubscribeOptionsBuilder();
            foreach (var topic in _removed) options.WithTopicFilter(topic);
            var result = await client.UnsubscribeAsync(options.Build(), cancellationToken).ConfigureAwait(false);
            if (result.Items.Any(item => (int)item.ResultCode >= 128))
                throw new InvalidOperationException("MQTT broker rejected one or more unsubscriptions.");
            _removed.Clear();
        }
        var subscriptions = changedTopics ?? _topics.Values.ToArray();
        if (subscriptions.Count > 0)
        {
            var options = new MqttClientSubscribeOptionsBuilder();
            foreach (var topic in subscriptions)
                options.WithTopicFilter(topic.Topic, (MqttQualityOfServiceLevel)topic.QoS);
            var result = await client.SubscribeAsync(options.Build(), cancellationToken).ConfigureAwait(false);
            if (result.Items.Any(item => (int)item.ResultCode >= 128))
                throw new InvalidOperationException("MQTT broker rejected one or more subscriptions.");
        }
    }

    private async Task ReceiveAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        args.AutoAcknowledge = false;
        if (Stopping.IsCancellationRequested) return;
        using var activity = FlowTracing.StartReceiving(this);
        var received = args.ApplicationMessage;
        var bytes = received.Payload.ToArray();
        string? text;
        try { text = Utf8.GetString(bytes); }
        catch (DecoderFallbackException) { text = null; }
        // Own the bytes before the native callback returns. Each broker delivery begins a new lineage.
        var fields = new Dictionary<string, object?>
        {
            ["Topic"] = received.Topic, ["Payload"] = Convert.ToBase64String(bytes), ["Encoding"] = "Base64", ["Text"] = text,
            ["QoS"] = (int)received.QualityOfServiceLevel, ["Retain"] = received.Retain, ["Timestamp"] = DateTimeOffset.UtcNow
        };
        try
        {
            if (_acknowledgementMode == MqttAcknowledgementMode.Manual && received.QualityOfServiceLevel != MqttQualityOfServiceLevel.AtMostOnce)
            {
                if (received.QualityOfServiceLevel != MqttQualityOfServiceLevel.AtLeastOnce)
                    throw new InvalidOperationException("Manual acknowledgement supports QoS 1 deliveries only.");
                var id = AddPending(args);
                if (id is null) return;
                fields["AcknowledgementId"] = id;
            }
            await EmitAsync(FlowMessage.Create(MqttJson.Value(fields)), Stopping).ConfigureAwait(false);
            args.AutoAcknowledge = _acknowledgementMode == MqttAcknowledgementMode.Automatic && !Stopping.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested) { activity.Canceled(); }
        catch (Exception error) { activity.Failed(error); Fault(error); throw; }
    }

    protected override async ValueTask OnDisposeAsync()
    {
        _stopAcknowledgements.Dispose();
        _connection.StateChanged -= OnConnectionStateChanged;
        _connection.Client.ApplicationMessageReceivedAsync -= ReceiveAsync;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _connection.ExecuteAsync(async (client, token) =>
        {
            _connection.RestoreSubscriptions = null;
            if (client.IsConnected && (_topics.Count != 0 || _removed.Count != 0))
            {
                var options = new MqttClientUnsubscribeOptionsBuilder();
                foreach (var topic in _topics.Keys.Concat(_removed).Distinct(StringComparer.Ordinal)) options.WithTopicFilter(topic);
                var result = await client.UnsubscribeAsync(options.Build(), token).ConfigureAwait(false);
                if (result.Items.Any(item => (int)item.ResultCode >= 128))
                    throw new InvalidOperationException("MQTT broker rejected subscription cleanup.");
            }
            _connection.ReleaseSubscriber(this);
            return true;
        }, timeout.Token).ConfigureAwait(false);
    }
}
