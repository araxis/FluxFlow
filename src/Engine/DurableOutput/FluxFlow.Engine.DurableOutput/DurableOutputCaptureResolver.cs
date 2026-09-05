using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FluxFlow.Composition.Addressing;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.DurableOutput;

internal sealed class DurableOutputCaptureResolver(
    DurableOutputConfiguration configuration,
    IDurableOutputStore store,
    TimeProvider clock) : IApplicationOutputCaptureResolver
{
    public IApplicationOutputCapture<T>? Resolve<T>(ApplicationAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!configuration.Captures.TryGetValue(address, out var definition))
            return null;
        if (definition.PayloadType != typeof(T) || definition.JsonTypeInfo is not JsonTypeInfo<T> jsonTypeInfo)
        {
            throw new InvalidOperationException(
                $"Durable output '{address}' expects payload type '{definition.PayloadType}', not '{typeof(T)}'.");
        }

        return new DurableOutputCapture<T>(
            definition.Address,
            definition.ContractName,
            jsonTypeInfo,
            store,
            clock);
    }
}

internal sealed class DurableOutputCapture<T>(
    ApplicationAddress address,
    string contractName,
    JsonTypeInfo<T> jsonTypeInfo,
    IDurableOutputStore store,
    TimeProvider clock) : IApplicationOutputCapture<T>
{
    private static readonly JsonElement NullPayload = CreateNullPayload();

    public async ValueTask CaptureAsync(
        FlowMessage<T> message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var durationStartedAt = DurableOutputInstrumentation.StartCaptureDuration(clock);
        var activity = DurableOutputInstrumentation.StartCaptureActivity(message.TraceId);
        var instrumentationResult = "failed";
        try
        {
            var payload = message.IsError
                ? NullPayload
                : JsonSerializer.SerializeToElement(message.Value, jsonTypeInfo);
            var envelope = new DurableOutputEnvelope(
                address,
                contractName,
                message.IsError,
                payload,
                message.Error,
                message.MessageId,
                message.TraceId,
                message.Timestamp,
                clock.GetUtcNow(),
                message.CorrelationId,
                message.CausationId,
                message.Headers);

            var result = await store.EnqueueAsync(envelope, cancellationToken).ConfigureAwait(false);
            if (result is null || result.Key != envelope.Key)
            {
                throw new InvalidOperationException(
                    "The durable output store returned an enqueue result for a different key.");
            }

            if (result.Status == DurableOutputEnqueueStatus.Conflict)
            {
                instrumentationResult = "conflict";
                throw new InvalidOperationException(
                    $"Durable output '{envelope.Key}' conflicts with different persisted content.");
            }

            if (!result.IsAccepted)
                throw new InvalidOperationException($"Unknown durable output enqueue status '{result.Status}'.");

            instrumentationResult = result.Status switch
            {
                DurableOutputEnqueueStatus.Enqueued => "enqueued",
                DurableOutputEnqueueStatus.AlreadyExists => "already_exists",
                _ => "failed"
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            instrumentationResult = "canceled";
            throw;
        }
        finally
        {
            DurableOutputInstrumentation.CompleteCapture(
                instrumentationResult,
                clock,
                durationStartedAt,
                activity);
        }
    }

    private static JsonElement CreateNullPayload()
    {
        using var document = JsonDocument.Parse("null");
        return document.RootElement.Clone();
    }
}
