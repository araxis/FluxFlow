using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Components.Timers.Timing;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Timers.Nodes;

public sealed class TimerDebounceNode<TInput> : FlowNodeBase, IAsyncDisposable
{
    private readonly TimerDebounceSettings _settings;
    private readonly ITimerClock _clock;
    private readonly BufferBlock<TInput> _input;
    private readonly BufferBlock<TInput> _output;
    private readonly CancellationTokenSource _processingCancellation = new();
    private readonly Task _processingTask;
    private int _faulted;
    private long _emitted;

    internal TimerDebounceNode(
        TimerDebounceSettings settings,
        ITimerClock clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Timer debounce bounded capacity must be greater than zero.");
        }

        var blockOptions = new DataflowBlockOptions { BoundedCapacity = settings.BoundedCapacity };
        _input = new BufferBlock<TInput>(blockOptions);
        _output = new BufferBlock<TInput>(blockOptions);
        _processingTask = RunAsync();
        CompleteWhen(_output.Completion);
    }

    public ITargetBlock<TInput> Input => _input;

    public ISourceBlock<TInput> Output => _output;

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Interlocked.Exchange(ref _faulted, 1);
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
        await _processingTask.ConfigureAwait(false);
        _processingCancellation.Dispose();
    }

    private async Task RunAsync()
    {
        var hasPending = false;
        TInput? pending = default;

        try
        {
            while (true)
            {
                if (!hasPending)
                {
                    if (!await _input.OutputAvailableAsync(_processingCancellation.Token)
                            .ConfigureAwait(false))
                    {
                        break;
                    }

                    while (_input.TryReceive(out var item))
                    {
                        pending = item;
                        hasPending = true;
                    }
                }

                using var raceCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(_processingCancellation.Token);
                var quietPeriod = _clock.DelayAsync(_settings.QuietPeriod, raceCancellation.Token).AsTask();
                var inputAvailable = _input.OutputAvailableAsync(raceCancellation.Token);
                var completed = await Task.WhenAny(quietPeriod, inputAvailable).ConfigureAwait(false);
                raceCancellation.Cancel();

                if (completed == inputAvailable)
                {
                    if (await inputAvailable.ConfigureAwait(false))
                    {
                        while (_input.TryReceive(out var item))
                        {
                            pending = item;
                            hasPending = true;
                        }

                        continue;
                    }

                    if (hasPending)
                    {
                        await EmitAsync(pending!).ConfigureAwait(false);
                        hasPending = false;
                    }

                    break;
                }

                await quietPeriod.ConfigureAwait(false);
                if (hasPending)
                {
                    await EmitAsync(pending!).ConfigureAwait(false);
                    hasPending = false;
                }
            }

            _output.Complete();
        }
        catch (OperationCanceledException) when (_processingCancellation.IsCancellationRequested)
        {
            if (Volatile.Read(ref _faulted) == 0)
            {
                _output.Complete();
            }
        }
        catch (Exception exception)
        {
            TryReportError(
                TimerErrorCodes.DebounceFailed,
                $"timer.debounce failed: {exception.Message}",
                exception,
                CreateErrorContext());
            TryEmitDiagnostic(
                TimerDiagnosticNames.DebounceFailed,
                FlowDiagnosticLevel.Error,
                "timer.debounce failed.",
                exception,
                CreateAttributes());
            ((IDataflowBlock)_output).Fault(exception);
        }
    }

    private async Task EmitAsync(TInput input)
    {
        await _output.SendAsync(input, _processingCancellation.Token).ConfigureAwait(false);

        var sequence = Interlocked.Increment(ref _emitted);
        TryEmitDiagnostic(
            TimerDiagnosticNames.DebounceEmitted,
            message: "timer.debounce emitted input.",
            attributes: CreateAttributes(sequence));
    }

    private Dictionary<string, object?> CreateAttributes(long? sequence = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = _settings.Name,
            ["inputType"] = _settings.InputType,
            ["quietPeriodMilliseconds"] = _settings.QuietPeriod.TotalMilliseconds
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
            $"quietPeriodMilliseconds={_settings.QuietPeriod.TotalMilliseconds}"
        };

        return string.Join("; ", values);
    }
}
