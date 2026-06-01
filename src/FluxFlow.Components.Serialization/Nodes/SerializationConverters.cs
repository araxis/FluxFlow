using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluxFlow.Components.Serialization.Nodes;

internal static class SerializationConverters
{
    public static JsonParseResult ParseJson(
        JsonParseRequest request,
        SerializationNodeOptions options)
    {
        var payload = ResolveTextPayload(
            request.Text,
            request.Bytes,
            request.Encoding,
            options);
        EnsureInputSize(payload.ByteCount, options);

        try
        {
            var documentOptions = CreateDocumentOptions(options);
            using var document = JsonDocument.Parse(payload.Text, documentOptions);
            var node = JsonNode.Parse(payload.Text, documentOptions: documentOptions);

            return new JsonParseResult
            {
                Value = node,
                Kind = document.RootElement.ValueKind,
                Text = payload.Text,
                ByteCount = payload.ByteCount,
                Encoding = payload.Encoding.WebName
            };
        }
        catch (JsonException exception)
        {
            throw new SerializationNodeException(
                SerializationErrorCodes.JsonParseFailed,
                $"json.parse failed: {exception.Message}",
                exception);
        }
    }

    public static JsonStringifyResult StringifyJson(
        JsonStringifyRequest request,
        SerializationNodeOptions options)
    {
        var encoding = ResolveEncoding(request.Encoding, options.DefaultEncoding);
        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = request.WriteIndented ?? options.WriteIndented
        };

        string text;
        try
        {
            text = request.Value switch
            {
                JsonNode node => node.ToJsonString(serializerOptions),
                JsonElement element => JsonSerializer.Serialize(element, serializerOptions),
                _ => JsonSerializer.Serialize(request.Value, serializerOptions)
            };
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new SerializationNodeException(
                SerializationErrorCodes.JsonStringifyFailed,
                $"json.stringify failed: {exception.Message}",
                exception);
        }

        var bytes = encoding.GetBytes(text);
        EnsureOutputSize(bytes.Length, options);
        return new JsonStringifyResult
        {
            Text = text,
            Bytes = bytes,
            ByteCount = bytes.Length,
            Encoding = encoding.WebName
        };
    }

    public static TextEncodeResult EncodeText(
        TextEncodeRequest request,
        SerializationNodeOptions options)
    {
        if (request.Text is null)
        {
            throw MissingInput("text.encode requires text input.");
        }

        var encoding = ResolveEncoding(request.Encoding, options.DefaultEncoding);
        var inputByteCount = encoding.GetByteCount(request.Text);
        EnsureInputSize(inputByteCount, options);
        var textBytes = encoding.GetBytes(request.Text);
        var preamble = request.EmitBom ? encoding.GetPreamble() : [];
        var bytes = preamble.Length == 0
            ? textBytes
            : [.. preamble, .. textBytes];
        EnsureOutputSize(bytes.Length, options);

        return new TextEncodeResult
        {
            Bytes = bytes,
            ByteCount = bytes.Length,
            Encoding = encoding.WebName
        };
    }

    public static TextDecodeResult DecodeText(
        TextDecodeRequest request,
        SerializationNodeOptions options)
    {
        if (request.Bytes is not { } bytes)
        {
            throw MissingInput("text.decode requires byte input.");
        }

        EnsureInputSize(bytes.Length, options);
        var encoding = ResolveEncoding(request.Encoding, options.DefaultEncoding);
        var offset = GetPreambleLength(bytes, encoding);
        var text = encoding.GetString(bytes, offset, bytes.Length - offset);

        return new TextDecodeResult
        {
            Text = text,
            ByteCount = bytes.Length,
            Encoding = encoding.WebName
        };
    }

    public static Base64EncodeResult EncodeBase64(
        Base64EncodeRequest request,
        SerializationNodeOptions options)
    {
        var payload = ResolveBytesPayload(
            request.Text,
            request.Bytes,
            request.Encoding,
            options);
        EnsureInputSize(payload.Bytes.Length, options);

        var base64 = Convert.ToBase64String(
            payload.Bytes,
            request.InsertLineBreaks
                ? Base64FormattingOptions.InsertLineBreaks
                : Base64FormattingOptions.None);
        EnsureOutputSize(Encoding.UTF8.GetByteCount(base64), options);

        return new Base64EncodeResult
        {
            Text = base64,
            ByteCount = payload.Bytes.Length,
            EncodedLength = base64.Length
        };
    }

    public static Base64DecodeResult DecodeBase64(
        Base64DecodeRequest request,
        SerializationNodeOptions options)
    {
        if (request.Text is null)
        {
            throw MissingInput("base64.decode requires text input.");
        }

        var input = request.Text;
        EnsureInputSize(Encoding.UTF8.GetByteCount(input), options);
        if (!request.AllowWhitespace && input.Any(char.IsWhiteSpace))
        {
            throw new SerializationNodeException(
                SerializationErrorCodes.Base64DecodeFailed,
                "base64.decode input contains whitespace.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(input);
        }
        catch (FormatException exception)
        {
            throw new SerializationNodeException(
                SerializationErrorCodes.Base64DecodeFailed,
                $"base64.decode failed: {exception.Message}",
                exception);
        }

        EnsureOutputSize(bytes.Length, options);
        var encoding = request.DecodeText
            ? ResolveEncoding(request.Encoding, options.DefaultEncoding)
            : null;
        return new Base64DecodeResult
        {
            Bytes = bytes,
            ByteCount = bytes.Length,
            Text = encoding is null ? null : encoding.GetString(bytes),
            Encoding = encoding?.WebName
        };
    }

    public static IReadOnlyDictionary<string, object?> JsonParseInputAttributes(JsonParseRequest request)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["hasText"] = request.Text is not null,
            ["byteCount"] = request.Bytes?.Length,
            ["encoding"] = request.Encoding,
            ["contentType"] = request.ContentType
        };

    public static IReadOnlyDictionary<string, object?> JsonParseOutputAttributes(JsonParseResult result)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = result.Kind.ToString(),
            ["byteCount"] = result.ByteCount,
            ["encoding"] = result.Encoding
        };

    public static IReadOnlyDictionary<string, object?> JsonStringifyInputAttributes(JsonStringifyRequest request)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["valueType"] = request.Value?.GetType().Name ?? "null",
            ["writeIndented"] = request.WriteIndented,
            ["encoding"] = request.Encoding
        };

    public static IReadOnlyDictionary<string, object?> JsonStringifyOutputAttributes(JsonStringifyResult result)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["byteCount"] = result.ByteCount,
            ["encoding"] = result.Encoding
        };

    public static IReadOnlyDictionary<string, object?> TextEncodeInputAttributes(TextEncodeRequest request)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["hasText"] = request.Text is not null,
            ["encoding"] = request.Encoding,
            ["emitBom"] = request.EmitBom
        };

    public static IReadOnlyDictionary<string, object?> TextEncodeOutputAttributes(TextEncodeResult result)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["byteCount"] = result.ByteCount,
            ["encoding"] = result.Encoding
        };

    public static IReadOnlyDictionary<string, object?> TextDecodeInputAttributes(TextDecodeRequest request)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["byteCount"] = request.Bytes?.Length,
            ["encoding"] = request.Encoding
        };

    public static IReadOnlyDictionary<string, object?> TextDecodeOutputAttributes(TextDecodeResult result)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["byteCount"] = result.ByteCount,
            ["encoding"] = result.Encoding
        };

    public static IReadOnlyDictionary<string, object?> Base64EncodeInputAttributes(Base64EncodeRequest request)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["hasBytes"] = request.Bytes is { Length: > 0 },
            ["hasText"] = request.Text is not null,
            ["encoding"] = request.Encoding,
            ["insertLineBreaks"] = request.InsertLineBreaks
        };

    public static IReadOnlyDictionary<string, object?> Base64EncodeOutputAttributes(Base64EncodeResult result)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["byteCount"] = result.ByteCount,
            ["encodedLength"] = result.EncodedLength
        };

    public static IReadOnlyDictionary<string, object?> Base64DecodeInputAttributes(Base64DecodeRequest request)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["hasText"] = request.Text is not null,
            ["decodeText"] = request.DecodeText,
            ["encoding"] = request.Encoding,
            ["allowWhitespace"] = request.AllowWhitespace
        };

    public static IReadOnlyDictionary<string, object?> Base64DecodeOutputAttributes(Base64DecodeResult result)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["byteCount"] = result.ByteCount,
            ["decodedText"] = result.Text is not null,
            ["encoding"] = result.Encoding
        };

    private static ResolvedTextPayload ResolveTextPayload(
        string? text,
        byte[]? bytes,
        string? requestedEncoding,
        SerializationNodeOptions options)
    {
        var encoding = ResolveEncoding(requestedEncoding, options.DefaultEncoding);
        if (text is not null)
        {
            var byteCount = encoding.GetByteCount(text);
            EnsureInputSize(byteCount, options);
            return new ResolvedTextPayload(
                text,
                byteCount,
                encoding);
        }

        if (bytes is not { } source)
        {
            throw MissingInput("json.parse requires text or byte input.");
        }

        EnsureInputSize(source.Length, options);
        var offset = GetPreambleLength(source, encoding);
        return new ResolvedTextPayload(
            encoding.GetString(source, offset, source.Length - offset),
            source.Length,
            encoding);
    }

    private static ResolvedBytesPayload ResolveBytesPayload(
        string? text,
        byte[]? bytes,
        string? requestedEncoding,
        SerializationNodeOptions options)
    {
        if (bytes is not null)
        {
            return new ResolvedBytesPayload(bytes);
        }

        if (text is null)
        {
            throw MissingInput("base64.encode requires byte or text input.");
        }

        var encoding = ResolveEncoding(requestedEncoding, options.DefaultEncoding);
        var byteCount = encoding.GetByteCount(text);
        EnsureInputSize(byteCount, options);
        return new ResolvedBytesPayload(encoding.GetBytes(text));
    }

    private static Encoding ResolveEncoding(string? requestedEncoding, string defaultEncoding)
    {
        var encodingName = string.IsNullOrWhiteSpace(requestedEncoding)
            ? defaultEncoding
            : requestedEncoding.Trim();
        try
        {
            return Encoding.GetEncoding(encodingName);
        }
        catch (ArgumentException exception)
        {
            throw new SerializationNodeException(
                SerializationErrorCodes.UnsupportedEncoding,
                $"Encoding '{encodingName}' is not supported.",
                exception);
        }
    }

    private static JsonDocumentOptions CreateDocumentOptions(SerializationNodeOptions options)
        => new()
        {
            AllowTrailingCommas = options.AllowTrailingCommas,
            CommentHandling = options.SkipComments
                ? JsonCommentHandling.Skip
                : JsonCommentHandling.Disallow
        };

    private static int GetPreambleLength(byte[] bytes, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        if (preamble.Length == 0 || bytes.Length < preamble.Length)
        {
            return 0;
        }

        return bytes.AsSpan(0, preamble.Length).SequenceEqual(preamble)
            ? preamble.Length
            : 0;
    }

    private static void EnsureInputSize(int byteCount, SerializationNodeOptions options)
    {
        if (byteCount > options.MaxInputBytes)
        {
            throw new SerializationNodeException(
                SerializationErrorCodes.InputTooLarge,
                $"Input byte count {byteCount} exceeds the configured limit of {options.MaxInputBytes} bytes.");
        }
    }

    private static void EnsureOutputSize(int byteCount, SerializationNodeOptions options)
    {
        if (byteCount > options.MaxOutputBytes)
        {
            throw new SerializationNodeException(
                SerializationErrorCodes.OutputTooLarge,
                $"Output byte count {byteCount} exceeds the configured limit of {options.MaxOutputBytes} bytes.");
        }
    }

    private static SerializationNodeException MissingInput(string message)
        => new(SerializationErrorCodes.MissingInput, message);

    private sealed record ResolvedTextPayload(
        string Text,
        int ByteCount,
        Encoding Encoding);

    private sealed record ResolvedBytesPayload(byte[] Bytes);
}
