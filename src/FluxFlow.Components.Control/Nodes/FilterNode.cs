using FluxFlow.Components.Control.Diagnostics;
using FluxFlow.Components.Control.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Control.Nodes;

/// <summary>
/// A standalone filter node. Post a <c>FlowMessage&lt;TInput&gt;</c> to
/// <c>Input</c>; the node evaluates a compiled predicate over the payload and
/// re-broadcasts the message on <c>Output</c> when it matches, dropping it when
/// it does not — the surviving message keeps its correlation id. Predicate
/// failures surface on <c>Errors</c> (with the original correlation id) and the
/// node keeps processing later messages. Diagnostics flow on <c>Events</c>.
/// Works with nothing but <c>new FilterNode&lt;T&gt;(options, predicate)</c> — no engine.
/// </summary>
public sealed class FilterNode<TInput> : FlowNode<TInput, TInput>
{
    public const string FilterPassed = ControlDiagnosticNames.FilterPassed;
    public const string FilterRejected = ControlDiagnosticNames.FilterRejected;
    public const string FilterFailed = ControlDiagnosticNames.FilterFailed;

    private readonly IFlowPredicate<TInput> _predicate;
    private readonly string _engineName;
    private readonly ControlExpressionOptions _options;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Builds a filter from a compiled <see cref="IFlowPredicate{TInput}"/>.
    /// </summary>
    public FilterNode(
        ControlExpressionOptions options,
        IFlowPredicate<TInput> predicate,
        string? engineName = null,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = RequireCapacity(options)
        })
    {
        _options = options;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _engineName = engineName ?? string.Empty;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Builds a filter that compiles <paramref name="expression"/> once against
    /// <paramref name="expressionEngine"/> and evaluates the compiled form per message.
    /// </summary>
    public FilterNode(
        ControlExpressionOptions options,
        IFlowExpressionEngine expressionEngine,
        IFlowMapContextFactory<TInput>? contextFactory = null,
        TimeProvider? clock = null)
        : this(
            options,
            BuildPredicate(options, expressionEngine, contextFactory),
            EngineName(expressionEngine),
            clock)
    {
    }

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

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = passed ? ControlDiagnosticNames.FilterPassed : ControlDiagnosticNames.FilterRejected,
            Level = FlowEventLevel.Information,
            Message = passed ? "flow.filter passed input." : "flow.filter rejected input.",
            Attributes = ControlNodeSupport.CreateAttributes(_options, _engineName, passed)
        });

        if (passed)
        {
            // Re-broadcast the same envelope so the correlation id flows forward.
            Emit(message.With(input));
        }

        return Task.CompletedTask;
    }

    private void ReportFailure(FlowMessage<TInput> source, Exception exception)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = ControlErrorCodes.FilterExpressionFailed,
            Message = $"flow.filter failed to evaluate input: {exception.Message}",
            Context = ControlNodeSupport.CreateErrorContext(_options, _engineName),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = ControlDiagnosticNames.FilterFailed,
            Level = FlowEventLevel.Error,
            Message = "flow.filter failed to evaluate input.",
            Attributes = ControlNodeSupport.CreateAttributes(_options, _engineName)
        });
    }

    private static int RequireCapacity(ControlExpressionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Filter bounded capacity must be greater than zero.");
        }

        return options.BoundedCapacity;
    }

    private static IFlowPredicate<TInput> BuildPredicate(
        ControlExpressionOptions options,
        IFlowExpressionEngine expressionEngine,
        IFlowMapContextFactory<TInput>? contextFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(expressionEngine);
        if (string.IsNullOrWhiteSpace(options.Expression))
        {
            throw new ArgumentException(
                "flow.filter requires a non-empty expression.", nameof(options));
        }

        // Compile the predicate expression once here (build time); the node only
        // evaluates the compiled form per message.
        return contextFactory is null
            ? new ExpressionFlowPredicate<TInput>(options.Expression, expressionEngine)
            : new ExpressionFlowPredicate<TInput>(options.Expression, expressionEngine, contextFactory);
    }

    private static string EngineName(IFlowExpressionEngine expressionEngine)
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);
        return expressionEngine.Name;
    }
}
