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
public class AssertionNode<T> : IFlowNode
{
    private readonly AssertionOptions _options;
    private readonly IFlowPredicate<T> _predicate;
    private readonly string _engineName;
    private readonly TimeProvider _clock;
    private readonly TransformBlock<FlowMessage<T>, FlowMessage<AssertionResult<T>>> _processor;
    private readonly BroadcastBlock<FlowMessage<AssertionResult<T>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public AssertionNode(
        AssertionOptions options,
        IFlowExpressionEngine expressionEngine,
        IFlowMapContextFactory<T>? contextFactory = null,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(expressionEngine);

        _engineName = expressionEngine.Name;
        _clock = clock ?? TimeProvider.System;
        _predicate = contextFactory is null
            ? new ExpressionFlowPredicate<T>(_options.Expression!, expressionEngine)
            : new ExpressionFlowPredicate<T>(_options.Expression!, expressionEngine, contextFactory);
        _processor = new TransformBlock<FlowMessage<T>, FlowMessage<AssertionResult<T>>>(
            Process,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _options.BoundedCapacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
        _processor.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<FlowMessage<T>> Input => _processor;
    public ISourceBlock<FlowMessage<AssertionResult<T>>> Output => _output;
    public ISourceBlock<FlowEvent> Events => _events;
    public Task Completion => _completion.Task;
    public void Complete() => _processor.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ((IDataflowBlock)_processor).Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Complete();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative unexpected-fault surface.
        }
    }

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

        _events.Post(new FlowEvent
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

    private async Task MonitorCompletionAsync()
    {
        try
        {
            await _processor.Completion.ConfigureAwait(false);
            await _output.Completion.ConfigureAwait(false);
            _events.Complete();
            await _events.Completion.ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            _events.Complete();
            _completion.TrySetException(exception);
        }
    }

    private static AssertionOptions ValidateOptions(AssertionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Expression))
            throw new ArgumentException("flow.assert requires 'expression'.", nameof(options));
        if (options.BoundedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Capacity must be positive.");
        return options;
    }
}

/// <summary>
/// JSON-oriented assertion node used by configuration composition.
/// </summary>
public sealed class JsonAssertionNode : AssertionNode<JsonElement>
{
    public JsonAssertionNode(
        AssertionOptions options,
        IFlowExpressionEngine expressionEngine,
        IFlowMapContextFactory<JsonElement>? contextFactory = null,
        TimeProvider? clock = null)
        : base(options, expressionEngine, contextFactory, clock)
    {
    }
}
