using FluxFlow.Data;
using FluxFlow.Nodes;
using System.Text.Json;

namespace FluxFlow.Components.FileSystem.Nodes;

internal sealed class FileSystemOperationPipeline<TInput> : FlowNode
{
    private readonly TimeProvider _clock;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _errorCode;
    private readonly string _rejectionEventName;
    private readonly Func<FlowMessage, TInput, CancellationToken, Task<FlowMessage>> _process;

    public FileSystemOperationPipeline(
        int boundedCapacity,
        TimeProvider clock,
        string operation,
        Func<FlowMessage, TInput, CancellationToken, Task<FlowMessage>> process)
        : base(CreateOptions(boundedCapacity))
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(process);
        _clock = clock;
        _errorCode = $"filesystem.{operation}.invalid_shape";
        _rejectionEventName = $"file.{operation}.rejected";
        _process = process;
    }

    public void PublishEvent(FlowEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        EmitEvent(@event);
    }

    protected override async Task ProcessAsync(FlowMessage message)
    {
        TInput input;
        try
        {
            input = (message.Value! is TInput typedInput0 ? typedInput0 : typeof(TInput) == typeof(JsonElement) ? (TInput)(object)message.Value!.Json : message.Value!.Json.Deserialize<TInput>(JsonOptions))
                ?? throw new JsonException($"{typeof(TInput).Name} cannot be null.");
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            FlowDiagnostics.ProcessingException(message, this, exception);
            var error = new FlowError(_errorCode,
                $"The workflow value cannot be materialized as {typeof(TInput).Name}.", "FileSystem",
                details: JsonSerializer.SerializeToElement(new { target = typeof(TInput).Name, reason = exception.Message }));
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = _rejectionEventName,
                Level = FlowEventLevel.Warning,
                Message = error.Message,
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["errorCode"] = error.Code
                }
            });
            await EmitAsync(message.WithError(error), Stopping).ConfigureAwait(false);
            return;
        }

        var result = await _process(message, input, Stopping).ConfigureAwait(false);
        await EmitAsync(result, Stopping).ConfigureAwait(false);
    }

    private static FlowNodeOptions CreateOptions(int boundedCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedCapacity, 1);
        return new FlowNodeOptions
        {
            InputCapacity = boundedCapacity,
            OutputCapacity = boundedCapacity
        };
    }
}
