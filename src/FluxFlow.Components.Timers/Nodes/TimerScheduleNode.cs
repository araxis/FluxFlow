using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;
using System.Globalization;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// Emits immutable workflow tick objects at occurrences of a cron schedule.
/// </summary>
public sealed class TimerScheduleNode : IFlowSource
{
    public const string Started = TimerDiagnosticNames.ScheduleStarted;
    public const string Tick = TimerDiagnosticNames.ScheduleTick;
    public const string Stopped = TimerDiagnosticNames.ScheduleStopped;
    public const string Failed = TimerDiagnosticNames.ScheduleFailed;

    private readonly TimerScheduleSource _source;

    public TimerScheduleNode(
        TimerScheduleSettings settings,
        TimeProvider? clock = null)
        => _source = new TimerScheduleSource(settings, clock);

    public ISourceBlock<FlowMessage<TimerScheduleTick>> Output => _source.Output;

    public ISourceBlock<FlowEvent> Events => _source.Events;

    public Task Completion => _source.Completion;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _source.StartAsync(cancellationToken);

    public void Complete() => _source.Complete();

    public void Fault(Exception exception) => _source.Fault(exception);

    public ValueTask DisposeAsync() => _source.DisposeAsync();
}

internal sealed class TimerScheduleSource : FlowSource<TimerScheduleTick>
{
    private const string Started = TimerScheduleNode.Started;
    private const string Tick = TimerScheduleNode.Tick;
    private const string Stopped = TimerScheduleNode.Stopped;

    private readonly TimerScheduleSettings _settings;
    private readonly TimeProvider _clock;
    private readonly CronSchedule _schedule;

    public TimerScheduleSource(
        TimerScheduleSettings settings,
        TimeProvider? clock = null)
        : base(BuildSourceOptions(settings))
    {
        _settings = settings;
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
            var nextSequence = sequence + 1;
            if (!await TryEmitTickAsync(nextSequence, startedAt, dueAt, cancellationToken)
                    .ConfigureAwait(false))
            {
                CompleteSchedule(startedAt, sequence);
                return;
            }

            sequence = nextSequence;
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

    private async Task<bool> TryEmitTickAsync(
        long sequence,
        DateTimeOffset startedAt,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        var timestamp = _clock.GetUtcNow();
        var drift = timestamp - dueAt;
        var tick = new TimerScheduleTick(
            timestamp,
            _settings.Name,
            sequence,
            startedAt,
            dueAt,
            _settings.Cron,
            _settings.TimeZone.Id,
            drift);

        if (!await EmitAsync(FlowMessage.Create(tick), cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        EmitEvent(new FlowEvent
        {
            Timestamp = timestamp,
            Name = Tick,
            Level = FlowEventLevel.Information,
            Message = $"Emitted timer schedule tick {sequence.ToString(CultureInfo.InvariantCulture)}.",
            Attributes = CreateAttributes(sequence, dueAt, drift: drift)
        });
        return true;
    }

    private static FlowSourceOptions BuildSourceOptions(TimerScheduleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.schedule 'BoundedCapacity' must be greater than zero.");
        }

        return new FlowSourceOptions { OutputCapacity = settings.BoundedCapacity };
    }

    private void CompleteSchedule(DateTimeOffset startedAt, long sequence)
        => EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = Stopped,
            Level = FlowEventLevel.Information,
            Message = $"Stopped timer schedule '{_settings.Name}'.",
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
