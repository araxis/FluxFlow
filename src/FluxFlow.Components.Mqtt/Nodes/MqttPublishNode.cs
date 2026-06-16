using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Validation;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttPublishNode : EventFlowNodeBase, IAsyncDisposable
{
    private const string NotConnectedMessage =
        "MQTT publish node is not connected; the mqtt.connection component does not establish a client yet.";

    private readonly IMqttConnectionHandle _connection;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<MqttPublishRequest> _input;
    private readonly BufferBlock<MqttPublishResult> _result;
    private readonly MqttPublishOptions _options;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private bool _disposed;

    internal MqttPublishNode(
        MqttPublishOptions options,
        IMqttConnectionHandle connection,
        TimeProvider clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
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
        _lifecycleCancellation.Dispose();
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

    private Task HandleAsync(MqttPublishRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var request = ResolveRequest(input);
        var topicValidation = MqttTopicValidator.ValidatePublishTopic(request.Topic);
        if (!topicValidation.IsValid)
        {
            ReportPublishError(
                MqttErrorCodes.PublishInvalidTopic,
                topicValidation.Message ?? "MQTT publish request uses an invalid topic.",
                request);
            return Task.CompletedTask;
        }

        if (request.Payload is null)
        {
            ReportPublishError(
                MqttErrorCodes.PublishInvalidPayload,
                "MQTT publish request requires a payload.",
                request);
            return Task.CompletedTask;
        }

        if (request.QualityOfService.HasValue &&
            !Enum.IsDefined(request.QualityOfService.Value))
        {
            ReportPublishError(
                MqttErrorCodes.PublishInvalidQualityOfService,
                "MQTT publish request uses an unsupported quality setting.",
                request);
            return Task.CompletedTask;
        }

        // The mqtt.connection component holds configuration only; no client is
        // established yet, so every otherwise-valid request reports not connected.
        ReportPublishError(
            MqttErrorCodes.PublishNotConnected,
            NotConnectedMessage,
            request);
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
            NotConnectedMessage,
            attributes: CreateDiagnosticAttributes(request, request.Payload.Length));
        return Task.CompletedTask;
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
            Timestamp = _clock.GetUtcNow(),
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

    private string CreateErrorContext(MqttPublishRequest request)
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

        values.Add($"connectionName={_connection.ConnectionName}");

        return string.Join("; ", values);
    }

    private Dictionary<string, object?> CreateDiagnosticAttributes(
        MqttPublishRequest request,
        int payloadBytes)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["topic"] = request.Topic,
            ["payloadBytes"] = payloadBytes,
            ["qualityOfService"] = request.QualityOfService,
            ["retain"] = request.Retain,
            ["connectionName"] = _connection.ConnectionName
        };

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            attributes["correlationId"] = request.CorrelationId;
        }

        return attributes;
    }

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
