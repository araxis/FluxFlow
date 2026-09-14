using System.Text.Json;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Diagnostics;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sources.Nodes;

/// <summary>
/// Emits deterministic numeric sequence objects as immutable workflow values.
/// </summary>
public sealed class SequenceSourceNode : FlowSource
{
    public const string Started = SourceDiagnosticNames.SequenceStarted;
    public const string Emitted = SourceDiagnosticNames.SequenceEmitted;
    public const string Completed = SourceDiagnosticNames.SequenceCompleted;
    public const string Failed = SourceDiagnosticNames.SequenceFailed;

    private readonly SequenceSourceOptions _options;
    private readonly TimeProvider _clock;

    public SequenceSourceNode(
        SequenceSourceOptions options,
        TimeProvider? clock = null)
        : base(BuildSourceOptions(options))
    {
        _options = options;
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        var emitted = 0;
        try
        {
            PublishDiagnostic(
                SequenceSourceNode.Started,
                "source.sequence started.",
                emitted);
            await SourceNodeTiming.DelayInitialAsync(
                _options.InitialDelayMilliseconds,
                _clock,
                cancellationToken).ConfigureAwait(false);

            for (var index = 0; index < _options.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var timestamp = _clock.GetUtcNow();
                var sequence = index + 1L;
                var value = _options.Start + (_options.Step * index);
                var item = new SequenceItem(
                    _options.EffectiveName,
                    sequence,
                    _options.Start,
                    _options.Step,
                    timestamp,
                    value);
                await EmitAsync(
                    FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(item, ValueJsonOptions))),
                    cancellationToken).ConfigureAwait(false);

                emitted++;
                PublishDiagnostic(
                    SequenceSourceNode.Emitted,
                    "source.sequence emitted item.",
                    emitted,
                    sequence,
                    value);
                if (index < _options.Count - 1)
                {
                    await SourceNodeTiming.DelayIntervalAsync(
                        _options.IntervalMilliseconds,
                        _clock,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            PublishDiagnostic(
                SequenceSourceNode.Completed,
                "source.sequence completed.",
                emitted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishDiagnostic(
                SequenceSourceNode.Completed,
                "source.sequence stopped.",
                emitted);
        }
        catch (Exception exception)
        {
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                Name = SequenceSourceNode.Failed,
                Level = FlowEventLevel.Error,
                Message = $"source.sequence failed: {exception.Message}",
                Attributes = CreateAttributes(emitted, exception: exception)
            });
            throw;
        }
    }

    private void PublishDiagnostic(
        string name,
        string message,
        int emitted,
        long? sequence = null,
        long? value = null)
        => EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = name,
            Level = FlowEventLevel.Information,
            Message = message,
            Attributes = CreateAttributes(emitted, sequence, value)
        });

    private Dictionary<string, object?> CreateAttributes(
        int emitted,
        long? sequence = null,
        long? value = null,
        Exception? exception = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["boundedCapacity"] = _options.BoundedCapacity,
            ["count"] = _options.Count,
            ["emitted"] = emitted,
            ["name"] = _options.EffectiveName,
            ["start"] = _options.Start,
            ["step"] = _options.Step
        };
        if (sequence.HasValue)
            attributes["sequence"] = sequence.Value;
        if (value.HasValue)
            attributes["value"] = value.Value;
        if (exception is not null)
        {
            attributes["exceptionType"] =
                exception.GetType().FullName ?? exception.GetType().Name;
        }

        return attributes;
    }

    private static FlowSourceOptions BuildSourceOptions(SequenceSourceOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.sequence option 'boundedCapacity' must be greater than zero.");
        }

        return new FlowSourceOptions { OutputCapacity = options.BoundedCapacity };
    }

    private static void ValidateOptions(SequenceSourceOptions options)
    {
        if (options.InitialDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.sequence option 'initialDelayMilliseconds' cannot be negative.");
        }

        if (options.IntervalMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.sequence option 'intervalMilliseconds' cannot be negative.");
        }

        if (options.Count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.sequence option 'count' must be greater than zero.");
        }

        if (options.Step == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.sequence option 'step' cannot be zero.");
        }
    }
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);
}
