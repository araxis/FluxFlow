using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Diagnostics;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mapping.Nodes;

public sealed class FlowMapperNode<TInput, TOutput> : FlowNodeBase
{
    private readonly IFlowExpressionEngine _expressionEngine;
    private readonly IMappingContextFactory _contextFactory;
    private readonly MappingNodeContext _nodeContext;
    private readonly MapperOptions _options;
    private readonly TransformManyBlock<TInput, TOutput> _input;
    private readonly BufferBlock<TOutput> _output;
    private readonly BufferBlock<TInput> _failed;

    public FlowMapperNode(
        MapperOptions options,
        IFlowExpressionEngine expressionEngine,
        IMappingContextFactory contextFactory,
        MappingNodeContext nodeContext)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _expressionEngine = expressionEngine ?? throw new ArgumentNullException(nameof(expressionEngine));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _nodeContext = nodeContext ?? throw new ArgumentNullException(nameof(nodeContext));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Expression);
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Mapper bounded capacity must be greater than zero.");
        }

        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true
        };
        var blockOptions = new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity };
        _input = new TransformManyBlock<TInput, TOutput>(MapAsync, inputOptions);
        _output = new BufferBlock<TOutput>(blockOptions);
        _failed = new BufferBlock<TInput>(blockOptions);
        _input.LinkTo(_output);
        _input.Completion.ContinueWith(
            CompleteOutputs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(Task.WhenAll(_output.Completion, _failed.Completion));
    }

    public ITargetBlock<TInput> Input => _input;

    public ISourceBlock<TOutput> Output => _output;

    public ISourceBlock<TInput> Failed => _failed;

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
            ((IDataflowBlock)_output).Fault(exception);
            ((IDataflowBlock)_failed).Fault(exception);
        }
    }

    private async Task<IEnumerable<TOutput>> MapAsync(TInput input)
    {
        try
        {
            var context = _contextFactory.Create(input, _nodeContext);
            var value = _expressionEngine.Evaluate(
                _options.Expression!,
                context,
                typeof(TOutput));

            var output = CoerceOutput(value);
            TryEmitDiagnostic(
                MappingDiagnosticNames.MapperSucceeded,
                message: "Mapped workflow message.",
                attributes: CreateAttributes());

            return [output];
        }
        catch (Exception exception)
        {
            TryReportError(
                MappingErrorCodes.MapperFailed,
                $"flow.mapper failed to map input: {exception.Message}",
                exception,
                CreateErrorContext());
            TryEmitDiagnostic(
                MappingDiagnosticNames.MapperFailed,
                FlowDiagnosticLevel.Error,
                "flow.mapper failed to map input.",
                exception,
                CreateAttributes());

            await _failed.SendAsync(input).ConfigureAwait(false);

            return [];
        }
    }

    private void CompleteOutputs(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            ((IDataflowBlock)_failed).Fault(exception);
            return;
        }

        _output.Complete();
        _failed.Complete();
    }

    private static TOutput CoerceOutput(object? value)
    {
        if (value is TOutput output)
        {
            return output;
        }

        if (value is null && default(TOutput) is null)
        {
            return default!;
        }

        var actualType = value?.GetType().Name ?? "null";
        throw new InvalidOperationException(
            $"Mapper expression returned '{actualType}', expected '{typeof(TOutput).Name}'.");
    }

    private Dictionary<string, object?> CreateAttributes()
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = _options.InputType,
            ["outputType"] = _options.EffectiveOutputType,
            ["engine"] = _expressionEngine.Name
        };

        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
        {
            attributes["expressionId"] = _options.ExpressionId;
        }

        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
        {
            attributes["expressionName"] = _options.ExpressionName;
        }

        return attributes;
    }

    private string CreateErrorContext()
    {
        var values = new List<string>
        {
            $"inputType={_options.InputType}",
            $"outputType={_options.EffectiveOutputType}",
            $"engine={_expressionEngine.Name}"
        };

        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
        {
            values.Add($"expressionId={_options.ExpressionId}");
        }

        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
        {
            values.Add($"expressionName={_options.ExpressionName}");
        }

        return string.Join("; ", values);
    }
}
