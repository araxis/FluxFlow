using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FluxFlow.Engine.DurableOutput;

internal sealed class SampleOutputDeliveryHandler(JsonTypeInfo<string> jsonTypeInfo) :
    IDurableOutputDeliveryHandler
{
    private readonly ConcurrentDictionary<DurableOutputKey, string> _deliveredByKey = new();
    private readonly TaskCompletionSource<string> _delivered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task<string> Delivered => _delivered.Task;

    public ValueTask DeliverAsync(
        DurableOutputEnvelope envelope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (envelope.IsError)
            throw new InvalidOperationException("This sample expects a value output.");

        var value = envelope.Payload.Deserialize(jsonTypeInfo)
            ?? throw new InvalidOperationException("The delivered string cannot be null.");
        var stored = _deliveredByKey.GetOrAdd(envelope.Key, value);
        if (!string.Equals(stored, value, StringComparison.Ordinal))
            throw new InvalidOperationException("A delivery key was reused with different content.");

        _delivered.TrySetResult(stored);
        return ValueTask.CompletedTask;
    }
}
