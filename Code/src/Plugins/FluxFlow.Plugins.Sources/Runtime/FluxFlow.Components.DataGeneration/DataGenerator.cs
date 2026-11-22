using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.DataGeneration;

/// <summary>Combines input data with fake values in one JSONata-generated payload per input.</summary>
public sealed class DataGenerator : FlowNode
{
    private readonly GeneratorExpression _expression;
    public ITargetBlock<FlowMessage> Input { get; }

    public DataGenerator(DataGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity);
        var execution = options.Execution ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        if (execution.MaxDegreeOfParallelism != 1)
            throw new ArgumentException("Data generation requires sequential execution.", nameof(options));
        _expression = new GeneratorExpression(options.Expression, options.Locale);
        Output = new BroadcastBlock<FlowMessage>(static message => message,
            new DataflowBlockOptions { BoundedCapacity = options.OutputCapacity });
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(
            message => message.ProcessMessageAsync(this, Generate, Output, Stopping), execution);
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private IEnumerable<FlowMessage> Generate(FlowMessage message)
    {
        Stopping.ThrowIfCancellationRequested();
        var value = _expression.Evaluate(message);
        Stopping.ThrowIfCancellationRequested();
        return [message.WithValue(value)];
    }
}
