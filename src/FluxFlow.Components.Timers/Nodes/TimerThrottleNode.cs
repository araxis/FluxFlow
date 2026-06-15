using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Timers.Nodes;

public sealed class TimerThrottleNode<TInput> : FlowNodeBase, IAsyncDisposable
{
    private readonly TimerThrottleSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<TInput> _output;
    private readonly CancellationTokenSource _processingCancellation = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly CancellationTokenSource _delayCancellation;
    private DateTimeOffset? _lastEmittedAt;
    private long _emitted;
    private bool _disposed;

    internal TimerThrottleNode(
        TimerThrottleSettings settings,
        TimeProvider clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Timer throttle bounded capacity must be greater than zero.");
        }

        _delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _processingCancellation.Token,
            _disposeCancellation.Token);
        var blockOptions = new DataflowBlockOptions { BoundedCapacity = settings.BoundedCapacity };
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = settings.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _input = new ActionBlock<TInput>(ThrottleAsync, inputOptions);
        _output = new BufferBlock<TInput>(blockOptions);
        _input.Completion.ContinueWith(
            CompleteOutput,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(_output.Completion);
    }

    public ITargetBlock<TInput> Input => _input;

    public ISourceBlock<TInput> Output => _output;

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            TryCancel(_processingCancellation);
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
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
        TryCancel(_disposeCancellation);
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Dispose tolerates nodes that completed in a faulted or canceled state.
        }

        _delayCancellation.Dispose();
        _disposeCancellation.Dispose();
        _processingCancellation.Dispose();
    }

    private async Task ThrottleAsync(TInput input)
    {
        try
        {
            await WaitForSlotAsync().ConfigureAwait(false);
            _lastEmittedAt = _clock.GetUtcNow();
            await _output.SendAsync(input, _processingCancellation.Token).ConfigureAwait(false);

            var sequence = Interlocked.Increment(ref _emitted);
            TryEmitDiagnostic(
                TimerDiagnosticNames.ThrottleEmitted,
                message: "timer.throttle emitted input.",
                attributes: CreateAttributes(sequence));
        }
        catch (OperationCanceledException) when (_processingCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            // Dispose cancels in-flight delays so disposal drains promptly.
        }
        catch (Exception exception)
        {
            TryReportError(
                TimerErrorCodes.ThrottleFailed,
                $"timer.throttle failed: {exception.Message}",
                exception,
                CreateErrorContext());
            TryEmitDiagnostic(
                TimerDiagnosticNames.ThrottleFailed,
                FlowDiagnosticLevel.Error,
                "timer.throttle failed.",
                exception,
                CreateAttributes());
        }
    }

    private async Task WaitForSlotAsync()
    {
        TimeSpan delay;
        if (_lastEmittedAt is null)
        {
            delay = _settings.EmitFirstImmediately ? TimeSpan.Zero : _settings.Interval;
        }
        else
        {
            var nextAllowedAt = _lastEmittedAt.Value + _settings.Interval;
            delay = nextAllowedAt - _clock.GetUtcNow();
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, _clock, _delayCancellation.Token).ConfigureAwait(false);
        }
    }

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The node was already disposed; cancellation is no longer required.
        }
    }

    private Dictionary<string, object?> CreateAttributes(long? sequence = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = _settings.Name,
            ["inputType"] = _settings.InputType,
            ["intervalMilliseconds"] = _settings.Interval.TotalMilliseconds,
            ["emitFirstImmediately"] = _settings.EmitFirstImmediately
        };

        if (sequence.HasValue)
        {
            attributes["sequence"] = sequence.Value;
        }

        return attributes;
    }

    private string CreateErrorContext()
    {
        var values = new List<string>
        {
            $"name={_settings.Name}",
            $"inputType={_settings.InputType}",
            $"intervalMilliseconds={_settings.Interval.TotalMilliseconds}",
            $"emitFirstImmediately={_settings.EmitFirstImmediately}"
        };

        return string.Join("; ", values);
    }

    private void CompleteOutput(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            return;
        }

        _output.Complete();
    }
}
