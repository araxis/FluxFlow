using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Diagnostics;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Components.Sources.Timing;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Sources.Nodes;

public sealed class SequenceSourceNode : SourceFlowNode<SourceSequenceItem>, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly SequenceSourceOptions _options;
    private readonly ISourceClock _clock;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _startRequested;
    private bool _disposed;

    public SequenceSourceNode(SequenceSourceOptions options)
        : this(options, SystemSourceClock.Instance)
    {
    }

    internal SequenceSourceNode(
        SequenceSourceOptions options,
        ISourceClock clock)
        : base(new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity })
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.sequence bounded capacity must be greater than zero.");
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            if (_startRequested)
            {
                throw new InvalidOperationException("source.sequence node has already started.");
            }

            _startRequested = true;
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = RunAsync(_runCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public override void Complete()
    {
        CancellationTokenSource? cancellation;
        lock (_stateLock)
        {
            cancellation = _runCancellation;
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
        _runCancellation?.Cancel();
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
        if (_runTask is not null)
        {
            await _runTask.ConfigureAwait(false);
        }

        _runCancellation?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var emitted = 0;
        try
        {
            TryEmitDiagnostic(
                SourceDiagnosticNames.SequenceStarted,
                message: "source.sequence started.",
                attributes: CreateAttributes(emitted));
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
                    Timestamp = _clock.UtcNow
                };
                await SendOutputAsync(item, cancellationToken).ConfigureAwait(false);
                emitted++;
                TryEmitDiagnostic(
                    SourceDiagnosticNames.SequenceEmitted,
                    message: "source.sequence emitted item.",
                    attributes: CreateAttributes(emitted, item));
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
            TryReportError(
                SourceErrorCodes.SequenceFailed,
                $"source.sequence failed: {exception.Message}",
                exception,
                CreateErrorContext(emitted));
            TryEmitDiagnostic(
                SourceDiagnosticNames.SequenceFailed,
                FlowDiagnosticLevel.Error,
                "source.sequence failed.",
                exception,
                CreateAttributes(emitted));
            base.Fault(exception);
        }
    }

    private void CompleteSequence(int emitted, string message)
    {
        TryEmitDiagnostic(
            SourceDiagnosticNames.SequenceCompleted,
            message: message,
            attributes: CreateAttributes(emitted));
        CompleteOutput();
    }

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
