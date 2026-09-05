using System.Text;
using System.Text.Json;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Nodes;

internal static class SerializationConverters
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new();

    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true
    };

    internal static JsonElement ParseJson(
        FlowContent content,
        SerializationNodeOptions options)
    {
        EnsureContentInputSize(content, options);
        try
        {
            var text = ResolveContentEncoding(content, options).GetString(content.Bytes.AsSpan());
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = options.AllowTrailingCommas,
                CommentHandling = options.SkipComments
                    ? JsonCommentHandling.Skip
                    : JsonCommentHandling.Disallow
            });
            return document.RootElement.Clone();
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

    internal static FlowContent StringifyJson(
        JsonElement value,
        SerializationNodeOptions options)
    {
        byte[] utf8;
        try
        {
            utf8 = JsonSerializer.SerializeToUtf8Bytes(
                value,
                options.WriteIndented ? IndentedJsonOptions : CompactJsonOptions);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or JsonException)
        {
            throw Failure(
                SerializationErrorCodeNames.JsonStringifyFailed,
                $"JSON value could not be serialized: {exception.Message}",
                exception);
        }

        var encoding = ResolveDefaultEncoding(options);
        var bytes = encoding.CodePage == Encoding.UTF8.CodePage
            ? utf8
            : encoding.GetBytes(Encoding.UTF8.GetString(utf8));
        EnsureOutputSize(bytes.Length, options);
        return FlowContent.FromBytes(bytes, "application/json", encoding.WebName);
    }

    internal static FlowContent EncodeText(
        string value,
        SerializationNodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        var encoding = ResolveDefaultEncoding(options);
        var bytes = encoding.GetBytes(value);
        EnsureInputSize(bytes.Length, options);
        EnsureOutputSize(bytes.Length, options);
        return FlowContent.FromBytes(bytes, "text/plain", encoding.WebName);
    }

    internal static string DecodeText(
        FlowContent content,
        SerializationNodeOptions options)
    {
        EnsureContentInputSize(content, options);
        try
        {
            var encoding = ResolveContentEncoding(content, options);
            var bytes = content.Bytes.AsSpan();
            var preamble = encoding.GetPreamble();
            if (preamble.Length > 0 && bytes.StartsWith(preamble))
            {
                bytes = bytes[preamble.Length..];
            }

            return encoding.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw Failure(
                SerializationErrorCodeNames.TextDecodeFailed,
                $"Text content could not be decoded: {exception.Message}",
                exception);
        }
    }

    internal static string EncodeBase64(
        FlowContent content,
        SerializationNodeOptions options)
    {
        EnsureContentInputSize(content, options);
        var text = Convert.ToBase64String(content.Bytes.AsSpan());
        EnsureOutputSize(Encoding.UTF8.GetByteCount(text), options);
        return text;
    }

    internal static FlowContent DecodeBase64(
        string value,
        SerializationNodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureInputSize(Encoding.UTF8.GetByteCount(value), options);

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value);
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
        => EnsureInputSize(content.Bytes.Length, options);

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

    private static SerializationFailureException Failure(
        string code,
        string message,
        Exception? exception = null)
        => new(code, message, exception);
}
