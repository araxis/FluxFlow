using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Globalization;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Timers.Nodes;

public sealed class TimerIntervalNode : SourceFlowNode<TimerTick>, IFlowEventSource, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly TimerIntervalSettings _settings;
    private readonly BroadcastBlock<FlowEvent> _events = new(static flowEvent => flowEvent);
    private CancellationTokenSource? _timerCancellation;
    private Task? _timerTask;
    private bool _started;
    private bool _disposed;

    private TimerIntervalNode(TimerIntervalSettings settings)
        : base(new DataflowBlockOptions { BoundedCapacity = settings.BoundedCapacity })
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Timer interval bounded capacity must be greater than zero.");
        }
    }

    public ISourceBlock<FlowEvent> Events => _events;

    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = TimerOptionsReader.ReadIntervalSettings(context.Definition);
        var node = new TimerIntervalNode(settings);

        return context.CreateNode(node)
            .Output(TimerComponentPorts.Output, node.Output)
            .Build();
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            if (_started)
            {
                throw new InvalidOperationException("timer.interval node has already started.");
            }

            _started = true;
            _timerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _timerTask = RunTimerAsync(_timerCancellation.Token);
        }

        TryEmitDiagnostic(
            TimerDiagnosticNames.IntervalStarted,
            message: $"Started timer interval '{_settings.Name}'.",
            attributes: CreateAttributes());
        return Task.CompletedTask;
    }

    public override void Complete()
    {
        CancellationTokenSource? cancellation;
        lock (_stateLock)
        {
            cancellation = _timerCancellation;
        }

        if (cancellation is null)
        {
            CompleteOutput();
            return;
        }

        cancellation.Cancel();
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _timerCancellation?.Cancel();
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

        if (_timerTask is not null)
        {
            await _timerTask.ConfigureAwait(false);
        }

        _timerCancellation?.Dispose();
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

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sequence = 0L;
        var nextDueAt = ResolveFirstDueAt(startedAt);

        try
        {
            if (_settings.EmitImmediately)
            {
                sequence = await EmitTickAsync(
                    sequence,
                    startedAt,
                    nextDueAt,
                    cancellationToken).ConfigureAwait(false);
                if (HasReachedMaxTicks(sequence))
                {
                    CompleteTimer(startedAt, sequence);
                    return;
                }

                nextDueAt = nextDueAt + _settings.Interval;
            }
            else if (_settings.InitialDelay > TimeSpan.Zero)
            {
                await DelayUntilAsync(nextDueAt, cancellationToken).ConfigureAwait(false);
                sequence = await EmitTickAsync(
                    sequence,
                    startedAt,
                    nextDueAt,
                    cancellationToken).ConfigureAwait(false);
                if (HasReachedMaxTicks(sequence))
                {
                    CompleteTimer(startedAt, sequence);
                    return;
                }

                nextDueAt = nextDueAt + _settings.Interval;
            }

            using var timer = new PeriodicTimer(_settings.Interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                sequence = await EmitTickAsync(
                    sequence,
                    startedAt,
                    nextDueAt,
                    cancellationToken).ConfigureAwait(false);
                if (HasReachedMaxTicks(sequence))
                {
                    CompleteTimer(startedAt, sequence);
                    return;
                }

                nextDueAt = nextDueAt + _settings.Interval;
            }

            CompleteTimer(startedAt, sequence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CompleteTimer(startedAt, sequence);
        }
        catch (Exception exception)
        {
            TryReportError(
                TimerErrorCodes.IntervalFailed,
                $"timer.interval failed: {exception.Message}",
                exception,
                CreateErrorContext());
            TryEmitDiagnostic(
                TimerDiagnosticNames.IntervalFailed,
                FlowDiagnosticLevel.Error,
                "timer.interval failed.",
                exception,
                CreateAttributes(sequence));
            base.Fault(exception);
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

    private static Task DelayUntilAsync(
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        var delay = dueAt - DateTimeOffset.UtcNow;
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
    }

    private async Task<long> EmitTickAsync(
        long currentSequence,
        DateTimeOffset startedAt,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sequence = currentSequence + 1;
        var timestamp = DateTimeOffset.UtcNow;
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

        await SendOutputAsync(tick, cancellationToken).ConfigureAwait(false);
        TryEmitDiagnostic(
            TimerDiagnosticNames.IntervalTick,
            message: $"Emitted timer interval tick {sequence.ToString(CultureInfo.InvariantCulture)}.",
            attributes: CreateAttributes(tick));
        EmitTickEvent(tick);
        return sequence;
    }

    private bool HasReachedMaxTicks(long sequence)
        => _settings.MaxTicks.HasValue && sequence >= _settings.MaxTicks.Value;

    private void CompleteTimer(DateTimeOffset startedAt, long sequence)
    {
        TryEmitDiagnostic(
            TimerDiagnosticNames.IntervalStopped,
            message: $"Stopped timer interval '{_settings.Name}'.",
            attributes: CreateAttributes(sequence, DateTimeOffset.UtcNow - startedAt));
        CompleteOutput();
    }

    private bool EmitTickEvent(TimerTick tick)
    {
        var attributes = new Dictionary<string, string>
        {
            ["name"] = tick.Name,
            ["sequence"] = tick.Sequence.ToString(CultureInfo.InvariantCulture),
            ["intervalMilliseconds"] = tick.Interval.TotalMilliseconds.ToString(CultureInfo.InvariantCulture),
            ["elapsedMilliseconds"] = tick.Elapsed.TotalMilliseconds.ToString(CultureInfo.InvariantCulture),
            ["driftMilliseconds"] = tick.Drift.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)
        };

        return _events.Post(new FlowEvent
        {
            Timestamp = tick.Timestamp,
            Type = TimerEventNames.IntervalTick,
            Source = Id.ToString(),
            SourceNodeId = Id,
            Subject = tick.Name,
            Channel = TimerEventNames.IntervalTick,
            Attributes = attributes
        });
    }

    private Dictionary<string, object?> CreateAttributes(
        TimerTick? tick = null)
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

    private string CreateErrorContext()
    {
        var values = new List<string>
        {
            $"name={_settings.Name}",
            $"intervalMilliseconds={_settings.Interval.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)}",
            $"initialDelayMilliseconds={_settings.InitialDelay.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)}",
            $"emitImmediately={_settings.EmitImmediately}"
        };

        if (_settings.MaxTicks.HasValue)
        {
            values.Add($"maxTicks={_settings.MaxTicks.Value}");
        }

        return string.Join("; ", values);
    }
}
