using System.Globalization;
using System.Text.Json;

namespace FluxFlow.ReportingSample;

/// <summary>
/// Consumer-owned, single-process sample archive. Atomic per-event files make replay idempotent.
/// This is not a recommended storage engine for high-volume production deployments.
/// </summary>
public sealed class ReportArchive : IReportArchive
{
    private const int MaximumRecordBytes = 1024 * 1024;
    private readonly string _directory;

    public ReportArchive(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
    }

    public async Task<bool> AppendAsync(ApplicationReportEvent record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Cursor.StreamId == Guid.Empty || record.Cursor.Sequence <= 0)
            throw new ArgumentException("An archived event requires a valid positive cursor.");
        var directory = StreamDirectory(record.Cursor.StreamId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, record.Cursor.Sequence.ToString("D20", CultureInfo.InvariantCulture) + ".json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
        if (bytes.Length > MaximumRecordBytes) throw new ArgumentException("Sample archive record exceeds one MiB.");
        if (File.Exists(path)) return await CheckDuplicateAsync(path, bytes, cancellationToken);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pending");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path)) { return await CheckDuplicateAsync(path, bytes, cancellationToken); }
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Written only after all received events have been durably acknowledged by AppendAsync.</summary>
    public async Task SaveCaptureAsync(ReportCapture capture, CancellationToken cancellationToken = default)
    {
        Validate(capture);
        var path = Path.Combine(_directory, capture.Id.ToString("N") + ".capture.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(capture);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            // Captures are immutable. A changed cutoff or loss status is a new capture.
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<ReportCapture> ReadCaptureAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBoundedAsync(Path.Combine(_directory, id.ToString("N") + ".capture.json"), cancellationToken);
        var capture = JsonSerializer.Deserialize<ReportCapture>(bytes) ?? throw new InvalidDataException("Missing capture metadata.");
        Validate(capture);
        if (capture.Id != id) throw new InvalidDataException("Capture identity does not match the requested file.");
        return capture;
    }

    /// <summary>
    /// Streams a frozen sequence interval; late arrivals beyond the cutoff cannot change this report.
    /// Event-time filtering uses [FromInclusive, ToExclusive). The capture is unfiltered; reports can be re-filtered.
    /// </summary>
    public Task<HistoricalReport> QueryAsync(Guid captureId, ApplicationReportFilter filter,
        string filterVersion, CancellationToken cancellationToken = default)
        => QueryCoreAsync(captureId, null, filter, filterVersion, cancellationToken);

    public Task<HistoricalReport> QueryAsync(Guid captureId, IApplicationReport report, ApplicationReportFilter filter,
        string filterVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        return QueryCoreAsync(captureId, report, filter, filterVersion, cancellationToken);
    }

    private async Task<HistoricalReport> QueryCoreAsync(Guid captureId, IApplicationReport? report,
        ApplicationReportFilter filter, string filterVersion, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterVersion);
        var compiled = filter.Compile();
        // Freeze the portable definition too; caller mutations cannot change report provenance.
        var serializedFilter = JsonSerializer.Serialize(filter);
        var capture = await ReadCaptureAsync(captureId, cancellationToken);
        var groups = new SortedDictionary<string, long>(StringComparer.Ordinal);
        long scanned = 0, matched = 0;
        var directory = StreamDirectory(capture.Start.StreamId);
        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!long.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) ||
                    sequence <= capture.Start.Sequence || sequence > capture.End.Sequence) continue;
                var record = JsonSerializer.Deserialize<ApplicationReportEvent>(await ReadBoundedAsync(path, cancellationToken))
                    ?? throw new InvalidDataException("Missing archived event.");
                if (record.Cursor.StreamId != capture.Start.StreamId || record.Cursor.Sequence != sequence)
                    throw new InvalidDataException("Record identity does not match its archive location.");
                scanned++;
                if (report is not null && !report.Includes(record) || !compiled.IsMatch(record)) continue;
                var type = record.Message.Headers.GetValueOrDefault("flow.event.type") ?? "unknown";
                if (!groups.ContainsKey(type) && groups.Count >= 256)
                    throw new InvalidOperationException("Report group limit exceeded; narrow the filter.");
                groups[type] = groups.GetValueOrDefault(type) + 1;
                matched++;
            }
        }
        var complete = capture.SourceCompletenessKnown && capture.Dropped == 0 && capture.RejectedEvents == 0 &&
            scanned == capture.End.Sequence - capture.Start.Sequence;
        return new HistoricalReport
        {
            Capture = capture, GeneratedAt = DateTimeOffset.UtcNow, FilterVersion = filterVersion,
            FilterJson = serializedFilter, Count = matched, CountsByEventType = groups,
            Completeness = complete ? "CompleteWithinReportingBoundary" : "Incomplete",
            AggregationVersion = "event-count.v1",
            ReportType = report?.Descriptor.Type, ReportCategory = report?.Descriptor.Category,
            ReportVersion = report?.Descriptor.Version
        };
    }

    private string StreamDirectory(Guid streamId) => Path.Combine(_directory, streamId.ToString("N"));

    private static void Validate(ReportCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Id == Guid.Empty || capture.Start.StreamId == Guid.Empty || capture.Start.StreamId != capture.End.StreamId ||
            capture.Start.Sequence < 0 || capture.End.Sequence < capture.Start.Sequence || capture.Dropped < 0 || capture.RejectedEvents < 0)
            throw new ArgumentException("Invalid capture scope or loss counts.");
    }

    private static async Task<bool> CheckDuplicateAsync(string path, byte[] bytes, CancellationToken token)
    {
        if (!(await ReadBoundedAsync(path, token)).AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("The same report cursor was supplied with different content.");
        return false;
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumRecordBytes) throw new InvalidDataException("Archive record exceeds the size limit.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token);
        return bytes;
    }
}

public sealed record ReportCapture
{
    /// <summary>False for live observational sources: local contiguous storage cannot prove upstream completeness.</summary>
    public bool SourceCompletenessKnown { get; init; }
    public required Guid Id { get; init; }
    public required ApplicationReportCursor Start { get; init; }
    public required ApplicationReportCursor End { get; init; }
    public required long Dropped { get; init; }
    public required long RejectedEvents { get; init; }
}

public sealed record HistoricalReport
{
    public string? ReportType { get; init; }
    public string? ReportCategory { get; init; }
    public int? ReportVersion { get; init; }
    public required ReportCapture Capture { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string FilterVersion { get; init; }
    public required string FilterJson { get; init; }
    public required string AggregationVersion { get; init; }
    public required string Completeness { get; init; }
    public required long Count { get; init; }
    public required IReadOnlyDictionary<string, long> CountsByEventType { get; init; }
}
