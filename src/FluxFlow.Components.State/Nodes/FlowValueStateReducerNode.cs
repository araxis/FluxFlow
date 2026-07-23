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
/// Maintains ordered keyed FlowValue state and emits operation outcomes through
/// one normal result output.
/// </summary>
public sealed class FlowValueStateReducerNode : IFlowNode
{
    private const int MaxTrackedRejectedKeys = 1024;

    private readonly FlowValueStateReducerOptions _options;
    private readonly IFlowCompiledExpression<FlowValue> _reducer;
    private readonly IFlowCompiledExpression<string?>? _keySelector;
    private readonly string _engineName;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, StoredState> _states = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rejectedKeys = new(StringComparer.Ordinal);
    private readonly TransformBlock<
        FlowMessage<FlowValueStateReducerInput>,
        FlowMessage<FlowResult<FlowValueStateReducerResult>>> _processor;
    private readonly BroadcastBlock<
        FlowMessage<FlowResult<FlowValueStateReducerResult>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _rejectedKeyTrackingCapReached;
    private int _disposed;

    public FlowValueStateReducerNode(
        FlowValueStateReducerOptions options,
        IFlowExpressionEngine expressionEngine,
        TimeProvider? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(expressionEngine);

        _engineName = expressionEngine.Name;
        _clock = clock ?? TimeProvider.System;
        _reducer = expressionEngine.Compile<FlowValue>(_options.Reducer);
        _keySelector = string.IsNullOrWhiteSpace(_options.KeyExpression)
            ? null
            : expressionEngine.Compile<string?>(_options.KeyExpression);
        _processor = new TransformBlock<
            FlowMessage<FlowValueStateReducerInput>,
            FlowMessage<FlowResult<FlowValueStateReducerResult>>>(
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

    public ITargetBlock<FlowMessage<FlowValueStateReducerInput>> Input => _processor;

    public ISourceBlock<FlowMessage<FlowResult<FlowValueStateReducerResult>>> Output
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

    private FlowMessage<FlowResult<FlowValueStateReducerResult>> Process(
        FlowMessage<FlowValueStateReducerInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;
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
            return message.With(FlowResult<FlowValueStateReducerResult>.Success(
                kind,
                result,
                timestamp));
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

    private FlowValueStateReducerResult Reduce(
        string key,
        FlowValueStateReducerInput input,
        FlowMessage<FlowValueStateReducerInput> message,
        DateTimeOffset timestamp)
    {
        if (!_states.TryGetValue(key, out var current))
        {
            EnsureCanTrackNewKey(key, message, timestamp);
            current = new StoredState(InitialState(input), 0);
        }

        FlowValue newState;
        try
        {
            newState = _reducer.Evaluate(CreateContext(key, input, current.State, current.Version))
                ?? throw new InvalidOperationException(
                    "The state reducer expression returned no FlowValue.");
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

    private FlowValueStateReducerResult Reset(
        string key,
        FlowValueStateReducerInput input,
        FlowMessage<FlowValueStateReducerInput> message,
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
            current?.State ?? FlowValue.Null,
            next,
            timestamp);
    }

    private FlowValueStateReducerResult Clear(
        string key,
        FlowValueStateReducerInput input,
        DateTimeOffset timestamp)
    {
        _states.TryGetValue(key, out var current);
        _states.Remove(key);
        return new FlowValueStateReducerResult
        {
            Key = key,
            PreviousState = current?.State ?? FlowValue.Null,
            Input = input.Input,
            NewState = FlowValue.Null,
            Operation = StateReducerOperation.Clear,
            Version = current is null ? 0 : current.Version + 1,
            UpdatedAt = timestamp
        };
    }

    private string ResolveKey(FlowValueStateReducerInput input)
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
        FlowMessage<FlowValueStateReducerInput> message,
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

    private FlowMessage<FlowResult<FlowValueStateReducerResult>> Failure(
        FlowMessage<FlowValueStateReducerInput> message,
        DateTimeOffset timestamp,
        StateOperationException exception)
    {
        var error = new DataFlowError(
            exception.Code,
            exception.Message,
            category: "State",
            isTransient: false,
            details: ErrorDetails(message.Payload, exception));
        PublishEvent(
            message,
            timestamp,
            StateDiagnosticNames.ReducerFailed,
            FlowEventLevel.Warning,
            error.Message,
            StateResultKinds.OperationFailed,
            message.Payload?.Key ?? string.Empty,
            version: null,
            isError: true);
        return message.With(FlowResult<FlowValueStateReducerResult>.Failure(
            StateResultKinds.OperationFailed,
            error,
            timestamp));
    }

    private FlowMapContext CreateContext(
        string key,
        FlowValueStateReducerInput input,
        FlowValue previousState,
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

    private FlowValue InitialState(FlowValueStateReducerInput input)
        => input.InitialState ?? _options.InitialState;

    private static FlowValueStateReducerResult CreateResult(
        string key,
        FlowValueStateReducerInput input,
        FlowValue previousState,
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

    private FlowValue ErrorDetails(
        FlowValueStateReducerInput? input,
        StateOperationException exception)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["engine"] = FlowValue.From(_engineName),
            ["input"] = input?.Input ?? FlowValue.Null,
            ["key"] = FlowValue.From(input?.Key ?? string.Empty),
            ["operation"] = FlowValue.From(input?.Operation.ToString() ?? "Unknown")
        };
        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
            details["expressionId"] = FlowValue.From(_options.ExpressionId);
        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
            details["expressionName"] = FlowValue.From(_options.ExpressionName);
        if (exception.InnerException is not null)
        {
            details["exceptionType"] = FlowValue.From(
                exception.InnerException.GetType().FullName ??
                exception.InnerException.GetType().Name);
        }

        return FlowValue.FromObject(details);
    }

    private void PublishEvent(
        FlowMessage<FlowValueStateReducerInput> message,
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
            ["operation"] = message.Payload?.Operation.ToString() ?? "Unknown",
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

    private sealed record StoredState(FlowValue State, long Version);

    private sealed class StateOperationException(
        string code,
        string message,
        Exception? innerException = null)
        : Exception(message, innerException)
    {
        public string Code { get; } = code;
    }
}
