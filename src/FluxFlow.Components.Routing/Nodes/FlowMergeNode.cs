using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Components.Routing.Timing;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

public sealed class FlowMergeNode<TInput> : FlowNodeBase
{
    private readonly MergeRoutingOptions _options;
    private readonly IRoutingClock _clock;
    private readonly Dictionary<string, ActionBlock<TInput>> _inputBlocks;
    private readonly IReadOnlyDictionary<string, ITargetBlock<TInput>> _inputs;
    private readonly ActionBlock<MergeCommand> _merge;
    private readonly BufferBlock<FlowMergeItem<TInput>> _output;
    private long _nextSequence;

    public FlowMergeNode(MergeRoutingOptions options)
        : this(options, SystemRoutingClock.Instance)
    {
    }

    public FlowMergeNode(
        MergeRoutingOptions options,
        IRoutingClock clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (options.Inputs.Length == 0)
        {
            throw new ArgumentException(
                "flow.merge requires at least one input.",
                nameof(options));
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.merge bounded capacity must be greater than zero.");
        }

        var blockOptions = new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity };
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        var mergeOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _output = new BufferBlock<FlowMergeItem<TInput>>(blockOptions);
        _merge = new ActionBlock<MergeCommand>(
            EmitAsync,
            mergeOptions);
        _inputBlocks = options.Inputs
            .ToDictionary(
                input => input.Trim(),
                input =>
                {
                    var source = input.Trim();
                    return new ActionBlock<TInput>(
                        value => QueueAsync(source, value),
                        inputOptions);
                },
                StringComparer.Ordinal);
        _inputs = _inputBlocks.ToDictionary(
            input => input.Key,
            input => (ITargetBlock<TInput>)input.Value,
            StringComparer.Ordinal);
        Task.WhenAll(_inputBlocks.Values.Select(input => input.Completion))
            .ContinueWith(
                CompleteMerge,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        _merge.Completion.ContinueWith(
            CompleteOutput,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(_output.Completion);
    }

    public IReadOnlyDictionary<string, ITargetBlock<TInput>> Inputs => _inputs;

    public ISourceBlock<FlowMergeItem<TInput>> Output => _output;

    public override void Complete()
    {
        foreach (var input in _inputBlocks.Values)
        {
            input.Complete();
        }
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            FaultNode(exception);
        }
        finally
        {
            foreach (var input in _inputBlocks.Values)
            {
                ((IDataflowBlock)input).Fault(exception);
            }

            ((IDataflowBlock)_merge).Fault(exception);
            ((IDataflowBlock)_output).Fault(exception);
        }
    }

    private async Task QueueAsync(
        string source,
        TInput input)
    {
        var accepted = await _merge.SendAsync(
            new MergeCommand(source, input)).ConfigureAwait(false);
        if (!accepted)
        {
            throw new InvalidOperationException("flow.merge rejected queued input.");
        }
    }

    private async Task EmitAsync(MergeCommand command)
    {
        var item = new FlowMergeItem<TInput>
        {
            Sequence = Interlocked.Increment(ref _nextSequence),
            Source = command.Source,
            Value = command.Value,
            ReceivedAt = _clock.UtcNow
        };
        await _output.SendAsync(item).ConfigureAwait(false);
        TryEmitDiagnostic(
            RoutingDiagnosticNames.MergeEmitted,
            message: "flow.merge emitted value.",
            attributes: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["inputType"] = _options.InputType,
                ["source"] = command.Source,
                ["sequence"] = item.Sequence
            });
    }

    private void CompleteMerge(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_merge).Fault(exception);
            return;
        }

        _merge.Complete();
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

    private sealed record MergeCommand(
        string Source,
        TInput Value);
}
