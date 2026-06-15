using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Timers.Nodes;

public sealed class TimerDelayNode<TInput> : FlowNodeBase, IAsyncDisposable
{
    private readonly TimerDelaySettings _settings;
    private readonly TimeProvider _clock;
    private readonly TransformBlock<TInput, DelayedInput> _input;
    private readonly ActionBlock<DelayedInput> _emitter;
    private readonly BufferBlock<TInput> _output;
    private readonly CancellationTokenSource _processingCancellation = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly CancellationTokenSource _delayCancellation;
    private bool _disposed;

    internal TimerDelayNode(
        TimerDelaySettings settings,
        TimeProvider clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Timer delay bounded capacity must be greater than zero.");
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
        var emitterOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = settings.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        // Stamp the due time when an item arrives so a burst of inputs is emitted a
        // constant offset after arrival instead of accumulating one delay per item.
        _input = new TransformBlock<TInput, DelayedInput>(Stamp, inputOptions);
        _emitter = new ActionBlock<DelayedInput>(DelayAsync, emitterOptions);
        _input.LinkTo(
            _emitter,
            new DataflowLinkOptions { PropagateCompletion = true });
        _output = new BufferBlock<TInput>(blockOptions);
        _emitter.Completion.ContinueWith(
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
            ((IDataflowBlock)_emitter).Fault(exception);
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

    private DelayedInput Stamp(TInput input)
        => new(input, _clock.GetUtcNow() + _settings.Delay);

    private async Task DelayAsync(DelayedInput delayed)
    {
        try
        {
            var remaining = delayed.DueAt - _clock.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, _clock, _delayCancellation.Token).ConfigureAwait(false);
            }

            await _output.SendAsync(delayed.Input, _processingCancellation.Token).ConfigureAwait(false);
            TryEmitDiagnostic(
                TimerDiagnosticNames.DelayEmitted,
                message: "timer.delay emitted input.",
                attributes: CreateAttributes());
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
                TimerErrorCodes.DelayFailed,
                $"timer.delay failed: {exception.Message}",
                exception,
                CreateErrorContext());
            TryEmitDiagnostic(
                TimerDiagnosticNames.DelayFailed,
                FlowDiagnosticLevel.Error,
                "timer.delay failed.",
                exception,
                CreateAttributes());
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

    private Dictionary<string, object?> CreateAttributes()
        => new(StringComparer.Ordinal)
        {
            ["name"] = _settings.Name,
            ["inputType"] = _settings.InputType,
            ["delayMilliseconds"] = _settings.Delay.TotalMilliseconds
        };

    private string CreateErrorContext()
    {
        var values = new List<string>
        {
            $"name={_settings.Name}",
            $"inputType={_settings.InputType}",
            $"delayMilliseconds={_settings.Delay.TotalMilliseconds}"
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

    private readonly record struct DelayedInput(TInput Input, DateTimeOffset DueAt);
}
