using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Diagnostics;
using FluxFlow.Components.State.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Components.State.Nodes;

/// <summary>
/// A standalone keyed state-reducer node. Post a
/// <c>FlowMessage&lt;StateReducerInput&gt;</c> to <c>Input</c>; the node keeps
/// per-key state, applies the configured reducer expression, and broadcasts a
/// <c>FlowMessage&lt;StateReducerResult&gt;</c> on <c>Output</c> carrying the same
/// correlation id. Reducer/key failures surface on <c>Errors</c> (with the input's
/// correlation id) and later messages keep flowing; per-operation notes and
/// key-limit warnings flow on <c>Events</c>. State updates are serial, so each key
/// observes deterministic, ordered changes. Works with nothing but
/// <c>new StateReducerNode(options, expressionEngine)</c> — no engine. The reducer
/// (and optional key) expression is compiled once at construction via
/// <see cref="IFlowExpressionEngine.Compile{T}"/>, so parsing happens here rather
/// than per message.
/// </summary>
public sealed class StateReducerNode : FlowNode<StateReducerInput, StateReducerResult>
{
    private const int MaxTrackedRejectedKeys = 1024;

    private readonly StateReducerOptions _options;
    private readonly IFlowReducer _reducer;
    private readonly string _engineName;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, StoredState> _states = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rejectedKeys = new(StringComparer.Ordinal);
    private bool _rejectedKeyTrackingCapReached;

    public StateReducerNode(
        StateReducerOptions options,
        IFlowExpressionEngine expressionEngine,
        TimeProvider? clock = null)
        : this(options, BuildReducer(options, expressionEngine), ResolveEngineName(expressionEngine), clock)
    {
    }

    internal StateReducerNode(
        StateReducerOptions options,
        IFlowReducer reducer,
        string engineName,
        TimeProvider? clock)
        : base(new FlowNodeOptions
        {
            InputCapacity = ValidateOptions(options).BoundedCapacity
        })
    {
        _options = options;
        _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
        _engineName = engineName ?? throw new ArgumentNullException(nameof(engineName));
        _clock = clock ?? TimeProvider.System;
    }

    protected override Task ProcessAsync(FlowMessage<StateReducerInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        try
        {
            var key = ResolveKey(input);
            var result = input.Operation switch
            {
                StateReducerOperation.Reduce => Reduce(key, input),
                StateReducerOperation.Reset => Reset(key, input),
                StateReducerOperation.Clear => Clear(key, input),
                _ => throw new InvalidOperationException(
                    $"state.reducer operation '{input.Operation}' is not supported.")
            };

            // Carry the correlation id forward onto the result.
            Emit(message.With(result));
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = ResolveDiagnosticName(input.Operation),
                Level = FlowEventLevel.Information,
                Message = ResolveDiagnosticMessage(input.Operation),
                Attributes = CreateResultAttributes(result, input.Operation)
            });
        }
        catch (StateReducerException exception)
        {
            ReportReducerError(
                exception.Code,
                exception.Message,
                message,
                exception.InnerException);
        }
        catch (Exception exception)
        {
            ReportReducerError(
                StateErrorCodes.ReducerFailed,
                $"state.reducer failed: {exception.Message}",
                message,
                exception);
        }

        return Task.CompletedTask;
    }

    private StateReducerResult Reduce(
        string key,
        StateReducerInput input)
    {
        if (!_states.TryGetValue(key, out var current))
        {
            if (!CanTrackNewKey(key))
            {
                throw new StateReducerException(
                    StateErrorCodes.KeyLimitReached,
                    $"state.reducer maxKeys limit reached; key '{key}' was not tracked.");
            }

            current = new StoredState(ResolveInitialState(input), 0);
        }

        var context = CreateContext(key, input, current.State, current.Version);
        object? newState;
        try
        {
            newState = _reducer.Reduce(context);
        }
        catch (Exception exception)
        {
            throw new StateReducerException(
                StateErrorCodes.ReducerFailed,
                $"state.reducer failed to evaluate reducer: {exception.Message}",
                exception);
        }

        var next = new StoredState(newState, current.Version + 1);
        _states[key] = next;
        return CreateResult(key, input, current.State, next);
    }

    private StateReducerResult Reset(
        string key,
        StateReducerInput input)
    {
        _states.TryGetValue(key, out var current);
        if (current is null && !CanTrackNewKey(key))
        {
            throw new StateReducerException(
                StateErrorCodes.KeyLimitReached,
                $"state.reducer maxKeys limit reached; key '{key}' was not reset.");
        }

        var next = new StoredState(ResolveInitialState(input), (current?.Version ?? 0) + 1);
        _states[key] = next;
        return CreateResult(key, input, current?.State, next);
    }

    private StateReducerResult Clear(
        string key,
        StateReducerInput input)
    {
        _states.TryGetValue(key, out var current);
        _states.Remove(key);

        return new StateReducerResult
        {
            Key = key,
            PreviousState = current?.State,
            Input = input.Input,
            NewState = null,
            Version = current is null ? 0 : current.Version + 1,
            UpdatedAt = _clock.GetUtcNow()
        };
    }

    private string ResolveKey(StateReducerInput input)
    {
        string? key;
        if (string.IsNullOrWhiteSpace(_options.KeyExpression))
        {
            key = input.Key;
        }
        else
        {
            try
            {
                key = _reducer.ResolveKey(
                    CreateContext(input.Key, input, ResolveInitialState(input), 0));
            }
            catch (Exception exception)
            {
                throw new StateReducerException(
                    StateErrorCodes.KeyEvaluationFailed,
                    $"state.reducer failed to evaluate key: {exception.Message}",
                    exception);
            }
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new StateReducerException(
                StateErrorCodes.InvalidKey,
                "state.reducer key cannot be empty.");
        }

        return key.Trim();
    }

    private bool CanTrackNewKey(string key)
    {
        if (_states.Count < _options.MaxKeys)
        {
            return true;
        }

        if (_rejectedKeys.Count >= MaxTrackedRejectedKeys)
        {
            if (!_rejectedKeyTrackingCapReached)
            {
                _rejectedKeyTrackingCapReached = true;
                EmitEvent(new FlowEvent
                {
                    Timestamp = _clock.GetUtcNow(),
                    Name = StateDiagnosticNames.KeyLimitReached,
                    Level = FlowEventLevel.Warning,
                    Message = "state.reducer key limit reached; further rejections will not be itemized.",
                    Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["maxKeys"] = _options.MaxKeys,
                        ["maxTrackedRejectedKeys"] = MaxTrackedRejectedKeys
                    }
                });
            }

            return false;
        }

        if (_rejectedKeys.Add(key))
        {
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                Name = StateDiagnosticNames.KeyLimitReached,
                Level = FlowEventLevel.Warning,
                Message = "state.reducer key limit reached.",
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["key"] = key,
                    ["maxKeys"] = _options.MaxKeys
                }
            });
        }

        return false;
    }

    private object? ResolveInitialState(StateReducerInput input)
        => input.InitialState ?? _options.InitialState;

    private FlowMapContext CreateContext(
        string key,
        StateReducerInput input,
        object? previousState,
        long version)
    {
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["key"] = key,
            ["request"] = input,
            ["input"] = input.Input,
            ["value"] = input.Input,
            ["state"] = previousState,
            ["previousState"] = previousState,
            ["initialState"] = ResolveInitialState(input),
            ["version"] = version,
            ["operation"] = input.Operation.ToString()
        };

        if (input.Variables is not null)
        {
            foreach (var (name, value) in input.Variables)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    variables[name] = value;
                }
            }
        }

        return new FlowMapContext { Variables = variables };
    }

    private StateReducerResult CreateResult(
        string key,
        StateReducerInput input,
        object? previousState,
        StoredState next)
        => new()
        {
            Key = key,
            PreviousState = previousState,
            Input = input.Input,
            NewState = next.State,
            Version = next.Version,
            UpdatedAt = _clock.GetUtcNow()
        };

    private void ReportReducerError(
        int code,
        string message,
        FlowMessage<StateReducerInput> source,
        Exception? exception)
    {
        var input = source.Payload;
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = CreateInputContext(input),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = StateDiagnosticNames.ReducerFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = CreateInputAttributes(input)
        });
    }

    private Dictionary<string, object?> CreateResultAttributes(
        StateReducerResult result,
        StateReducerOperation operation)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["key"] = result.Key,
            ["version"] = result.Version,
            ["operation"] = operation.ToString(),
            ["engine"] = _engineName,
            ["keyCount"] = _states.Count
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

    private Dictionary<string, object?> CreateInputAttributes(StateReducerInput input)
        => new(StringComparer.Ordinal)
        {
            ["key"] = input.Key,
            ["operation"] = input.Operation.ToString(),
            ["engine"] = _engineName
        };

    private static string CreateInputContext(StateReducerInput input)
        => $"key={input.Key}; operation={input.Operation}";

    private static string ResolveDiagnosticName(StateReducerOperation operation)
        => operation switch
        {
            StateReducerOperation.Reset => StateDiagnosticNames.ReducerReset,
            StateReducerOperation.Clear => StateDiagnosticNames.ReducerCleared,
            _ => StateDiagnosticNames.ReducerUpdated
        };

    private static string ResolveDiagnosticMessage(StateReducerOperation operation)
        => operation switch
        {
            StateReducerOperation.Reset => "state.reducer reset state.",
            StateReducerOperation.Clear => "state.reducer cleared state.",
            _ => "state.reducer updated state."
        };

    private static StateReducerOptions ValidateOptions(StateReducerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Reducer))
        {
            throw new InvalidOperationException(
                "state.reducer option 'reducer' is required.");
        }

        if (options.KeyExpression is not null &&
            string.IsNullOrWhiteSpace(options.KeyExpression))
        {
            throw new InvalidOperationException(
                "state.reducer option 'keyExpression' cannot be empty when set.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "state.reducer option 'boundedCapacity' must be greater than zero.");
        }

        if (options.MaxKeys < 0)
        {
            throw new InvalidOperationException(
                "state.reducer option 'maxKeys' must be zero or greater.");
        }

        return options;
    }

    // Compile the reducer (and optional key) expression once at construction so
    // parsing happens here rather than per message.
    private static CompiledFlowReducer BuildReducer(
        StateReducerOptions options,
        IFlowExpressionEngine expressionEngine)
    {
        ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(expressionEngine);

        var reducerExpr = expressionEngine.Compile<object?>(options.Reducer);
        var keyExpr = string.IsNullOrWhiteSpace(options.KeyExpression)
            ? null
            : expressionEngine.Compile<string?>(options.KeyExpression!);
        return new CompiledFlowReducer(reducerExpr, keyExpr);
    }

    private static string ResolveEngineName(IFlowExpressionEngine expressionEngine)
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);
        return expressionEngine.Name;
    }

    private sealed record StoredState(object? State, long Version);

    private sealed class StateReducerException(
        int code,
        string message,
        Exception? innerException = null)
        : Exception(message, innerException)
    {
        public int Code { get; } = code;
    }
}
