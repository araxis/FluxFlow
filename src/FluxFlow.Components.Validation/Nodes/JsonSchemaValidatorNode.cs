using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Diagnostics;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Engine.Components;
using Json.Schema;
using System.Text;
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

    private readonly JsonSchemaValidatorOptions _options;
    private readonly ValidationComponentOptions.IValidationValueSelector _selector;
    private readonly JsonSchemaValidatorContext _nodeContext;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<JsonSchemaValidationResult<TInput>> _result;
    private readonly BufferBlock<TInput> _valid;
    private readonly BufferBlock<TInput> _invalid;
    private readonly CancellationToken _processingCancellationToken;
    private JsonSchema? _schema;

    internal JsonSchemaValidatorNode(
        JsonSchemaValidatorOptions options,
        ValidationComponentOptions.IValidationValueSelector selector,
        JsonSchemaValidatorContext nodeContext)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _nodeContext = nodeContext ?? throw new ArgumentNullException(nameof(nodeContext));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Validator bounded capacity must be greater than zero.");
        }

        var blockOptions = new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity };
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true
        };
        _processingCancellationToken = inputOptions.CancellationToken;
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
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _schema = LoadSchema();
            TryEmitDiagnostic(
                ValidationDiagnosticNames.JsonSchemaLoaded,
                message: "Loaded JSON schema validator.",
                attributes: CreateAttributes());

            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var code = IsMissingSchema() ? ValidationErrorCodes.SchemaMissing : ValidationErrorCodes.SchemaLoadFailed;
            TryReportError(
                code,
                $"json.schema-validator failed to load schema: {exception.Message}",
                exception,
                CreateErrorContext());
            TryEmitDiagnostic(
                ValidationDiagnosticNames.JsonSchemaFailed,
                FlowDiagnosticLevel.Error,
                "json.schema-validator failed to load schema.",
                exception,
                CreateAttributes());

            throw new InvalidOperationException("json.schema-validator failed to load schema.", exception);
        }
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
        if (_schema is null)
        {
            TryReportError(
                ValidationErrorCodes.ValidatorNotStarted,
                "json.schema-validator has not started.",
                context: CreateErrorContext());
            return;
        }

        object? selectedValue;
        try
        {
            _processingCancellationToken.ThrowIfCancellationRequested();
            selectedValue = _selector.Select(input, _nodeContext);
        }
        catch (OperationCanceledException) when (_processingCancellationToken.IsCancellationRequested)
        {
            throw;
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
            Timestamp = DateTimeOffset.UtcNow,
            Input = input,
            Value = selectedValue,
            IsValid = evaluation.IsValid,
            SchemaId = _options.SchemaId,
            ValueSelector = _options.EffectiveValueSelector,
            Issues = issues
        };

        await _result.SendAsync(result, _processingCancellationToken).ConfigureAwait(false);
        await (evaluation.IsValid ? _valid : _invalid)
            .SendAsync(input, _processingCancellationToken)
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

    private JsonSchema LoadSchema()
    {
        var schemaText = ReadSchemaText();
        var baseUri = ResolveSchemaBaseUri();
        return baseUri is null
            ? JsonSchema.FromText(schemaText)
            : JsonSchema.FromText(schemaText, null, baseUri);
    }

    private Uri? ResolveSchemaBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(_options.SchemaId) &&
            Uri.TryCreate(_options.SchemaId, UriKind.Absolute, out var schemaIdUri))
        {
            return schemaIdUri;
        }

        return string.IsNullOrWhiteSpace(_options.SchemaPath)
            ? null
            : new Uri(Path.GetFullPath(_options.SchemaPath));
    }

    private string ReadSchemaText()
    {
        if (_options.Schema.HasValue)
        {
            var schema = _options.Schema.Value;
            return schema.ValueKind == JsonValueKind.String
                ? schema.GetString() ?? throw new InvalidOperationException("Schema text was empty.")
                : schema.GetRawText();
        }

        if (!string.IsNullOrWhiteSpace(_options.SchemaPath))
        {
            return File.ReadAllText(_options.SchemaPath);
        }

        throw new InvalidOperationException("Schema or schemaPath is required.");
    }

    private bool IsMissingSchema()
        => !_options.Schema.HasValue && string.IsNullOrWhiteSpace(_options.SchemaPath);

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
            ["inputType"] = _options.InputType,
            ["valueSelector"] = _options.EffectiveValueSelector
        };

        if (!string.IsNullOrWhiteSpace(_options.SchemaId))
        {
            attributes["schemaId"] = _options.SchemaId;
        }

        if (!string.IsNullOrWhiteSpace(_options.SchemaPath))
        {
            attributes["schemaPath"] = _options.SchemaPath;
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
            $"inputType={_options.InputType}",
            $"valueSelector={_options.EffectiveValueSelector}"
        };

        if (!string.IsNullOrWhiteSpace(_options.SchemaId))
        {
            values.Add($"schemaId={_options.SchemaId}");
        }

        if (!string.IsNullOrWhiteSpace(_options.SchemaPath))
        {
            values.Add($"schemaPath={_options.SchemaPath}");
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
