using FluxFlow.Components.Sources.Diagnostics;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Sources.Nodes;

public sealed class GeneratedSourceNode<TOutput> : SourceFlowNode<TOutput>, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly GeneratedSourceOptions _options;
    private readonly IReadOnlyList<TOutput> _items;
    private readonly TimeProvider _clock;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _startRequested;
    private bool _completedBeforeStart;
    private bool _disposed;

    public GeneratedSourceNode(
        GeneratedSourceOptions options,
        IReadOnlyList<TOutput> items)
        : this(options, items, TimeProvider.System)
    {
    }

    internal GeneratedSourceNode(
        GeneratedSourceOptions options,
        IReadOnlyList<TOutput> items,
        TimeProvider clock)
        : base(new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity })
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "source.generated bounded capacity must be greater than zero.");
        }

        if (options.MaxItems.HasValue && options.MaxItems.Value <= 0)
        {
            throw new ArgumentException(
                "source.generated option 'maxItems' must be greater than zero.",
                nameof(options));
        }

        if (options.Loop && !options.MaxItems.HasValue)
        {
            throw new ArgumentException(
                "source.generated option 'maxItems' is required when 'loop' is true.",
                nameof(options));
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            if (_completedBeforeStart)
            {
                return Task.CompletedTask;
            }

            if (_startRequested)
            {
                throw new InvalidOperationException("source.generated node has already started.");
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
            // The node was already disposed; the run loop has stopped.
        }
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            _runCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The node was already disposed; the run loop has stopped.
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
                SourceDiagnosticNames.GeneratedStarted,
                message: "source.generated started.",
                attributes: CreateAttributes(emitted));
            await SourceNodeTiming.DelayInitialAsync(
                _options.InitialDelayMilliseconds,
                _clock,
                cancellationToken).ConfigureAwait(false);

            var targetCount = ResolveTargetCount();
            for (var index = 0; index < targetCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = _items[index % _items.Count];
                if (!await SendOutputAsync(item, cancellationToken).ConfigureAwait(false))
                {
                    CompleteGenerated(emitted, "source.generated stopped.");
                    return;
                }

                emitted++;
                TryEmitDiagnostic(
                    SourceDiagnosticNames.GeneratedEmitted,
                    message: "source.generated emitted item.",
                    attributes: CreateAttributes(emitted));
                if (index < targetCount - 1)
                {
                    await SourceNodeTiming.DelayIntervalAsync(
                        _options.IntervalMilliseconds,
                        _clock,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            CompleteGenerated(emitted, "source.generated completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CompleteGenerated(emitted, "source.generated stopped.");
        }
        catch (Exception exception)
        {
            TryReportError(
                SourceErrorCodes.GeneratedFailed,
                $"source.generated failed: {exception.Message}",
                exception,
                CreateErrorContext(emitted));
            TryEmitDiagnostic(
                SourceDiagnosticNames.GeneratedFailed,
                FlowDiagnosticLevel.Error,
                "source.generated failed.",
                exception,
                CreateAttributes(emitted));
            base.Fault(exception);
        }
    }

    private int ResolveTargetCount()
    {
        if (_items.Count == 0)
        {
            return 0;
        }

        return _options.Loop
            ? _options.MaxItems!.Value
            : Math.Min(_options.MaxItems ?? _items.Count, _items.Count);
    }

    private void CompleteGenerated(int emitted, string message)
    {
        TryEmitDiagnostic(
            SourceDiagnosticNames.GeneratedCompleted,
            message: message,
            attributes: CreateAttributes(emitted));
        CompleteOutput();
    }

    private Dictionary<string, object?> CreateAttributes(int emitted)
        => new(StringComparer.Ordinal)
        {
            ["name"] = _options.EffectiveName,
            ["outputType"] = _options.EffectiveOutputType,
            ["items"] = _items.Count,
            ["loop"] = _options.Loop,
            ["emitted"] = emitted,
            ["boundedCapacity"] = _options.BoundedCapacity
        };

    private string CreateErrorContext(int emitted)
        => string.Join(
            "; ",
            [
                $"name={_options.EffectiveName}",
                $"outputType={_options.EffectiveOutputType}",
                $"items={_items.Count}",
                $"loop={_options.Loop}",
                $"emitted={emitted}"
            ]);
}
