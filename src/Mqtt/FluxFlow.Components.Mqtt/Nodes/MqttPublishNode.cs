using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Validation;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Nodes;

/// <summary>
/// A standalone MQTT publish node. Post a <c>FlowMessage&lt;MqttPublishRequest&gt;</c>
/// to <c>Input</c>; the node publishes through the injected
/// <see cref="IMqttPublisher"/> and broadcasts a
/// <c>FlowMessage&lt;MqttPublishResult&gt;</c> on <c>Output</c> carrying the same
/// correlation id (failures on <c>Errors</c>, a note on <c>Events</c>). The node
/// never creates, starts, stops, reconnects, or disposes a client. Works with
/// only a publisher implementation; no engine is required.
/// </summary>
public sealed class MqttPublishNode : FlowNode<MqttPublishRequest, MqttPublishResult>
{
    private const string NotConnectedMessage =
        "MQTT publisher is not available.";

    private readonly IMqttPublisher _publisher;
    private readonly MqttPublishOptions _options;
    private readonly TimeProvider _clock;

    public MqttPublishNode(
        IMqttPublisher publisher,
        MqttPublishOptions? options = null,
        TimeProvider? clock = null)
        : this(publisher, ValidateOptions(options), clock, validated: true)
    {
    }

    private MqttPublishNode(
        IMqttPublisher publisher,
        MqttPublishOptions options,
        TimeProvider? clock,
        bool validated)
        : base(new FlowNodeOptions { InputCapacity = options.BoundedCapacity })
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options;
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task ProcessAsync(FlowMessage<MqttPublishRequest> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = message.Payload;

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

        if (!Enum.IsDefined(request.QualityOfService))
        {
            ReportPublishError(
                MqttErrorCodes.PublishInvalidQualityOfService,
                "MQTT publish request uses an unsupported quality setting.",
                request,
                message);
            return;
        }

        var publishTimeout = TimeSpan.FromMilliseconds(_options.PublishTimeoutMilliseconds);
        using var publishCancellation = CancellationTokenSource.CreateLinkedTokenSource(Stopping);
        publishCancellation.CancelAfter(publishTimeout);
        try
        {
            await _publisher.PublishAsync(request, publishCancellation.Token)
                .AsTask()
                .WaitAsync(publishTimeout, Stopping)
                .ConfigureAwait(false);

            var result = new MqttPublishResult
            {
                Timestamp = _clock.GetUtcNow(),
                Topic = request.Topic!,
                PayloadBytes = request.Payload.Length,
                PayloadPreview = request.PayloadPreview,
                QualityOfService = request.QualityOfService,
                Retain = request.Retain,
                Properties = request.Properties
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
        catch (MqttClientUnavailableException exception)
        {
            ReportNotConnected(request, message, exception);
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

    private void ReportNotConnected(
        MqttPublishRequest request,
        FlowMessage<MqttPublishRequest> source,
        Exception? exception = null)
    {
        ReportPublishError(
            MqttErrorCodes.PublishNotConnected,
            exception?.Message ?? NotConnectedMessage,
            request,
            source,
            exception);
        EmitPublishFailedEvent(request, source, exception);
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
                : exception is MqttClientUnavailableException
                    ? exception.Message
                    : $"MQTT publish failed for '{request.Topic}': {exception.Message}",
            Attributes = CreateEventAttributes(request, request.Payload?.Length ?? 0)
        });

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

        values.Add($"qualityOfService={request.QualityOfService}");
        values.Add($"retain={request.Retain}");

        if (!string.IsNullOrWhiteSpace(request.Properties?.CorrelationId))
        {
            values.Add($"mqttCorrelationId={request.Properties.CorrelationId}");
        }

        if (!string.IsNullOrWhiteSpace(request.Properties?.ResponseTopic))
        {
            values.Add($"responseTopic={request.Properties.ResponseTopic}");
        }

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
            ["qualityOfService"] = request.QualityOfService.ToString(),
            ["retain"] = request.Retain
        };

        if (!string.IsNullOrWhiteSpace(request.Properties?.CorrelationId))
        {
            attributes["mqttCorrelationId"] = request.Properties.CorrelationId;
        }

        if (!string.IsNullOrWhiteSpace(request.Properties?.ResponseTopic))
        {
            attributes["responseTopic"] = request.Properties.ResponseTopic;
        }

        return attributes;
    }

    private static MqttPublishOptions ValidateOptions(MqttPublishOptions? options)
    {
        var resolved = options ?? new MqttPublishOptions();

        if (resolved.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                resolved.BoundedCapacity,
                "MQTT publish bounded capacity must be greater than zero.");
        }

        if (resolved.PublishTimeoutMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                resolved.PublishTimeoutMilliseconds,
                "MQTT publish timeout must be greater than zero.");
        }

        return resolved;
    }
}
