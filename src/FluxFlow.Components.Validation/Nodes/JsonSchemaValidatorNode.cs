using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Diagnostics;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Nodes;
using Json.Schema;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Validation.Nodes;

/// <summary>
/// A standalone JSON-schema validator node. Post a <c>FlowMessage&lt;TInput&gt;</c>
/// to <c>Input</c>; the node selects a value from the payload, evaluates it against
/// a pre-compiled <see cref="JsonSchema"/>, and broadcasts a
/// <c>FlowMessage&lt;JsonSchemaValidationResult&lt;TInput&gt;&gt;</c> on <c>Output</c>.
/// In addition it fans the original input out to one of two extra ports —
/// <c>Valid</c> when the schema accepts it, <c>Invalid</c> when it rejects it —
/// each carrying the same correlation id. Selector / conversion / evaluation
/// failures surface on <c>Errors</c> (with the original correlation id) and the
/// node keeps processing later messages. Works with nothing but
/// <c>new JsonSchemaValidatorNode&lt;T&gt;(schema)</c> — no engine.
/// </summary>
public sealed class JsonSchemaValidatorNode<TInput>
    : FlowNode<TInput, JsonSchemaValidationResult<TInput>>
{
    public const string SchemaLoaded = ValidationDiagnosticNames.JsonSchemaLoaded;
    public const string SchemaValid = ValidationDiagnosticNames.JsonSchemaValid;
    public const string SchemaInvalid = ValidationDiagnosticNames.JsonSchemaInvalid;
    public const string SchemaFailed = ValidationDiagnosticNames.JsonSchemaFailed;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.List
    };

    private readonly JsonSchema _schema;
    private readonly IJsonSchemaValueSelector<TInput> _selector;
    private readonly JsonSchemaValidatorContext _nodeContext;
    private readonly JsonSchemaValidatorMetadata _metadata;
    private readonly TimeProvider _clock;
    private readonly BroadcastBlock<FlowMessage<TInput>> _valid;
    private readonly BroadcastBlock<FlowMessage<TInput>> _invalid;

    public JsonSchemaValidatorNode(
        JsonSchema schema,
        IJsonSchemaValueSelector<TInput>? selector = null,
        string? valueSelector = null,
        string? schemaId = null,
        string? schemaPath = null,
        TimeProvider? clock = null,
        JsonSchemaValidatorOptions? options = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (options ?? JsonSchemaValidatorOptions.Default).BoundedCapacity
        })
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _selector = selector ?? DefaultValueSelector.Instance;
        _clock = clock ?? TimeProvider.System;
        var effectiveSelector = string.IsNullOrWhiteSpace(valueSelector)
            ? JsonSchemaValidatorOptions.DefaultValueSelector
            : valueSelector.Trim();
        _nodeContext = new JsonSchemaValidatorContext
        {
            InputType = typeof(TInput),
            ValueSelector = effectiveSelector
        };
        _metadata = new JsonSchemaValidatorMetadata
        {
            InputType = typeof(TInput).Name,
            ValueSelector = effectiveSelector,
            SchemaId = schemaId,
            SchemaPath = schemaPath
        };

        _valid = AddOutput<FlowMessage<TInput>>();
        _invalid = AddOutput<FlowMessage<TInput>>();

        // One-time "loaded" note, mirroring the old StartAsync diagnostic.
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = SchemaLoaded,
            Level = FlowEventLevel.Information,
            Message = "Loaded JSON schema validator.",
            Attributes = CreateAttributes()
        });
    }

    /// <summary>Original input when the schema accepts it; broadcast, carries the correlation id.</summary>
    public ISourceBlock<FlowMessage<TInput>> Valid => _valid;

    /// <summary>Original input when the schema rejects it; broadcast, carries the correlation id.</summary>
    public ISourceBlock<FlowMessage<TInput>> Invalid => _invalid;

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        object? selectedValue;
        try
        {
            selectedValue = _selector.Select(input, _nodeContext);
        }
        catch (Exception exception)
        {
            ReportProcessingError(
                message,
                ValidationErrorCodes.ValueSelectorFailed,
                $"json.schema-validator value selector failed: {exception.Message}",
                exception);
            return Task.CompletedTask;
        }

        JsonElement value;
        try
        {
            value = ToJsonElement(selectedValue);
        }
        catch (Exception exception)
        {
            ReportProcessingError(
                message,
                ValidationErrorCodes.ValueConversionFailed,
                $"json.schema-validator could not convert selected value: {exception.Message}",
                exception);
            return Task.CompletedTask;
        }

        EvaluationResults evaluation;
        try
        {
            evaluation = _schema.Evaluate(value, EvaluationOptions);
        }
        catch (Exception exception)
        {
            ReportProcessingError(
                message,
                ValidationErrorCodes.EvaluationFailed,
                $"json.schema-validator evaluation failed: {exception.Message}",
                exception);
            return Task.CompletedTask;
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

        // Carry the correlation id forward onto the result and the branched input.
        Emit(message.With(result));
        (evaluation.IsValid ? _valid : _invalid).Post(message);

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = evaluation.IsValid ? SchemaValid : SchemaInvalid,
            Level = FlowEventLevel.Information,
            Message = evaluation.IsValid
                ? "json.schema-validator accepted input."
                : "json.schema-validator rejected input.",
            Attributes = CreateAttributes(evaluation.IsValid, issues.Count)
        });

        return Task.CompletedTask;
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
        FlowMessage<TInput> source,
        int code,
        string message,
        Exception exception)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = CreateErrorContext(),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = SchemaFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = CreateAttributes()
        });
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

    private sealed class DefaultValueSelector : IJsonSchemaValueSelector<TInput>
    {
        public static DefaultValueSelector Instance { get; } = new();

        public object? Select(TInput input, JsonSchemaValidatorContext context) => input;
    }
}
