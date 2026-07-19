using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Diagnostics;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Json.Schema;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Validation.Nodes;

/// <summary>
/// Evaluates immutable workflow values against a precompiled JSON Schema.
/// Schema rejection and expected processing failures are emitted through one
/// normal <see cref="FlowResult{T}"/> output.
/// </summary>
public sealed class FlowValueJsonSchemaValidatorNode : IFlowNode
{
    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.List
    };

    private readonly JsonSchema _schema;
    private readonly IJsonSchemaFlowValueSelector _selector;
    private readonly JsonSchemaValidatorContext _nodeContext;
    private readonly string? _schemaId;
    private readonly string? _schemaPath;
    private readonly string _valueSelector;
    private readonly TimeProvider _clock;
    private readonly TransformBlock<
        FlowMessage<FlowValue>,
        FlowMessage<FlowResult<JsonSchemaFlowValueValidationResult>>> _processor;
    private readonly BroadcastBlock<
        FlowMessage<FlowResult<JsonSchemaFlowValueValidationResult>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public FlowValueJsonSchemaValidatorNode(
        JsonSchema schema,
        IJsonSchemaFlowValueSelector? selector = null,
        string? valueSelector = null,
        string? schemaId = null,
        string? schemaPath = null,
        TimeProvider? clock = null,
        JsonSchemaValidatorOptions? options = null)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        var validatedOptions = ValidateOptions(options ?? JsonSchemaValidatorOptions.Default);
        _selector = selector ?? DefaultSelector.Instance;
        _valueSelector = string.IsNullOrWhiteSpace(valueSelector)
            ? validatedOptions.EffectiveValueSelector
            : valueSelector.Trim();
        _schemaId = NormalizeOptional(schemaId ?? validatedOptions.SchemaId);
        _schemaPath = NormalizeOptional(schemaPath ?? validatedOptions.SchemaPath);
        _clock = clock ?? TimeProvider.System;
        _nodeContext = new JsonSchemaValidatorContext
        {
            InputType = typeof(FlowValue),
            ValueSelector = _valueSelector
        };
        _processor = new TransformBlock<
            FlowMessage<FlowValue>,
            FlowMessage<FlowResult<JsonSchemaFlowValueValidationResult>>>(
                Process,
                new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = validatedOptions.BoundedCapacity,
                    MaxDegreeOfParallelism = 1,
                    EnsureOrdered = true
                });
        _processor.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<FlowMessage<FlowValue>> Input => _processor;

    public ISourceBlock<FlowMessage<FlowResult<JsonSchemaFlowValueValidationResult>>> Output
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

    private FlowMessage<FlowResult<JsonSchemaFlowValueValidationResult>> Process(
        FlowMessage<FlowValue> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var timestamp = _clock.GetUtcNow();
        if (message.Payload is null)
        {
            return Failure(
                message,
                timestamp,
                ValidationResultKinds.MissingInput,
                ValidationErrorCodeNames.MissingInput,
                "json.schema-validator requires FlowValue input.");
        }

        FlowValue selectedValue;
        try
        {
            selectedValue = _selector.Select(message.Payload, _nodeContext)
                ?? throw new InvalidOperationException("The value selector returned no FlowValue.");
        }
        catch (Exception exception)
        {
            return Failure(
                message,
                timestamp,
                ValidationResultKinds.ValueSelectorFailed,
                ValidationErrorCodeNames.ValueSelectorFailed,
                $"json.schema-validator value selector failed: {exception.Message}",
                exception);
        }

        var value = FlowValueJsonSchemaConverter.Convert(selectedValue);
        EvaluationResults evaluation;
        try
        {
            evaluation = _schema.Evaluate(value, EvaluationOptions);
        }
        catch (Exception exception)
        {
            return Failure(
                message,
                timestamp,
                ValidationResultKinds.EvaluationFailed,
                ValidationErrorCodeNames.EvaluationFailed,
                $"json.schema-validator evaluation failed: {exception.Message}",
                exception);
        }

        var issues = ReadIssues(evaluation);
        var result = new JsonSchemaFlowValueValidationResult
        {
            Timestamp = timestamp,
            Input = message.Payload,
            Value = selectedValue,
            IsValid = evaluation.IsValid,
            SchemaId = _schemaId,
            ValueSelector = _valueSelector,
            Issues = issues
        };
        var resultKind = evaluation.IsValid
            ? ValidationResultKinds.Valid
            : ValidationResultKinds.Invalid;
        PublishEvent(
            message,
            timestamp,
            evaluation.IsValid
                ? ValidationDiagnosticNames.JsonSchemaValid
                : ValidationDiagnosticNames.JsonSchemaInvalid,
            FlowEventLevel.Information,
            evaluation.IsValid
                ? "json.schema-validator accepted input."
                : "json.schema-validator rejected input.",
            resultKind,
            issues.Count,
            isError: false);
        return message.With(FlowResult<JsonSchemaFlowValueValidationResult>.Success(
            resultKind,
            result,
            timestamp));
    }

    private FlowMessage<FlowResult<JsonSchemaFlowValueValidationResult>> Failure(
        FlowMessage<FlowValue> message,
        DateTimeOffset timestamp,
        string resultKind,
        string errorCode,
        string errorMessage,
        Exception? exception = null)
    {
        var error = new DataFlowError(
            errorCode,
            errorMessage,
            category: "Validation",
            isTransient: false,
            details: CreateErrorDetails(message.Payload, exception));
        PublishEvent(
            message,
            timestamp,
            ValidationDiagnosticNames.JsonSchemaFailed,
            FlowEventLevel.Warning,
            error.Message,
            resultKind,
            issueCount: 0,
            isError: true);
        return message.With(FlowResult<JsonSchemaFlowValueValidationResult>.Failure(
            resultKind,
            error,
            timestamp));
    }

    private void PublishEvent(
        FlowMessage<FlowValue> message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text,
        string resultKind,
        int issueCount,
        bool isError)
        => _events.Post(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = level,
            Message = text,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaId"] = _schemaId,
                ["schemaPath"] = _schemaPath,
                ["valueSelector"] = _valueSelector,
                ["resultKind"] = resultKind,
                ["issueCount"] = issueCount,
                ["isError"] = isError
            }
        });

    private FlowValue CreateErrorDetails(FlowValue? input, Exception? exception)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["inputKind"] = input is null ? FlowValue.Null : FlowValue.From(input.Kind.ToString()),
            ["schemaId"] = OptionalValue(_schemaId),
            ["schemaPath"] = OptionalValue(_schemaPath),
            ["valueSelector"] = FlowValue.From(_valueSelector)
        };
        if (exception is not null)
        {
            details["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name);
        }

        return FlowValue.FromObject(details);
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
            ((IDataflowBlock)_output).Fault(exception);
            _events.Complete();
            _completion.TrySetException(exception);
        }
    }

    private static IReadOnlyList<JsonSchemaValidationIssue> ReadIssues(
        EvaluationResults evaluation)
    {
        var issues = new List<JsonSchemaValidationIssue>();
        foreach (var result in Walk(evaluation))
        {
            if (result.Errors is null)
                continue;

            foreach (var error in result.Errors)
            {
                issues.Add(new JsonSchemaValidationIssue
                {
                    Keyword = string.IsNullOrWhiteSpace(error.Key) ? null : error.Key,
                    Message = error.Value,
                    EvaluationPath = result.EvaluationPath.ToString(),
                    InstanceLocation = result.InstanceLocation.ToString(),
                    SchemaLocation = result.SchemaLocation?.ToString()
                });
            }
        }

        return issues;
    }

    private static IEnumerable<EvaluationResults> Walk(EvaluationResults result)
    {
        yield return result;
        foreach (var child in result.Details ?? [])
        {
            foreach (var descendant in Walk(child))
                yield return descendant;
        }
    }

    private static JsonSchemaValidatorOptions ValidateOptions(
        JsonSchemaValidatorOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new ArgumentException(
                "json.schema-validator option 'inputType' cannot be empty.",
                nameof(options));
        }
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "json.schema-validator option 'boundedCapacity' must be greater than zero.");
        }

        return options;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FlowValue OptionalValue(string? value)
        => value is null ? FlowValue.Null : FlowValue.From(value);

    private sealed class DefaultSelector : IJsonSchemaFlowValueSelector
    {
        internal static DefaultSelector Instance { get; } = new();

        public FlowValue Select(FlowValue input, JsonSchemaValidatorContext context)
            => input;
    }
}
