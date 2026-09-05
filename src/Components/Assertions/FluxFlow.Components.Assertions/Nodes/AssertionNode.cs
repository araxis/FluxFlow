using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Diagnostics;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Assertions.Nodes;

/// <summary>
/// Evaluates typed values and emits assertion outcomes or evaluation errors
/// through one normal output.
/// </summary>
public class AssertionNode<T> : FlowNode<T, AssertionResult<T>>
{
    private readonly AssertionOptions _options;
    private readonly IFlowPredicate<T> _predicate;
    private readonly string _engineName;
    private readonly TimeProvider _clock;
    public AssertionNode(
        AssertionOptions options,
        IFlowExpressionEngine expressionEngine,
        IFlowMapContextFactory<T>? contextFactory = null,
        TimeProvider? clock = null)
        : base(CreateNodeOptions(options))
    {
        _options = ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(expressionEngine);

        _engineName = expressionEngine.Name;
        _clock = clock ?? TimeProvider.System;
        _predicate = contextFactory is null
            ? new ExpressionFlowPredicate<T>(_options.Expression!, expressionEngine)
            : new ExpressionFlowPredicate<T>(_options.Expression!, expressionEngine, contextFactory);
    }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage<T> message)
        => await EmitAsync(Process(message), Stopping).ConfigureAwait(false);

    private FlowMessage<AssertionResult<T>> Process(FlowMessage<T> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message.WithError<AssertionResult<T>>(message.Error!);

        var timestamp = _clock.GetUtcNow();
        bool passed;
        try
        {
            passed = _predicate.IsMatch(message.Value);
        }
        catch (Exception exception)
        {
            var error = new FlowError(
                AssertionErrorCodeNames.EvaluationFailed,
                $"flow.assert failed to evaluate input: {exception.Message}",
                category: "Assertions",
                isTransient: false,
                details: CreateErrorDetails(exception));
            PublishEvent(
                message,
                timestamp,
                AssertionDiagnosticNames.ExpressionFailed,
                FlowEventLevel.Warning,
                error.Message,
                AssertionResultKinds.EvaluationFailed,
                passed: null,
                isError: true);
            return message.WithError<AssertionResult<T>>(error);
        }

        var resultKind = passed ? AssertionResultKinds.Passed : AssertionResultKinds.Failed;
        var result = new AssertionResult<T>
        {
            EvaluatedAt = timestamp,
            Input = message.Value,
            Passed = passed,
            Description = _options.EffectiveDescription,
            Message = passed ? "Assertion passed." : _options.EffectiveFailureMessage,
            Expression = _options.Expression!,
            ExpressionId = _options.ExpressionId,
            ExpressionName = _options.ExpressionName,
            EngineName = _engineName,
            InputType = typeof(T).FullName ?? typeof(T).Name
        };
        PublishEvent(
            message,
            timestamp,
            AssertionDiagnosticNames.Evaluated,
            FlowEventLevel.Information,
            passed ? "flow.assert passed input." : "flow.assert failed input.",
            resultKind,
            passed,
            isError: false);
        return message.With(result);
    }

    private void PublishEvent(
        FlowMessage<T> message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text,
        string resultKind,
        bool? passed,
        bool isError)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["engine"] = _engineName,
            ["inputType"] = typeof(T).FullName ?? typeof(T).Name,
            ["isError"] = isError,
            ["resultKind"] = resultKind
        };
        if (passed.HasValue)
            attributes["passed"] = passed.Value;
        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
            attributes["expressionId"] = _options.ExpressionId;
        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
            attributes["expressionName"] = _options.ExpressionName;

        EmitEvent(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = level,
            Message = text,
            Attributes = attributes
        });
    }

    private JsonElement CreateErrorDetails(Exception exception)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["engine"] = _engineName,
            ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
            ["expressionId"] = _options.ExpressionId,
            ["expressionName"] = _options.ExpressionName,
            ["inputType"] = typeof(T).FullName ?? typeof(T).Name
        });

    private static AssertionOptions ValidateOptions(AssertionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Expression))
            throw new ArgumentException("flow.assert requires 'expression'.", nameof(options));
        if (options.BoundedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Capacity must be positive.");
        return options;
    }

    private static FlowNodeOptions CreateNodeOptions(AssertionOptions? options)
    {
        var validated = ValidateOptions(options);
        return new FlowNodeOptions
        {
            InputCapacity = validated.BoundedCapacity,
            OutputCapacity = validated.BoundedCapacity
        };
    }
}

/// <summary>
/// Canonical runtime assertion node. JSON materialization is owned by this
/// component and is not represented as a separate workflow node.
/// </summary>
public sealed class JsonAssertionNode : FlowNode
{
    private readonly AssertionOptions _options;
    private readonly IFlowPredicate<JsonElement> _predicate;
    private readonly string _engineName;
    private readonly TimeProvider _clock;

    public JsonAssertionNode(
        AssertionOptions options,
        IFlowExpressionEngine expressionEngine,
        IFlowMapContextFactory<JsonElement>? contextFactory = null,
        TimeProvider? clock = null)
        : base(CreateRuntimeNodeOptions(options))
    {
        _options = ValidateRuntimeOptions(options);
        ArgumentNullException.ThrowIfNull(expressionEngine);

        _engineName = expressionEngine.Name;
        _clock = clock ?? TimeProvider.System;
        _predicate = contextFactory is null
            ? new ExpressionFlowPredicate<JsonElement>(_options.Expression!, expressionEngine)
            : new ExpressionFlowPredicate<JsonElement>(_options.Expression!, expressionEngine, contextFactory);
    }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage message)
        => await EmitAsync(ProcessRuntime(message), Stopping).ConfigureAwait(false);

    private FlowMessage ProcessRuntime(FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message.WithError(message.Error!);

        var input = FlowValueMaterializers.JsonElement.Materialize(message.Value).Value;
        var timestamp = _clock.GetUtcNow();
        bool passed;
        try
        {
            passed = _predicate.IsMatch(input);
        }
        catch (Exception exception)
        {
            var error = new FlowError(
                AssertionErrorCodeNames.EvaluationFailed,
                $"flow.assert failed to evaluate input: {exception.Message}",
                category: "Assertions",
                isTransient: false,
                details: CreateRuntimeErrorDetails(exception));
            PublishRuntimeEvent(
                message,
                timestamp,
                AssertionDiagnosticNames.ExpressionFailed,
                FlowEventLevel.Warning,
                error.Message,
                AssertionResultKinds.EvaluationFailed,
                passed: null,
                isError: true);
            return message.WithError(error);
        }

        var resultKind = passed ? AssertionResultKinds.Passed : AssertionResultKinds.Failed;
        var result = new AssertionResult<JsonElement>
        {
            EvaluatedAt = timestamp,
            Input = input,
            Passed = passed,
            Description = _options.EffectiveDescription,
            Message = passed ? "Assertion passed." : _options.EffectiveFailureMessage,
            Expression = _options.Expression!,
            ExpressionId = _options.ExpressionId,
            ExpressionName = _options.ExpressionName,
            EngineName = _engineName,
            InputType = typeof(JsonElement).FullName ?? typeof(JsonElement).Name
        };
        PublishRuntimeEvent(
            message,
            timestamp,
            AssertionDiagnosticNames.Evaluated,
            FlowEventLevel.Information,
            passed ? "flow.assert passed input." : "flow.assert failed input.",
            resultKind,
            passed,
            isError: false);
        return message.With(FlowValue.From(result));
    }

    private void PublishRuntimeEvent(
        FlowMessage message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text,
        string resultKind,
        bool? passed,
        bool isError)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["engine"] = _engineName,
            ["inputType"] = typeof(JsonElement).FullName ?? typeof(JsonElement).Name,
            ["isError"] = isError,
            ["resultKind"] = resultKind
        };
        if (passed.HasValue)
            attributes["passed"] = passed.Value;
        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
            attributes["expressionId"] = _options.ExpressionId;
        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
            attributes["expressionName"] = _options.ExpressionName;

        EmitEvent(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = level,
            Message = text,
            Attributes = attributes
        });
    }

    private JsonElement CreateRuntimeErrorDetails(Exception exception)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["engine"] = _engineName,
            ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
            ["expressionId"] = _options.ExpressionId,
            ["expressionName"] = _options.ExpressionName,
            ["inputType"] = typeof(JsonElement).FullName ?? typeof(JsonElement).Name
        });

    private static AssertionOptions ValidateRuntimeOptions(AssertionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Expression))
            throw new ArgumentException("flow.assert requires 'expression'.", nameof(options));
        if (options.BoundedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Capacity must be positive.");
        return options;
    }

    private static FlowNodeOptions CreateRuntimeNodeOptions(AssertionOptions? options)
    {
        var validated = ValidateRuntimeOptions(options);
        return new FlowNodeOptions
        {
            InputCapacity = validated.BoundedCapacity,
            OutputCapacity = validated.BoundedCapacity
        };
    }
}
