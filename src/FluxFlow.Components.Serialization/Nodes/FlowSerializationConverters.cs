using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Nodes;

internal static class FlowSerializationConverters
{
    public static FlowContentCodecCatalog CreateJsonCatalog(SerializationNodeOptions options)
        => new([], new ConfiguredJsonCodec(options));

    public static FlowContentCodecCatalog CreateTextCatalog(SerializationNodeOptions options)
        => new([], new ConfiguredTextCodec(options.DefaultEncoding));

    public static FlowValue ParseJson(
        FlowContent content,
        SerializationNodeOptions options,
        FlowContentCodecCatalog catalog)
    {
        EnsureContentInputSize(content, options);
        try
        {
            return content.ReadAsFlowValue(catalog);
        }
        catch (Exception exception) when (
            exception is JsonException or DecoderFallbackException)
        {
            throw Failure(
                SerializationErrorCodeNames.JsonParseFailed,
                $"JSON content could not be parsed: {exception.Message}",
                exception);
        }
    }

    public static FlowContent StringifyJson(
        FlowValue value,
        SerializationNodeOptions options)
    {
        byte[] utf8;
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Indented = options.WriteIndented
            }))
            {
                WriteJsonValue(writer, value);
            }

            utf8 = stream.ToArray();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            throw Failure(
                SerializationErrorCodeNames.JsonStringifyFailed,
                $"FlowValue could not be serialized as JSON: {exception.Message}",
                exception);
        }

        var encoding = ResolveDefaultEncoding(options);
        var bytes = encoding.CodePage == Encoding.UTF8.CodePage
            ? utf8
            : encoding.GetBytes(Encoding.UTF8.GetString(utf8));
        EnsureOutputSize(bytes.Length, options);
        return FlowContent.FromBytes(bytes, "application/json", encoding.WebName);
    }

    public static FlowContent EncodeText(
        FlowValue value,
        SerializationNodeOptions options)
    {
        if (value.Kind != FlowValueKind.String)
        {
            throw Failure(
                SerializationErrorCodeNames.TextEncodeFailed,
                $"text.encode requires a string FlowValue, not {value.Kind}.");
        }

        var encoding = ResolveDefaultEncoding(options);
        var bytes = encoding.GetBytes(value.GetString());
        EnsureInputSize(bytes.Length, options);
        EnsureOutputSize(bytes.Length, options);
        return FlowContent.FromBytes(bytes, "text/plain", encoding.WebName);
    }

    public static FlowValue DecodeText(
        FlowContent content,
        SerializationNodeOptions options,
        FlowContentCodecCatalog catalog)
    {
        EnsureContentInputSize(content, options);
        var value = content.ReadAsFlowValue(catalog);
        if (value.Kind == FlowValueKind.String)
            return value;

        if (value.Kind == FlowValueKind.Binary)
        {
            var bytes = value.GetBinary();
            EnsureInputSize(bytes.Length, options);
            return FlowValue.From(ResolveContentEncoding(content, options)
                .GetString(bytes.AsSpan()));
        }

        throw Failure(
            SerializationErrorCodeNames.TextDecodeFailed,
            $"text.decode requires byte-backed content or a string/binary FlowValue, not {value.Kind}.");
    }

    public static FlowValue EncodeBase64(
        FlowContent content,
        SerializationNodeOptions options)
    {
        byte[] bytes;
        if (content.HasOriginalRepresentation)
        {
            bytes = content.OriginalBytes.ToArray();
        }
        else
        {
            var value = content.ReadAsFlowValue(FlowContentCodecCatalog.CreateDefault());
            switch (value.Kind)
            {
                case FlowValueKind.Binary:
                    bytes = value.GetBinary().ToArray();
                    break;
                case FlowValueKind.String:
                    bytes = ResolveContentEncoding(content, options)
                        .GetBytes(value.GetString());
                    break;
                default:
                    throw Failure(
                        SerializationErrorCodeNames.Base64EncodeFailed,
                        $"base64.encode requires byte-backed content or a string/binary FlowValue, not {value.Kind}.");
            }
        }

        EnsureInputSize(bytes.Length, options);
        var text = Convert.ToBase64String(bytes);
        EnsureOutputSize(Encoding.UTF8.GetByteCount(text), options);
        return FlowValue.From(text);
    }

    public static FlowContent DecodeBase64(
        FlowValue value,
        SerializationNodeOptions options)
    {
        if (value.Kind != FlowValueKind.String)
        {
            throw Failure(
                SerializationErrorCodeNames.Base64DecodeFailed,
                $"base64.decode requires a string FlowValue, not {value.Kind}.");
        }

        var text = value.GetString();
        EnsureInputSize(Encoding.UTF8.GetByteCount(text), options);

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text);
        }
        catch (FormatException exception)
        {
            throw Failure(
                SerializationErrorCodeNames.Base64DecodeFailed,
                $"Base64 text could not be decoded: {exception.Message}",
                exception);
        }

        EnsureOutputSize(bytes.Length, options);
        return FlowContent.FromBytes(bytes, "application/octet-stream");
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, FlowValue value)
    {
        switch (value.Kind)
        {
            case FlowValueKind.Null:
                writer.WriteNullValue();
                break;
            case FlowValueKind.Boolean:
                writer.WriteBooleanValue(value.GetBoolean());
                break;
            case FlowValueKind.Integer:
                writer.WriteRawValue(
                    value.GetInteger().ToString(CultureInfo.InvariantCulture),
                    skipInputValidation: false);
                break;
            case FlowValueKind.Decimal:
                writer.WriteNumberValue(value.GetDecimal());
                break;
            case FlowValueKind.FloatingPoint:
                writer.WriteNumberValue(value.GetFloatingPoint());
                break;
            case FlowValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case FlowValueKind.Binary:
                writer.WriteBase64StringValue(value.GetBinary().AsSpan());
                break;
            case FlowValueKind.DateTimeOffset:
                writer.WriteStringValue(value.GetDateTimeOffset().ToString("O", CultureInfo.InvariantCulture));
                break;
            case FlowValueKind.Date:
                writer.WriteStringValue(value.GetDate().ToString("O", CultureInfo.InvariantCulture));
                break;
            case FlowValueKind.Time:
                writer.WriteStringValue(value.GetTime().ToString("O", CultureInfo.InvariantCulture));
                break;
            case FlowValueKind.Duration:
                writer.WriteStringValue(value.GetDuration().ToString("c", CultureInfo.InvariantCulture));
                break;
            case FlowValueKind.Guid:
                writer.WriteStringValue(value.GetGuid().ToString("D"));
                break;
            case FlowValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.GetArray())
                    WriteJsonValue(writer, item);
                writer.WriteEndArray();
                break;
            case FlowValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.GetObject().OrderBy(
                    item => item.Key,
                    StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteJsonValue(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            default:
                throw new InvalidOperationException(
                    $"FlowValue kind '{value.Kind}' cannot be serialized as JSON.");
        }
    }

    private static FlowValue ReadJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Null => FlowValue.Null,
            JsonValueKind.False => FlowValue.From(false),
            JsonValueKind.True => FlowValue.From(true),
            JsonValueKind.String => FlowValue.From(element.GetString()!),
            JsonValueKind.Number => ReadJsonNumber(element.GetRawText()),
            JsonValueKind.Array => FlowValue.FromArray(element.EnumerateArray().Select(ReadJsonValue)),
            JsonValueKind.Object => ReadJsonObject(element),
            _ => throw new JsonException(
                $"JSON value kind '{element.ValueKind}' is not supported.")
        };

    private static FlowValue ReadJsonNumber(string text)
    {
        try
        {
            if (!text.Contains('.') && text.IndexOfAny(['e', 'E']) < 0)
                return FlowValue.From(BigInteger.Parse(text, CultureInfo.InvariantCulture));

            if (decimal.TryParse(
                text,
                NumberStyles.Number | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out var decimalValue))
            {
                return FlowValue.From(decimalValue);
            }

            return FlowValue.From(double.Parse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (
            exception is FormatException or ArgumentException or OverflowException)
        {
            throw new JsonException(
                "JSON number is outside the supported FlowValue range.",
                exception);
        }
    }

    private static FlowValue ReadJsonObject(JsonElement element)
    {
        try
        {
            return FlowValue.FromObject(element.EnumerateObject().Select(
                property => new KeyValuePair<string, FlowValue>(
                    property.Name,
                    ReadJsonValue(property.Value))));
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("JSON object contains duplicate properties.", exception);
        }
    }

    private static Encoding ResolveContentEncoding(
        FlowContent content,
        SerializationNodeOptions options)
    {
        var declared = content.Encoding;
        if (string.IsNullOrWhiteSpace(declared))
            declared = ReadCharset(content.ContentType);
        return ResolveEncoding(declared, options.DefaultEncoding);
    }

    private static Encoding ResolveDefaultEncoding(SerializationNodeOptions options)
        => Encoding.GetEncoding(options.DefaultEncoding);

    private static string DecodeText(
        ImmutableArray<byte> content,
        string? encoding,
        string fallback)
    {
        var resolved = ResolveEncoding(encoding, fallback);
        var bytes = content.AsSpan();
        var preamble = resolved.GetPreamble();
        if (preamble.Length > 0 &&
            bytes.Length >= preamble.Length &&
            bytes[..preamble.Length].SequenceEqual(preamble))
        {
            bytes = bytes[preamble.Length..];
        }

        return resolved.GetString(bytes);
    }

    private static Encoding ResolveEncoding(string? name, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            try
            {
                return Encoding.GetEncoding(name.Trim().Trim('"'));
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException)
            {
                // Invalid transport metadata uses the configured fallback.
            }
        }

        return Encoding.GetEncoding(fallback);
    }

    private static string? ReadCharset(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;

        foreach (var segment in contentType.Split(';').Skip(1))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0 ||
                !string.Equals(
                    segment[..separator].Trim(),
                    "charset",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = segment[(separator + 1)..].Trim().Trim('"');
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    private static void EnsureContentInputSize(
        FlowContent content,
        SerializationNodeOptions options)
    {
        if (content.HasOriginalRepresentation)
            EnsureInputSize(content.OriginalBytes.Length, options);
    }

    private static void EnsureInputSize(int byteCount, SerializationNodeOptions options)
    {
        if (byteCount > options.MaxInputBytes)
        {
            throw Failure(
                SerializationErrorCodeNames.InputTooLarge,
                $"Input byte count {byteCount} exceeds the configured limit of {options.MaxInputBytes} bytes.");
        }
    }

    private static void EnsureOutputSize(int byteCount, SerializationNodeOptions options)
    {
        if (byteCount > options.MaxOutputBytes)
        {
            throw Failure(
                SerializationErrorCodeNames.OutputTooLarge,
                $"Output byte count {byteCount} exceeds the configured limit of {options.MaxOutputBytes} bytes.");
        }
    }

    private static FlowSerializationException Failure(
        string code,
        string message,
        Exception? exception = null)
        => new(code, message, exception);

    private sealed class ConfiguredJsonCodec(SerializationNodeOptions options)
        : IFlowContentCodec
    {
        public FlowValue Decode(ImmutableArray<byte> content, string? encoding)
        {
            var text = DecodeText(content, encoding, options.DefaultEncoding);
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = options.AllowTrailingCommas,
                CommentHandling = options.SkipComments
                    ? JsonCommentHandling.Skip
                    : JsonCommentHandling.Disallow
            });
            return ReadJsonValue(document.RootElement);
        }
    }

    private sealed class ConfiguredTextCodec(string fallbackEncoding)
        : IFlowContentCodec
    {
        public FlowValue Decode(ImmutableArray<byte> content, string? encoding)
            => FlowValue.From(DecodeText(content, encoding, fallbackEncoding));
    }
}
