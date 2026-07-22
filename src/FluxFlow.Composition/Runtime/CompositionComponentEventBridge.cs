using System.Collections;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
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
                static attribute => ToFlowValue(attribute.Value),
                StringComparer.Ordinal)
        };
        var message = FlowMessage.Create(payload, source.CorrelationId) with
        {
            Timestamp = timestamp
        };

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

    private static FlowValue ToFlowValue(object? value)
    {
        try
        {
            return value switch
            {
                null => FlowValue.Null,
                FlowValue flowValue => flowValue,
                bool boolean => FlowValue.From(boolean),
                byte number => FlowValue.From(number),
                sbyte number => FlowValue.From(number),
                short number => FlowValue.From(number),
                ushort number => FlowValue.From(number),
                int number => FlowValue.From(number),
                uint number => FlowValue.From(number),
                long number => FlowValue.From(number),
                ulong number => FlowValue.From(new BigInteger(number)),
                BigInteger number => FlowValue.From(number),
                decimal number => FlowValue.From(number),
                float number => FlowValue.From(number),
                double number => FlowValue.From(number),
                string text => FlowValue.From(text),
                char character => FlowValue.From(character.ToString()),
                DateTimeOffset timestamp => FlowValue.From(timestamp),
                DateTime timestamp => FlowValue.From(new DateTimeOffset(timestamp)),
                DateOnly date => FlowValue.From(date),
                TimeOnly time => FlowValue.From(time),
                TimeSpan duration => FlowValue.From(duration),
                Guid id => FlowValue.From(id),
                byte[] bytes => FlowValue.FromBinary(bytes),
                ReadOnlyMemory<byte> bytes => FlowValue.FromBinary(bytes),
                IReadOnlyDictionary<string, object?> dictionary => FlowValue.FromObject(
                    dictionary.Select(static item =>
                        new KeyValuePair<string, FlowValue>(item.Key, ToFlowValue(item.Value)))),
                IDictionary dictionary => FlowValue.FromObject(
                    dictionary.Cast<DictionaryEntry>().Select(static item =>
                        new KeyValuePair<string, FlowValue>(
                            Convert.ToString(item.Key, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                            ToFlowValue(item.Value)))),
                IEnumerable items => FlowValue.FromArray(
                    items.Cast<object?>().Select(ToFlowValue)),
                _ => FromJson(value)
            };
        }
        catch
        {
            return FlowValue.From(value?.ToString() ?? string.Empty);
        }
    }

    private static FlowValue FromJson(object value)
        => JsonSerializer.Deserialize<FlowValue>(JsonSerializer.Serialize(value))
           ?? FlowValue.Null;
}
