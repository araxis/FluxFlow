using System.Text;
using System.Text.Json;

namespace FluxFlow.Data;

/// <summary>
/// Primitive canonical-value materializers. Domain request materializers remain
/// in the modules that own those request types.
/// </summary>
public static class FlowValueMaterializers
{
    public static IFlowValueMaterializer<JsonElement> JsonElement { get; } =
        new JsonElementMaterializer();

    public static IFlowValueMaterializer<string> String { get; } =
        new StringMaterializer();

    public static IFlowValueMaterializer<FlowContent> Content { get; } =
        new ContentMaterializer();

    public static IFlowValueMaterializer<T> Primitive<T>()
    {
        if (typeof(T) == typeof(JsonElement))
            return (IFlowValueMaterializer<T>)(object)JsonElement;
        if (typeof(T) == typeof(string))
            return (IFlowValueMaterializer<T>)(object)String;
        if (typeof(T) == typeof(FlowContent))
            return (IFlowValueMaterializer<T>)(object)Content;

        throw new InvalidOperationException(
            $"Type '{typeof(T)}' is not a canonical primitive. Its owning module must provide IFlowValueMaterializer<{typeof(T).Name}>.");
    }

    private sealed class JsonElementMaterializer : IFlowValueMaterializer<JsonElement>
    {
        public FlowValueShape InputShape { get; } =
            FlowValueShape.Any("Any JSON value.");

        public FlowValueMaterializationResult<JsonElement> Materialize(FlowValue value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return FlowValueMaterializationResult<JsonElement>.Success(value.ToJsonElement());
        }
    }

    private sealed class StringMaterializer : IFlowValueMaterializer<string>
    {
        public FlowValueShape InputShape { get; } =
            FlowValueShape.String("A JSON string.");

        public FlowValueMaterializationResult<string> Materialize(FlowValue value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var element = value.ToJsonElement();
            if (element.ValueKind == JsonValueKind.String)
            {
                return FlowValueMaterializationResult<string>.Success(
                    element.GetString() ?? string.Empty);
            }

            return FlowValueMaterializationResult<string>.Failure(
                InvalidPrimitive("flow.value.expected_string", "string", element.ValueKind));
        }
    }

    private sealed class ContentMaterializer : IFlowValueMaterializer<FlowContent>
    {
        private static readonly JsonSerializerOptions SerializerOptions =
            new(JsonSerializerDefaults.Web);

        public FlowValueShape InputShape { get; } = FlowValueShape.Any(
            "A string, a FlowContent object, or any JSON value encoded as UTF-8 content.");

        public FlowValueMaterializationResult<FlowContent> Materialize(FlowValue value)
        {
            ArgumentNullException.ThrowIfNull(value);
            try
            {
                var element = value.ToJsonElement();
                if (element.ValueKind == JsonValueKind.Object &&
                    element.TryGetProperty("bytes", out _))
                {
                    var content = element.Deserialize<FlowContent>(SerializerOptions)
                        ?? throw new JsonException("Flow content cannot be null.");
                    return FlowValueMaterializationResult<FlowContent>.Success(content);
                }

                if (element.ValueKind == JsonValueKind.String)
                {
                    return FlowValueMaterializationResult<FlowContent>.Success(
                        FlowContent.FromBytes(
                            Encoding.UTF8.GetBytes(element.GetString() ?? string.Empty),
                            "text/plain",
                            "utf-8"));
                }

                return FlowValueMaterializationResult<FlowContent>.Success(
                    FlowContent.FromBytes(
                        Encoding.UTF8.GetBytes(element.GetRawText()),
                        "application/json",
                        "utf-8"));
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException or NotSupportedException)
            {
                return FlowValueMaterializationResult<FlowContent>.Failure(
                    new FlowError(
                        "flow.value.invalid_content",
                        "The workflow value cannot be materialized as exact content.",
                        "Materialization",
                        details: JsonSerializer.SerializeToElement(new
                        {
                            target = nameof(FlowContent),
                            reason = exception.Message
                        })));
            }
        }
    }

    private static FlowError InvalidPrimitive(
        string code,
        string target,
        JsonValueKind actual)
        => new(
            code,
            $"The workflow value must be a {target}.",
            "Materialization",
            details: JsonSerializer.SerializeToElement(new
            {
                target,
                actual = actual.ToString()
            }));
}

/// <summary>
/// JSON mechanics shared by closed, module-owned request materializers.
/// It has no registry and no knowledge of component contracts.
/// </summary>
public abstract class JsonFlowValueMaterializer<T> : IFlowValueMaterializer<T>
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);
    private readonly string _errorCode;
    private readonly string _category;

    protected JsonFlowValueMaterializer(
        string errorCode,
        string category,
        FlowValueShape inputShape)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        _errorCode = errorCode;
        _category = category;
        InputShape = inputShape ?? throw new ArgumentNullException(nameof(inputShape));
    }

    public FlowValueShape InputShape { get; }

    public virtual FlowValueMaterializationResult<T> Materialize(FlowValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            var materialized = value.Deserialize<T>(SerializerOptions)
                ?? throw new JsonException($"{typeof(T).Name} cannot be null.");
            return FlowValueMaterializationResult<T>.Success(materialized);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            return FlowValueMaterializationResult<T>.Failure(
                new FlowError(
                    _errorCode,
                    $"The workflow value cannot be materialized as {typeof(T).Name}.",
                    _category,
                    details: JsonSerializer.SerializeToElement(new
                    {
                        target = typeof(T).Name,
                        reason = exception.Message
                    })));
        }
    }
}
