using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Timing;
using FluxFlow.Components.Mqtt.Validation;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttPublishNode : EventFlowNodeBase, IAsyncDisposable
{
    private readonly IMqttClientFactory _clientFactory;
    private readonly MqttClientFactoryContext _factoryContext;
    private readonly IMqttClock _clock;
    private readonly ActionBlock<MqttPublishRequest> _input;
    private readonly BufferBlock<MqttPublishResult> _result;
    private readonly MqttPublishOptions _options;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private MqttHealthMonitor? _healthMonitor;
    private MqttClientLease? _clientLease;
    private bool _disposed;

    private MqttPublishNode(
        MqttPublishOptions options,
        MqttClientFactoryContext factoryContext,
        IMqttClientFactory clientFactory,
        IMqttClock clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _factoryContext = factoryContext ?? throw new ArgumentNullException(nameof(factoryContext));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

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
        IMqttClientFactory clientFactory,
        IMqttClock clock)
    {
        var options = MqttOptionsReader.ReadPublishOptions(context.Definition);
        var node = new MqttPublishNode(
            options,
            MqttClientFactoryContexts.Create(context, options, clock),
            clientFactory,
            clock);

        return context.CreateNode(node)
            .Input(MqttComponentPorts.Input, node.Input)
            .Output(MqttComponentPorts.Result, node.Result)
            .Build();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var clientLease = await _clientFactory.CreateAsync(_factoryContext, cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(clientLease);
        _clientLease = clientLease;
        StartHealthMonitor(clientLease.Adapter);
    }

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _lifecycleCancellation.Cancel();
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
        Complete();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
        }

        _lifecycleCancellation.Cancel();
        await StopHealthMonitorAsync().ConfigureAwait(false);

        if (_clientLease is not null)
        {
            await _clientLease.DisposeAsync().ConfigureAwait(false);
        }

        _lifecycleCancellation.Dispose();
    }

    protected override void OnNodeCompleted()
    {
        CancelHealthMonitor();
        _result.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        CancelHealthMonitor();
        ((IDataflowBlock)_result).Fault(exception);
        base.OnNodeFaulted(exception);
    }

    private void StartHealthMonitor(IMqttClientAdapter adapter)
        => _healthMonitor = MqttHealthMonitor.Start(adapter, _clock, EmitHealth, EmitHealthFailure);

    private void CancelHealthMonitor()
        => _healthMonitor?.Cancel();

    private async ValueTask StopHealthMonitorAsync()
    {
        if (_healthMonitor is not null)
        {
            await _healthMonitor.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void EmitHealthFailure(MqttClientHealthEvent health, Exception exception)
    {
        TryEmitDiagnostic(
            MqttDiagnosticNames.ConnectionHealthChanged,
            FlowDiagnosticLevel.Error,
            "MQTT connection health stream failed.",
            exception,
            MqttHealthSignal.CreateDiagnosticAttributes(
                health,
                _factoryContext.ConnectionName));
        EmitHealthEvent(health);
    }

    private void EmitHealth(MqttClientHealthEvent health)
    {
        TryEmitDiagnostic(
            MqttDiagnosticNames.ConnectionHealthChanged,
            MqttHealthSignal.GetLevel(health),
            MqttHealthSignal.CreateMessage(health),
            attributes: MqttHealthSignal.CreateDiagnosticAttributes(
                health,
                _factoryContext.ConnectionName));
        EmitHealthEvent(health);
    }

    private bool EmitHealthEvent(MqttClientHealthEvent health)
        => EmitMqttEvent(
            type: MqttEventNames.ConnectionHealthChanged,
            subject: MqttHealthSignal.CreateSubject(health, _factoryContext.ConnectionName),
            status: health.State.ToString(),
            channel: MqttEventNames.ConnectionHealthChanged,
            attributes: MqttHealthSignal.CreateEventAttributes(
                health,
                _factoryContext.ConnectionName));

    private async Task HandleAsync(MqttPublishRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_clientLease is null)
        {
            ReportPublishError(
                MqttErrorCodes.PublishNotStarted,
                "MQTT publish node has not started.",
                input);
            return;
        }

        var request = ResolveRequest(input);
        var topicValidation = MqttTopicValidator.ValidatePublishTopic(request.Topic);
        if (!topicValidation.IsValid)
        {
            ReportPublishError(
                MqttErrorCodes.PublishInvalidTopic,
                topicValidation.Message ?? "MQTT publish request uses an invalid topic.",
                request);
            return;
        }

        if (request.Payload is null)
        {
            ReportPublishError(
                MqttErrorCodes.PublishInvalidPayload,
                "MQTT publish request requires a payload.",
                request);
            return;
        }

        if (request.QualityOfService.HasValue &&
            !Enum.IsDefined(request.QualityOfService.Value))
        {
            ReportPublishError(
                MqttErrorCodes.PublishInvalidQualityOfService,
                "MQTT publish request uses an unsupported quality setting.",
                request);
            return;
        }

        var publishTimeout = TimeSpan.FromMilliseconds(_options.PublishTimeoutMilliseconds);
        using var publishCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifecycleCancellation.Token);
        publishCancellation.CancelAfter(publishTimeout);
        try
        {
            await _clientLease.Adapter.PublishAsync(request, publishCancellation.Token)
                .AsTask()
                .WaitAsync(publishTimeout, _lifecycleCancellation.Token)
                .ConfigureAwait(false);

            var result = new MqttPublishResult
            {
                Timestamp = _clock.UtcNow,
                Topic = request.Topic!,
                PayloadBytes = request.Payload.Length,
                PayloadPreview = request.PayloadPreview,
                QualityOfService = request.QualityOfService ?? _options.QualityOfService,
                Retain = request.Retain ?? _options.Retain,
                CorrelationId = request.CorrelationId
            };

            await _result.SendAsync(result).ConfigureAwait(false);
            EmitMqttEvent(
                type: MqttEventNames.PublishSucceeded,
                subject: request.Topic,
                channel: MqttEventNames.PublishSucceeded,
                payloadBytes: result.PayloadBytes,
                payloadPreview: result.PayloadPreview,
                attributes: CreateEventAttributes(request, result));
            TryEmitDiagnostic(
                MqttDiagnosticNames.PublishSucceeded,
                message: $"Published MQTT message to '{request.Topic}'.",
                attributes: CreateDiagnosticAttributes(request, result.PayloadBytes));
        }
        catch (Exception exception) when (
            exception is TimeoutException ||
            (exception is OperationCanceledException &&
             publishCancellation.IsCancellationRequested &&
             !_lifecycleCancellation.IsCancellationRequested))
        {
            ReportPublishError(
                MqttErrorCodes.PublishTimedOut,
                $"MQTT publish timed out after {_options.PublishTimeoutMilliseconds} ms.",
                request,
                exception);
            EmitMqttEvent(
                type: MqttEventNames.PublishFailed,
                subject: request.Topic,
                status: "failed",
                channel: MqttEventNames.PublishFailed,
                payloadBytes: request.Payload.Length,
                payloadPreview: request.PayloadPreview,
                attributes: CreateEventAttributes(request, request.Payload.Length));
            TryEmitDiagnostic(
                MqttDiagnosticNames.PublishFailed,
                FlowDiagnosticLevel.Error,
                $"MQTT publish timed out for '{request.Topic}'.",
                exception,
                CreateDiagnosticAttributes(request, request.Payload.Length));
        }
        catch (Exception exception)
        {
            ReportPublishError(
                MqttErrorCodes.PublishFailed,
                $"MQTT publish failed: {exception.Message}",
                request,
                exception);
            EmitMqttEvent(
                type: MqttEventNames.PublishFailed,
                subject: request.Topic,
                status: "failed",
                channel: MqttEventNames.PublishFailed,
                payloadBytes: request.Payload.Length,
                payloadPreview: request.PayloadPreview,
                attributes: CreateEventAttributes(request, request.Payload.Length));
            TryEmitDiagnostic(
                MqttDiagnosticNames.PublishFailed,
                FlowDiagnosticLevel.Error,
                $"MQTT publish failed for '{request.Topic}'.",
                exception,
                CreateDiagnosticAttributes(request, request.Payload.Length));
        }
    }

    private bool EmitMqttEvent(
        string type,
        string? subject = null,
        string? status = null,
        string? channel = null,
        int? payloadBytes = null,
        string? payloadPreview = null,
        IReadOnlyDictionary<string, string>? attributes = null)
        => EmitEvent(new FlowEvent
        {
            Timestamp = _clock.UtcNow,
            Type = type,
            Source = Id.ToString(),
            SourceNodeId = Id,
            Subject = subject,
            Status = status,
            Channel = channel,
            PayloadBytes = payloadBytes,
            PayloadPreview = payloadPreview,
            Attributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal)
        });

    private MqttPublishRequest ResolveRequest(MqttPublishRequest input)
        => input with
        {
            Topic = string.IsNullOrWhiteSpace(input.Topic)
                ? _options.DefaultTopic
                : input.Topic,
            QualityOfService = input.QualityOfService ?? _options.QualityOfService,
            Retain = input.Retain ?? _options.Retain
        };

    private void ReportPublishError(
        int code,
        string message,
        MqttPublishRequest request,
        Exception? exception = null)
        => TryReportError(
            code,
            message,
            exception,
            CreateErrorContext(request));

    private static string CreateErrorContext(MqttPublishRequest request)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Topic))
        {
            values.Add($"topic={request.Topic}");
        }

        if (request.Payload is not null)
        {
            values.Add($"payloadBytes={request.Payload.Length}");
        }

        var qualityOfService = request.QualityOfService;
        if (qualityOfService.HasValue)
        {
            values.Add($"qualityOfService={qualityOfService.Value}");
        }

        var retain = request.Retain;
        if (retain.HasValue)
        {
            values.Add($"retain={retain.Value}");
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            values.Add($"correlationId={request.CorrelationId}");
        }

        return string.Join("; ", values);
    }

    private static Dictionary<string, object?> CreateDiagnosticAttributes(
        MqttPublishRequest request,
        int payloadBytes)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["topic"] = request.Topic,
            ["payloadBytes"] = payloadBytes,
            ["qualityOfService"] = request.QualityOfService,
            ["retain"] = request.Retain
        };

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            attributes["correlationId"] = request.CorrelationId;
        }

        return attributes;
    }

    private static Dictionary<string, string> CreateEventAttributes(
        MqttPublishRequest request,
        MqttPublishResult result)
        => CreateEventAttributes(request, result.PayloadBytes);

    private static Dictionary<string, string> CreateEventAttributes(
        MqttPublishRequest request,
        int payloadBytes)
    {
        var attributes = new Dictionary<string, string>
        {
            ["payloadBytes"] = payloadBytes.ToString(),
            ["qualityOfService"] = request.QualityOfService?.ToString() ?? string.Empty,
            ["retain"] = request.Retain?.ToString() ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            attributes["correlationId"] = request.CorrelationId;
        }

        return attributes;
    }
}
