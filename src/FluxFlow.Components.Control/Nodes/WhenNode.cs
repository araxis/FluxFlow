using FluxFlow.Components.Control.Diagnostics;
using FluxFlow.Components.Control.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Control.Nodes;

/// <summary>
/// A standalone routing node. Post a <c>FlowMessage&lt;TInput&gt;</c> to
/// <c>Input</c>; the node evaluates a compiled predicate over the payload and
/// fans the original message to one of two ports — <c>WhenTrue</c> when the
/// predicate matches, <c>WhenFalse</c> when it does not — each carrying the same
/// correlation id. <c>WhenTrue</c> is the node's primary <c>Output</c>;
/// <c>WhenFalse</c> is an additional broadcast port. Predicate failures surface on
/// <c>Errors</c> (with the original correlation id) and the node keeps processing
/// later messages. Diagnostics flow on <c>Events</c>. Works with nothing but
/// <c>new WhenNode&lt;T&gt;(options, predicate)</c> — no engine.
/// </summary>
public sealed class WhenNode<TInput> : FlowNode<TInput, TInput>
{
    public const string WhenRouted = ControlDiagnosticNames.WhenRouted;
    public const string WhenFailed = ControlDiagnosticNames.WhenFailed;

    private readonly IFlowPredicate<TInput> _predicate;
    private readonly string _engineName;
    private readonly ControlExpressionOptions _options;
    private readonly TimeProvider _clock;
    private readonly BroadcastBlock<FlowMessage<TInput>> _whenFalse;

    /// <summary>
    /// Builds a router from a compiled <see cref="IFlowPredicate{TInput}"/>.
    /// </summary>
    public WhenNode(
        ControlExpressionOptions options,
        IFlowPredicate<TInput> predicate,
        string? engineName = null,
        TimeProvider? clock = null)
        : this(ValidateOptions(options, requireExpression: false), predicate, engineName, clock)
    {
    }

    private WhenNode(
        ValidatedOptions options,
        IFlowPredicate<TInput> predicate,
        string? engineName,
        TimeProvider? clock)
        : base(options.FlowNodeOptions)
    {
        _options = options.ControlOptions;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _engineName = engineName ?? string.Empty;
        _clock = clock ?? TimeProvider.System;
        _whenFalse = AddOutput<FlowMessage<TInput>>();
    }

    /// <summary>
    /// Builds a router that compiles <paramref name="expression"/> once against
    /// <paramref name="expressionEngine"/> and evaluates the compiled form per message.
    /// </summary>
    public WhenNode(
        ControlExpressionOptions options,
        IFlowExpressionEngine expressionEngine,
        IFlowMapContextFactory<TInput>? contextFactory = null,
        TimeProvider? clock = null)
        : this(ValidateOptions(options, requireExpression: true), expressionEngine, contextFactory, clock)
    {
    }

    private WhenNode(
        ValidatedOptions options,
        IFlowExpressionEngine expressionEngine,
        IFlowMapContextFactory<TInput>? contextFactory,
        TimeProvider? clock)
        : this(
            options,
            BuildPredicate(options.Expression!, expressionEngine, contextFactory),
            EngineName(expressionEngine),
            clock)
    {
    }

    /// <summary>Input the predicate accepted; broadcast (the node's primary <c>Output</c>), carries the correlation id.</summary>
    public ISourceBlock<FlowMessage<TInput>> WhenTrue => Output;

    /// <summary>Input the predicate rejected; broadcast, carries the correlation id.</summary>
    public ISourceBlock<FlowMessage<TInput>> WhenFalse => _whenFalse;

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
            ReportFailure(message, exception);
            return Task.CompletedTask;
        }

        var route = passed ? ControlComponentPorts.WhenTrue : ControlComponentPorts.WhenFalse;
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = ControlDiagnosticNames.WhenRouted,
            Level = FlowEventLevel.Information,
            Message = $"flow.when routed input to {route}.",
            Attributes = ControlNodeSupport.CreateAttributes(_options, _engineName, passed, route)
        });

        // Fan the original envelope to the matching branch; correlation id flows forward.
        if (passed)
        {
            Emit(message.With(input));
        }
        else
        {
            _whenFalse.Post(message.With(input));
        }

        return Task.CompletedTask;
    }

    private void ReportFailure(FlowMessage<TInput> source, Exception exception)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = ControlErrorCodes.WhenExpressionFailed,
            Message = $"flow.when failed to evaluate input: {exception.Message}",
            Context = ControlNodeSupport.CreateErrorContext(_options, _engineName),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = ControlDiagnosticNames.WhenFailed,
            Level = FlowEventLevel.Error,
            Message = "flow.when failed to evaluate input.",
            Attributes = ControlNodeSupport.CreateAttributes(_options, _engineName)
        });
    }

    private static ValidatedOptions ValidateOptions(
        ControlExpressionOptions? options,
        bool requireExpression)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (requireExpression && string.IsNullOrWhiteSpace(options.Expression))
        {
            throw new ArgumentException(
                "flow.when requires configuration value 'expression'.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new ArgumentException(
                "flow.when option 'inputType' cannot be empty.", nameof(options));
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.when option 'boundedCapacity' must be greater than zero.");
        }

        return new ValidatedOptions(options);
    }

    private static IFlowPredicate<TInput> BuildPredicate(
        string expression,
        IFlowExpressionEngine expressionEngine,
        IFlowMapContextFactory<TInput>? contextFactory)
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);

        // Compile the predicate expression once here (build time); the node only
        // evaluates the compiled form per message.
        return contextFactory is null
            ? new ExpressionFlowPredicate<TInput>(expression, expressionEngine)
            : new ExpressionFlowPredicate<TInput>(expression, expressionEngine, contextFactory);
    }

    private static string EngineName(IFlowExpressionEngine expressionEngine)
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);
        return expressionEngine.Name;
    }

    private sealed class ValidatedOptions(ControlExpressionOptions controlOptions)
    {
        public ControlExpressionOptions ControlOptions { get; } = controlOptions;

        public string? Expression { get; } = controlOptions.Expression;

        public FlowNodeOptions FlowNodeOptions { get; } = new()
        {
            InputCapacity = controlOptions.BoundedCapacity
        };
    }
}
