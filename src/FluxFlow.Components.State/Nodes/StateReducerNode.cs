using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Diagnostics;
using FluxFlow.Components.State.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.State.Nodes;

/// <summary>
/// Maintains ordered keyed typed state and emits operation outcomes through
/// one normal result output.
/// </summary>
public class StateReducerNode<T> : IFlowNode
{
    private const int MaxTrackedRejectedKeys = 1024;

    private readonly StateReducerOptions<T> _options;
    private readonly IFlowCompiledExpression<T> _reducer;
    private readonly IFlowCompiledExpression<string?>? _keySelector;
    private readonly string _engineName;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, StoredState> _states = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rejectedKeys = new(StringComparer.Ordinal);
    private readonly TransformBlock<
        FlowMessage<StateReducerInput<T>>,
        FlowMessage<StateReducerResult<T>>> _processor;
    private readonly BroadcastBlock<
        FlowMessage<StateReducerResult<T>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _rejectedKeyTrackingCapReached;
    private int _disposed;

    public StateReducerNode(
        StateReducerOptions<T> options,
        IFlowExpressionEngine expressionEngine,
        TimeProvider? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(expressionEngine);

        _engineName = expressionEngine.Name;
        _clock = clock ?? TimeProvider.System;
        _reducer = expressionEngine.Compile<T>(_options.Reducer);
        _keySelector = string.IsNullOrWhiteSpace(_options.KeyExpression)
            ? null
            : expressionEngine.Compile<string?>(_options.KeyExpression);
        _processor = new TransformBlock<
            FlowMessage<StateReducerInput<T>>,
            FlowMessage<StateReducerResult<T>>>(
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

    public ITargetBlock<FlowMessage<StateReducerInput<T>>> Input => _processor;

    public ISourceBlock<FlowMessage<StateReducerResult<T>>> Output
        => _output;

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

    private FlowMessage<StateReducerResult<T>> Process(
        FlowMessage<StateReducerInput<T>> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message.WithError<StateReducerResult<T>>(message.Error!);

        var input = message.Value;
        var timestamp = _clock.GetUtcNow();
        if (input is null)
        {
            return Failure(
                message,
                timestamp,
                new StateOperationException(
                    StateErrorCodeNames.InvalidMessage,
                    "state.reducer requires a command input."));
        }

        try
        {
            var key = ResolveKey(input);
            var result = input.Operation switch
            {
                StateReducerOperation.Reduce => Reduce(key, input, message, timestamp),
                StateReducerOperation.Reset => Reset(key, input, message, timestamp),
                StateReducerOperation.Clear => Clear(key, input, timestamp),
                _ => throw new StateOperationException(
                    StateErrorCodeNames.InvalidMessage,
                    $"state.reducer operation '{input.Operation}' is not supported.")
            };
            var kind = ResultKind(input.Operation);
            PublishEvent(
                message,
                timestamp,
                DiagnosticName(input.Operation),
                FlowEventLevel.Information,
                DiagnosticMessage(input.Operation),
                kind,
                result.Key,
                result.Version,
                isError: false);
            return message.With(result);
        }
        catch (StateOperationException exception)
        {
            return Failure(message, timestamp, exception);
        }
        catch (Exception exception)
        {
            return Failure(
                message,
                timestamp,
                new StateOperationException(
                    StateErrorCodeNames.ReducerFailed,
                    $"state.reducer failed: {exception.Message}",
                    exception));
        }
    }

    private StateReducerResult<T> Reduce(
        string key,
        StateReducerInput<T> input,
        FlowMessage<StateReducerInput<T>> message,
        DateTimeOffset timestamp)
    {
        if (!_states.TryGetValue(key, out var current))
        {
            EnsureCanTrackNewKey(key, message, timestamp);
            current = new StoredState(InitialState(input), 0);
        }

        T? newState;
        try
        {
            newState = _reducer.Evaluate(CreateContext(key, input, current.State, current.Version));
        }
        catch (Exception exception)
        {
            throw new StateOperationException(
                StateErrorCodeNames.ReducerFailed,
                $"state.reducer failed to evaluate reducer: {exception.Message}",
                exception);
        }

        var next = new StoredState(newState, current.Version + 1);
        _states[key] = next;
        return CreateResult(key, input, current.State, next, timestamp);
    }

    private StateReducerResult<T> Reset(
        string key,
        StateReducerInput<T> input,
        FlowMessage<StateReducerInput<T>> message,
        DateTimeOffset timestamp)
    {
        _states.TryGetValue(key, out var current);
        if (current is null)
            EnsureCanTrackNewKey(key, message, timestamp);

        var next = new StoredState(InitialState(input), (current?.Version ?? 0) + 1);
        _states[key] = next;
        return CreateResult(
            key,
            input,
            current is null ? default : current.State,
            next,
            timestamp);
    }

    private StateReducerResult<T> Clear(
        string key,
        StateReducerInput<T> input,
        DateTimeOffset timestamp)
    {
        _states.TryGetValue(key, out var current);
        _states.Remove(key);
        return new StateReducerResult<T>
        {
            Key = key,
            PreviousState = current is null ? default : current.State,
            Input = input.Input,
            NewState = default,
            Operation = StateReducerOperation.Clear,
            Version = current is null ? 0 : current.Version + 1,
            UpdatedAt = timestamp
        };
    }

    private string ResolveKey(StateReducerInput<T> input)
    {
        string? key;
        if (_keySelector is null)
        {
            key = input.Key;
        }
        else
        {
            try
            {
                key = _keySelector.Evaluate(CreateContext(
                    input.Key,
                    input,
                    InitialState(input),
                    version: 0));
            }
            catch (Exception exception)
            {
                throw new StateOperationException(
                    StateErrorCodeNames.KeyEvaluationFailed,
                    $"state.reducer failed to evaluate key: {exception.Message}",
                    exception);
            }
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new StateOperationException(
                StateErrorCodeNames.InvalidKey,
                "state.reducer key cannot be empty.");
        }

        return key.Trim();
    }

    private void EnsureCanTrackNewKey(
        string key,
        FlowMessage<StateReducerInput<T>> message,
        DateTimeOffset timestamp)
    {
        if (_states.Count < _options.MaxKeys)
            return;

        if (_rejectedKeys.Count >= MaxTrackedRejectedKeys)
        {
            if (!_rejectedKeyTrackingCapReached)
            {
                _rejectedKeyTrackingCapReached = true;
                PublishEvent(
                    message,
                    timestamp,
                    StateDiagnosticNames.KeyLimitReached,
                    FlowEventLevel.Warning,
                    "state.reducer key limit reached; further rejections will not be itemized.",
                    StateResultKinds.OperationFailed,
                    key,
                    version: null,
                    isError: true);
            }
        }
        else if (_rejectedKeys.Add(key))
        {
            PublishEvent(
                message,
                timestamp,
                StateDiagnosticNames.KeyLimitReached,
                FlowEventLevel.Warning,
                "state.reducer key limit reached.",
                StateResultKinds.OperationFailed,
                key,
                version: null,
                isError: true);
        }

        throw new StateOperationException(
            StateErrorCodeNames.KeyLimitReached,
            $"state.reducer maxKeys limit reached; key '{key}' was not tracked.");
    }

    private FlowMessage<StateReducerResult<T>> Failure(
        FlowMessage<StateReducerInput<T>> message,
        DateTimeOffset timestamp,
        StateOperationException exception)
    {
        var error = new DataFlowError(
            exception.Code,
            exception.Message,
            category: "State",
            isTransient: false,
            details: ErrorDetails(message.Value, exception));
        PublishEvent(
            message,
            timestamp,
            StateDiagnosticNames.ReducerFailed,
            FlowEventLevel.Warning,
            error.Message,
            StateResultKinds.OperationFailed,
            message.Value?.Key ?? string.Empty,
            version: null,
            isError: true);
        return message.WithError<StateReducerResult<T>>(error);
    }

    private FlowMapContext CreateContext(
        string key,
        StateReducerInput<T> input,
        T? previousState,
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
            ["initialState"] = InitialState(input),
            ["version"] = version,
            ["operation"] = input.Operation.ToString()
        };
        foreach (var (name, value) in input.Variables)
        {
            if (!string.IsNullOrWhiteSpace(name))
                variables[name] = value;
        }

        return new FlowMapContext { Variables = variables };
    }

    private T? InitialState(StateReducerInput<T> input)
    {
        if (input.HasInitialState)
            return input.InitialState;
        return _options.HasInitialState ? _options.InitialState : default;
    }

    private static StateReducerResult<T> CreateResult(
        string key,
        StateReducerInput<T> input,
        T? previousState,
        StoredState next,
        DateTimeOffset timestamp)
        => new()
        {
            Key = key,
            PreviousState = previousState,
            Input = input.Input,
            NewState = next.State,
            Operation = input.Operation,
            Version = next.Version,
            UpdatedAt = timestamp
        };

    private JsonElement ErrorDetails(
        StateReducerInput<T>? input,
        StateOperationException exception)
    {
        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["engine"] = _engineName,
            ["key"] = input?.Key ?? string.Empty,
            ["operation"] = input?.Operation.ToString() ?? "Unknown"
        };
        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
            details["expressionId"] = _options.ExpressionId;
        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
            details["expressionName"] = _options.ExpressionName;
        if (exception.InnerException is not null)
        {
            details["exceptionType"] = exception.InnerException.GetType().FullName ??
                exception.InnerException.GetType().Name;
        }

        return JsonSerializer.SerializeToElement(details);
    }

    private void PublishEvent(
        FlowMessage<StateReducerInput<T>> message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text,
        string resultKind,
        string key,
        long? version,
        bool isError)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["engine"] = _engineName,
            ["isError"] = isError,
            ["key"] = key,
            ["keyCount"] = _states.Count,
            ["operation"] = message.Value?.Operation.ToString() ?? "Unknown",
            ["resultKind"] = resultKind
        };
        if (version.HasValue)
            attributes["version"] = version.Value;
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
            try
            {
                ((IDataflowBlock)_output).Fault(exception);
            }
            catch
            {
                // The output may already be terminal.
            }
            _events.Complete();
            _completion.TrySetException(exception);
        }
    }

    private static string ResultKind(StateReducerOperation operation)
        => operation switch
        {
            StateReducerOperation.Reset => StateResultKinds.Reset,
            StateReducerOperation.Clear => StateResultKinds.Cleared,
            _ => StateResultKinds.Updated
        };

    private static string DiagnosticName(StateReducerOperation operation)
        => operation switch
        {
            StateReducerOperation.Reset => StateDiagnosticNames.ReducerReset,
            StateReducerOperation.Clear => StateDiagnosticNames.ReducerCleared,
            _ => StateDiagnosticNames.ReducerUpdated
        };

    private static string DiagnosticMessage(StateReducerOperation operation)
        => operation switch
        {
            StateReducerOperation.Reset => "state.reducer reset state.",
            StateReducerOperation.Clear => "state.reducer cleared state.",
            _ => "state.reducer updated state."
        };

    private sealed record StoredState(T? State, long Version);

    private sealed class StateOperationException(
        string code,
        string message,
        Exception? innerException = null)
        : Exception(message, innerException)
    {
        public string Code { get; } = code;
    }
}

/// <summary>
/// JSON-oriented state reducer used by configuration composition.
/// </summary>
public sealed class JsonStateReducerNode : StateReducerNode<JsonElement>
{
    public JsonStateReducerNode(
        StateReducerOptions<JsonElement> options,
        IFlowExpressionEngine expressionEngine,
        TimeProvider? clock = null)
        : base(options, expressionEngine, clock)
    {
    }
}
