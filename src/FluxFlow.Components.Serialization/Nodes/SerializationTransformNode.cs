using FluxFlow.Components.Serialization.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Serialization.Nodes;

internal sealed class SerializationTransformNode<TInput, TOutput> : FlowNodeBase
{
    private readonly string _nodeType;
    private readonly SerializationNodeOptions _options;
    private readonly Func<TInput, SerializationNodeOptions, TOutput> _convert;
    private readonly int _failureCode;
    private readonly string _successDiagnosticName;
    private readonly string _failureDiagnosticName;
    private readonly Func<TInput, IReadOnlyDictionary<string, object?>> _inputAttributes;
    private readonly Func<TOutput, IReadOnlyDictionary<string, object?>> _outputAttributes;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<TOutput> _output;

    public SerializationTransformNode(
        string nodeType,
        SerializationNodeOptions options,
        Func<TInput, SerializationNodeOptions, TOutput> convert,
        int failureCode,
        string successDiagnosticName,
        string failureDiagnosticName,
        Func<TInput, IReadOnlyDictionary<string, object?>> inputAttributes,
        Func<TOutput, IReadOnlyDictionary<string, object?>> outputAttributes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        _nodeType = nodeType;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _convert = convert ?? throw new ArgumentNullException(nameof(convert));
        _failureCode = failureCode;
        _successDiagnosticName = successDiagnosticName
            ?? throw new ArgumentNullException(nameof(successDiagnosticName));
        _failureDiagnosticName = failureDiagnosticName
            ?? throw new ArgumentNullException(nameof(failureDiagnosticName));
        _inputAttributes = inputAttributes ?? throw new ArgumentNullException(nameof(inputAttributes));
        _outputAttributes = outputAttributes ?? throw new ArgumentNullException(nameof(outputAttributes));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"{nodeType} bounded capacity must be greater than zero.");
        }

        var blockOptions = new DataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity
        };
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _input = new ActionBlock<TInput>(ConvertAsync, inputOptions);
        _output = new BufferBlock<TOutput>(blockOptions);
        _input.Completion.ContinueWith(
            CompleteOutput,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(_output.Completion);
    }

    public ITargetBlock<TInput> Input => _input;

    public ISourceBlock<TOutput> Output => _output;

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

    private async Task ConvertAsync(TInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        TOutput result;
        try
        {
            result = _convert(input, _options);
        }
        catch (SerializationNodeException exception)
        {
            ReportConversionError(
                exception.Code,
                exception.Message,
                input,
                exception.InnerException);
            return;
        }
        catch (Exception exception)
        {
            ReportConversionError(
                _failureCode,
                $"{_nodeType} failed: {exception.Message}",
                input,
                exception);
            return;
        }

        await _output.SendAsync(result).ConfigureAwait(false);
        TryEmitDiagnostic(
            _successDiagnosticName,
            message: $"{_nodeType} converted input.",
            attributes: _outputAttributes(result));
    }

    private void ReportConversionError(
        int code,
        string message,
        TInput input,
        Exception? exception)
    {
        var attributes = _inputAttributes(input);
        TryReportError(code, message, exception, CreateErrorContext(attributes));
        TryEmitDiagnostic(
            _failureDiagnosticName,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            attributes);
    }

    private string CreateErrorContext(IReadOnlyDictionary<string, object?> attributes)
    {
        var values = new List<string>
        {
            $"nodeType={_nodeType}"
        };
        foreach (var attribute in attributes)
        {
            if (attribute.Value is not null)
            {
                values.Add($"{attribute.Key}={attribute.Value}");
            }
        }

        return string.Join("; ", values);
    }

    private void CompleteOutput(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            return;
        }

        _output.Complete();
    }
}
