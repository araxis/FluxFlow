using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Components.Timers.Timing;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Timers.Nodes;

public sealed class TimerDelayNode<TInput> : FlowNodeBase, IAsyncDisposable
{
    private readonly TimerDelaySettings _settings;
    private readonly ITimerClock _clock;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<TInput> _output;
    private readonly CancellationTokenSource _processingCancellation = new();

    internal TimerDelayNode(
        TimerDelaySettings settings,
        ITimerClock clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Timer delay bounded capacity must be greater than zero.");
        }

        var blockOptions = new DataflowBlockOptions { BoundedCapacity = settings.BoundedCapacity };
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = settings.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _input = new ActionBlock<TInput>(DelayAsync, inputOptions);
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
            _processingCancellation.Cancel();
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
        Complete();
        await Completion.ConfigureAwait(false);
        _processingCancellation.Dispose();
    }

    private async Task DelayAsync(TInput input)
    {
        try
        {
            if (_settings.Delay > TimeSpan.Zero)
            {
                await _clock.DelayAsync(_settings.Delay, _processingCancellation.Token).ConfigureAwait(false);
            }

            await _output.SendAsync(input, _processingCancellation.Token).ConfigureAwait(false);
            TryEmitDiagnostic(
                TimerDiagnosticNames.DelayEmitted,
                message: "timer.delay emitted input.",
                attributes: CreateAttributes());
        }
        catch (OperationCanceledException) when (_processingCancellation.IsCancellationRequested)
        {
            throw;
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
}
