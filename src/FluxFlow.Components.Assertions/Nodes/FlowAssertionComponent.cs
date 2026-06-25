using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Diagnostics;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Assertions.Nodes;

/// <summary>
/// A standalone assertion node. Post a <c>FlowMessage&lt;TInput&gt;</c> to
/// <c>Input</c>; the node evaluates the payload against a pre-compiled boolean
/// expression and broadcasts a <c>FlowMessage&lt;FlowAssertionResult&gt;</c> on
/// <c>Output</c>. In addition it fans the original input out to one of two extra
/// ports — <c>Passed</c> when the assertion holds, <c>Failed</c> when it does not —
/// each carrying the same correlation id. Expression evaluation failures surface on
/// <c>Errors</c> (with the original correlation id) and the node keeps processing
/// later messages; diagnostics flow on <c>Events</c>. The predicate is compiled once
/// at construction from the supplied <see cref="IFlowExpressionEngine"/>; works with
/// nothing but <c>new FlowAssertionComponent&lt;T&gt;(options, engine)</c> — no engine
/// runtime, no registry.
/// </summary>
public sealed class FlowAssertionComponent<TInput> : FlowNode<TInput, FlowAssertionResult>
{
    private readonly IFlowPredicate<TInput> _predicate;
    private readonly AssertionResultMetadata _metadata;
    private readonly TimeProvider _clock;
    private readonly BroadcastBlock<FlowMessage<TInput>> _passed;
    private readonly BroadcastBlock<FlowMessage<TInput>> _failed;

    public FlowAssertionComponent(
        AssertionOptions options,
        IFlowExpressionEngine expressionEngine,
        IFlowMapContextFactory<TInput>? contextFactory = null,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = ValidateOptions(options).BoundedCapacity
        })
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);
        _clock = clock ?? TimeProvider.System;

        // Compile the predicate expression once at construction; IsMatch only
        // evaluates the compiled form per message.
        _predicate = contextFactory is null
            ? new ExpressionFlowPredicate<TInput>(options.Expression!, expressionEngine)
            : new ExpressionFlowPredicate<TInput>(options.Expression!, expressionEngine, contextFactory);
        _metadata = CreateMetadata(options, expressionEngine.Name);

        _passed = AddOutput<FlowMessage<TInput>>();
        _failed = AddOutput<FlowMessage<TInput>>();
    }

    /// <summary>Original input when the assertion passes; broadcast, carries the correlation id.</summary>
    public ISourceBlock<FlowMessage<TInput>> Passed => _passed;

    /// <summary>Original input when the assertion fails; broadcast, carries the correlation id.</summary>
    public ISourceBlock<FlowMessage<TInput>> Failed => _failed;

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        bool passed;
        try
        {
            passed = _predicate.IsMatch(input);
        }
        catch (Exception exception)
        {
            ReportProcessingError(
                message,
                AssertionErrorCodes.ExpressionFailed,
                $"flow.assert failed to evaluate input: {exception.Message}",
                exception);
            return Task.CompletedTask;
        }

        var result = CreateResult(input, passed);

        // Carry the correlation id forward onto the result and the branched input.
        Emit(message.With(result));
        if (passed && _metadata.EmitPassedInput)
        {
            _passed.Post(message);
        }

        if (!passed && _metadata.EmitFailedInput)
        {
            _failed.Post(message);
        }

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = AssertionDiagnosticNames.Evaluated,
            Level = FlowEventLevel.Information,
            Message = passed ? "flow.assert passed input." : "flow.assert failed input.",
            Attributes = AssertionNodeSupport.CreateAttributes(_metadata, passed)
        });

        return Task.CompletedTask;
    }

    private FlowAssertionResult CreateResult(TInput input, bool passed)
    {
        var status = passed
            ? FlowAssertionStatus.Passed
            : FlowAssertionStatus.Failed;
        return new FlowAssertionResult
        {
            Description = _metadata.EffectiveDescription,
            Expression = _metadata.Expression,
            ExpressionId = _metadata.ExpressionId,
            ExpressionName = _metadata.ExpressionName,
            InputType = _metadata.InputType,
            Status = status,
            Message = passed ? "Assertion passed." : _metadata.EffectiveFailureMessage,
            Value = input,
            EvaluatedAt = _clock.GetUtcNow(),
            Failure = passed
                ? null
                : new AssertionFailure
                {
                    Description = _metadata.EffectiveDescription,
                    Message = _metadata.EffectiveFailureMessage,
                    Expression = _metadata.Expression,
                    InputType = _metadata.InputType,
                    Value = input
                }
        };
    }

    private void ReportProcessingError(
        FlowMessage<TInput> source,
        int code,
        string message,
        Exception exception)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = AssertionNodeSupport.CreateErrorContext(_metadata),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = AssertionDiagnosticNames.ExpressionFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = AssertionNodeSupport.CreateAttributes(_metadata)
        });
    }

    private static AssertionResultMetadata CreateMetadata(AssertionOptions options, string engineName)
        => new()
        {
            EffectiveDescription = options.EffectiveDescription,
            Expression = options.Expression!,
            ExpressionId = options.ExpressionId,
            ExpressionName = options.ExpressionName,
            EngineName = engineName,
            InputType = options.InputType,
            EffectiveFailureMessage = options.EffectiveFailureMessage,
            EmitPassedInput = options.EmitPassedInput,
            EmitFailedInput = options.EmitFailedInput,
            BoundedCapacity = options.BoundedCapacity
        };

    private static AssertionOptions ValidateOptions(AssertionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Expression))
        {
            throw new ArgumentException("flow.assert requires configuration value 'expression'.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new ArgumentException("flow.assert option 'inputType' cannot be empty.", nameof(options));
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
