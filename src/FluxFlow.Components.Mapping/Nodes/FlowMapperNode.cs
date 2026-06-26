using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Diagnostics;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mapping.Nodes;

/// <summary>
/// A standalone mapper node — a "blockified" expression mapper. Post a
/// <c>FlowMessage&lt;TInput&gt;</c> to <c>Input</c>; the node maps the payload with a
/// host-provided <see cref="IFlowExpressionEngine"/> (the mapping expression is
/// compiled once at construction) and broadcasts a <c>FlowMessage&lt;TOutput&gt;</c> on
/// <c>Output</c> carrying the same correlation id. When a mapping throws, the original
/// input is fanned to the <c>Failed</c> port and a <see cref="FlowError"/> surfaces on
/// <c>Errors</c> (same correlation id); the node keeps processing later messages.
/// Diagnostics go to <c>Events</c>. Works with nothing but
/// <c>new FlowMapperNode&lt;TIn, TOut&gt;(options, expressionEngine)</c> — no engine.
/// </summary>
public sealed class FlowMapperNode<TInput, TOutput> : FlowNode<TInput, TOutput>
{
    public const string MapperSucceeded = MappingDiagnosticNames.MapperSucceeded;
    public const string MapperFailed = MappingDiagnosticNames.MapperFailed;

    private readonly IFlowMapper<TInput, TOutput> _mapper;
    private readonly IMappingContextFactory _contextFactory;
    private readonly MappingNodeContext _nodeContext;
    private readonly string _engineName;
    private readonly MapperOptions _options;
    private readonly TimeProvider _clock;
    private readonly BroadcastBlock<FlowMessage<TInput>> _failed;

    public FlowMapperNode(
        MapperOptions options,
        IFlowExpressionEngine expressionEngine,
        IMappingContextFactory? contextFactory = null,
        TimeProvider? clock = null)
        : this(ValidateOptions(options), expressionEngine, contextFactory, clock)
    {
    }

    private FlowMapperNode(
        ValidatedOptions options,
        IFlowExpressionEngine expressionEngine,
        IMappingContextFactory? contextFactory,
        TimeProvider? clock)
        : base(options.FlowNodeOptions)
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);

        _options = options.MapperOptions;
        _engineName = expressionEngine.Name;
        _contextFactory = contextFactory ?? DefaultMappingContextFactory.Instance;
        _clock = clock ?? TimeProvider.System;
        _nodeContext = new MappingNodeContext
        {
            Options = _options,
            InputType = typeof(TInput),
            OutputType = typeof(TOutput)
        };

        // Compile the mapper expression once; the node evaluates the compiled form
        // per message via the supplied FlowMapContext.
        _mapper = new ExpressionFlowMapper<TInput, TOutput>(options.Expression, expressionEngine);

        _failed = AddOutput<FlowMessage<TInput>>();
    }

    /// <summary>Original input when the mapping fails; broadcast, carries the correlation id.</summary>
    public ISourceBlock<FlowMessage<TInput>> Failed => _failed;

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        TOutput output;
        try
        {
            var context = _contextFactory.Create(input, _nodeContext);
            output = _mapper.Map(input, context);
        }
        catch (Exception exception)
        {
            ReportFailure(message, exception);
            return Task.CompletedTask;
        }

        // Carry the correlation id forward onto the mapped result.
        Emit(message.With(output));
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = MapperSucceeded,
            Level = FlowEventLevel.Information,
            Message = "Mapped workflow message.",
            Attributes = CreateAttributes()
        });

        return Task.CompletedTask;
    }

    private void ReportFailure(FlowMessage<TInput> source, Exception exception)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = MappingErrorCodes.MapperFailed,
            Message = $"flow.mapper failed to map input: {DescribeFailure(exception)}",
            Context = CreateErrorContext(),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = MapperFailed,
            Level = FlowEventLevel.Error,
            Message = "flow.mapper failed to map input.",
            Attributes = CreateAttributes()
        });

        // Fan the original input (correlation id intact) to the Failed port.
        _failed.Post(source);
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

    private static ValidatedOptions ValidateOptions(MapperOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Mapper bounded capacity must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.Expression))
        {
            throw new ArgumentException("flow.mapper requires an expression.", nameof(options));
        }

        return new ValidatedOptions(options);
    }

    private sealed class ValidatedOptions(MapperOptions mapperOptions)
    {
        public MapperOptions MapperOptions { get; } = mapperOptions;

        public string Expression { get; } = mapperOptions.Expression!;

        public FlowNodeOptions FlowNodeOptions { get; } = new()
        {
            InputCapacity = mapperOptions.BoundedCapacity
        };
    }
}
