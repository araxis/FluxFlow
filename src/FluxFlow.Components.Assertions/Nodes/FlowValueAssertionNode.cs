using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Diagnostics;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Assertions.Nodes;

/// <summary>
/// Evaluates immutable workflow values and emits pass, fail, and expected
/// evaluation failures through one normal result output.
/// </summary>
public sealed class FlowValueAssertionNode : IFlowNode
{
    private readonly FlowValueAssertionOptions _options;
    private readonly IFlowPredicate<FlowValue> _predicate;
    private readonly string _engineName;
    private readonly TimeProvider _clock;
    private readonly TransformBlock<
        FlowMessage<FlowValue>,
        FlowMessage<FlowResult<FlowValueAssertionResult>>> _processor;
    private readonly BroadcastBlock<FlowMessage<FlowResult<FlowValueAssertionResult>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public FlowValueAssertionNode(
        FlowValueAssertionOptions options,
        IFlowExpressionEngine expressionEngine,
        IFlowMapContextFactory<FlowValue>? contextFactory = null,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(expressionEngine);

        _engineName = expressionEngine.Name;
        _clock = clock ?? TimeProvider.System;
        _predicate = contextFactory is null
            ? new ExpressionFlowPredicate<FlowValue>(_options.Expression!, expressionEngine)
            : new ExpressionFlowPredicate<FlowValue>(
                _options.Expression!,
                expressionEngine,
                contextFactory);
        _processor = new TransformBlock<
            FlowMessage<FlowValue>,
            FlowMessage<FlowResult<FlowValueAssertionResult>>>(
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

    public ITargetBlock<FlowMessage<FlowValue>> Input => _processor;

    public ISourceBlock<FlowMessage<FlowResult<FlowValueAssertionResult>>> Output => _output;

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

    private FlowMessage<FlowResult<FlowValueAssertionResult>> Process(
        FlowMessage<FlowValue> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var timestamp = _clock.GetUtcNow();
        if (message.Payload is null)
        {
            return Failure(
                message,
                timestamp,
                AssertionResultKinds.MissingInput,
                AssertionErrorCodeNames.MissingInput,
                "flow.assert requires FlowValue input.",
                AssertionDiagnosticNames.InputMissing);
        }

        bool passed;
        try
        {
            passed = _predicate.IsMatch(message.Payload);
        }
        catch (Exception exception)
        {
            return Failure(
                message,
                timestamp,
                AssertionResultKinds.EvaluationFailed,
                AssertionErrorCodeNames.EvaluationFailed,
                $"flow.assert failed to evaluate input: {exception.Message}",
                AssertionDiagnosticNames.ExpressionFailed,
                exception);
        }

        var resultKind = passed
            ? AssertionResultKinds.Passed
            : AssertionResultKinds.Failed;
        var result = new FlowValueAssertionResult
        {
            EvaluatedAt = timestamp,
            Input = message.Payload,
            Passed = passed,
            Description = _options.EffectiveDescription,
            Message = passed
                ? "Assertion passed."
                : _options.EffectiveFailureMessage,
            Expression = _options.Expression!,
            ExpressionId = _options.ExpressionId,
            ExpressionName = _options.ExpressionName,
            EngineName = _engineName,
            InputType = _options.InputType
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
        return message.With(FlowResult<FlowValueAssertionResult>.Success(
            resultKind,
            result,
            timestamp));
    }

    private FlowMessage<FlowResult<FlowValueAssertionResult>> Failure(
        FlowMessage<FlowValue> message,
        DateTimeOffset timestamp,
        string resultKind,
        string errorCode,
        string errorMessage,
        string diagnosticName,
        Exception? exception = null)
    {
        var error = new DataFlowError(
            errorCode,
            errorMessage,
            category: "Assertions",
            isTransient: false,
            details: CreateErrorDetails(message.Payload, exception));
        PublishEvent(
            message,
            timestamp,
            diagnosticName,
            FlowEventLevel.Warning,
            error.Message,
            resultKind,
            passed: null,
            isError: true);
        return message.With(FlowResult<FlowValueAssertionResult>.Failure(
            resultKind,
            error,
            timestamp));
    }

    private void PublishEvent(
        FlowMessage<FlowValue> message,
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
            ["inputType"] = _options.InputType,
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

    private FlowValue CreateErrorDetails(FlowValue? input, Exception? exception)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["engine"] = FlowValue.From(_engineName),
            ["input"] = input ?? FlowValue.Null,
            ["inputType"] = FlowValue.From(_options.InputType)
        };
        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
            details["expressionId"] = FlowValue.From(_options.ExpressionId);
        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
            details["expressionName"] = FlowValue.From(_options.ExpressionName);
        if (exception is not null)
        {
            details["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name);
        }

        return FlowValue.FromObject(details);
    }

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

    private static FlowValueAssertionOptions ValidateOptions(
        FlowValueAssertionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Expression))
        {
            throw new ArgumentException(
                "flow.assert requires configuration value 'expression'.",
                nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new ArgumentException(
                "flow.assert option 'inputType' cannot be empty.",
                nameof(options));
        }
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.assert option 'boundedCapacity' must be greater than zero.");
        }

        return options;
    }
}
