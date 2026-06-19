using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;
using System.Globalization;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// A standalone cron-schedule source. Call <c>StartAsync</c> and the node broadcasts a
/// <c>FlowMessage&lt;ScheduleTick&gt;</c> on <c>Output</c> at each occurrence of the
/// configured cron expression (plus diagnostic notes on <c>Events</c>), minting a fresh
/// correlation id per tick. It runs until <see cref="TimerScheduleSettings.MaxTicks"/>
/// is reached (source complete) or it is stopped via <c>Complete</c>/dispose. Timing is
/// driven by the injected <see cref="TimeProvider"/>, so tests can advance a
/// FakeTimeProvider to fire ticks deterministically. Works with nothing but
/// <c>new TimerScheduleNode(settings)</c> — no engine.
/// </summary>
public sealed class TimerScheduleNode : FlowSource<ScheduleTick>
{
    public const string Started = TimerDiagnosticNames.ScheduleStarted;
    public const string Tick = TimerDiagnosticNames.ScheduleTick;
    public const string Stopped = TimerDiagnosticNames.ScheduleStopped;
    public const string Failed = TimerDiagnosticNames.ScheduleFailed;

    private readonly TimerScheduleSettings _settings;
    private readonly TimeProvider _clock;
    private readonly CronSchedule _schedule;

    public TimerScheduleNode(
        TimerScheduleSettings settings,
        TimeProvider? clock = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? TimeProvider.System;

        if (string.IsNullOrWhiteSpace(_settings.Cron))
        {
            throw new ArgumentException(
                "timer.schedule 'Cron' must be a non-empty cron expression.", nameof(settings));
        }

        if (_settings.MaxTicks is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.schedule 'MaxTicks' must be greater than zero when set.");
        }

        if (_settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.schedule 'BoundedCapacity' must be greater than zero.");
        }

        // Compiling the cron up front validates the expression in the constructor.
        _schedule = CronSchedule.Parse(_settings.Cron);
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = _clock.GetUtcNow();
        EmitEvent(new FlowEvent
        {
            Timestamp = startedAt,
            Name = Started,
            Level = FlowEventLevel.Information,
            Message = $"Started timer schedule '{_settings.Name}'.",
            Attributes = CreateAttributes()
        });

        var sequence = 0L;
        while (true)
        {
            var dueAt = _schedule.GetNextOccurrence(_clock.GetUtcNow(), _settings.TimeZone)
                ?? throw new InvalidOperationException(
                    $"timer.schedule could not find the next occurrence for '{_settings.Cron}'.");
            await DelayUntilAsync(dueAt, cancellationToken).ConfigureAwait(false);
            sequence = EmitTick(sequence, startedAt, dueAt);
            if (_settings.MaxTicks.HasValue && sequence >= _settings.MaxTicks.Value)
            {
                CompleteSchedule(startedAt, sequence);
                return;
            }
        }
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

    private long EmitTick(
        long currentSequence,
        DateTimeOffset startedAt,
        DateTimeOffset dueAt)
    {
        var sequence = currentSequence + 1;
        var timestamp = _clock.GetUtcNow();
        var tick = new ScheduleTick
        {
            Timestamp = timestamp,
            Name = _settings.Name,
            Sequence = sequence,
            StartedAt = startedAt,
            DueAt = dueAt,
            Cron = _settings.Cron,
            TimeZoneId = _settings.TimeZone.Id,
            Drift = timestamp - dueAt
        };

        Emit(FlowMessage.Create(tick));
        EmitEvent(new FlowEvent
        {
            Timestamp = timestamp,
            Name = Tick,
            Level = FlowEventLevel.Information,
            Message = $"Emitted timer schedule tick {sequence.ToString(CultureInfo.InvariantCulture)}.",
            Attributes = CreateAttributes(tick)
        });
        return sequence;
    }

    private void CompleteSchedule(DateTimeOffset startedAt, long sequence)
        => EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = Stopped,
            Level = FlowEventLevel.Information,
            Message = $"Stopped timer schedule '{_settings.Name}'.",
            Attributes = CreateAttributes(sequence, _clock.GetUtcNow() - startedAt)
        });

    private Dictionary<string, object?> CreateAttributes(ScheduleTick? tick = null)
    {
        var attributes = CreateAttributes(tick?.Sequence);
        if (tick is null)
        {
            return attributes;
        }

        attributes["dueAt"] = tick.DueAt;
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
            ["cron"] = _settings.Cron,
            ["timeZoneId"] = _settings.TimeZone.Id
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
