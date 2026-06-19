using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Validation;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Nodes;

/// <summary>
/// A standalone MQTT publish node. Post a <c>FlowMessage&lt;MqttPublishRequest&gt;</c>
/// to <c>Input</c>; the node borrows the adapter the injected
/// <see cref="IMqttConnectionHandle"/> established and broadcasts a
/// <c>FlowMessage&lt;MqttPublishResult&gt;</c> on <c>Output</c> carrying the same
/// correlation id (failures on <c>Errors</c>, a note on <c>Events</c>). The node never
/// creates or disposes a client; if no client is established the request reports
/// not-connected on the error port. Works with nothing but a connection handle — no
/// engine.
/// </summary>
public sealed class MqttPublishNode : FlowNode<MqttPublishRequest, MqttPublishResult>
{
    private const string NotConnectedMessage =
        "MQTT publish node is not connected; establish the mqtt.connection client (host ConnectAsync) before publishing.";

    private readonly IMqttConnectionHandle _connection;
    private readonly MqttPublishOptions _options;
    private readonly TimeProvider _clock;

    public MqttPublishNode(
        IMqttConnectionHandle connection,
        MqttPublishOptions? options = null,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (options ?? new MqttPublishOptions()).BoundedCapacity
        })
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? new MqttPublishOptions();
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task ProcessAsync(FlowMessage<MqttPublishRequest> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = ResolveRequest(message.Payload);

        var topicValidation = MqttTopicValidator.ValidatePublishTopic(request.Topic);
        if (!topicValidation.IsValid)
        {
            ReportPublishError(
                MqttErrorCodes.PublishInvalidTopic,
                topicValidation.Message ?? "MQTT publish request uses an invalid topic.",
                request,
                message);
            return;
        }

        if (request.Payload is null)
        {
            ReportPublishError(
                MqttErrorCodes.PublishInvalidPayload,
                "MQTT publish request requires a payload.",
                request,
                message);
            return;
        }

        if (request.QualityOfService.HasValue &&
            !Enum.IsDefined(request.QualityOfService.Value))
        {
            ReportPublishError(
                MqttErrorCodes.PublishInvalidQualityOfService,
                "MQTT publish request uses an unsupported quality setting.",
                request,
                message);
            return;
        }

        // Borrow the adapter the connection node established. The publish node
        // never creates or disposes a client; if no client is established the
        // request reports not connected.
        if (!_connection.TryGetAdapter(out var adapter))
        {
            ReportNotConnected(request, message);
            return;
        }

        var publishTimeout = TimeSpan.FromMilliseconds(_options.PublishTimeoutMilliseconds);
        using var publishCancellation = CancellationTokenSource.CreateLinkedTokenSource(Stopping);
        publishCancellation.CancelAfter(publishTimeout);
        try
        {
            await adapter.PublishAsync(request, publishCancellation.Token)
                .AsTask()
                .WaitAsync(publishTimeout, Stopping)
                .ConfigureAwait(false);

            var result = new MqttPublishResult
            {
                Timestamp = _clock.GetUtcNow(),
                Topic = request.Topic!,
                PayloadBytes = request.Payload.Length,
                PayloadPreview = request.PayloadPreview,
                QualityOfService = request.QualityOfService ?? _options.QualityOfService,
                Retain = request.Retain ?? _options.Retain,
                CorrelationId = request.CorrelationId
            };

            // Carry the correlation id forward onto the result.
            Emit(message.With(result));
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = MqttEventNames.PublishSucceeded,
                Level = FlowEventLevel.Information,
                Message = $"Published MQTT message to '{request.Topic}'.",
                Attributes = CreateEventAttributes(request, result.PayloadBytes)
            });
        }
        catch (Exception exception) when (
            exception is TimeoutException ||
            (exception is OperationCanceledException &&
             publishCancellation.IsCancellationRequested &&
             !Stopping.IsCancellationRequested))
        {
            ReportPublishError(
                MqttErrorCodes.PublishTimedOut,
                $"MQTT publish timed out after {_options.PublishTimeoutMilliseconds} ms.",
                request,
                message,
                exception);
            EmitPublishFailedEvent(request, message, exception);
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            // Requested stop, not a failure.
        }
        catch (Exception exception)
        {
            ReportPublishError(
                MqttErrorCodes.PublishFailed,
                $"MQTT publish failed: {exception.Message}",
                request,
                message,
                exception);
            EmitPublishFailedEvent(request, message, exception);
        }
    }

    private void ReportNotConnected(MqttPublishRequest request, FlowMessage<MqttPublishRequest> source)
    {
        ReportPublishError(
            MqttErrorCodes.PublishNotConnected,
            NotConnectedMessage,
            request,
            source);
        EmitPublishFailedEvent(request, source);
    }

    private bool EmitPublishFailedEvent(
        MqttPublishRequest request,
        FlowMessage<MqttPublishRequest> source,
        Exception? exception = null)
        => EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = MqttEventNames.PublishFailed,
            Level = FlowEventLevel.Error,
            Message = exception is null
                ? NotConnectedMessage
                : $"MQTT publish failed for '{request.Topic}': {exception.Message}",
            Attributes = CreateEventAttributes(request, request.Payload?.Length ?? 0)
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
        FlowMessage<MqttPublishRequest> source,
        Exception? exception = null)
        => EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = CreateErrorContext(request),
            Exception = exception
        });

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

    private Dictionary<string, object?> CreateEventAttributes(
        MqttPublishRequest request,
        int payloadBytes)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["topic"] = request.Topic,
            ["payloadBytes"] = payloadBytes,
            ["qualityOfService"] = request.QualityOfService?.ToString(),
            ["retain"] = request.Retain,
            ["connectionName"] = _connection.ConnectionName
        };

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            attributes["correlationId"] = request.CorrelationId;
        }

        return attributes;
    }
}
