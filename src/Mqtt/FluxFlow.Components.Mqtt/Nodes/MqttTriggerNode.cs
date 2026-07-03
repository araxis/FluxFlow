using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Validation;
using FluxFlow.Components.RequestReply;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

/// <summary>
/// A standalone MQTT trigger source. The node opens one subscription through the
/// injected <see cref="IMqttTriggerSource"/>, emits received messages on
/// <see cref="FlowSource{TOutput}.Output"/>, and can optionally wait for a correlated
/// <see cref="MqttTriggerResponse"/> before acknowledging the received message.
/// </summary>
public sealed class MqttTriggerNode : FlowSource<MqttReceivedMessage>
{
    private readonly IMqttTriggerSource _triggerSource;
    private readonly MqttTriggerOptions _options;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<FlowMessage<MqttTriggerResponse>> _responses;
    private readonly CorrelatedRequestTracker<PendingDelivery, MqttTriggerResponse>? _tracker;

    public MqttTriggerNode(
        IMqttTriggerSource triggerSource,
        MqttTriggerOptions? options = null,
        TimeProvider? clock = null)
        : base(BuildSourceOptions(options))
    {
        _triggerSource = triggerSource ?? throw new ArgumentNullException(nameof(triggerSource));
        _options = options ?? new MqttTriggerOptions();
        ValidateOptions(_options);
        _clock = clock ?? TimeProvider.System;

        _responses = new ActionBlock<FlowMessage<MqttTriggerResponse>>(
            HandleResponseAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _options.BoundedCapacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });

        _tracker = _options.Mode == MqttTriggerMode.RequestReply
            ? new CorrelatedRequestTracker<PendingDelivery, MqttTriggerResponse>(
                CompleteTrackedResponseAsync,
                FailTrackedRequestAsync,
                new CorrelatedRequestTrackerOptions
                {
                    Timeout = _options.ResponseTimeout,
                    SweepInterval = CreateSweepInterval(_options.ResponseTimeout)
                },
                _clock)
            : null;
    }

    public ITargetBlock<FlowMessage<MqttTriggerResponse>> Responses => _responses;

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        IMqttSubscription? subscription = null;

        try
        {
            subscription = await _triggerSource
                .SubscribeAsync(_options, cancellationToken)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(subscription);

            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                Name = MqttEventNames.TriggerStarted,
                Level = FlowEventLevel.Information,
                Message = $"Started MQTT trigger '{_options.TopicFilter}'.",
                Attributes = CreateTriggerAttributes()
            });

            await PumpAsync(subscription, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (MqttClientUnavailableException exception)
        {
            ReportTriggerError(
                MqttErrorCodes.TriggerNotConnected,
                exception.Message,
                exception,
                FlowEventLevel.Information);
        }
        catch (Exception exception)
        {
            ReportTriggerError(
                MqttErrorCodes.TriggerStartupFailed,
                $"MQTT trigger startup failed: {exception.Message}",
                exception);
        }
        finally
        {
            await CompleteResponsesAsync().ConfigureAwait(false);
            if (_tracker is not null)
            {
                await _tracker.FailAllAsync(new OperationCanceledException("MQTT trigger stopped."))
                    .ConfigureAwait(false);
                await _tracker.DisposeAsync().ConfigureAwait(false);
            }

            if (subscription is not null)
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
                EmitEvent(new FlowEvent
                {
                    Timestamp = _clock.GetUtcNow(),
                    Name = MqttEventNames.TriggerStopped,
                    Level = FlowEventLevel.Information,
                    Message = $"Stopped MQTT trigger '{_options.TopicFilter}'.",
                    Attributes = CreateTriggerAttributes()
                });
            }
        }
    }

    protected override async ValueTask OnDisposeAsync()
    {
        await CompleteResponsesAsync().ConfigureAwait(false);
        if (_tracker is not null)
        {
            await _tracker.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task PumpAsync(IMqttSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var context in subscription.Messages
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                try
                {
                    await ProcessReceivedAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    ReportTriggerError(
                        MqttErrorCodes.TriggerFailed,
                        $"MQTT trigger message handling failed: {exception.Message}",
                        exception);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportTriggerError(
                MqttErrorCodes.TriggerFailed,
                $"MQTT trigger failed: {exception.Message}",
                exception);
        }
    }

    private async Task ProcessReceivedAsync(
        IMqttReceivedContext? context,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            ReportTriggerError(
                MqttErrorCodes.TriggerFailed,
                "MQTT trigger received a null message context.",
                new ArgumentNullException(nameof(context)),
                FlowEventLevel.Warning);
            return;
        }

        var message = context.Message;
        if (message is null)
        {
            ReportTriggerError(
                MqttErrorCodes.TriggerFailed,
                "MQTT trigger received a null message payload.",
                new ArgumentNullException(nameof(context.Message)),
                FlowEventLevel.Warning);
            return;
        }

        var envelope = FlowMessage.Create(message, ToCorrelationId(message.CorrelationId));
        EmitReceivedEvent(envelope);

        if (_options.Mode == MqttTriggerMode.RequestReply)
        {
            var tracker = _tracker ?? throw new InvalidOperationException(
                "MQTT trigger request/reply tracker is not configured.");
            var pending = new PendingDelivery(context);
            var startResult = tracker.TryAdd(envelope.CorrelationId, pending);
            if (startResult == CorrelatedRequestStartResult.DuplicateCorrelationId)
            {
                var exception = new InvalidOperationException(
                    $"MQTT trigger already has a pending response for correlation id '{envelope.CorrelationId}'.");
                ReportTriggerError(
                    MqttErrorCodes.TriggerDuplicateCorrelation,
                    exception.Message,
                    exception,
                    FlowEventLevel.Warning,
                    message,
                    envelope.CorrelationId);
                await RejectIfNeededAsync(context, exception, envelope.CorrelationId).ConfigureAwait(false);
                return;
            }

            if (startResult == CorrelatedRequestStartResult.Stopped)
            {
                var exception = new InvalidOperationException("MQTT trigger is stopping.");
                ReportTriggerError(
                    MqttErrorCodes.TriggerFailed,
                    exception.Message,
                    exception,
                    FlowEventLevel.Warning,
                    message,
                    envelope.CorrelationId);
                await RejectIfNeededAsync(context, exception, envelope.CorrelationId).ConfigureAwait(false);
                return;
            }

            if (!await EmitAsync(envelope, cancellationToken).ConfigureAwait(false))
            {
                tracker.TryRemove(envelope.CorrelationId, out _);
                var exception = new InvalidOperationException("MQTT trigger output is not accepting messages.");
                ReportTriggerError(
                    MqttErrorCodes.TriggerFailed,
                    exception.Message,
                    exception,
                    FlowEventLevel.Warning,
                    message,
                    envelope.CorrelationId);
                await RejectIfNeededAsync(context, exception, envelope.CorrelationId).ConfigureAwait(false);
                return;
            }

            if (_options.Acknowledgement == MqttTriggerAcknowledgement.OnEmit)
            {
                await AcknowledgeAsync(context, envelope.CorrelationId).ConfigureAwait(false);
            }

            return;
        }

        if (!await EmitAsync(envelope, cancellationToken).ConfigureAwait(false))
        {
            var exception = new InvalidOperationException("MQTT trigger output is not accepting messages.");
            ReportTriggerError(
                MqttErrorCodes.TriggerFailed,
                exception.Message,
                exception,
                FlowEventLevel.Warning,
                message,
                envelope.CorrelationId);
            await RejectIfNeededAsync(context, exception, envelope.CorrelationId).ConfigureAwait(false);
            return;
        }

        if (_options.Acknowledgement == MqttTriggerAcknowledgement.OnEmit)
        {
            await AcknowledgeAsync(context, envelope.CorrelationId).ConfigureAwait(false);
        }
    }

    private async Task HandleResponseAsync(FlowMessage<MqttTriggerResponse> response)
    {
        try
        {
            if (_tracker is null
                || !await _tracker.TryCompleteAsync(response).ConfigureAwait(false))
            {
                EmitEvent(new FlowEvent
                {
                    Timestamp = _clock.GetUtcNow(),
                    CorrelationId = response.CorrelationId,
                    Name = MqttEventNames.TriggerResponseIgnored,
                    Level = FlowEventLevel.Warning,
                    Message = "MQTT trigger response did not match a pending message.",
                    Attributes = CreateTriggerAttributes()
                });
                return;
            }
        }
        catch (Exception exception)
        {
            ReportTriggerError(
                MqttErrorCodes.TriggerResponseFailed,
                $"MQTT trigger response handling failed: {exception.Message}",
                exception);
        }
    }

    private async ValueTask CompleteTrackedResponseAsync(
        CorrelationId correlationId,
        PendingDelivery pending,
        FlowMessage<MqttTriggerResponse> response,
        CancellationToken cancellationToken)
    {
        var triggerResponse = response.Payload
            ?? MqttTriggerResponse.Failure("MQTT trigger response payload was null.");

        if (triggerResponse.Succeeded)
        {
            if (_options.Acknowledgement == MqttTriggerAcknowledgement.OnSuccessfulResponse)
            {
                await AcknowledgeAsync(pending.Context, correlationId)
                    .ConfigureAwait(false);
            }

            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = correlationId,
                Name = MqttEventNames.TriggerResponseSucceeded,
                Level = FlowEventLevel.Information,
                Message = "MQTT trigger response completed successfully.",
                Attributes = CreateTriggerAttributes(pending.Context.Message)
            });
            return;
        }

        var exception = new InvalidOperationException(
            string.IsNullOrWhiteSpace(triggerResponse.ErrorMessage)
                ? "MQTT trigger response failed."
                : triggerResponse.ErrorMessage);

        if (_options.Acknowledgement == MqttTriggerAcknowledgement.OnSuccessfulResponse)
        {
            await RejectAsync(pending.Context, exception, correlationId)
                .ConfigureAwait(false);
        }

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = correlationId,
            Name = MqttEventNames.TriggerResponseFailed,
            Level = FlowEventLevel.Warning,
            Message = exception.Message,
            Attributes = CreateTriggerAttributes(pending.Context.Message)
        });
    }

    private async ValueTask FailTrackedRequestAsync(
        CorrelationId correlationId,
        PendingDelivery pending,
        Exception error,
        CancellationToken cancellationToken)
    {
        if (_options.Acknowledgement == MqttTriggerAcknowledgement.OnSuccessfulResponse)
        {
            await RejectAsync(pending.Context, error, correlationId).ConfigureAwait(false);
        }

        if (error is TimeoutException)
        {
            ReportTriggerError(
                MqttErrorCodes.TriggerResponseTimedOut,
                $"MQTT trigger response timed out after {_options.ResponseTimeout}.",
                error,
                FlowEventLevel.Warning,
                pending.Context.Message,
                correlationId);
        }
    }

    private async ValueTask AcknowledgeAsync(
        IMqttReceivedContext context,
        CorrelationId correlationId)
    {
        try
        {
            await context.AckAsync(CancellationToken.None).ConfigureAwait(false);
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = correlationId,
                Name = MqttEventNames.TriggerAcknowledged,
                Level = FlowEventLevel.Information,
                Message = "Acknowledged MQTT trigger message.",
                Attributes = CreateTriggerAttributes(context.Message)
            });
        }
        catch (Exception exception)
        {
            ReportTriggerError(
                MqttErrorCodes.TriggerAcknowledgementFailed,
                $"MQTT trigger acknowledgement failed: {exception.Message}",
                exception,
                FlowEventLevel.Error,
                context.Message,
                correlationId);
        }
    }

    private async ValueTask RejectIfNeededAsync(
        IMqttReceivedContext context,
        Exception error,
        CorrelationId correlationId)
    {
        if (_options.Acknowledgement == MqttTriggerAcknowledgement.None)
        {
            return;
        }

        await RejectAsync(context, error, correlationId).ConfigureAwait(false);
    }

    private async ValueTask RejectAsync(
        IMqttReceivedContext context,
        Exception error,
        CorrelationId correlationId)
    {
        try
        {
            await context.NackAsync(error, CancellationToken.None).ConfigureAwait(false);
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = correlationId,
                Name = MqttEventNames.TriggerRejected,
                Level = FlowEventLevel.Warning,
                Message = "Rejected MQTT trigger message.",
                Attributes = CreateTriggerAttributes(context.Message)
            });
        }
        catch (Exception exception)
        {
            ReportTriggerError(
                MqttErrorCodes.TriggerAcknowledgementFailed,
                $"MQTT trigger rejection failed: {exception.Message}",
                exception,
                FlowEventLevel.Error,
                context.Message,
                correlationId);
        }
    }

    private async ValueTask CompleteResponsesAsync()
    {
        _responses.Complete();
        try
        {
            await _responses.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
        }
    }

    private void EmitReceivedEvent(FlowMessage<MqttReceivedMessage> envelope)
    {
        var message = envelope.Payload;
        var payloadBytes = message.Payload?.Length ?? 0;

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = envelope.CorrelationId,
            Name = MqttEventNames.TriggerReceived,
            Level = FlowEventLevel.Information,
            Message = $"Received MQTT trigger message from '{message.Topic}'.",
            Attributes = CreateTriggerAttributes(message, payloadBytes)
        });
    }

    private static CorrelationId? ToCorrelationId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new CorrelationId(value);

    private void ReportTriggerError(
        int code,
        string message,
        Exception? exception = null,
        FlowEventLevel level = FlowEventLevel.Error,
        MqttReceivedMessage? receivedMessage = null,
        CorrelationId? correlationId = null)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = correlationId,
            Code = code,
            Message = message,
            Context = CreateTriggerContext(receivedMessage),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = correlationId,
            Name = MqttEventNames.TriggerFailed,
            Level = level,
            Message = message,
            Attributes = CreateTriggerAttributes(receivedMessage)
        });
    }

    private string CreateTriggerContext(MqttReceivedMessage? message = null)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(_options.TopicFilter))
        {
            values.Add($"topicFilter={_options.TopicFilter}");
        }

        if (!string.IsNullOrWhiteSpace(message?.Topic))
        {
            values.Add($"topic={message.Topic}");
        }

        values.Add($"qualityOfService={_options.QualityOfService}");
        values.Add($"receiveRetainedMessages={_options.ReceiveRetainedMessages}");
        values.Add($"retainAsPublished={_options.RetainAsPublished}");
        values.Add($"mode={_options.Mode}");
        values.Add($"acknowledgement={_options.Acknowledgement}");
        return string.Join("; ", values);
    }

    private Dictionary<string, object?> CreateTriggerAttributes(
        MqttReceivedMessage? message = null,
        int? payloadBytes = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["topicFilter"] = _options.TopicFilter,
            ["qualityOfService"] = _options.QualityOfService.ToString(),
            ["receiveRetainedMessages"] = _options.ReceiveRetainedMessages,
            ["retainAsPublished"] = _options.RetainAsPublished,
            ["mode"] = _options.Mode.ToString(),
            ["acknowledgement"] = _options.Acknowledgement.ToString(),
            ["responseTimeout"] = _options.ResponseTimeout
        };

        if (message is not null)
        {
            attributes["topic"] = message.Topic;
            attributes["payloadBytes"] = payloadBytes ?? message.Payload?.Length ?? 0;
            attributes["retain"] = message.Retain;
            attributes["correlationId"] = message.CorrelationId;
            attributes["responseTopic"] = message.ResponseTopic;
        }

        return attributes;
    }

    private static void ValidateOptions(MqttTriggerOptions options)
    {
        var topicValidation = MqttTopicValidator.ValidateSubscriptionFilter(options.TopicFilter);
        if (!topicValidation.IsValid)
        {
            throw new ArgumentException(
                topicValidation.Message ?? "MQTT trigger uses an invalid topic filter.",
                nameof(options));
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.BoundedCapacity,
                "MQTT trigger bounded capacity must be greater than zero.");
        }

        if (options.ResponseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ResponseTimeout,
                "MQTT trigger response timeout must be greater than zero.");
        }

        if (!Enum.IsDefined(options.QualityOfService))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.QualityOfService,
                "MQTT trigger options use an unsupported quality setting.");
        }

        if (!Enum.IsDefined(options.Mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Mode,
                "MQTT trigger mode is not supported.");
        }

        if (!Enum.IsDefined(options.Acknowledgement))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Acknowledgement,
                "MQTT trigger acknowledgement mode is not supported.");
        }

        if (options.Acknowledgement == MqttTriggerAcknowledgement.OnSuccessfulResponse
            && options.Mode != MqttTriggerMode.RequestReply)
        {
            throw new ArgumentException(
                "Acknowledging on successful response requires MQTT trigger request/reply mode.",
                nameof(options));
        }
    }

    private static FlowSourceOptions BuildSourceOptions(MqttTriggerOptions? options)
    {
        options ??= new MqttTriggerOptions();
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.BoundedCapacity,
                "MQTT trigger bounded capacity must be greater than zero.");
        }

        return new FlowSourceOptions { OutputCapacity = options.BoundedCapacity };
    }

    private static TimeSpan CreateSweepInterval(TimeSpan timeout)
        => timeout < TimeSpan.FromSeconds(1) ? timeout : TimeSpan.FromSeconds(1);

    private sealed record PendingDelivery(IMqttReceivedContext Context);
}
