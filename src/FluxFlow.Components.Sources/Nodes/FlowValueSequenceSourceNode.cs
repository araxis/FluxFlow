using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Sources.Diagnostics;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sources.Nodes;

/// <summary>
/// Emits deterministic numeric sequence objects as immutable workflow values
/// without a universal error port.
/// </summary>
public sealed class FlowValueSequenceSourceNode : IFlowSource
{
    public const string Started = SourceDiagnosticNames.SequenceStarted;
    public const string Emitted = SourceDiagnosticNames.SequenceEmitted;
    public const string Completed = SourceDiagnosticNames.SequenceCompleted;
    public const string Failed = SourceDiagnosticNames.SequenceFailed;

    private readonly SequenceSourceOptions _options;
    private readonly TimeProvider _clock;
    private readonly FlowValueSourcePipeline _pipeline;

    public FlowValueSequenceSourceNode(
        SequenceSourceOptions options,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options);
        _clock = clock ?? TimeProvider.System;
        _pipeline = new FlowValueSourcePipeline(_options.BoundedCapacity, RunAsync);
    }

    public ISourceBlock<FlowMessage<FlowValue>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _pipeline.StartAsync(cancellationToken);

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var emitted = 0;
        try
        {
            PublishDiagnostic(Started, "source.sequence started.", emitted);
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
                var item = FlowValue.FromObject(new Dictionary<string, FlowValue>(
                    StringComparer.Ordinal)
                {
                    ["name"] = FlowValue.From(_options.EffectiveName),
                    ["sequence"] = FlowValue.From(sequence),
                    ["start"] = FlowValue.From(_options.Start),
                    ["step"] = FlowValue.From(_options.Step),
                    ["timestamp"] = FlowValue.From(timestamp),
                    ["value"] = FlowValue.From(value)
                });
                if (!await _pipeline.EmitAsync(
                    FlowMessage.Create(item),
                    cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                emitted++;
                PublishDiagnostic(
                    Emitted,
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

            PublishDiagnostic(Completed, "source.sequence completed.", emitted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishDiagnostic(Completed, "source.sequence stopped.", emitted);
        }
        catch (Exception exception)
        {
            PublishFailure(exception, emitted);
            throw;
        }
    }

    private void PublishDiagnostic(
        string name,
        string message,
        int emitted,
        long? sequence = null,
        long? value = null)
        => _pipeline.PublishEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = name,
            Level = FlowEventLevel.Information,
            Message = message,
            Attributes = CreateAttributes(emitted, sequence, value)
        });

    private void PublishFailure(Exception exception, int emitted)
        => _pipeline.PublishEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = Failed,
            Level = FlowEventLevel.Error,
            Message = $"source.sequence failed: {exception.Message}",
            Attributes = CreateAttributes(emitted, exception: exception)
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

    private static SequenceSourceOptions ValidateOptions(SequenceSourceOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.sequence option 'boundedCapacity' must be greater than zero.");
        }

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

        return options;
    }
}
