using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttSubscribeNode : SourceFlowNode<MqttReceivedMessage>, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly IMqttClientFactory _clientFactory;
    private readonly MqttSubscriptionOptions _options;
    private CancellationTokenSource? _subscriptionCancellation;
    private Task? _subscriptionTask;
    private IMqttClientAdapter? _adapter;
    private bool _started;
    private bool _disposed;

    private MqttSubscribeNode(
        MqttSubscriptionOptions options,
        IMqttClientFactory clientFactory)
        : base(new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity })
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        IMqttClientFactory clientFactory)
    {
        var node = new MqttSubscribeNode(
            MqttOptionsReader.ReadSubscriptionOptions(context.Definition),
            clientFactory);

        return context.CreateNode(node)
            .Output(MqttComponentPorts.Output, node.Output)
            .Build();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_started)
            {
                throw new InvalidOperationException("MQTT subscribe node has already started.");
            }

            _started = true;
        }

        var adapter = await _clientFactory.CreateAsync(_options.Connection, cancellationToken)
            .ConfigureAwait(false);
        var subscriptionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        lock (_stateLock)
        {
            _adapter = adapter;
            _subscriptionCancellation = subscriptionCancellation;
            _subscriptionTask = RunSubscriptionAsync(adapter, subscriptionCancellation.Token);
        }
    }

    public override void Complete()
    {
        if (_subscriptionCancellation is null)
        {
            base.Complete();
            return;
        }

        _subscriptionCancellation.Cancel();
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _subscriptionCancellation?.Cancel();
        base.Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscriptionCancellation?.Cancel();

        if (_subscriptionTask is not null)
        {
            await _subscriptionTask.ConfigureAwait(false);
        }

        _subscriptionCancellation?.Dispose();

        if (_adapter is not null)
        {
            await _adapter.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RunSubscriptionAsync(
        IMqttClientAdapter adapter,
        CancellationToken cancellationToken)
    {
        TryEmitDiagnostic(
            MqttDiagnosticNames.SubscribeStarted,
            message: $"Started MQTT subscription '{_options.TopicFilter}'.");

        try
        {
            await foreach (var message in adapter
                .SubscribeAsync(_options, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                var payloadBytes = message.Payload?.Length ?? 0;

                TryEmitDiagnostic(
                    MqttDiagnosticNames.SubscribeReceived,
                    message: $"Received MQTT message from '{message.Topic}'.",
                    attributes: new Dictionary<string, object?>
                    {
                        ["topic"] = message.Topic,
                        ["payloadBytes"] = payloadBytes
                    });

                await SendOutputAsync(message, cancellationToken).ConfigureAwait(false);
            }

            TryEmitDiagnostic(
                MqttDiagnosticNames.SubscribeStopped,
                message: $"Stopped MQTT subscription '{_options.TopicFilter}'.");
            CompleteOutput();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryEmitDiagnostic(
                MqttDiagnosticNames.SubscribeStopped,
                message: $"Stopped MQTT subscription '{_options.TopicFilter}'.");
            CompleteOutput();
        }
        catch (Exception exception)
        {
            TryReportError(
                MqttErrorCodes.SubscribeFailed,
                $"MQTT subscribe failed: {exception.Message}",
                exception);
            TryEmitDiagnostic(
                MqttDiagnosticNames.SubscribeFailed,
                FlowDiagnosticLevel.Error,
                $"MQTT subscribe failed for '{_options.TopicFilter}'.",
                exception);
            base.Fault(exception);
        }
    }
}
