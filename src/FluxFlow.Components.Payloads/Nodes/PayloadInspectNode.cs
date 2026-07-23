using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Diagnostics;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Payloads.Nodes;

/// <summary>
/// Inspects canonical <see cref="FlowContent"/> while preserving its exact
/// representation and cached decoded <see cref="FlowValue"/>. Expected content
/// failures are emitted as normal <see cref="FlowResult{T}"/> values.
/// </summary>
public sealed class PayloadInspectNode : IFlowNode
{
    private static readonly JsonSerializerOptions FormattedJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly FlowContentCodecCatalog DefaultCodecs = CreateDefaultCodecs();

    private readonly PayloadInspectOptions _options;
    private readonly FlowContentCodecCatalog _codecs;
    private readonly TimeProvider _clock;
    private readonly TransformBlock<
        FlowMessage<FlowContent>,
        FlowMessage<FlowResult<PayloadInspectionResult>>> _processor;
    private readonly BroadcastBlock<FlowMessage<FlowResult<PayloadInspectionResult>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public PayloadInspectNode(
        PayloadInspectOptions? options = null,
        FlowContentCodecCatalog? codecs = null,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options ?? PayloadInspectOptions.Default);
        _codecs = codecs ?? DefaultCodecs;
        _clock = clock ?? TimeProvider.System;
        _processor = new TransformBlock<
            FlowMessage<FlowContent>,
            FlowMessage<FlowResult<PayloadInspectionResult>>>(
                Process,
                new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = _options.BoundedCapacity,
                    MaxDegreeOfParallelism = 1,
                    EnsureOrdered = true
                });
        _processor.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<FlowMessage<FlowContent>> Input => _processor;

    public ISourceBlock<FlowMessage<FlowResult<PayloadInspectionResult>>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public void Complete() => _processor.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ((IDataflowBlock)_processor).Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Complete();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative fault surface.
        }
    }

    private FlowMessage<FlowResult<PayloadInspectionResult>> Process(
        FlowMessage<FlowContent> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var timestamp = _clock.GetUtcNow();

        InspectionOutcome outcome;
        if (message.Payload is null)
        {
            const string errorMessage = "payload.inspect requires FlowContent input.";
            outcome = InspectionOutcome.Failure(
                PayloadInspectionResultKinds.InspectFailed,
                new PayloadInspectionResult
                {
                    Timestamp = timestamp,
                    Kind = PayloadKind.Empty,
                    ParseError = errorMessage
                },
                CreateError(
                    PayloadErrorCodeNames.InspectFailed,
                    errorMessage,
                    content: null));
        }
        else
        {
            try
            {
                outcome = Inspect(message.Payload, timestamp);
            }
            catch (Exception exception)
            {
                var inspection = CreateBaseInspection(message.Payload, timestamp) with
                {
                    Kind = message.Payload.HasOriginalRepresentation
                        ? PayloadKind.Binary
                        : PayloadKind.Value,
                    ParseError = exception.Message
                };
                outcome = InspectionOutcome.Failure(
                    PayloadInspectionResultKinds.InspectFailed,
                    inspection,
                    CreateError(
                        PayloadErrorCodeNames.InspectFailed,
                        $"payload.inspect failed: {exception.Message}",
                        message.Payload,
                        exception));
            }
        }

        PublishEvent(message, outcome, timestamp);
        var result = outcome.Error is null
            ? FlowResult<PayloadInspectionResult>.Success(
                outcome.ResultKind,
                outcome.Inspection,
                timestamp)
            : FlowResult<PayloadInspectionResult>.Failure(
                outcome.ResultKind,
                outcome.Error,
                timestamp,
                outcome.Inspection);
        return message.With(result);
    }

    private InspectionOutcome Inspect(FlowContent content, DateTimeOffset timestamp)
    {
        var inspection = CreateBaseInspection(content, timestamp);
        if (inspection.ByteCount > _options.MaxInputBytes)
        {
            inspection = inspection with
            {
                Kind = content.HasOriginalRepresentation ? PayloadKind.Binary : PayloadKind.Value,
                FormattedPreview =
                    $"payload too large: {inspection.ByteCount} bytes exceeds maxInputBytes {_options.MaxInputBytes}.",
                FormattedPreviewTruncated = true
            };
            return InspectionOutcome.Failure(
                PayloadInspectionResultKinds.InputTooLarge,
                inspection,
                CreateError(
                    PayloadErrorCodeNames.InputTooLarge,
                    $"Payload size {inspection.ByteCount} exceeds maxInputBytes {_options.MaxInputBytes}.",
                    content));
        }

        if (content.HasOriginalRepresentation && content.OriginalBytes.Length == 0)
        {
            return InspectionOutcome.Success(inspection with
            {
                Kind = PayloadKind.Empty,
                TextPreview = string.Empty,
                FormattedPreview = string.Empty
            });
        }

        FlowValue decoded;
        try
        {
            decoded = content.ReadAsFlowValue(_codecs);
        }
        catch (Exception exception)
        {
            var isParseFailure = IsJsonContentType(content.ContentType);
            var preview = TryReadTextPreview(content);
            inspection = inspection with
            {
                Kind = content.HasOriginalRepresentation ? PayloadKind.Binary : PayloadKind.Value,
                TextPreview = preview.Value,
                TextPreviewTruncated = preview.Truncated,
                ParseError = exception.Message
            };
            return InspectionOutcome.Failure(
                isParseFailure
                    ? PayloadInspectionResultKinds.ParseFailed
                    : PayloadInspectionResultKinds.DecodeFailed,
                inspection,
                CreateError(
                    isParseFailure
                        ? PayloadErrorCodeNames.ParseFailed
                        : PayloadErrorCodeNames.DecodeFailed,
                    $"Payload content could not be decoded: {exception.Message}",
                    content,
                    exception));
        }

        inspection = inspection with { DecodedValue = decoded };
        if (IsJsonContentType(content.ContentType))
            return InspectJson(content, decoded, inspection);
        if (IsXmlContentType(content.ContentType))
            return InspectXml(content, decoded, inspection);
        if (IsTextContentType(content.ContentType))
            return InspectText(content, decoded, inspection);

        var formatted = decoded.Kind == FlowValueKind.Binary
            ? default
            : LimitFormattedPreview(decoded.ToString());
        return InspectionOutcome.Success(inspection with
        {
            Kind = decoded.Kind == FlowValueKind.Binary
                ? PayloadKind.Binary
                : PayloadKind.Value,
            FormattedPreview = formatted.Value,
            FormattedPreviewTruncated = formatted.Truncated
        });
    }

    private InspectionOutcome InspectJson(
        FlowContent content,
        FlowValue decoded,
        PayloadInspectionResult inspection)
    {
        var text = ReadText(content, decoded);
        var preview = CreateTextPreview(text);
        var formatted = _options.FormatJson
            ? FormatJson(decoded)
            : default;

        return InspectionOutcome.Success(inspection with
        {
            Kind = decoded.Kind switch
            {
                FlowValueKind.Object => PayloadKind.JsonObject,
                FlowValueKind.Array => PayloadKind.JsonArray,
                _ => PayloadKind.JsonScalar
            },
            TextPreview = preview.Value,
            TextPreviewTruncated = preview.Truncated,
            FormattedPreview = formatted.Value,
            FormattedPreviewTruncated = formatted.Truncated
        });
    }

    private InspectionOutcome InspectXml(
        FlowContent content,
        FlowValue decoded,
        PayloadInspectionResult inspection)
    {
        if (decoded.Kind != FlowValueKind.String)
        {
            return XmlFailure(
                content,
                inspection,
                "Declared XML content did not decode to text.");
        }

        var text = decoded.GetString();
        var preview = CreateTextPreview(text);
        try
        {
            var document = XDocument.Parse(text);
            var formatted = _options.FormatXml
                ? LimitFormattedPreview(document.ToString(SaveOptions.None))
                : default;
            return InspectionOutcome.Success(inspection with
            {
                Kind = PayloadKind.Xml,
                TextPreview = preview.Value,
                TextPreviewTruncated = preview.Truncated,
                FormattedPreview = formatted.Value,
                FormattedPreviewTruncated = formatted.Truncated
            });
        }
        catch (Exception exception) when (
            exception is System.Xml.XmlException or InvalidOperationException)
        {
            return XmlFailure(content, inspection with
            {
                TextPreview = preview.Value,
                TextPreviewTruncated = preview.Truncated
            }, exception.Message, exception);
        }
    }

    private InspectionOutcome XmlFailure(
        FlowContent content,
        PayloadInspectionResult inspection,
        string message,
        Exception? exception = null)
    {
        inspection = inspection with
        {
            Kind = PayloadKind.Text,
            ParseError = message
        };
        return InspectionOutcome.Failure(
            PayloadInspectionResultKinds.ParseFailed,
            inspection,
            CreateError(
                PayloadErrorCodeNames.ParseFailed,
                $"Payload content could not be parsed as XML: {message}",
                content,
                exception));
    }

    private InspectionOutcome InspectText(
        FlowContent content,
        FlowValue decoded,
        PayloadInspectionResult inspection)
    {
        if (decoded.Kind != FlowValueKind.String)
        {
            return InspectionOutcome.Failure(
                PayloadInspectionResultKinds.DecodeFailed,
                inspection with
                {
                    Kind = PayloadKind.Value,
                    ParseError = "Declared text content did not decode to a string."
                },
                CreateError(
                    PayloadErrorCodeNames.DecodeFailed,
                    "Declared text content did not decode to a string.",
                    content));
        }

        var text = decoded.GetString();
        var preview = CreateTextPreview(text);
        var result = inspection with
        {
            Kind = PayloadKind.Text,
            TextPreview = preview.Value,
            TextPreviewTruncated = preview.Truncated
        };

        if (!_options.DetectBase64 || !TryDecodeBase64(text.Trim(), out var bytes))
            return InspectionOutcome.Success(result);

        var formatted = TryCreateDecodedPreview(bytes);
        return InspectionOutcome.Success(result with
        {
            Kind = PayloadKind.Base64,
            FormattedPreview = formatted.Value,
            FormattedPreviewTruncated = formatted.Truncated,
            Base64DecodedByteCount = bytes.Length
        });
    }

    private PayloadInspectionResult CreateBaseInspection(
        FlowContent content,
        DateTimeOffset timestamp)
        => new()
        {
            Timestamp = timestamp,
            Content = content,
            ContentType = NormalizeOptional(content.ContentType),
            ByteCount = GetByteCount(content),
            DetectedEncoding = ResolveEncodingName(content)
        };

    private static int GetByteCount(FlowContent content)
    {
        if (content.HasOriginalRepresentation)
            return content.OriginalBytes.Length;

        try
        {
            var value = content.ReadAsFlowValue(DefaultCodecs);
            return value.Kind switch
            {
                FlowValueKind.Binary => value.GetBinary().Length,
                FlowValueKind.String => Encoding.UTF8.GetByteCount(value.GetString()),
                _ => Encoding.UTF8.GetByteCount(value.ToString())
            };
        }
        catch
        {
            return 0;
        }
    }

    private static string ReadText(FlowContent content, FlowValue decoded)
    {
        if (content.HasOriginalRepresentation)
            return ResolveEncoding(content).GetString(content.OriginalBytes.AsSpan());
        if (decoded.Kind == FlowValueKind.String)
            return decoded.GetString();
        return decoded.ToString();
    }

    private Preview TryReadTextPreview(FlowContent content)
    {
        if (!content.HasOriginalRepresentation)
            return default;

        try
        {
            return CreateTextPreview(
                ResolveEncoding(content).GetString(content.OriginalBytes.AsSpan()));
        }
        catch
        {
            return default;
        }
    }

    private Preview CreateTextPreview(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= _options.MaxPreviewBytes)
            return new Preview(text, Truncated: false);

        return new Preview(
            Encoding.UTF8.GetString(bytes, 0, _options.MaxPreviewBytes),
            Truncated: true);
    }

    private Preview FormatJson(FlowValue value)
    {
        using var document = JsonDocument.Parse(value.ToString());
        return LimitFormattedPreview(
            JsonSerializer.Serialize(document.RootElement, FormattedJsonOptions));
    }

    private Preview TryCreateDecodedPreview(byte[] decoded)
    {
        try
        {
            var encoding = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);
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
        InspectionOutcome outcome,
        DateTimeOffset timestamp)
        => _events.Post(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = outcome.Error is null
                ? PayloadDiagnosticNames.Inspected
                : PayloadDiagnosticNames.Failed,
            Level = outcome.Error is null
                ? FlowEventLevel.Information
                : FlowEventLevel.Warning,
            Message = outcome.Error?.Message ?? "payload.inspect classified content.",
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = outcome.Inspection.Kind.ToString(),
                ["resultKind"] = outcome.ResultKind,
                ["isError"] = outcome.Error is not null,
                ["byteCount"] = outcome.Inspection.ByteCount,
                ["contentType"] = outcome.Inspection.ContentType
            }
        });

    private static DataFlowError CreateError(
        string code,
        string message,
        FlowContent? content,
        Exception? exception = null)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["byteCount"] = FlowValue.From(content?.HasOriginalRepresentation == true
                ? content.OriginalBytes.Length
                : 0),
            ["contentType"] = OptionalValue(content?.ContentType),
            ["encoding"] = OptionalValue(content?.Encoding)
        };
        if (exception is not null)
        {
            details["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name);
        }

        return new DataFlowError(
            code,
            message,
            category: "Payloads",
            isTransient: false,
            details: FlowValue.FromObject(details));
    }

    private static FlowValue OptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? FlowValue.Null : FlowValue.From(value.Trim());

    private static FlowContentCodecCatalog CreateDefaultCodecs()
    {
        var json = new JsonFlowContentCodec();
        var text = new TextFlowContentCodec();
        return new FlowContentCodecCatalog(
        [
            new(FlowContentCodecMatch.ExactMediaType, "application/json", json),
            new(FlowContentCodecMatch.StructuredSuffix, "json", json),
            new(FlowContentCodecMatch.ExactMediaType, "application/xml", text),
            new(FlowContentCodecMatch.StructuredSuffix, "xml", text),
            new(FlowContentCodecMatch.MediaFamily, "text", text)
        ],
        new BinaryFlowContentCodec());
    }

    private static Encoding ResolveEncoding(FlowContent content)
    {
        var name = ReadDeclaredEncodingName(content);
        if (string.IsNullOrWhiteSpace(name))
            return Encoding.UTF8;

        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }

    private static string? ResolveEncodingName(FlowContent content)
    {
        var declared = ReadDeclaredEncodingName(content);
        if (IsTextualContentType(content.ContentType) && content.HasOriginalRepresentation)
            return ResolveEncoding(content).WebName;

        return declared;
    }

    private static string? ReadDeclaredEncodingName(FlowContent content)
    {
        if (!string.IsNullOrWhiteSpace(content.Encoding))
            return content.Encoding.Trim().Trim('"');

        if (!string.IsNullOrWhiteSpace(content.ContentType))
        {
            foreach (var segment in content.ContentType.Split(';').Skip(1))
            {
                var separator = segment.IndexOf('=');
                if (separator <= 0 ||
                    !segment[..separator].Trim().Equals(
                        "charset",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var charset = segment[(separator + 1)..].Trim().Trim('"');
                if (charset.Length > 0)
                    return charset;
            }
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

    private async Task MonitorCompletionAsync()
    {
        try
        {
            await _processor.Completion.ConfigureAwait(false);
            await _output.Completion.ConfigureAwait(false);
            _events.Complete();
            await _events.Completion.ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            _events.Complete();
            _completion.TrySetException(exception);
        }
    }

    private static PayloadInspectOptions ValidateOptions(PayloadInspectOptions options)
    {
        if (options.MaxInputBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "payload.inspect option 'maxInputBytes' must be greater than zero.");
        }
        if (options.MaxPreviewBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "payload.inspect option 'maxPreviewBytes' must be greater than zero.");
        }
        if (options.MaxFormattedChars <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "payload.inspect option 'maxFormattedChars' must be greater than zero.");
        }
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "payload.inspect option 'boundedCapacity' must be greater than zero.");
        }

        return options;
    }

    private sealed record InspectionOutcome(
        string ResultKind,
        PayloadInspectionResult Inspection,
        DataFlowError? Error)
    {
        internal static InspectionOutcome Success(PayloadInspectionResult inspection)
            => new(PayloadInspectionResultKinds.Inspected, inspection, Error: null);

        internal static InspectionOutcome Failure(
            string resultKind,
            PayloadInspectionResult inspection,
            DataFlowError error)
            => new(resultKind, inspection, error);
    }

    private readonly record struct Preview(string? Value, bool Truncated);
}
