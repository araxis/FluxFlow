using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

public sealed class FlowForkNode<TInput> : FlowNodeBase
{
    private readonly ForkRoutingOptions _options;
    private readonly Dictionary<string, BufferBlock<TInput>> _outputBlocks;
    private readonly IReadOnlyDictionary<string, ISourceBlock<TInput>> _outputs;
    private readonly ActionBlock<TInput> _input;
    private readonly CancellationToken _processingCancellationToken;

    public FlowForkNode(ForkRoutingOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.Outputs.Length == 0)
        {
            throw new ArgumentException(
                "flow.fork requires at least one output.",
                nameof(options));
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.fork bounded capacity must be greater than zero.");
        }

        var blockOptions = new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity };
        _outputBlocks = options.Outputs
            .ToDictionary(
                output => output.Trim(),
                _ => new BufferBlock<TInput>(blockOptions),
                StringComparer.Ordinal);
        _outputs = _outputBlocks.ToDictionary(
            output => output.Key,
            output => (ISourceBlock<TInput>)output.Value,
            StringComparer.Ordinal);
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _processingCancellationToken = inputOptions.CancellationToken;
        _input = new ActionBlock<TInput>(ForkAsync, inputOptions);
        _input.Completion.ContinueWith(
            CompleteOutputs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(Task.WhenAll(_outputBlocks.Values.Select(output => output.Completion)));
    }

    public ITargetBlock<TInput> Input => _input;

    public IReadOnlyDictionary<string, ISourceBlock<TInput>> Outputs => _outputs;

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
            foreach (var output in _outputBlocks.Values)
            {
                ((IDataflowBlock)output).Fault(exception);
            }
        }
    }

    private async Task ForkAsync(TInput input)
    {
        _processingCancellationToken.ThrowIfCancellationRequested();
        foreach (var output in _outputBlocks.Values)
        {
            await output.SendAsync(input, _processingCancellationToken)
                .ConfigureAwait(false);
        }

        TryEmitDiagnostic(
            RoutingDiagnosticNames.ForkForwarded,
            message: "flow.fork forwarded value.",
            attributes: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["inputType"] = _options.InputType,
                ["outputs"] = _outputBlocks.Count
            });
    }

    private void CompleteOutputs(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            foreach (var output in _outputBlocks.Values)
            {
                ((IDataflowBlock)output).Fault(exception);
            }

            return;
        }

        foreach (var output in _outputBlocks.Values)
        {
            output.Complete();
        }
    }
}
