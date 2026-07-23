using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Sources.Diagnostics;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sources.Nodes;

/// <summary>
/// Emits configured immutable workflow values as source messages.
/// </summary>
public sealed class GeneratedSourceNode : IFlowSource
{
    public const string Started = SourceDiagnosticNames.GeneratedStarted;
    public const string Emitted = SourceDiagnosticNames.GeneratedEmitted;
    public const string Completed = SourceDiagnosticNames.GeneratedCompleted;
    public const string Failed = SourceDiagnosticNames.GeneratedFailed;

    private readonly GeneratedSource _source;

    public GeneratedSourceNode(
        GeneratedSourceOptions options,
        IReadOnlyList<FlowValue> items,
        TimeProvider? clock = null)
        => _source = new GeneratedSource(options, items, clock);

    public ISourceBlock<FlowMessage<FlowValue>> Output => _source.Output;

    public ISourceBlock<FlowEvent> Events => _source.Events;

    public Task Completion => _source.Completion;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _source.StartAsync(cancellationToken);

    public void Complete() => _source.Complete();

    public void Fault(Exception exception) => _source.Fault(exception);

    public ValueTask DisposeAsync() => _source.DisposeAsync();
}

internal sealed class GeneratedSource : FlowSource<FlowValue>
{
    private readonly GeneratedSourceOptions _options;
    private readonly IReadOnlyList<FlowValue> _items;
    private readonly TimeProvider _clock;

    internal GeneratedSource(
        GeneratedSourceOptions options,
        IReadOnlyList<FlowValue> items,
        TimeProvider? clock = null)
        : base(BuildSourceOptions(options))
    {
        _options = ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(items);
        _items = items.Select(static item => item ?? FlowValue.Null).ToArray();
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        var emitted = 0;
        try
        {
            PublishDiagnostic(
                GeneratedSourceNode.Started,
                "source.generated started.",
                emitted);
            await SourceNodeTiming.DelayInitialAsync(
                _options.InitialDelayMilliseconds,
                _clock,
                cancellationToken).ConfigureAwait(false);

            var targetCount = ResolveTargetCount();
            for (var index = 0; index < targetCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = _items[index % _items.Count];
                if (!await EmitAsync(
                    FlowMessage.Create(item),
                    cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                emitted++;
                PublishDiagnostic(
                    GeneratedSourceNode.Emitted,
                    "source.generated emitted item.",
                    emitted);
                if (index < targetCount - 1)
                {
                    await SourceNodeTiming.DelayIntervalAsync(
                        _options.IntervalMilliseconds,
                        _clock,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            PublishDiagnostic(
                GeneratedSourceNode.Completed,
                "source.generated completed.",
                emitted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishDiagnostic(
                GeneratedSourceNode.Completed,
                "source.generated stopped.",
                emitted);
        }
        catch (Exception exception)
        {
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                Name = GeneratedSourceNode.Failed,
                Level = FlowEventLevel.Error,
                Message = $"source.generated failed: {exception.Message}",
                Attributes = CreateAttributes(emitted, exception)
            });
            throw;
        }
    }

    private int ResolveTargetCount()
    {
        if (_items.Count == 0)
            return 0;

        return _options.Loop
            ? _options.MaxItems!.Value
            : Math.Min(_options.MaxItems ?? _items.Count, _items.Count);
    }

    private void PublishDiagnostic(string name, string message, int emitted)
        => EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = name,
            Level = FlowEventLevel.Information,
            Message = message,
            Attributes = CreateAttributes(emitted)
        });

    private Dictionary<string, object?> CreateAttributes(
        int emitted,
        Exception? exception = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["boundedCapacity"] = _options.BoundedCapacity,
            ["emitted"] = emitted,
            ["items"] = _items.Count,
            ["loop"] = _options.Loop,
            ["name"] = _options.EffectiveName,
            ["outputType"] = nameof(FlowValue)
        };
        if (exception is not null)
        {
            attributes["exceptionType"] =
                exception.GetType().FullName ?? exception.GetType().Name;
        }

        return attributes;
    }

    private static FlowSourceOptions BuildSourceOptions(GeneratedSourceOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.generated bounded capacity must be greater than zero.");
        }

        return new FlowSourceOptions { OutputCapacity = options.BoundedCapacity };
    }

    private static GeneratedSourceOptions ValidateOptions(GeneratedSourceOptions options)
    {
        if (options.InitialDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.generated option 'initialDelayMilliseconds' cannot be negative.");
        }

        if (options.IntervalMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.generated option 'intervalMilliseconds' cannot be negative.");
        }

        if (options.MaxItems.HasValue && options.MaxItems.Value <= 0)
        {
            throw new ArgumentException(
                "source.generated option 'maxItems' must be greater than zero.",
                nameof(options));
        }

        if (options.Loop && !options.MaxItems.HasValue)
        {
            throw new ArgumentException(
                "source.generated option 'maxItems' is required when 'loop' is true.",
                nameof(options));
        }

        return options;
    }
}
