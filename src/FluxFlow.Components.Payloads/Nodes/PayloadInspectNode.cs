using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Diagnostics;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;

namespace FluxFlow.Components.Payloads.Nodes;

public sealed class PayloadInspectNode : FlowNodeBase
{
    private static readonly JsonSerializerOptions FormattedJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly PayloadInspectOptions _options;
    private readonly ActionBlock<PayloadInspectionRequest> _input;
    private readonly BufferBlock<PayloadInspectionResult> _output;

    private PayloadInspectNode(PayloadInspectOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Payload inspect bounded capacity must be greater than zero.");
        }

        _input = new ActionBlock<PayloadInspectionRequest>(
            InspectAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.BoundedCapacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _output = new BufferBlock<PayloadInspectionResult>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        CompleteWhen(_input.Completion);
    }

    public ITargetBlock<PayloadInspectionRequest> Input => _input;

    public ISourceBlock<PayloadInspectionResult> Output => _output;

    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = PayloadOptionsReader.ReadInspectOptions(context.Definition);
        var node = new PayloadInspectNode(options);

        return context.CreateNode(node)
            .Input(PayloadComponentPorts.Input, node.Input)
            .Output(PayloadComponentPorts.Output, node.Output)
            .Output(PayloadComponentPorts.Errors, node.Errors)
            .Build();
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
            ((IDataflowBlock)_output).Fault(exception);
        }
    }

    protected override void OnNodeCompleted()
    {
        _output.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        ((IDataflowBlock)_output).Fault(exception);
        base.OnNodeFaulted(exception);
    }

    private async Task InspectAsync(PayloadInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        PayloadInspectionResult result;
        try
        {
            result = Inspect(request);
        }
        catch (PayloadInspectNodeException exception)
        {
            ReportInspectionError(
                exception.Code,
                exception.Message,
                request,
                exception.InnerException);
            return;
        }
        catch (Exception exception)
        {
            ReportInspectionError(
                PayloadErrorCodes.InspectFailed,
                $"payload.inspect failed: {exception.Message}",
                request,
                exception);
            return;
        }

        await _output.SendAsync(result).ConfigureAwait(false);
        TryEmitDiagnostic(
            PayloadDiagnosticNames.Inspected,
            message: "payload.inspect classified input.",
            attributes: CreateAttributes(result));
    }

    private PayloadInspectionResult Inspect(PayloadInspectionRequest request)
    {
        var payload = ResolvePayload(request);
        if (payload.ByteCount == 0 && string.IsNullOrEmpty(payload.Text))
        {
            return new PayloadInspectionResult
            {
                Kind = PayloadKind.Empty,
                ContentType = NormalizeContentType(request.ContentType),
                ByteCount = 0,
                DetectedEncoding = payload.EncodingName,
                TextPreview = string.Empty,
                FormattedPreview = string.Empty
            };
        }

        if (payload.IsBinary || payload.Text is null)
        {
            return new PayloadInspectionResult
            {
                Kind = PayloadKind.Binary,
                ContentType = NormalizeContentType(request.ContentType),
                ByteCount = payload.ByteCount,
                DetectedEncoding = payload.EncodingName
            };
        }

        var text = payload.Text;
        var trimmed = text.Trim();
        var preview = CreateTextPreview(text, payload.Encoding);

        if (TryInspectJson(trimmed, request.ContentType, out var jsonResult))
        {
            return jsonResult with
            {
                ContentType = NormalizeContentType(request.ContentType),
                ByteCount = payload.ByteCount,
                DetectedEncoding = payload.EncodingName,
                TextPreview = preview.Value,
                TextPreviewTruncated = preview.Truncated
            };
        }

        if (TryInspectXml(trimmed, request.ContentType, out var xmlResult))
        {
            return xmlResult with
            {
                ContentType = NormalizeContentType(request.ContentType),
                ByteCount = payload.ByteCount,
                DetectedEncoding = payload.EncodingName,
                TextPreview = preview.Value,
                TextPreviewTruncated = preview.Truncated
            };
        }

        if (_options.DetectBase64 &&
            TryInspectBase64(trimmed, request.ContentType, out var base64Result))
        {
            return base64Result with
            {
                ContentType = NormalizeContentType(request.ContentType),
                ByteCount = payload.ByteCount,
                DetectedEncoding = payload.EncodingName,
                TextPreview = preview.Value,
                TextPreviewTruncated = preview.Truncated
            };
        }

        return new PayloadInspectionResult
        {
            Kind = PayloadKind.Text,
            ContentType = NormalizeContentType(request.ContentType),
            ByteCount = payload.ByteCount,
            DetectedEncoding = payload.EncodingName,
            TextPreview = preview.Value,
            TextPreviewTruncated = preview.Truncated
        };
    }

    private ResolvedPayload ResolvePayload(PayloadInspectionRequest request)
    {
        Encoding? hintEncoding = null;
        var encodingName = ResolveEncodingName(request);
        if (!string.IsNullOrWhiteSpace(encodingName))
        {
            try
            {
                hintEncoding = Encoding.GetEncoding(encodingName);
            }
            catch (ArgumentException exception)
            {
                throw new PayloadInspectNodeException(
                    PayloadErrorCodes.UnsupportedEncoding,
                    $"payload.inspect unsupported encoding hint '{encodingName}'.",
                    exception);
            }
        }

        if (request.Text is not null)
        {
            var encoding = hintEncoding ?? Encoding.UTF8;
            var textBytes = request.Bytes ?? encoding.GetBytes(request.Text);
            return new ResolvedPayload(
                request.Text,
                textBytes.Length,
                encoding,
                encoding.WebName,
                IsBinary: false);
        }

        if (request.Bytes is not { Length: > 0 } bytes)
        {
            return new ResolvedPayload(
                string.Empty,
                0,
                hintEncoding ?? Encoding.UTF8,
                (hintEncoding ?? Encoding.UTF8).WebName,
                IsBinary: false);
        }

        var detected = hintEncoding ?? DetectEncoding(bytes);
        if (detected is null)
        {
            return new ResolvedPayload(
                Text: null,
                bytes.Length,
                Encoding: null,
                EncodingName: null,
                IsBinary: true);
        }

        try
        {
            var offset = GetPreambleLength(bytes, detected);
            var text = detected.GetString(bytes, offset, bytes.Length - offset);
            return new ResolvedPayload(
                text,
                bytes.Length,
                detected,
                detected.WebName,
                IsBinary: false);
        }
        catch (DecoderFallbackException)
        {
            return new ResolvedPayload(
                Text: null,
                bytes.Length,
                Encoding: null,
                EncodingName: null,
                IsBinary: true);
        }
    }

    private static Encoding? DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
        }

        if (bytes.Length >= 2 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xFE)
        {
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);
        }

        if (bytes.Length >= 2 &&
            bytes[0] == 0xFE &&
            bytes[1] == 0xFF)
        {
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true);
        }

        if (bytes.Contains((byte)0))
        {
            return null;
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    }

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

    private bool TryInspectJson(
        string trimmed,
        string? contentType,
        out PayloadInspectionResult result)
    {
        result = default!;
        var candidate = IsJsonContentType(contentType) || LooksLikeJson(trimmed);
        if (!candidate)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var kind = document.RootElement.ValueKind switch
            {
                JsonValueKind.Object => PayloadKind.JsonObject,
                JsonValueKind.Array => PayloadKind.JsonArray,
                _ => PayloadKind.JsonScalar
            };
            var formatted = _options.FormatJson
                ? LimitFormattedPreview(
                    JsonSerializer.Serialize(document.RootElement, FormattedJsonOptions))
                : new Preview(null, Truncated: false);

            result = new PayloadInspectionResult
            {
                Kind = kind,
                FormattedPreview = formatted.Value,
                FormattedPreviewTruncated = formatted.Truncated
            };
            return true;
        }
        catch (JsonException exception)
        {
            result = new PayloadInspectionResult
            {
                Kind = PayloadKind.Text,
                ParseError = exception.Message
            };
            return true;
        }
    }

    private bool TryInspectXml(
        string trimmed,
        string? contentType,
        out PayloadInspectionResult result)
    {
        result = default!;
        var candidate = IsXmlContentType(contentType) || trimmed.StartsWith('<');
        if (!candidate)
        {
            return false;
        }

        try
        {
            var document = XDocument.Parse(trimmed);
            var formatted = _options.FormatXml
                ? LimitFormattedPreview(document.ToString(SaveOptions.None))
                : new Preview(null, Truncated: false);

            result = new PayloadInspectionResult
            {
                Kind = PayloadKind.Xml,
                FormattedPreview = formatted.Value,
                FormattedPreviewTruncated = formatted.Truncated
            };
            return true;
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException)
        {
            result = new PayloadInspectionResult
            {
                Kind = PayloadKind.Text,
                ParseError = exception.Message
            };
            return true;
        }
    }

    private bool TryInspectBase64(
        string trimmed,
        string? contentType,
        out PayloadInspectionResult result)
    {
        result = default!;
        if (trimmed.Length < 8 ||
            IsJsonContentType(contentType) ||
            IsXmlContentType(contentType) ||
            !IsBase64Candidate(trimmed))
        {
            return false;
        }

        try
        {
            var decoded = Convert.FromBase64String(trimmed);
            var formatted = TryCreateDecodedPreview(decoded);
            result = new PayloadInspectionResult
            {
                Kind = PayloadKind.Base64,
                FormattedPreview = formatted.Value,
                FormattedPreviewTruncated = formatted.Truncated,
                Base64DecodedByteCount = decoded.Length
            };
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private Preview TryCreateDecodedPreview(byte[] decoded)
    {
        var encoding = DetectEncoding(decoded);
        if (encoding is null)
        {
            return new Preview(null, Truncated: false);
        }

        try
        {
            var offset = GetPreambleLength(decoded, encoding);
            var text = encoding.GetString(decoded, offset, decoded.Length - offset);
            return LimitFormattedPreview(text);
        }
        catch (DecoderFallbackException)
        {
            return new Preview(null, Truncated: false);
        }
    }

    private Preview CreateTextPreview(string text, Encoding? encoding)
    {
        encoding ??= Encoding.UTF8;
        var bytes = encoding.GetBytes(text);
        if (bytes.Length <= _options.MaxPreviewBytes)
        {
            return new Preview(text, Truncated: false);
        }

        var previewEncoding = CreatePreviewEncoding(encoding);
        return new Preview(
            previewEncoding.GetString(bytes, 0, _options.MaxPreviewBytes),
            Truncated: true);
    }

    private static Encoding CreatePreviewEncoding(Encoding encoding)
        => Encoding.GetEncoding(
            encoding.WebName,
            EncoderFallback.ReplacementFallback,
            DecoderFallback.ReplacementFallback);

    private Preview LimitFormattedPreview(string value)
    {
        if (value.Length <= _options.MaxFormattedChars)
        {
            return new Preview(value, Truncated: false);
        }

        return new Preview(value[.._options.MaxFormattedChars], Truncated: true);
    }

    private static string? NormalizeContentType(string? contentType)
        => string.IsNullOrWhiteSpace(contentType)
            ? null
            : contentType.Trim();

    private static string? ResolveEncodingName(PayloadInspectionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.EncodingHint))
        {
            return request.EncodingHint.Trim();
        }

        if (string.IsNullOrWhiteSpace(request.ContentType))
        {
            return null;
        }

        foreach (var segment in request.ContentType.Split(';', StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                parts[0].Equals("charset", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(parts[1]))
            {
                return parts[1].Trim('"');
            }
        }

        return null;
    }

    private static bool IsJsonContentType(string? contentType)
        => contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsXmlContentType(string? contentType)
        => contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true;

    private static bool LooksLikeJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var first = value[0];
        return first is '{' or '[' or '"' or '-' ||
               char.IsDigit(first) ||
               value is "true" or "false" or "null";
    }

    private static bool IsBase64Candidate(string value)
    {
        if (value.Length % 4 != 0)
        {
            return false;
        }

        return value.All(static character =>
            char.IsLetterOrDigit(character) ||
            character is '+' or '/' or '=');
    }

    private void ReportInspectionError(
        int code,
        string message,
        PayloadInspectionRequest request,
        Exception? exception = null)
    {
        TryReportError(code, message, exception, CreateErrorContext(request));
        TryEmitDiagnostic(
            PayloadDiagnosticNames.Failed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CreateAttributes(request));
    }

    private Dictionary<string, object?> CreateAttributes(PayloadInspectionRequest request)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["hasBytes"] = request.Bytes is { Length: > 0 },
            ["hasText"] = request.Text is not null
        };

        if (!string.IsNullOrWhiteSpace(request.ContentType))
        {
            attributes["contentType"] = request.ContentType;
        }

        if (!string.IsNullOrWhiteSpace(request.EncodingHint))
        {
            attributes["encodingHint"] = request.EncodingHint;
        }

        return attributes;
    }

    private static Dictionary<string, object?> CreateAttributes(PayloadInspectionResult result)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = result.Kind.ToString(),
            ["byteCount"] = result.ByteCount
        };

        if (!string.IsNullOrWhiteSpace(result.ContentType))
        {
            attributes["contentType"] = result.ContentType;
        }

        if (!string.IsNullOrWhiteSpace(result.DetectedEncoding))
        {
            attributes["detectedEncoding"] = result.DetectedEncoding;
        }

        if (result.Base64DecodedByteCount.HasValue)
        {
            attributes["base64DecodedByteCount"] = result.Base64DecodedByteCount.Value;
        }

        return attributes;
    }

    private static string CreateErrorContext(PayloadInspectionRequest request)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ContentType))
        {
            values.Add($"contentType={request.ContentType}");
        }

        if (!string.IsNullOrWhiteSpace(request.EncodingHint))
        {
            values.Add($"encodingHint={request.EncodingHint}");
        }

        if (request.Bytes is { } bytes)
        {
            values.Add($"byteCount={bytes.Length}");
        }

        if (request.Text is not null)
        {
            values.Add("hasText=true");
        }

        return string.Join("; ", values);
    }

    private sealed record ResolvedPayload(
        string? Text,
        int ByteCount,
        Encoding? Encoding,
        string? EncodingName,
        bool IsBinary);

    private sealed record Preview(string? Value, bool Truncated);

    private sealed class PayloadInspectNodeException : Exception
    {
        public PayloadInspectNodeException(int code, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
        }

        public int Code { get; }
    }
}
