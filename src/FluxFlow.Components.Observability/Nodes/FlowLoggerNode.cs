using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Observability.Nodes;

public sealed class FlowLoggerNode<TInput> : FlowNodeBase
{
    private readonly FlowLoggerOptions _options;
    private readonly FlowLogLevel _level;
    private readonly IReadOnlyDictionary<string, ObservabilityComponentOptions.IValueSelector> _attributeSelectors;
    private readonly ObservabilityNodeContext _nodeContext;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<FlowLogEntry> _entries;
    private readonly CancellationToken _processingCancellationToken;
    private long _sequence;

    internal FlowLoggerNode(
        FlowLoggerOptions options,
        IReadOnlyDictionary<string, ObservabilityComponentOptions.IValueSelector> attributeSelectors,
        ObservabilityNodeContext nodeContext)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _level = ObservabilityOptionsReader.ResolveLogLevel(options.Level);
        _attributeSelectors = attributeSelectors ?? throw new ArgumentNullException(nameof(attributeSelectors));
        _nodeContext = nodeContext ?? throw new ArgumentNullException(nameof(nodeContext));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Logger bounded capacity must be greater than zero.");
        }

        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _processingCancellationToken = inputOptions.CancellationToken;
        _input = new ActionBlock<TInput>(LogAsync, inputOptions);
        _entries = new BufferBlock<FlowLogEntry>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        _input.Completion.ContinueWith(
            CompleteOutput,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(_entries.Completion);
    }

    public ITargetBlock<TInput> Input => _input;

    public ISourceBlock<FlowLogEntry> Entries => _entries;

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
            ((IDataflowBlock)_entries).Fault(exception);
        }
    }

    private async Task LogAsync(TInput input)
    {
        _processingCancellationToken.ThrowIfCancellationRequested();
        var sequence = Interlocked.Increment(ref _sequence);
        var timestamp = DateTimeOffset.UtcNow;
        var attributes = CreateSelectedAttributes(input);
        var messageValues = CreateMessageValues(input, sequence, attributes);
        var entry = new FlowLogEntry
        {
            Timestamp = timestamp,
            Level = _level,
            Category = _options.EffectiveCategory,
            Message = ObservabilityNodeSupport.RenderMessage(
                _options.EffectiveMessageTemplate,
                messageValues),
            InputType = _options.InputType,
            Sequence = sequence,
            Attributes = attributes
        };

        await _entries.SendAsync(entry, _processingCancellationToken).ConfigureAwait(false);
        TryEmitDiagnostic(
            ObservabilityDiagnosticNames.LoggerEmitted,
            message: "flow.logger emitted entry.",
            attributes: ObservabilityNodeSupport.CreateAttributes(
                ObservabilityComponentTypes.Logger.Value,
                _options.InputType,
                _options.EffectiveCategory,
                sequence));
    }

    private Dictionary<string, object?> CreateSelectedAttributes(TInput input)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, selector) in _attributeSelectors)
        {
            try
            {
                attributes[name] = selector.Select(input, _nodeContext);
            }
            catch (Exception exception)
            {
                TryReportError(
                    ObservabilityErrorCodes.LoggerAttributeSelectorFailed,
                    $"flow.logger failed to select attribute '{name}': {exception.Message}",
                    exception,
                    CreateErrorContext(name));
                TryEmitDiagnostic(
                    ObservabilityDiagnosticNames.LoggerFailed,
                    FlowDiagnosticLevel.Error,
                    $"flow.logger failed to select attribute '{name}'.",
                    exception,
                    ObservabilityNodeSupport.CreateAttributes(
                        ObservabilityComponentTypes.Logger.Value,
                        _options.InputType,
                        _options.EffectiveCategory));
            }
        }

        return attributes;
    }

    private Dictionary<string, object?> CreateMessageValues(
        TInput input,
        long sequence,
        IReadOnlyDictionary<string, object?> attributes)
    {
        var values = new Dictionary<string, object?>(attributes, StringComparer.Ordinal)
        {
            ["category"] = _options.EffectiveCategory,
            ["inputType"] = _options.InputType,
            ["level"] = _level.ToString(),
            ["sequence"] = sequence,
            ["input"] = input
        };

        return values;
    }

    private string CreateErrorContext(string selector)
    {
        var values = new List<string>
        {
            $"inputType={_options.InputType}",
            $"category={_options.EffectiveCategory}",
            $"selector={selector}"
        };

        return string.Join("; ", values);
    }

    private void CompleteOutput(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_entries).Fault(exception);
            return;
        }

        _entries.Complete();
    }
}
