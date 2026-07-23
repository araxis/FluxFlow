using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using System.Globalization;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// Emits immutable workflow tick objects on a fixed interval.
/// </summary>
public sealed class TimerIntervalNode : IFlowSource
{
    public const string Started = TimerDiagnosticNames.IntervalStarted;
    public const string Tick = TimerDiagnosticNames.IntervalTick;
    public const string Stopped = TimerDiagnosticNames.IntervalStopped;
    public const string Failed = TimerDiagnosticNames.IntervalFailed;

    private readonly TimerIntervalSource _source;

    public TimerIntervalNode(
        TimerIntervalSettings settings,
        TimeProvider? clock = null)
        => _source = new TimerIntervalSource(settings, clock);

    public ISourceBlock<FlowMessage<FlowValue>> Output => _source.Output;

    public ISourceBlock<FlowEvent> Events => _source.Events;

    public Task Completion => _source.Completion;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _source.StartAsync(cancellationToken);

    public void Complete() => _source.Complete();

    public void Fault(Exception exception) => _source.Fault(exception);

    public ValueTask DisposeAsync() => _source.DisposeAsync();
}

internal sealed class TimerIntervalSource : FlowSource<FlowValue>
{
    private const string Started = TimerIntervalNode.Started;
    private const string Tick = TimerIntervalNode.Tick;
    private const string Stopped = TimerIntervalNode.Stopped;

    private readonly TimerIntervalSettings _settings;
    private readonly TimeProvider _clock;

    public TimerIntervalSource(
        TimerIntervalSettings settings,
        TimeProvider? clock = null)
        : base(BuildSourceOptions(settings))
    {
        _settings = settings;
        _clock = clock ?? TimeProvider.System;

        if (_settings.Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.interval 'Interval' must be greater than zero.");
        }

        if (_settings.InitialDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.interval 'InitialDelay' cannot be negative.");
        }

        if (_settings.MaxTicks is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.interval 'MaxTicks' must be greater than zero when set.");
        }

    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = _clock.GetUtcNow();
        EmitEvent(new FlowEvent
        {
            Timestamp = startedAt,
            Name = Started,
            Level = FlowEventLevel.Information,
            Message = $"Started timer interval '{_settings.Name}'.",
            Attributes = CreateAttributes()
        });

        var sequence = 0L;
        var nextDueAt = ResolveFirstDueAt(startedAt);

        if (_settings.EmitImmediately)
        {
            var nextSequence = sequence + 1;
            if (!await TryEmitTickAsync(nextSequence, startedAt, nextDueAt, cancellationToken)
                    .ConfigureAwait(false))
            {
                CompleteTimer(startedAt, sequence);
                return;
            }

            sequence = nextSequence;
            if (HasReachedMaxTicks(sequence))
            {
                CompleteTimer(startedAt, sequence);
                return;
            }

            nextDueAt = startedAt + _settings.Interval;
        }

        while (true)
        {
            await DelayUntilAsync(nextDueAt, cancellationToken).ConfigureAwait(false);
            var nextSequence = sequence + 1;
            if (!await TryEmitTickAsync(nextSequence, startedAt, nextDueAt, cancellationToken)
                    .ConfigureAwait(false))
            {
                CompleteTimer(startedAt, sequence);
                return;
            }

            sequence = nextSequence;
            if (HasReachedMaxTicks(sequence))
            {
                CompleteTimer(startedAt, sequence);
                return;
            }

            nextDueAt += _settings.Interval;
        }
    }

    private DateTimeOffset ResolveFirstDueAt(DateTimeOffset startedAt)
    {
        if (_settings.EmitImmediately)
        {
            return startedAt;
        }

        return startedAt + (_settings.InitialDelay > TimeSpan.Zero
            ? _settings.InitialDelay
            : _settings.Interval);
    }

    private async Task DelayUntilAsync(
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        var delay = dueAt - _clock.GetUtcNow();
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryEmitTickAsync(
        long sequence,
        DateTimeOffset startedAt,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        var timestamp = _clock.GetUtcNow();
        var elapsed = timestamp - startedAt;
        var drift = timestamp - dueAt;
        var tick = FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["timestamp"] = FlowValue.From(timestamp),
            ["name"] = FlowValue.From(_settings.Name),
            ["sequence"] = FlowValue.From(sequence),
            ["startedAt"] = FlowValue.From(startedAt),
            ["dueAt"] = FlowValue.From(dueAt),
            ["elapsed"] = FlowValue.From(elapsed),
            ["interval"] = FlowValue.From(_settings.Interval),
            ["drift"] = FlowValue.From(drift)
        });

        if (!await EmitAsync(FlowMessage.Create(tick), cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        EmitEvent(new FlowEvent
        {
            Timestamp = timestamp,
            Name = Tick,
            Level = FlowEventLevel.Information,
            Message = $"Emitted timer interval tick {sequence.ToString(CultureInfo.InvariantCulture)}.",
            Attributes = CreateAttributes(sequence, dueAt, elapsed, drift)
        });
        return true;
    }

    private bool HasReachedMaxTicks(long sequence)
        => _settings.MaxTicks.HasValue && sequence >= _settings.MaxTicks.Value;

    private static FlowSourceOptions BuildSourceOptions(TimerIntervalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.interval 'BoundedCapacity' must be greater than zero.");
        }

        return new FlowSourceOptions { OutputCapacity = settings.BoundedCapacity };
    }

    private void CompleteTimer(DateTimeOffset startedAt, long sequence)
        => EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = Stopped,
            Level = FlowEventLevel.Information,
            Message = $"Stopped timer interval '{_settings.Name}'.",
            Attributes = CreateAttributes(sequence, elapsed: _clock.GetUtcNow() - startedAt)
        });

    private Dictionary<string, object?> CreateAttributes(
        long? sequence = null,
        DateTimeOffset? dueAt = null,
        TimeSpan? elapsed = null,
        TimeSpan? drift = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = _settings.Name,
            ["intervalMilliseconds"] = _settings.Interval.TotalMilliseconds,
            ["initialDelayMilliseconds"] = _settings.InitialDelay.TotalMilliseconds,
            ["emitImmediately"] = _settings.EmitImmediately
        };

        if (_settings.MaxTicks.HasValue)
        {
            attributes["maxTicks"] = _settings.MaxTicks.Value;
        }

        if (sequence.HasValue)
        {
            attributes["sequence"] = sequence.Value;
        }

        if (elapsed.HasValue)
        {
            attributes["elapsedMilliseconds"] = elapsed.Value.TotalMilliseconds;
        }

        if (dueAt.HasValue)
        {
            attributes["dueAt"] = dueAt.Value;
        }

        if (drift.HasValue)
        {
            attributes["driftMilliseconds"] = drift.Value.TotalMilliseconds;
        }

        return attributes;
    }
}
