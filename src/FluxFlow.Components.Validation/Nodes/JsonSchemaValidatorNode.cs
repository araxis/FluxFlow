using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Diagnostics;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Json.Schema;

namespace FluxFlow.Components.Validation.Nodes;

/// <summary>
/// Evaluates JSON values against a precompiled JSON Schema.
/// </summary>
public sealed class JsonSchemaValidatorNode : FlowNode<JsonElement, JsonSchemaValidationResult>
{
    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.List
    };

    private readonly JsonSchema _schema;
    private readonly IJsonSchemaValueSelector _selector;
    private readonly JsonSchemaValidatorContext _nodeContext;
    private readonly string? _schemaId;
    private readonly string? _schemaPath;
    private readonly string _valueSelector;
    private readonly TimeProvider _clock;
    public JsonSchemaValidatorNode(
        JsonSchema schema,
        IJsonSchemaValueSelector? selector = null,
        string? valueSelector = null,
        string? schemaId = null,
        string? schemaPath = null,
        TimeProvider? clock = null,
        JsonSchemaValidatorOptions? options = null)
        : base(CreateNodeOptions(options ?? JsonSchemaValidatorOptions.Default))
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
            InputType = typeof(JsonElement),
            ValueSelector = _valueSelector
        };
    }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage<JsonElement> message)
        => await EmitAsync(Process(message), Stopping).ConfigureAwait(false);

    private FlowMessage<JsonSchemaValidationResult> Process(FlowMessage<JsonElement> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message.WithError<JsonSchemaValidationResult>(message.Error!);

        var timestamp = _clock.GetUtcNow();
        JsonElement selectedValue;
        try
        {
            selectedValue = _selector.Select(message.Value, _nodeContext).Clone();
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

        EvaluationResults evaluation;
        try
        {
            evaluation = _schema.Evaluate(selectedValue, EvaluationOptions);
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
        var result = new JsonSchemaValidationResult
        {
            Timestamp = timestamp,
            Input = message.Value.Clone(),
            Value = selectedValue,
            IsValid = evaluation.IsValid,
            SchemaId = _schemaId,
            ValueSelector = _valueSelector,
            Issues = issues
        };
        var resultKind = evaluation.IsValid ? ValidationResultKinds.Valid : ValidationResultKinds.Invalid;
        PublishEvent(
            message,
            timestamp,
            evaluation.IsValid ? ValidationDiagnosticNames.JsonSchemaValid : ValidationDiagnosticNames.JsonSchemaInvalid,
            FlowEventLevel.Information,
            evaluation.IsValid
                ? "json.schema-validator accepted input."
                : "json.schema-validator rejected input.",
            resultKind,
            issues.Count,
            isError: false);
        return message.With(result);
    }

    private FlowMessage<JsonSchemaValidationResult> Failure(
        FlowMessage<JsonElement> message,
        DateTimeOffset timestamp,
        string resultKind,
        string errorCode,
        string errorMessage,
        Exception exception)
    {
        var error = new FlowError(
            errorCode,
            errorMessage,
            category: "Validation",
            isTransient: false,
            details: CreateErrorDetails(message.Value, exception));
        PublishEvent(
            message,
            timestamp,
            ValidationDiagnosticNames.JsonSchemaFailed,
            FlowEventLevel.Warning,
            error.Message,
            resultKind,
            issueCount: 0,
            isError: true);
        return message.WithError<JsonSchemaValidationResult>(error);
    }

    private void PublishEvent(
        FlowMessage<JsonElement> message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text,
        string resultKind,
        int issueCount,
        bool isError)
        => EmitEvent(new FlowEvent
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

    private JsonElement CreateErrorDetails(JsonElement input, Exception exception)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputKind"] = input.ValueKind.ToString(),
            ["schemaId"] = _schemaId,
            ["schemaPath"] = _schemaPath,
            ["valueSelector"] = _valueSelector,
            ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name
        });

    private static IReadOnlyList<JsonSchemaValidationIssue> ReadIssues(EvaluationResults evaluation)
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

    private static JsonSchemaValidatorOptions ValidateOptions(JsonSchemaValidatorOptions options)
    {
        if (options.BoundedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Capacity must be positive.");
        return options;
    }

    private static FlowNodeOptions CreateNodeOptions(JsonSchemaValidatorOptions options)
    {
        var validated = ValidateOptions(options);
        return new FlowNodeOptions
        {
            InputCapacity = validated.BoundedCapacity,
            OutputCapacity = validated.BoundedCapacity
        };
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class DefaultSelector : IJsonSchemaValueSelector
    {
        internal static DefaultSelector Instance { get; } = new();
        public JsonElement Select(JsonElement input, JsonSchemaValidatorContext context) => input;
    }
}
