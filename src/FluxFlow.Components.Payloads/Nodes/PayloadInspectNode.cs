using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Diagnostics;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Payloads.Nodes;

/// <summary>
/// Inspects exact payload bytes and decodes them only when the declared media
/// type requires JSON, XML, or text processing.
/// </summary>
public sealed class PayloadInspectNode : FlowNode<FlowContent, PayloadInspectionResult>
{
    private static readonly JsonSerializerOptions FormattedJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly PayloadInspectOptions _options;
    private readonly TimeProvider _clock;
    public PayloadInspectNode(
        PayloadInspectOptions? options = null,
        TimeProvider? clock = null)
        : base(CreateNodeOptions(options ?? PayloadInspectOptions.Default))
    {
        _options = ValidateOptions(options ?? PayloadInspectOptions.Default);
        _clock = clock ?? TimeProvider.System;
    }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage<FlowContent> message)
        => await EmitAsync(Process(message), Stopping).ConfigureAwait(false);

    private FlowMessage<PayloadInspectionResult> Process(FlowMessage<FlowContent> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message.WithError<PayloadInspectionResult>(message.Error!);

        var timestamp = _clock.GetUtcNow();
        try
        {
            var inspection = Inspect(message.Value, timestamp);
            PublishEvent(message, inspection, error: null, timestamp);
            return message.With(inspection);
        }
        catch (PayloadInspectionException exception)
        {
            var error = CreateError(exception.Code, exception.Message, message.Value, exception);
            PublishEvent(message, inspection: null, error, timestamp);
            return message.WithError<PayloadInspectionResult>(error);
        }
        catch (Exception exception)
        {
            var error = CreateError(
                PayloadErrorCodeNames.InspectFailed,
                $"payload.inspect failed: {exception.Message}",
                message.Value,
                exception);
            PublishEvent(message, inspection: null, error, timestamp);
            return message.WithError<PayloadInspectionResult>(error);
        }
    }

    private PayloadInspectionResult Inspect(FlowContent content, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(content);
        var byteCount = content.Bytes.Length;
        if (byteCount > _options.MaxInputBytes)
        {
            throw new PayloadInspectionException(
                PayloadErrorCodeNames.InputTooLarge,
                $"Payload size {byteCount} exceeds maxInputBytes {_options.MaxInputBytes}.");
        }

        var result = new PayloadInspectionResult
        {
            Timestamp = timestamp,
            Content = content,
            ContentType = NormalizeOptional(content.ContentType),
            ByteCount = byteCount,
            DetectedEncoding = IsTextualContentType(content.ContentType)
                ? ResolveEncoding(content).WebName
                : NormalizeOptional(content.Encoding)
        };

        if (byteCount == 0)
        {
            return result with
            {
                Kind = PayloadKind.Empty,
                TextPreview = string.Empty,
                FormattedPreview = string.Empty
            };
        }

        if (IsJsonContentType(content.ContentType))
            return InspectJson(content, result);
        if (IsXmlContentType(content.ContentType))
            return InspectXml(content, result);
        if (IsTextContentType(content.ContentType))
            return InspectText(content, result);

        return result with { Kind = PayloadKind.Binary };
    }

    private PayloadInspectionResult InspectJson(
        FlowContent content,
        PayloadInspectionResult inspection)
    {
        try
        {
            var text = ResolveEncoding(content).GetString(content.Bytes.AsSpan());
            using var document = JsonDocument.Parse(text);
            var json = document.RootElement.Clone();
            var preview = CreateTextPreview(text);
            var formatted = _options.FormatJson
                ? LimitFormattedPreview(JsonSerializer.Serialize(json, FormattedJsonOptions))
                : default;

            return inspection with
            {
                Kind = json.ValueKind switch
                {
                    JsonValueKind.Object => PayloadKind.JsonObject,
                    JsonValueKind.Array => PayloadKind.JsonArray,
                    _ => PayloadKind.JsonScalar
                },
                JsonValue = json,
                TextPreview = preview.Value,
                TextPreviewTruncated = preview.Truncated,
                FormattedPreview = formatted.Value,
                FormattedPreviewTruncated = formatted.Truncated
            };
        }
        catch (JsonException exception)
        {
            throw new PayloadInspectionException(
                PayloadErrorCodeNames.ParseFailed,
                $"Payload content could not be parsed as JSON: {exception.Message}",
                exception);
        }
    }

    private PayloadInspectionResult InspectXml(
        FlowContent content,
        PayloadInspectionResult inspection)
    {
        var text = ReadText(content);
        var preview = CreateTextPreview(text);
        try
        {
            var document = XDocument.Parse(text);
            var formatted = _options.FormatXml
                ? LimitFormattedPreview(document.ToString(SaveOptions.None))
                : default;
            return inspection with
            {
                Kind = PayloadKind.Xml,
                TextPreview = preview.Value,
                TextPreviewTruncated = preview.Truncated,
                FormattedPreview = formatted.Value,
                FormattedPreviewTruncated = formatted.Truncated
            };
        }
        catch (System.Xml.XmlException exception)
        {
            throw new PayloadInspectionException(
                PayloadErrorCodeNames.ParseFailed,
                $"Payload content could not be parsed as XML: {exception.Message}",
                exception);
        }
    }

    private PayloadInspectionResult InspectText(
        FlowContent content,
        PayloadInspectionResult inspection)
    {
        var text = ReadText(content);
        var preview = CreateTextPreview(text);
        var result = inspection with
        {
            Kind = PayloadKind.Text,
            TextPreview = preview.Value,
            TextPreviewTruncated = preview.Truncated
        };

        if (!_options.DetectBase64 || !TryDecodeBase64(text.Trim(), out var bytes))
            return result;

        var formatted = TryCreateDecodedPreview(bytes);
        return result with
        {
            Kind = PayloadKind.Base64,
            FormattedPreview = formatted.Value,
            FormattedPreviewTruncated = formatted.Truncated,
            Base64DecodedByteCount = bytes.Length
        };
    }

    private string ReadText(FlowContent content)
        => ResolveEncoding(content).GetString(content.Bytes.AsSpan());

    private Preview CreateTextPreview(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= _options.MaxPreviewBytes)
            return new Preview(text, Truncated: false);

        return new Preview(
            Encoding.UTF8.GetString(bytes, 0, _options.MaxPreviewBytes),
            Truncated: true);
    }

    private Preview TryCreateDecodedPreview(byte[] decoded)
    {
        try
        {
            var encoding = new UTF8Encoding(false, true);
            return LimitFormattedPreview(encoding.GetString(decoded));
        }
        catch (DecoderFallbackException)
        {
            return default;
        }
    }

    private Preview LimitFormattedPreview(string value)
        => value.Length <= _options.MaxFormattedChars
            ? new Preview(value, Truncated: false)
            : new Preview(value[.._options.MaxFormattedChars], Truncated: true);

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length < 8 || value.Length % 4 != 0)
            return false;

        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void PublishEvent(
        FlowMessage<FlowContent> message,
        PayloadInspectionResult? inspection,
        FlowError? error,
        DateTimeOffset timestamp)
        => EmitEvent(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = error is null ? PayloadDiagnosticNames.Inspected : PayloadDiagnosticNames.Failed,
            Level = error is null ? FlowEventLevel.Information : FlowEventLevel.Warning,
            Message = error?.Message ?? "payload.inspect classified content.",
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = inspection?.Kind.ToString(),
                ["isError"] = error is not null,
                ["byteCount"] = inspection?.ByteCount ?? message.Value.Bytes.Length,
                ["contentType"] = inspection?.ContentType ?? message.Value.ContentType
            }
        });

    private static FlowError CreateError(
        string code,
        string message,
        FlowContent content,
        Exception exception)
        => new(
            code,
            message,
            category: "Payloads",
            isTransient: false,
            details: JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["byteCount"] = content.Bytes.Length,
                ["contentType"] = NormalizeOptional(content.ContentType),
                ["encoding"] = NormalizeOptional(content.Encoding),
                ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name
            }));

    private static Encoding ResolveEncoding(FlowContent content)
    {
        var name = ReadDeclaredEncodingName(content);
        if (string.IsNullOrWhiteSpace(name))
            return Encoding.UTF8;

        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }

    private static string? ReadDeclaredEncodingName(FlowContent content)
    {
        if (!string.IsNullOrWhiteSpace(content.Encoding))
            return content.Encoding.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(content.ContentType))
            return null;

        foreach (var segment in content.ContentType.Split(';').Skip(1))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0 ||
                !segment[..separator].Trim().Equals("charset", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var charset = segment[(separator + 1)..].Trim().Trim('"');
            if (charset.Length > 0)
                return charset;
        }

        return null;
    }

    private static bool IsTextualContentType(string? contentType)
        => IsJsonContentType(contentType) ||
           IsXmlContentType(contentType) ||
           IsTextContentType(contentType);

    private static bool IsJsonContentType(string? contentType)
    {
        var mediaType = ReadMediaType(contentType);
        return mediaType == "application/json" || mediaType.EndsWith("+json", StringComparison.Ordinal);
    }

    private static bool IsXmlContentType(string? contentType)
    {
        var mediaType = ReadMediaType(contentType);
        return mediaType is "application/xml" or "text/xml" ||
               mediaType.EndsWith("+xml", StringComparison.Ordinal);
    }

    private static bool IsTextContentType(string? contentType)
        => ReadMediaType(contentType).StartsWith("text/", StringComparison.Ordinal);

    private static string ReadMediaType(string? contentType)
        => string.IsNullOrWhiteSpace(contentType)
            ? string.Empty
            : contentType.Split(';', 2)[0].Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PayloadInspectOptions ValidateOptions(PayloadInspectOptions options)
    {
        if (options.MaxInputBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "maxInputBytes must be positive.");
        if (options.MaxPreviewBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "maxPreviewBytes must be positive.");
        if (options.MaxFormattedChars <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "maxFormattedChars must be positive.");
        if (options.BoundedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "boundedCapacity must be positive.");
        return options;
    }

    private static FlowNodeOptions CreateNodeOptions(PayloadInspectOptions options)
    {
        var validated = ValidateOptions(options);
        return new FlowNodeOptions
        {
            InputCapacity = validated.BoundedCapacity,
            OutputCapacity = validated.BoundedCapacity
        };
    }

    private sealed class PayloadInspectionException : Exception
    {
        internal PayloadInspectionException(string code, string message, Exception? inner = null)
            : base(message, inner)
            => Code = code;

        internal string Code { get; }
    }

    private readonly record struct Preview(string? Value, bool Truncated);
}
