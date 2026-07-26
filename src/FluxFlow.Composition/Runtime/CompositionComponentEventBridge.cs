using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

internal sealed class CompositionComponentEventBridge : IAsyncDisposable
{
    private const int Capacity = 256;

    private readonly string _componentAddress;
    private readonly BroadcastBlock<FlowMessage<CompositionComponentEvent>> _output = new(
        static message => message,
        new DataflowBlockOptions { BoundedCapacity = Capacity });
    private readonly ActionBlock<FlowEvent> _forwarder;
    private readonly IDisposable? _sourceLink;
    private readonly Task _completion;
    private int _disposed;

    public CompositionComponentEventBridge(
        string workflowName,
        string componentName,
        ISourceBlock<FlowEvent>? source,
        Task componentCompletion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);
        ArgumentNullException.ThrowIfNull(componentCompletion);

        _componentAddress = $"{workflowName}.{componentName}";
        _forwarder = new ActionBlock<FlowEvent>(
            Forward,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = Capacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _sourceLink = source?.LinkTo(
            _forwarder,
            new DataflowLinkOptions { PropagateCompletion = false });
        _completion = CompleteAsync(componentCompletion);
    }

    public ISourceBlock<FlowMessage<CompositionComponentEvent>> Output => _output;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _sourceLink?.Dispose();
        _forwarder.Complete();
        _output.Complete();
        await Task.WhenAll(_forwarder.Completion, _output.Completion).ConfigureAwait(false);
    }

    private void Forward(FlowEvent source)
    {
        var timestamp = source.Timestamp == default
            ? DateTimeOffset.UtcNow
            : source.Timestamp;
        var payload = new CompositionComponentEvent
        {
            ComponentAddress = _componentAddress,
            Timestamp = timestamp,
            Name = source.Name,
            Level = source.Level,
            Message = source.Message,
            Attributes = source.Attributes.ToDictionary(
                static attribute => attribute.Key,
                static attribute => ToInvariantText(attribute.Value),
                StringComparer.Ordinal)
        };
        var message = FlowMessage.Create(payload, source.CorrelationId);

        _output.Post(message);
    }

    private async Task CompleteAsync(Task componentCompletion)
    {
        try
        {
            await componentCompletion.ConfigureAwait(false);
        }
        catch
        {
            // Component completion remains the observable fault channel.
        }
        finally
        {
            _sourceLink?.Dispose();
            _forwarder.Complete();
        }

        try
        {
            await _forwarder.Completion.ConfigureAwait(false);
        }
        catch
        {
            // Event forwarding must not fault the component or application runtime.
        }

        _output.Complete();
        await _output.Completion.ConfigureAwait(false);
    }

    private static string ToInvariantText(object? value)
    {
        try
        {
            return value switch
            {
                null => string.Empty,
                string text => text,
                byte[] bytes => Convert.ToBase64String(bytes),
                ReadOnlyMemory<byte> bytes => Convert.ToBase64String(bytes.Span),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)
                    ?? string.Empty,
                _ => JsonSerializer.Serialize(value)
            };
        }
        catch
        {
            return value?.ToString() ?? string.Empty;
        }
    }
}
