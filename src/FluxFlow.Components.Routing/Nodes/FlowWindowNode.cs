using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

public sealed class FlowWindowNode<TInput> : FlowNodeBase, IAsyncDisposable
{
    private readonly WindowRoutingOptions _options;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _timeLimit;
    private readonly ActionBlock<TInput> _input;
    private readonly ActionBlock<WindowCommand> _commands;
    private readonly BufferBlock<FlowWindow<TInput>> _output;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private readonly List<TInput> _items = [];
    private volatile CancellationTokenSource? _timerCancellation;
    private DateTimeOffset? _startedAt;
    private long _nextSequence;
    private long _windowVersion;
    private bool _disposed;

    public FlowWindowNode(WindowRoutingOptions options)
        : this(options, TimeProvider.System)
    {
    }

    public FlowWindowNode(
        WindowRoutingOptions options,
        TimeProvider clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.window bounded capacity must be greater than zero.");
        }

        if (options.MaxItems < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.window max item count cannot be negative.");
        }

        if (options.TimeMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.window time window cannot be negative.");
        }

        if (options.MaxItems == 0 && options.TimeMilliseconds == 0)
        {
            throw new ArgumentException(
                "flow.window requires maxItems or timeMilliseconds.",
                nameof(options));
        }

        _timeLimit = TimeSpan.FromMilliseconds(options.TimeMilliseconds);
        var blockOptions = new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity };
        var executionOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _output = new BufferBlock<FlowWindow<TInput>>(blockOptions);
        _commands = new ActionBlock<WindowCommand>(ProcessCommandAsync, executionOptions);
        _input = new ActionBlock<TInput>(
            async input => await _commands.SendAsync(
                WindowCommand.FromInput(input),
                _lifecycleCancellation.Token).ConfigureAwait(false),
            executionOptions);
        _input.Completion.ContinueWith(
            completion => _ = CompleteCommandsAsync(completion),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _commands.Completion.ContinueWith(
            CompleteOutput,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(_output.Completion);
    }

    public ITargetBlock<TInput> Input => _input;

    public ISourceBlock<FlowWindow<TInput>> Output => _output;

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            _lifecycleCancellation.Cancel();
            TryCancelTimer();
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
            ((IDataflowBlock)_commands).Fault(exception);
            ((IDataflowBlock)_output).Fault(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Complete();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Dispose must not throw when the node faulted.
        }
        finally
        {
            _timerCancellation?.Dispose();
            _lifecycleCancellation.Dispose();
        }
    }

    private async Task ProcessCommandAsync(WindowCommand command)
    {
        try
        {
            switch (command.Kind)
            {
                case WindowCommandKind.Input:
                    await AddInputAsync(command.Input!).ConfigureAwait(false);
                    break;
                case WindowCommandKind.Timer:
                    await EmitByTimeAsync(command.WindowVersion).ConfigureAwait(false);
                    break;
                case WindowCommandKind.Complete:
                    await CompleteWindowAsync().ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"flow.window command '{command.Kind}' is not supported.");
            }
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TryReportError(
                RoutingErrorCodes.WindowFailed,
                $"flow.window failed: {exception.Message}",
                exception,
                CreateErrorContext());
            TryEmitDiagnostic(
                RoutingDiagnosticNames.WindowFailed,
                FlowDiagnosticLevel.Error,
                "flow.window failed.",
                exception,
                CreateAttributes());
        }
    }

    private async Task AddInputAsync(TInput input)
    {
        var now = _clock.GetUtcNow();
        if (_items.Count == 0)
        {
            StartWindow(now);
        }

        _items.Add(input);
        if (_options.MaxItems > 0 && _items.Count >= _options.MaxItems)
        {
            await EmitWindowAsync(FlowWindowEmitReason.Count, _clock.GetUtcNow())
                .ConfigureAwait(false);
        }
    }

    private async Task EmitByTimeAsync(long version)
    {
        if (version != _windowVersion || _items.Count == 0)
        {
            return;
        }

        await EmitWindowAsync(FlowWindowEmitReason.Time, _clock.GetUtcNow())
            .ConfigureAwait(false);
    }

    private async Task CompleteWindowAsync()
    {
        TryCancelTimer();
        if (_items.Count > 0 && _options.EmitPartialOnCompletion)
        {
            await EmitWindowAsync(FlowWindowEmitReason.Completion, _clock.GetUtcNow())
                .ConfigureAwait(false);
            return;
        }

        ClearWindow();
    }

    private void StartWindow(DateTimeOffset startedAt)
    {
        _startedAt = startedAt;
        _windowVersion++;
        ScheduleTimer(_windowVersion);
    }

    private async Task EmitWindowAsync(
        FlowWindowEmitReason reason,
        DateTimeOffset emittedAt)
    {
        if (_items.Count == 0 || _startedAt is null)
        {
            return;
        }

        TryCancelTimer();
        var window = new FlowWindow<TInput>
        {
            Sequence = ++_nextSequence,
            Items = _items.ToArray(),
            StartedAt = _startedAt.Value,
            EmittedAt = emittedAt,
            Reason = reason
        };

        ClearWindow();
        await _output.SendAsync(window, _lifecycleCancellation.Token).ConfigureAwait(false);
        TryEmitDiagnostic(
            RoutingDiagnosticNames.WindowEmitted,
            message: "flow.window emitted window.",
            attributes: CreateAttributes(window));
    }

    private void ClearWindow()
    {
        _items.Clear();
        _startedAt = null;
        _windowVersion++;
    }

    private void ScheduleTimer(long version)
    {
        if (_timeLimit <= TimeSpan.Zero)
        {
            return;
        }

        var previous = Interlocked.Exchange(ref _timerCancellation, null);
        previous?.Cancel();
        previous?.Dispose();
        var timerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifecycleCancellation.Token);
        _timerCancellation = timerCancellation;
        _ = RunTimerAsync(version, timerCancellation.Token);
    }

    private void TryCancelTimer()
    {
        var timerCancellation = Interlocked.Exchange(ref _timerCancellation, null);
        timerCancellation?.Cancel();
        timerCancellation?.Dispose();
    }

    private async Task RunTimerAsync(
        long version,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_timeLimit, _clock, cancellationToken).ConfigureAwait(false);
            await _commands.SendAsync(
                WindowCommand.Timer(version),
                _lifecycleCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task CompleteCommandsAsync(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } completionException)
        {
            ((IDataflowBlock)_commands).Fault(completionException);
            return;
        }

        try
        {
            await _commands.SendAsync(
                WindowCommand.Complete(),
                CancellationToken.None).ConfigureAwait(false);
            _commands.Complete();
        }
        catch (Exception exception)
        {
            ((IDataflowBlock)_commands).Fault(exception);
        }
    }

    private void CompleteOutput(Task completion)
    {
        TryCancelTimer();
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            return;
        }

        _output.Complete();
    }

    private Dictionary<string, object?> CreateAttributes(
        FlowWindow<TInput>? window = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = _options.InputType,
            ["maxItems"] = _options.MaxItems,
            ["timeMilliseconds"] = _options.TimeMilliseconds,
            ["emitPartialOnCompletion"] = _options.EmitPartialOnCompletion
        };

        if (window is not null)
        {
            attributes["sequence"] = window.Sequence;
            attributes["count"] = window.Count;
            attributes["reason"] = window.Reason.ToString();
            attributes["durationMilliseconds"] = window.Duration.TotalMilliseconds;
        }

        return attributes;
    }

    private string CreateErrorContext()
        => $"inputType={_options.InputType}; maxItems={_options.MaxItems}; timeMilliseconds={_options.TimeMilliseconds}";

    private sealed record WindowCommand(
        WindowCommandKind Kind,
        TInput? Input = default,
        long WindowVersion = 0)
    {
        public static WindowCommand FromInput(TInput input)
            => new(WindowCommandKind.Input, input);

        public static WindowCommand Timer(long version)
            => new(WindowCommandKind.Timer, WindowVersion: version);

        public static WindowCommand Complete()
            => new(WindowCommandKind.Complete);
    }

    private enum WindowCommandKind
    {
        Input,
        Timer,
        Complete
    }
}
