using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Diagnostics;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mapping.Nodes;

public sealed class FlowMapperNode<TInput, TOutput> : FlowNodeBase
{
    private readonly IFlowMapper<TInput, TOutput> _mapper;
    private readonly IMappingContextFactory _contextFactory;
    private readonly MappingNodeContext _nodeContext;
    private readonly string _engineName;
    private readonly MapperOptions _options;
    private readonly TransformManyBlock<TInput, TOutput> _input;
    private readonly BufferBlock<TOutput> _output;
    private readonly BufferBlock<TInput> _failed;

    public FlowMapperNode(
        MapperOptions options,
        IFlowMapper<TInput, TOutput> mapper,
        IMappingContextFactory contextFactory,
        MappingNodeContext nodeContext,
        string engineName)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _nodeContext = nodeContext ?? throw new ArgumentNullException(nameof(nodeContext));
        _engineName = engineName ?? throw new ArgumentNullException(nameof(engineName));
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
            var output = _mapper.Map(input, context);
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
                $"flow.mapper failed to map input: {DescribeFailure(exception)}",
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

    private string DescribeFailure(Exception exception)
        => exception switch
        {
            // The compiled-mapper path surfaces a wrong-type or null result as a raw
            // InvalidCastException/NullReferenceException. Restore the descriptive
            // "expected type" message that the interpreted path used to produce.
            InvalidCastException => DescribeIncompatibleResult(exception),
            NullReferenceException => DescribeIncompatibleResult(exception),
            _ => exception.Message
        };

    private string DescribeIncompatibleResult(Exception exception)
        => $"the mapping expression returned an incompatible or null value; " +
            $"expected output type '{_nodeContext.OutputType}' (configured as '{_options.EffectiveOutputType}'). " +
            $"{exception.Message}";

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

    private Dictionary<string, object?> CreateAttributes()
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = _options.InputType,
            ["outputType"] = _options.EffectiveOutputType,
            ["engine"] = _engineName
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
            $"engine={_engineName}"
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
