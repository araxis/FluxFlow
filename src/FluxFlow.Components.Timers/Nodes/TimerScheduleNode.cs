using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Components;
using System.Globalization;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Timers.Nodes;

public sealed class TimerScheduleNode : SourceFlowNode<ScheduleTick>, IFlowEventSource, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly TimerScheduleSettings _settings;
    private readonly TimeProvider _clock;
    private readonly BroadcastBlock<FlowEvent> _events = new(static flowEvent => flowEvent);
    private CancellationTokenSource? _scheduleCancellation;
    private Task? _scheduleTask;
    private bool _started;
    private bool _completedBeforeStart;
    private bool _disposed;

    internal TimerScheduleNode(
        TimerScheduleSettings settings,
        TimeProvider clock)
        : base(new DataflowBlockOptions { BoundedCapacity = settings.BoundedCapacity })
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Timer schedule bounded capacity must be greater than zero.");
        }
    }

    public ISourceBlock<FlowEvent> Events => _events;

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            if (_completedBeforeStart)
            {
                return Task.CompletedTask;
            }

            if (_started)
            {
                throw new InvalidOperationException("timer.schedule node has already started.");
            }

            _started = true;
            _scheduleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            TryEmitDiagnostic(
                TimerDiagnosticNames.ScheduleStarted,
                message: $"Started timer schedule '{_settings.Name}'.",
                attributes: CreateAttributes());
            _scheduleTask = RunScheduleAsync(_scheduleCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public override void Complete()
    {
        CancellationTokenSource? cancellation;
        lock (_stateLock)
        {
            cancellation = _scheduleCancellation;
            if (cancellation is null)
            {
                _completedBeforeStart = true;
            }
        }

        if (cancellation is null)
        {
            CompleteOutput();
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The node was already disposed; the schedule loop has stopped.
        }
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            _scheduleCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The node was already disposed; the schedule loop has stopped.
        }

        base.Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Complete();

        if (_scheduleTask is not null)
        {
            await _scheduleTask.ConfigureAwait(false);
        }

        _scheduleCancellation?.Dispose();
    }

    protected override void OnNodeCompleted()
    {
        _events.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        ((IDataflowBlock)_events).Fault(exception);
        base.OnNodeFaulted(exception);
    }

    private async Task RunScheduleAsync(CancellationToken cancellationToken)
    {
        var startedAt = _clock.GetUtcNow();
        var sequence = 0L;

        try
        {
            while (true)
            {
                var dueAt = _settings.Schedule.GetNextOccurrence(_clock.GetUtcNow(), _settings.TimeZone)
                    ?? throw new InvalidOperationException(
                        $"timer.schedule could not find the next occurrence for '{_settings.Cron}'.");
                await DelayUntilAsync(dueAt, cancellationToken).ConfigureAwait(false);
                var emitted = await EmitTickAsync(
                    sequence,
                    startedAt,
                    dueAt,
                    cancellationToken).ConfigureAwait(false);
                if (emitted is null)
                {
                    CompleteSchedule(startedAt, sequence);
                    return;
                }

                sequence = emitted.Value;
                if (_settings.MaxTicks.HasValue && sequence >= _settings.MaxTicks.Value)
                {
                    CompleteSchedule(startedAt, sequence);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CompleteSchedule(startedAt, sequence);
        }
        catch (Exception exception)
        {
            TryReportError(
                TimerErrorCodes.ScheduleFailed,
                $"timer.schedule failed: {exception.Message}",
                exception,
                CreateErrorContext());
            TryEmitDiagnostic(
                TimerDiagnosticNames.ScheduleFailed,
                FlowDiagnosticLevel.Error,
                "timer.schedule failed.",
                exception,
                CreateAttributes(sequence));
            base.Fault(exception);
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

    private async Task<long?> EmitTickAsync(
        long currentSequence,
        DateTimeOffset startedAt,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        if (!await SendOutputAsync(tick, cancellationToken).ConfigureAwait(false))
        {
            // The output declined the tick because it has completed; stop the loop.
            return null;
        }

        TryEmitDiagnostic(
            TimerDiagnosticNames.ScheduleTick,
            message: $"Emitted timer schedule tick {sequence.ToString(CultureInfo.InvariantCulture)}.",
            attributes: CreateAttributes(tick));
        EmitTickEvent(tick);
        return sequence;
    }

    private void CompleteSchedule(DateTimeOffset startedAt, long sequence)
    {
        TryEmitDiagnostic(
            TimerDiagnosticNames.ScheduleStopped,
            message: $"Stopped timer schedule '{_settings.Name}'.",
            attributes: CreateAttributes(sequence, _clock.GetUtcNow() - startedAt));
        CompleteOutput();
    }

    private bool EmitTickEvent(ScheduleTick tick)
    {
        var attributes = new Dictionary<string, string>
        {
            ["name"] = tick.Name,
            ["sequence"] = tick.Sequence.ToString(CultureInfo.InvariantCulture),
            ["cron"] = tick.Cron,
            ["timeZoneId"] = tick.TimeZoneId,
            ["driftMilliseconds"] = tick.Drift.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)
        };

        return _events.Post(new FlowEvent
        {
            Timestamp = tick.Timestamp,
            Type = TimerEventNames.ScheduleTick,
            Source = Id.ToString(),
            SourceNodeId = Id,
            Subject = tick.Name,
            Channel = TimerEventNames.ScheduleTick,
            Attributes = attributes
        });
    }

    private Dictionary<string, object?> CreateAttributes(
        ScheduleTick? tick = null)
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

    private string CreateErrorContext()
    {
        var values = new List<string>
        {
            $"name={_settings.Name}",
            $"cron={_settings.Cron}",
            $"timeZoneId={_settings.TimeZone.Id}"
        };

        if (_settings.MaxTicks.HasValue)
        {
            values.Add($"maxTicks={_settings.MaxTicks.Value}");
        }

        return string.Join("; ", values);
    }
}
