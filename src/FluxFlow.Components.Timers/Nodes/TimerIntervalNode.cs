using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;
using System.Globalization;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// A standalone interval source — a "blockified" periodic timer. Call <c>StartAsync</c>
/// and the node broadcasts a <c>FlowMessage&lt;TimerTick&gt;</c> on <c>Output</c> on a
/// fixed interval (plus diagnostic notes on <c>Events</c>), minting a fresh correlation
/// id per tick. It runs until <see cref="TimerIntervalSettings.MaxTicks"/> is reached
/// (source complete) or it is stopped via <c>Complete</c>/dispose. Timing is driven by
/// the injected <see cref="TimeProvider"/>, so tests can advance a FakeTimeProvider to
/// fire ticks deterministically. Works with nothing but
/// <c>new TimerIntervalNode(settings)</c> — no engine.
/// </summary>
public sealed class TimerIntervalNode : FlowSource<TimerTick>
{
    public const string Started = TimerDiagnosticNames.IntervalStarted;
    public const string Tick = TimerDiagnosticNames.IntervalTick;
    public const string Stopped = TimerDiagnosticNames.IntervalStopped;
    public const string Failed = TimerDiagnosticNames.IntervalFailed;

    private readonly TimerIntervalSettings _settings;
    private readonly TimeProvider _clock;

    public TimerIntervalNode(
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
        var tick = new TimerTick
        {
            Timestamp = timestamp,
            Name = _settings.Name,
            Sequence = sequence,
            StartedAt = startedAt,
            DueAt = dueAt,
            Elapsed = timestamp - startedAt,
            Interval = _settings.Interval,
            Drift = timestamp - dueAt
        };

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
            Attributes = CreateAttributes(tick)
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
            Attributes = CreateAttributes(sequence, _clock.GetUtcNow() - startedAt)
        });

    private Dictionary<string, object?> CreateAttributes(TimerTick? tick = null)
    {
        var attributes = CreateAttributes(tick?.Sequence);
        if (tick is null)
        {
            return attributes;
        }

        attributes["dueAt"] = tick.DueAt;
        attributes["elapsedMilliseconds"] = tick.Elapsed.TotalMilliseconds;
        attributes["driftMilliseconds"] = tick.Drift.TotalMilliseconds;
        return attributes;
    }

    private Dictionary<string, object?> CreateAttributes(
        long? sequence,
        TimeSpan? elapsed = null)
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

        return attributes;
    }
}
