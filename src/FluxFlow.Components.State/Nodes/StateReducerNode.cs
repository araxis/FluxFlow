using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Diagnostics;
using FluxFlow.Components.State.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.State.Nodes;

public sealed class StateReducerNode : FlowNodeBase
{
    private readonly StateReducerOptions _options;
    private readonly IFlowExpressionEngine _expressionEngine;
    private readonly ActionBlock<StateReducerInput> _input;
    private readonly BufferBlock<StateReducerResult> _output;
    private readonly Dictionary<string, StoredState> _states = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rejectedKeys = new(StringComparer.Ordinal);
    private readonly CancellationToken _processingCancellationToken;

    private StateReducerNode(
        StateReducerOptions options,
        IFlowExpressionEngine expressionEngine)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _expressionEngine = expressionEngine ?? throw new ArgumentNullException(nameof(expressionEngine));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "state.reducer bounded capacity must be greater than zero.");
        }

        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _processingCancellationToken = inputOptions.CancellationToken;
        _input = new ActionBlock<StateReducerInput>(ReduceAsync, inputOptions);
        _output = new BufferBlock<StateReducerResult>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        CompleteWhen(_input.Completion);
    }

    public ITargetBlock<StateReducerInput> Input => _input;

    public ISourceBlock<StateReducerResult> Output => _output;

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        StateComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = StateOptionsReader.ReadReducerOptions(context.Definition);
        var expressionEngine = componentOptions.ResolveExpressionEngine(options.Engine);
        var node = new StateReducerNode(options, expressionEngine);

        return context.CreateNode(node)
            .Input(StateComponentPorts.Input, node.Input)
            .Output(StateComponentPorts.Output, node.Output)
            .Output(StateComponentPorts.Errors, node.Errors)
            .Build();
    }

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
            ((IDataflowBlock)_output).Fault(exception);
        }
    }

    protected override void OnNodeCompleted()
    {
        _output.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        ((IDataflowBlock)_output).Fault(exception);
        base.OnNodeFaulted(exception);
    }

    private async Task ReduceAsync(StateReducerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            _processingCancellationToken.ThrowIfCancellationRequested();
            var key = ResolveKey(input);
            var result = input.Operation switch
            {
                StateReducerOperation.Reduce => Reduce(key, input),
                StateReducerOperation.Reset => Reset(key, input),
                StateReducerOperation.Clear => Clear(key, input),
                _ => throw new InvalidOperationException(
                    $"state.reducer operation '{input.Operation}' is not supported.")
            };

            await _output.SendAsync(result, _processingCancellationToken).ConfigureAwait(false);
            TryEmitDiagnostic(
                ResolveDiagnosticName(input.Operation),
                message: ResolveDiagnosticMessage(input.Operation),
                attributes: CreateResultAttributes(result, input.Operation));
        }
        catch (OperationCanceledException) when (_processingCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StateReducerException exception)
        {
            ReportReducerError(
                exception.Code,
                exception.Message,
                input,
                exception.InnerException);
        }
        catch (Exception exception)
        {
            ReportReducerError(
                StateErrorCodes.ReducerFailed,
                $"state.reducer failed: {exception.Message}",
                input,
                exception);
        }
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
            newState = _expressionEngine.Evaluate(
                _options.Reducer,
                context,
                typeof(object));
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
            UpdatedAt = DateTimeOffset.UtcNow
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
                var value = _expressionEngine.Evaluate(
                    _options.KeyExpression!,
                    CreateContext(input.Key, input, ResolveInitialState(input), 0),
                    typeof(string));
                key = value switch
                {
                    string text => text,
                    null => null,
                    _ => throw new InvalidOperationException(
                        $"Key expression returned '{value.GetType().Name}', expected String.")
                };
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

        if (_rejectedKeys.Add(key))
        {
            TryEmitDiagnostic(
                StateDiagnosticNames.KeyLimitReached,
                FlowDiagnosticLevel.Warning,
                "state.reducer key limit reached.",
                attributes: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["key"] = key,
                    ["maxKeys"] = _options.MaxKeys
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

    private static StateReducerResult CreateResult(
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
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private void ReportReducerError(
        int code,
        string message,
        StateReducerInput input,
        Exception? exception)
    {
        TryReportError(code, message, exception, CreateInputContext(input));
        TryEmitDiagnostic(
            StateDiagnosticNames.ReducerFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CreateInputAttributes(input));
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
            ["engine"] = _expressionEngine.Name,
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
            ["engine"] = _expressionEngine.Name
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
