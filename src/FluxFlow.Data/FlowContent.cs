using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Text.Json.Serialization;

namespace FluxFlow.Data;

public sealed class FlowContent
{
    private readonly object _decodeGate = new();
    private FlowValue? _value;
    private ExceptionDispatchInfo? _decodeFailure;
    private bool _decodeAttempted;

    private FlowContent(
        ImmutableArray<byte> originalBytes,
        bool hasOriginalRepresentation,
        FlowValue? value,
        string? contentType,
        string? encoding)
    {
        OriginalBytes = originalBytes;
        HasOriginalRepresentation = hasOriginalRepresentation;
        _value = value;
        _decodeAttempted = value is not null;
        ContentType = NormalizeOptional(contentType);
        Encoding = NormalizeOptional(encoding);
    }

    [JsonIgnore]
    public ImmutableArray<byte> OriginalBytes { get; }

    public bool HasOriginalRepresentation { get; }

    public string? ContentType { get; }

    public string? Encoding { get; }

    public static FlowContent FromBytes(
        ReadOnlyMemory<byte> bytes,
        string? contentType = null,
        string? encoding = null)
        => new(
            ImmutableArray.CreateRange(bytes.ToArray()),
            hasOriginalRepresentation: true,
            value: null,
            contentType,
            encoding);

    public static FlowContent FromValue(
        FlowValue value,
        string? contentType = null,
        string? encoding = null)
        => new(
            ImmutableArray<byte>.Empty,
            hasOriginalRepresentation: false,
            value ?? throw new ArgumentNullException(nameof(value)),
            contentType,
            encoding);

    public FlowValue ReadAsFlowValue(FlowContentCodecCatalog codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);

        lock (_decodeGate)
        {
            if (_decodeAttempted)
            {
                _decodeFailure?.Throw();
                return _value!;
            }

            _decodeAttempted = true;
            try
            {
                _value = codecs.Decode(OriginalBytes, ContentType, Encoding);
                return _value;
            }
            catch (Exception exception)
            {
                _decodeFailure = ExceptionDispatchInfo.Capture(exception);
                throw;
            }
        }
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
