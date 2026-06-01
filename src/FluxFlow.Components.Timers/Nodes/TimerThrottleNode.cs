using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Timers.Nodes;

public sealed class TimerThrottleNode<TInput> : FlowNodeBase, IAsyncDisposable
{
    private readonly TimerThrottleSettings _settings;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<TInput> _output;
    private readonly CancellationTokenSource _processingCancellation = new();
    private DateTimeOffset? _lastEmittedAt;
    private long _emitted;

    internal TimerThrottleNode(TimerThrottleSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Timer throttle bounded capacity must be greater than zero.");
        }

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

    private async Task ThrottleAsync(TInput input)
    {
        try
        {
            await WaitForSlotAsync().ConfigureAwait(false);
            _lastEmittedAt = DateTimeOffset.UtcNow;
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
            delay = nextAllowedAt - DateTimeOffset.UtcNow;
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, _processingCancellation.Token).ConfigureAwait(false);
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
