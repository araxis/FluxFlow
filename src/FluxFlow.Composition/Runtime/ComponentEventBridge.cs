using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

internal sealed class ComponentEventBridge : IAsyncDisposable
{
    private const int Capacity = 256;

    private readonly string _componentAddress;
    private readonly FlowOutput<FlowMessage<ComponentEvent>> _output = new(
        new FlowOutputOptions { Capacity = Capacity });
    private readonly ActionBlock<FlowEvent> _forwarder;
    private readonly IDisposable? _sourceLink;
    private readonly Task _completion;
    private int _disposed;

    public ComponentEventBridge(
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
            ForwardAsync,
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

    public ISourceBlock<FlowMessage<ComponentEvent>> Output => _output;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _sourceLink?.Dispose();
        _forwarder.Complete();
        try
        {
            await _completion.ConfigureAwait(false);
        }
        finally
        {
            await _output.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ForwardAsync(FlowEvent source)
    {
        var timestamp = source.Timestamp == default
            ? DateTimeOffset.UtcNow
            : source.Timestamp;
        var payload = new ComponentEvent
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

        if (await _output.SendAsync(message).ConfigureAwait(false))
            return;

        await _output.Completion.ConfigureAwait(false);
        throw new InvalidOperationException(
            "Component event output is no longer accepting data.");
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
            _output.Complete();
            await _output.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _output.Fault(Unwrap(exception));
            await _output.Completion.ConfigureAwait(false);
        }
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

    private static Exception Unwrap(Exception exception)
        => exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
            ? aggregate.InnerException!
            : exception;
}
