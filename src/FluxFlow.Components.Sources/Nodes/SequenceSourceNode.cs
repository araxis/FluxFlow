using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Diagnostics;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sources.Nodes;

/// <summary>
/// A standalone deterministic sequence source. Call <c>StartAsync</c> and the node
/// broadcasts <see cref="SequenceSourceOptions.Count"/> <c>FlowMessage&lt;SourceSequenceItem&gt;</c>
/// values on <c>Output</c> (each minting a fresh correlation id), then completes — honoring
/// the configured initial delay and inter-item interval off the injected
/// <see cref="TimeProvider"/>, so tests can advance a FakeTimeProvider instead of sleeping.
/// Lifecycle notes are emitted on <c>Events</c> using <see cref="SourceDiagnosticNames"/>;
/// failures surface a <see cref="FlowError"/> on <c>Errors</c>. Works with nothing but
/// <c>new SequenceSourceNode(options)</c> — no engine.
/// </summary>
public sealed class SequenceSourceNode : FlowSource<SourceSequenceItem>
{
    public const string Started = SourceDiagnosticNames.SequenceStarted;
    public const string Emitted = SourceDiagnosticNames.SequenceEmitted;
    public const string Completed = SourceDiagnosticNames.SequenceCompleted;
    public const string Failed = SourceDiagnosticNames.SequenceFailed;

    private readonly SequenceSourceOptions _options;
    private readonly TimeProvider _clock;

    public SequenceSourceNode(SequenceSourceOptions options, TimeProvider? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? TimeProvider.System;

        if (_options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.sequence option 'boundedCapacity' must be greater than zero.");
        }

        if (_options.InitialDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.sequence option 'initialDelayMilliseconds' cannot be negative.");
        }

        if (_options.IntervalMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.sequence option 'intervalMilliseconds' cannot be negative.");
        }

        if (_options.Count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.sequence option 'count' must be greater than zero.");
        }

        if (_options.Step == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.sequence option 'step' cannot be zero.");
        }
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        var emitted = 0;
        try
        {
            EmitDiagnostic(Started, "source.sequence started.", CreateAttributes(emitted));
            await SourceNodeTiming.DelayInitialAsync(
                _options.InitialDelayMilliseconds,
                _clock,
                cancellationToken).ConfigureAwait(false);

            for (var index = 0; index < _options.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = new SourceSequenceItem
                {
                    Name = _options.EffectiveName,
                    Sequence = index + 1L,
                    Value = _options.Start + (_options.Step * index),
                    Start = _options.Start,
                    Step = _options.Step,
                    Timestamp = _clock.GetUtcNow()
                };
                Emit(FlowMessage.Create(item));
                emitted++;
                EmitDiagnostic(
                    Emitted,
                    "source.sequence emitted item.",
                    CreateAttributes(emitted, item));
                if (index < _options.Count - 1)
                {
                    await SourceNodeTiming.DelayIntervalAsync(
                        _options.IntervalMilliseconds,
                        _clock,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            CompleteSequence(emitted, "source.sequence completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CompleteSequence(emitted, "source.sequence stopped.");
        }
        catch (Exception exception)
        {
            ReportFailure(exception, emitted);
            throw;
        }
    }

    private void CompleteSequence(int emitted, string message)
        => EmitDiagnostic(Completed, message, CreateAttributes(emitted));

    private void ReportFailure(Exception exception, int emitted)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            Code = SourceErrorCodes.SequenceFailed,
            Message = $"source.sequence failed: {exception.Message}",
            Context = CreateErrorContext(emitted),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = Failed,
            Level = FlowEventLevel.Error,
            Message = "source.sequence failed.",
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

    private Dictionary<string, object?> CreateAttributes(
        int emitted,
        SourceSequenceItem? item = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = _options.EffectiveName,
            ["start"] = _options.Start,
            ["step"] = _options.Step,
            ["count"] = _options.Count,
            ["emitted"] = emitted,
            ["boundedCapacity"] = _options.BoundedCapacity
        };

        if (item is not null)
        {
            attributes["sequence"] = item.Sequence;
            attributes["value"] = item.Value;
        }

        return attributes;
    }

    private string CreateErrorContext(int emitted)
        => string.Join(
            "; ",
            [
                $"name={_options.EffectiveName}",
                $"start={_options.Start}",
                $"step={_options.Step}",
                $"count={_options.Count}",
                $"emitted={emitted}"
            ]);
}
