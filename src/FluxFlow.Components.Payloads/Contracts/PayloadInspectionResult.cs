namespace FluxFlow.Components.Payloads.Contracts;

public sealed record PayloadInspectionResult
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public PayloadKind Kind { get; init; }
    public string? ContentType { get; init; }
    public int ByteCount { get; init; }
    public string? DetectedEncoding { get; init; }
    public string? TextPreview { get; init; }
    public bool TextPreviewTruncated { get; init; }
    public string? FormattedPreview { get; init; }
    public bool FormattedPreviewTruncated { get; init; }
    public string? ParseError { get; init; }
    public int? Base64DecodedByteCount { get; init; }
}
