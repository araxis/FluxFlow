using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttPublishNode : EventFlowNodeBase, IAsyncDisposable
{
    private readonly IMqttClientFactory _clientFactory;
    private readonly ActionBlock<MqttPublishRequest> _input;
    private readonly BufferBlock<MqttPublishResult> _result;
    private readonly MqttPublishOptions _options;
    private IMqttClientAdapter? _adapter;
    private bool _disposed;

    private MqttPublishNode(
        MqttPublishOptions options,
        IMqttClientFactory clientFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));

        _input = new ActionBlock<MqttPublishRequest>(
            HandleAsync,
            new ExecutionDataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        _result = new BufferBlock<MqttPublishResult>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        CompleteWhen(_input.Completion);
    }

    public ITargetBlock<MqttPublishRequest> Input => _input;

    public ISourceBlock<MqttPublishResult> Result => _result;

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        IMqttClientFactory clientFactory)
    {
        var node = new MqttPublishNode(
            MqttOptionsReader.ReadPublishOptions(context.Definition),
            clientFactory);

        return context.CreateNode(node)
            .Input(MqttComponentPorts.Input, node.Input)
            .Output(MqttComponentPorts.Result, node.Result)
            .Build();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
        => _adapter = await _clientFactory.CreateAsync(_options.Connection, cancellationToken)
            .ConfigureAwait(false);

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
            ((IDataflowBlock)_result).Fault(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_adapter is not null)
        {
            await _adapter.DisposeAsync().ConfigureAwait(false);
        }
    }

    protected override void OnNodeCompleted()
    {
        _result.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        ((IDataflowBlock)_result).Fault(exception);
        base.OnNodeFaulted(exception);
    }

    private async Task HandleAsync(MqttPublishRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var adapter = _adapter;
        if (adapter is null)
        {
            TryReportError(
                MqttErrorCodes.PublishFailed,
                "MQTT publish node has not started.");
            return;
        }

        var request = ResolveRequest(input);
        if (string.IsNullOrWhiteSpace(request.Topic))
        {
            TryReportError(
                MqttErrorCodes.PublishFailed,
                "MQTT publish request requires a topic or a default topic option.");
            return;
        }

        if (request.Payload is null)
        {
            TryReportError(
                MqttErrorCodes.PublishFailed,
                "MQTT publish request requires a payload.");
            return;
        }

        try
        {
            await adapter.PublishAsync(request).ConfigureAwait(false);

            var result = new MqttPublishResult
            {
                Timestamp = DateTimeOffset.UtcNow,
                Topic = request.Topic,
                PayloadBytes = request.Payload.Length,
                QualityOfService = request.QualityOfService ?? _options.QualityOfService,
                Retain = request.Retain ?? _options.Retain,
                CorrelationId = request.CorrelationId
            };

            await _result.SendAsync(result).ConfigureAwait(false);
            EmitEvent(
                MqttEventNames.PublishSucceeded,
                subject: request.Topic,
                channel: MqttEventNames.PublishSucceeded,
                payloadBytes: result.PayloadBytes);
            TryEmitDiagnostic(
                MqttDiagnosticNames.PublishSucceeded,
                message: $"Published MQTT message to '{request.Topic}'.",
                attributes: new Dictionary<string, object?>
                {
                    ["topic"] = request.Topic,
                    ["payloadBytes"] = result.PayloadBytes
                });
        }
        catch (Exception exception)
        {
            TryReportError(
                MqttErrorCodes.PublishFailed,
                $"MQTT publish failed: {exception.Message}",
                exception);
            TryEmitDiagnostic(
                MqttDiagnosticNames.PublishFailed,
                FlowDiagnosticLevel.Error,
                $"MQTT publish failed for '{request.Topic}'.",
                exception);
        }
    }

    private MqttPublishRequest ResolveRequest(MqttPublishRequest input)
        => input with
        {
            Topic = string.IsNullOrWhiteSpace(input.Topic)
                ? _options.DefaultTopic
                : input.Topic,
            QualityOfService = input.QualityOfService ?? _options.QualityOfService,
            Retain = input.Retain ?? _options.Retain
        };
}
