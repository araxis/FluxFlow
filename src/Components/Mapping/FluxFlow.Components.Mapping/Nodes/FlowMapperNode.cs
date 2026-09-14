using System.Text.Json;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Diagnostics;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mapping.Nodes;

/// <summary>
/// Maps typed values through a compiled expression and emits mapping failures
/// through the normal output contract.
/// </summary>
public class FlowMapperNode<TInput, TOutput> : FlowNode
{
    public const string MapperSucceeded = MappingDiagnosticNames.MapperSucceeded;
    public const string MapperFailed = MappingDiagnosticNames.MapperFailed;

    private readonly IFlowMapper<TInput, TOutput> _mapper;
    private readonly IMappingContextFactory _contextFactory;
    private readonly MappingNodeContext _nodeContext;
    private readonly MapperOptions _options;
    private readonly string _engineName;
    private readonly TimeProvider _clock;
    public FlowMapperNode(
        MapperOptions options,
        IFlowExpressionEngine expressionEngine,
        IMappingContextFactory? contextFactory = null,
        TimeProvider? clock = null)
        : base(CreateNodeOptions(options))
    {
        _options = ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(expressionEngine);

        _engineName = expressionEngine.Name;
        _contextFactory = contextFactory ?? DefaultMappingContextFactory.Instance;
        _clock = clock ?? TimeProvider.System;
        _nodeContext = new MappingNodeContext
        {
            Options = _options,
            InputType = typeof(TInput),
            OutputType = typeof(TOutput)
        };
        _mapper = new ExpressionFlowMapper<TInput, TOutput>(_options.Expression!, expressionEngine);
    }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage message)
        => await EmitAsync(Process(message), Stopping).ConfigureAwait(false);

    private FlowMessage Process(FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message;

        var timestamp = _clock.GetUtcNow();
        try
        {
            var input = (message.Value is TInput typedInput0 ? typedInput0 : typeof(TInput) == typeof(JsonElement) ? (TInput)(object)message.Value.Json : message.Value.Json.Deserialize<TInput>(ValueJsonOptions))!;
            var context = _contextFactory.Create(input, _nodeContext);
            var value = _mapper.Map(input, context);
            PublishEvent(
                message,
                timestamp,
                MapperSucceeded,
                FlowEventLevel.Information,
                "Mapped workflow value.");
            return message.With(value is FlowValue typedValue ? typedValue
                : value is JsonElement json ? new JsonFlowValue(json)
                : value is JsonDocument document ? new JsonFlowValue(document.RootElement)
                : new JsonFlowValue(JsonSerializer.SerializeToElement(value, ValueJsonOptions)));
        }
        catch (Exception exception)
        {
            FlowDiagnostics.ProcessingException(message, this, exception);
            var error = new FlowError(
                MappingErrorCodeNames.MapperFailed,
                $"flow.mapper failed to map input: {exception.Message}",
                category: "Mapping",
                isTransient: false,
                details: CreateErrorDetails(exception));
            PublishEvent(
                message,
                timestamp,
                MapperFailed,
                FlowEventLevel.Warning,
                error.Message);
            return message.WithError(error);
        }
    }

    private void PublishEvent(
        FlowMessage message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text)
        => EmitEvent(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = level,
            Message = text,
            Attributes = CreateAttributes()
        });

    private Dictionary<string, object?> CreateAttributes()
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = typeof(TInput).FullName ?? typeof(TInput).Name,
            ["outputType"] = typeof(TOutput).FullName ?? typeof(TOutput).Name,
            ["engine"] = _engineName
        };
        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
            attributes["expressionId"] = _options.ExpressionId;
        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
            attributes["expressionName"] = _options.ExpressionName;
        return attributes;
    }

    private JsonElement CreateErrorDetails(Exception exception)
    {
        var details = CreateAttributes();
        details["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name;
        return JsonSerializer.SerializeToElement(details);
    }

    private static MapperOptions ValidateOptions(MapperOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BoundedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Mapper capacity must be positive.");
        if (string.IsNullOrWhiteSpace(options.Expression))
            throw new ArgumentException("flow.mapper requires an expression.", nameof(options));
        return options;
    }

    private static FlowNodeOptions CreateNodeOptions(MapperOptions? options)
    {
        var validated = ValidateOptions(options);
        return new FlowNodeOptions
        {
            InputCapacity = validated.BoundedCapacity,
            OutputCapacity = validated.BoundedCapacity
        };
    }
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>Mapper component with JSON-local evaluation and canonical ports.</summary>
public sealed class JsonMapperNode(
    MapperOptions options,
    IFlowExpressionEngine expressionEngine,
    IMappingContextFactory? contextFactory = null,
    TimeProvider? clock = null)
    : FlowMapperNode<JsonElement, JsonElement>(options, expressionEngine, contextFactory, clock);
