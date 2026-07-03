using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>
/// A standalone switch node. Post a <c>FlowMessage&lt;TInput&gt;</c> to <c>Input</c>;
/// the node extracts a route key from the payload (via the injected selector), decides
/// whether it matches a configured route, and fans the original message out: matched
/// inputs on <c>Output</c> (the primary <c>Matched</c> branch), unmatched inputs on
/// <c>Default</c>, an optional neutral <c>Routed</c> envelope on its own port, and the
/// original input to any configured per-route output port. Every emitted message carries
/// the source correlation id. Key-selector failures surface on <c>Errors</c> and the node
/// keeps processing. Works with nothing but <c>new FlowSwitchNode&lt;T&gt;(options, selector)</c>
/// — no engine.
/// </summary>
public sealed class FlowSwitchNode<TInput> : FlowNode<TInput, TInput>
{
    private readonly SwitchRoutingOptions _options;
    private readonly Func<TInput, string?> _routeKeySelector;
    private readonly string? _engineName;
    private readonly TimeProvider _clock;
    private readonly HashSet<string> _routes;
    private readonly Dictionary<string, string> _routeOutputPorts;
    private readonly Dictionary<string, BroadcastBlock<FlowMessage<TInput>>> _routeOutputBlocks;
    private readonly IReadOnlyDictionary<string, ISourceBlock<FlowMessage<TInput>>> _routeOutputs;
    private readonly BroadcastBlock<FlowMessage<TInput>> _default;
    private readonly BroadcastBlock<FlowMessage<TInput>>? _routed;

    public FlowSwitchNode(
        SwitchRoutingOptions options,
        Func<TInput, string?> routeKeySelector,
        string? engineName = null,
        TimeProvider? clock = null)
        : this(ValidateOptions(options), routeKeySelector, engineName, clock)
    {
    }

    private FlowSwitchNode(
        ValidatedOptions options,
        Func<TInput, string?> routeKeySelector,
        string? engineName,
        TimeProvider? clock)
        : base(options.FlowNodeOptions)
    {
        _options = options.SwitchOptions;
        _routeKeySelector = routeKeySelector ?? throw new ArgumentNullException(nameof(routeKeySelector));
        _engineName = engineName;
        _clock = clock ?? TimeProvider.System;

        _routes = CreateRouteSet(options.SwitchOptions);
        _routeOutputPorts = CreateRouteOutputPortMap(options.SwitchOptions);

        // The primary Output is the Matched branch; Default and (optionally) Routed plus
        // each configured route-output port are extra broadcast ports.
        _default = AddOutput<FlowMessage<TInput>>();
        if (options.SwitchOptions.EmitRouteEnvelope)
        {
            _routed = AddOutput<FlowMessage<TInput>>();
        }

        _routeOutputBlocks = _routeOutputPorts.Values
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                portName => portName,
                _ => AddOutput<FlowMessage<TInput>>(),
                StringComparer.Ordinal);
        _routeOutputs = _routeOutputBlocks.ToDictionary(
            routeOutput => routeOutput.Key,
            routeOutput => (ISourceBlock<FlowMessage<TInput>>)routeOutput.Value,
            StringComparer.Ordinal);
    }

    /// <summary>Matched inputs, carrying the correlation id forward (primary branch).</summary>
    public ISourceBlock<FlowMessage<TInput>> Matched => Output;

    /// <summary>Unmatched inputs (default route), carrying the correlation id forward.</summary>
    public ISourceBlock<FlowMessage<TInput>> Default => _default;

    /// <summary>
    /// The original message re-broadcast when <see cref="SwitchRoutingOptions.EmitRouteEnvelope"/>
    /// is set; null otherwise. Carries the correlation id forward.
    /// </summary>
    public ISourceBlock<FlowMessage<TInput>>? Routed => _routed;

    /// <summary>Per-route output ports keyed by configured port name.</summary>
    public IReadOnlyDictionary<string, ISourceBlock<FlowMessage<TInput>>> RouteOutputs => _routeOutputs;

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        string? routeKey;
        try
        {
            routeKey = _routeKeySelector(input);
        }
        catch (Exception exception)
        {
            EmitError(new FlowError
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Code = RoutingErrorCodes.SwitchExpressionFailed,
                Message = $"flow.switch failed to evaluate input: {exception.Message}",
                Context = CreateErrorContext(),
                Exception = exception
            });
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = RoutingDiagnosticNames.SwitchFailed,
                Level = FlowEventLevel.Error,
                Message = "flow.switch failed to evaluate input.",
                Attributes = CreateAttributes()
            });
            return Task.CompletedTask;
        }

        var matched = IsMatch(routeKey);
        string? outputPort = null;

        if (matched && _options.EmitMatchedInput)
        {
            Emit(message);
        }

        if (matched
            && routeKey is not null
            && _routeOutputPorts.TryGetValue(routeKey, out var routeOutputPort)
            && _routeOutputBlocks.TryGetValue(routeOutputPort, out var routeOutput))
        {
            outputPort = routeOutputPort;
            routeOutput.Post(message);
        }

        if (_routed is not null)
        {
            _routed.Post(message);
        }

        if (!matched && _options.EmitDefaultInput)
        {
            _default.Post(message);
        }

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = RoutingDiagnosticNames.SwitchRouted,
            Level = FlowEventLevel.Information,
            Message = matched
                ? "flow.switch matched route."
                : "flow.switch used default route.",
            Attributes = CreateAttributes(routeKey, matched, outputPort)
        });

        return Task.CompletedTask;
    }

    private bool IsMatch(string? routeKey)
    {
        if (routeKey is null)
        {
            return false;
        }

        return _routes.Count == 0 || _routes.Contains(routeKey);
    }

    private static HashSet<string> CreateRouteSet(SwitchRoutingOptions options)
    {
        var comparer = options.CaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        return options.Routes
            .Select(route => route.Trim())
            .Where(route => route.Length > 0)
            .ToHashSet(comparer);
    }

    private static Dictionary<string, string> CreateRouteOutputPortMap(SwitchRoutingOptions options)
    {
        var comparer = options.CaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        return options.RouteOutputs
            .Select(routeOutput => new
            {
                Route = routeOutput.Key.Trim(),
                Port = routeOutput.Value.Trim()
            })
            .ToDictionary(routeOutput => routeOutput.Route, routeOutput => routeOutput.Port, comparer);
    }

    private Dictionary<string, object?> CreateAttributes(
        string? routeKey = null,
        bool? matched = null,
        string? outputPort = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = _options.InputType,
            ["engine"] = _engineName,
            ["routes"] = _options.Routes.Length,
            ["routeOutputs"] = _options.RouteOutputs.Count,
            ["caseSensitive"] = _options.CaseSensitive
        };

        if (!string.IsNullOrWhiteSpace(routeKey))
        {
            attributes["routeKey"] = routeKey;
        }

        if (matched.HasValue)
        {
            attributes["matched"] = matched.Value;
        }

        if (!string.IsNullOrWhiteSpace(outputPort))
        {
            attributes["outputPort"] = outputPort;
        }

        if (!string.IsNullOrWhiteSpace(_options.DefaultRoute))
        {
            attributes["defaultRoute"] = _options.DefaultRoute;
        }

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
            $"engine={_engineName}",
            $"routes={_options.Routes.Length}",
            $"routeOutputs={_options.RouteOutputs.Count}"
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

    private static ValidatedOptions ValidateOptions(SwitchRoutingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new ArgumentException(
                "flow.switch option 'inputType' cannot be empty.", nameof(options));
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.switch option 'boundedCapacity' must be greater than zero.");
        }

        return new ValidatedOptions(options);
    }

    private sealed class ValidatedOptions(SwitchRoutingOptions switchOptions)
    {
        public SwitchRoutingOptions SwitchOptions { get; } = switchOptions;

        public FlowNodeOptions FlowNodeOptions { get; } = new()
        {
            InputCapacity = switchOptions.BoundedCapacity
        };
    }
}
