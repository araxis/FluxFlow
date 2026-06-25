using FluxFlow.Components.Sources.Diagnostics;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sources.Nodes;

/// <summary>
/// A standalone generated source. Call <c>StartAsync</c> and the node broadcasts its
/// pre-materialized <typeparamref name="TOutput"/> items as <c>FlowMessage&lt;TOutput&gt;</c>
/// on <c>Output</c> (each minting a fresh correlation id), honoring the configured
/// <see cref="GeneratedSourceOptions.MaxItems"/>/<see cref="GeneratedSourceOptions.Loop"/>
/// count plus the initial delay and inter-item interval off the injected
/// <see cref="TimeProvider"/>, then completes. Lifecycle notes are emitted on <c>Events</c>
/// using <see cref="SourceDiagnosticNames"/>; failures surface a <see cref="FlowError"/> on
/// <c>Errors</c>. The host materializes/deserializes the items it wants to emit and hands
/// them in directly — no engine, registry, or string-to-Type resolution.
/// </summary>
public sealed class GeneratedSourceNode<TOutput> : FlowSource<TOutput>
{
    public const string Started = SourceDiagnosticNames.GeneratedStarted;
    public const string Emitted = SourceDiagnosticNames.GeneratedEmitted;
    public const string Completed = SourceDiagnosticNames.GeneratedCompleted;
    public const string Failed = SourceDiagnosticNames.GeneratedFailed;

    private readonly GeneratedSourceOptions _options;
    private readonly IReadOnlyList<TOutput> _items;
    private readonly TimeProvider _clock;

    public GeneratedSourceNode(
        GeneratedSourceOptions options,
        IReadOnlyList<TOutput> items,
        TimeProvider? clock = null)
        : base(BuildSourceOptions(options))
    {
        _options = options;
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _clock = clock ?? TimeProvider.System;

        if (_options.InitialDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.generated option 'initialDelayMilliseconds' cannot be negative.");
        }

        if (_options.IntervalMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.generated option 'intervalMilliseconds' cannot be negative.");
        }

        if (_options.MaxItems.HasValue && _options.MaxItems.Value <= 0)
        {
            throw new ArgumentException(
                "source.generated option 'maxItems' must be greater than zero.",
                nameof(options));
        }

        if (_options.Loop && !_options.MaxItems.HasValue)
        {
            throw new ArgumentException(
                "source.generated option 'maxItems' is required when 'loop' is true.",
                nameof(options));
        }
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        var emitted = 0;
        try
        {
            EmitDiagnostic(Started, "source.generated started.", CreateAttributes(emitted));
            await SourceNodeTiming.DelayInitialAsync(
                _options.InitialDelayMilliseconds,
                _clock,
                cancellationToken).ConfigureAwait(false);

            var targetCount = ResolveTargetCount();
            for (var index = 0; index < targetCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = _items[index % _items.Count];
                if (!await EmitAsync(FlowMessage.Create(item), cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                emitted++;
                EmitDiagnostic(Emitted, "source.generated emitted item.", CreateAttributes(emitted));
                if (index < targetCount - 1)
                {
                    await SourceNodeTiming.DelayIntervalAsync(
                        _options.IntervalMilliseconds,
                        _clock,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            CompleteGenerated(emitted, "source.generated completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CompleteGenerated(emitted, "source.generated stopped.");
        }
        catch (Exception exception)
        {
            ReportFailure(exception, emitted);
            throw;
        }
    }

    private int ResolveTargetCount()
    {
        if (_items.Count == 0)
        {
            return 0;
        }

        return _options.Loop
            ? _options.MaxItems!.Value
            : Math.Min(_options.MaxItems ?? _items.Count, _items.Count);
    }

    private static FlowSourceOptions BuildSourceOptions(GeneratedSourceOptions options)
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

    private void CompleteGenerated(int emitted, string message)
        => EmitDiagnostic(Completed, message, CreateAttributes(emitted));

    private void ReportFailure(Exception exception, int emitted)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            Code = SourceErrorCodes.GeneratedFailed,
            Message = $"source.generated failed: {exception.Message}",
            Context = CreateErrorContext(emitted),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = Failed,
            Level = FlowEventLevel.Error,
            Message = "source.generated failed.",
            Attributes = CreateAttributes(emitted)
        });
    }

    private void EmitDiagnostic(
        string name,
        string message,
        IReadOnlyDictionary<string, object?> attributes)
        => EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = name,
            Level = FlowEventLevel.Information,
            Message = message,
            Attributes = attributes
        });

    private Dictionary<string, object?> CreateAttributes(int emitted)
        => new(StringComparer.Ordinal)
        {
            ["name"] = _options.EffectiveName,
            ["outputType"] = _options.EffectiveOutputType,
            ["items"] = _items.Count,
            ["loop"] = _options.Loop,
            ["emitted"] = emitted,
            ["boundedCapacity"] = _options.BoundedCapacity
        };

    private string CreateErrorContext(int emitted)
        => string.Join(
            "; ",
            [
                $"name={_options.EffectiveName}",
                $"outputType={_options.EffectiveOutputType}",
                $"items={_items.Count}",
                $"loop={_options.Loop}",
                $"emitted={emitted}"
            ]);
}
