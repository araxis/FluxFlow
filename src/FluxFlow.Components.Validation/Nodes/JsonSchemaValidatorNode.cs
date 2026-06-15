using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Diagnostics;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Engine.Components;
using Json.Schema;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Validation.Nodes;

public sealed class JsonSchemaValidatorNode<TInput> : FlowNodeBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.List
    };

    private readonly JsonSchema _schema;
    private readonly ValidationComponentOptions.IValidationValueSelector _selector;
    private readonly JsonSchemaValidatorContext _nodeContext;
    private readonly JsonSchemaValidatorMetadata _metadata;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<JsonSchemaValidationResult<TInput>> _result;
    private readonly BufferBlock<TInput> _valid;
    private readonly BufferBlock<TInput> _invalid;

    internal JsonSchemaValidatorNode(
        JsonSchema schema,
        ValidationComponentOptions.IValidationValueSelector selector,
        JsonSchemaValidatorContext nodeContext,
        JsonSchemaValidatorMetadata metadata,
        TimeProvider clock,
        int boundedCapacity)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _nodeContext = nodeContext ?? throw new ArgumentNullException(nameof(nodeContext));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (boundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(boundedCapacity),
                "Validator bounded capacity must be greater than zero.");
        }

        var blockOptions = new DataflowBlockOptions { BoundedCapacity = boundedCapacity };
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = boundedCapacity,
            EnsureOrdered = true
        };
        _input = new ActionBlock<TInput>(ValidateAsync, inputOptions);
        _result = new BufferBlock<JsonSchemaValidationResult<TInput>>(blockOptions);
        _valid = new BufferBlock<TInput>(blockOptions);
        _invalid = new BufferBlock<TInput>(blockOptions);
        _input.Completion.ContinueWith(
            CompleteOutputs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(Task.WhenAll(_result.Completion, _valid.Completion, _invalid.Completion));
    }

    public ITargetBlock<TInput> Input => _input;

    public ISourceBlock<JsonSchemaValidationResult<TInput>> Result => _result;

    public ISourceBlock<TInput> Valid => _valid;

    public ISourceBlock<TInput> Invalid => _invalid;

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryEmitDiagnostic(
            ValidationDiagnosticNames.JsonSchemaLoaded,
            message: "Loaded JSON schema validator.",
            attributes: CreateAttributes());

        return Task.CompletedTask;
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
            ((IDataflowBlock)_result).Fault(exception);
            ((IDataflowBlock)_valid).Fault(exception);
            ((IDataflowBlock)_invalid).Fault(exception);
        }
    }

    private async Task ValidateAsync(TInput input)
    {
        object? selectedValue;
        try
        {
            selectedValue = _selector.Select(input, _nodeContext);
        }
        catch (Exception exception)
        {
            ReportProcessingError(
                ValidationErrorCodes.ValueSelectorFailed,
                $"json.schema-validator value selector failed: {exception.Message}",
                exception);
            return;
        }

        JsonElement value;
        try
        {
            value = ToJsonElement(selectedValue);
        }
        catch (Exception exception)
        {
            ReportProcessingError(
                ValidationErrorCodes.ValueConversionFailed,
                $"json.schema-validator could not convert selected value: {exception.Message}",
                exception);
            return;
        }

        EvaluationResults evaluation;
        try
        {
            evaluation = _schema.Evaluate(value, EvaluationOptions);
        }
        catch (Exception exception)
        {
            ReportProcessingError(
                ValidationErrorCodes.EvaluationFailed,
                $"json.schema-validator evaluation failed: {exception.Message}",
                exception);
            return;
        }

        var issues = ReadIssues(evaluation);
        var result = new JsonSchemaValidationResult<TInput>
        {
            Timestamp = _clock.GetUtcNow(),
            Input = input,
            Value = selectedValue,
            IsValid = evaluation.IsValid,
            SchemaId = _metadata.SchemaId,
            ValueSelector = _metadata.ValueSelector,
            Issues = issues
        };

        await _result.SendAsync(result).ConfigureAwait(false);
        await (evaluation.IsValid ? _valid : _invalid)
            .SendAsync(input)
            .ConfigureAwait(false);

        TryEmitDiagnostic(
            evaluation.IsValid
                ? ValidationDiagnosticNames.JsonSchemaValid
                : ValidationDiagnosticNames.JsonSchemaInvalid,
            message: evaluation.IsValid
                ? "json.schema-validator accepted input."
                : "json.schema-validator rejected input.",
            attributes: CreateAttributes(evaluation.IsValid, issues.Count));
    }

    private static JsonElement ToJsonElement(object? value)
    {
        if (value is null)
        {
            return JsonSerializer.SerializeToElement<object?>(null, SerializerOptions);
        }

        if (value is JsonElement element)
        {
            return element.Clone();
        }

        if (value is JsonDocument jsonDocument)
        {
            return jsonDocument.RootElement.Clone();
        }

        if (value is JsonNode node)
        {
            using var parsedNodeDocument = JsonDocument.Parse(node.ToJsonString());
            return parsedNodeDocument.RootElement.Clone();
        }

        if (value is byte[] bytes)
        {
            using var parsedBytesDocument = JsonDocument.Parse(bytes);
            return parsedBytesDocument.RootElement.Clone();
        }

        if (value is string text)
        {
            var trimmed = text.Trim();
            if (trimmed.Length > 0)
            {
                try
                {
                    using var parsedTextDocument = JsonDocument.Parse(trimmed);
                    return parsedTextDocument.RootElement.Clone();
                }
                catch (JsonException)
                {
                    return JsonSerializer.SerializeToElement(text, SerializerOptions);
                }
            }
        }

        return JsonSerializer.SerializeToElement(value, value.GetType(), SerializerOptions);
    }

    private static IReadOnlyList<JsonSchemaValidationIssue> ReadIssues(EvaluationResults evaluation)
    {
        var issues = new List<JsonSchemaValidationIssue>();
        foreach (var result in Walk(evaluation))
        {
            if (result.Errors is null)
            {
                continue;
            }

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
            {
                yield return descendant;
            }
        }
    }

    private void ReportProcessingError(
        int code,
        string message,
        Exception exception)
    {
        TryReportError(code, message, exception, CreateErrorContext());
        TryEmitDiagnostic(
            ValidationDiagnosticNames.JsonSchemaFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CreateAttributes());
    }

    private Dictionary<string, object?> CreateAttributes(
        bool? isValid = null,
        int? issueCount = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = _metadata.InputType,
            ["valueSelector"] = _metadata.ValueSelector
        };

        if (!string.IsNullOrWhiteSpace(_metadata.SchemaId))
        {
            attributes["schemaId"] = _metadata.SchemaId;
        }

        if (!string.IsNullOrWhiteSpace(_metadata.SchemaPath))
        {
            attributes["schemaPath"] = _metadata.SchemaPath;
        }

        if (isValid.HasValue)
        {
            attributes["isValid"] = isValid.Value;
        }

        if (issueCount.HasValue)
        {
            attributes["issueCount"] = issueCount.Value;
        }

        return attributes;
    }

    private string CreateErrorContext()
    {
        var values = new List<string>
        {
            $"inputType={_metadata.InputType}",
            $"valueSelector={_metadata.ValueSelector}"
        };

        if (!string.IsNullOrWhiteSpace(_metadata.SchemaId))
        {
            values.Add($"schemaId={_metadata.SchemaId}");
        }

        if (!string.IsNullOrWhiteSpace(_metadata.SchemaPath))
        {
            values.Add($"schemaPath={_metadata.SchemaPath}");
        }

        return string.Join("; ", values);
    }

    private void CompleteOutputs(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_result).Fault(exception);
            ((IDataflowBlock)_valid).Fault(exception);
            ((IDataflowBlock)_invalid).Fault(exception);
            return;
        }

        _result.Complete();
        _valid.Complete();
        _invalid.Complete();
    }
}
