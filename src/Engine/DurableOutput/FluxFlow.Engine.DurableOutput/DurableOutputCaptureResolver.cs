using System.Text.Json;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.DurableOutput;

internal sealed class DurableOutputCaptureResolver(
    DurableOutputConfiguration configuration,
    IDurableOutputStore store,
    TimeProvider clock) : IApplicationOutputCaptureResolver
{
    public IApplicationOutputCapture? Resolve(ApplicationAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!configuration.Captures.TryGetValue(address, out var definition))
            return null;
        return new DurableOutputCapture(
            definition.Address,
            definition.ContractName,
            store,
            clock);
    }
}

internal sealed class DurableOutputCapture(
    ApplicationAddress address,
    string contractName,
    IDurableOutputStore store,
    TimeProvider clock) : IApplicationOutputCapture
{
    private static readonly JsonElement NullPayload = FlowValue.Null.ToJsonElement();

    public async ValueTask CaptureAsync(
        FlowMessage message,
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
                : message.Value!.ToJsonElement();
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
}
