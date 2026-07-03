using FluxFlow.Components.Journal.Contracts;

namespace FluxFlow.Components.Journal.Stores;

public sealed class InMemoryJournalStore : IJournalStore
{
    private readonly object gate = new();
    private readonly List<JournalEntry> entries = [];
    private readonly HashSet<string> ids = new(StringComparer.Ordinal);
    private readonly JournalRetentionOptions? retention;
    private long nextPosition;

    public InMemoryJournalStore()
        : this(retention: null)
    {
    }

    public InMemoryJournalStore(JournalRetentionOptions? retention)
    {
        if (retention?.MaxRecords is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retention),
                "Maximum journal records cannot be negative.");
        }

        this.retention = retention;
    }

    public ValueTask<JournalAppendResult> AppendAsync(
        JournalRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = NormalizeRecord(record);
        lock (gate)
        {
            if (!ids.Add(normalized.Id))
            {
                throw new InvalidOperationException($"Journal record '{normalized.Id}' already exists.");
            }

            var position = nextPosition++;
            entries.Add(new JournalEntry(position, normalized));
            ApplyAppendRetention();
            return ValueTask.FromResult(new JournalAppendResult
            {
                Record = normalized,
                Position = position
            });
        }
    }

    public ValueTask<JournalQueryResult> QueryAsync(
        JournalQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        JournalQueryMatcher.Validate(query);
        List<JournalEntry> snapshot;
        lock (gate)
        {
            snapshot = [.. entries];
        }

        var matched = snapshot
            .Where(entry => JournalQueryMatcher.IsMatch(entry.Record, query))
            .OrderBy(entry => entry.Position)
            .Select(entry => entry.Record)
            .ToList();

        var totalMatched = matched.Count;
        var records = matched
            .Skip(query.Offset)
            .Take(query.Limit ?? int.MaxValue)
            .ToList();

        return ValueTask.FromResult(new JournalQueryResult
        {
            Records = records,
            TotalMatched = totalMatched,
            Offset = query.Offset,
            Limit = query.Limit,
            HasMore = query.Offset + records.Count < totalMatched
        });
    }

    public ValueTask<JournalPruneResult> PruneAsync(
        JournalRetentionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var cutoff = ValidateRetention(options);
        lock (gate)
        {
            var before = entries.Count;
            if (cutoff.HasValue)
            {
                entries.RemoveAll(entry =>
                {
                    if (entry.Record.Timestamp >= cutoff.Value)
                    {
                        return false;
                    }

                    ids.Remove(entry.Record.Id);
                    return true;
                });
            }

            if (options.MaxRecords.HasValue && entries.Count > options.MaxRecords.Value)
            {
                RemoveOldest(entries.Count - options.MaxRecords.Value);
            }

            return ValueTask.FromResult(new JournalPruneResult
            {
                Removed = before - entries.Count,
                Remaining = entries.Count,
                DeleteBefore = cutoff,
                MaxRecords = options.MaxRecords
            });
        }
    }

    private void ApplyAppendRetention()
    {
        if (retention?.MaxRecords is not { } maxRecords || entries.Count <= maxRecords)
        {
            return;
        }

        RemoveOldest(entries.Count - maxRecords);
    }

    private void RemoveOldest(int count)
    {
        for (var index = 0; index < count; index++)
        {
            ids.Remove(entries[index].Record.Id);
        }

        entries.RemoveRange(0, count);
    }

    private static JournalRecord NormalizeRecord(JournalRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Id))
        {
            throw new ArgumentException("Journal record id is required.", nameof(record));
        }

        if (record.Timestamp == default)
        {
            throw new ArgumentException("Journal record timestamp is required.", nameof(record));
        }

        return record with
        {
            Id = record.Id.Trim(),
            Type = NormalizeOptional(record.Type),
            Status = NormalizeOptional(record.Status),
            Source = NormalizeOptional(record.Source),
            WorkflowId = NormalizeOptional(record.WorkflowId),
            WorkflowName = NormalizeOptional(record.WorkflowName),
            NodeId = NormalizeOptional(record.NodeId),
            ComponentId = NormalizeOptional(record.ComponentId),
            Subject = NormalizeOptional(record.Subject),
            Channel = NormalizeOptional(record.Channel),
            Severity = NormalizeOptional(record.Severity),
            Level = NormalizeOptional(record.Level),
            Summary = NormalizeOptional(record.Summary),
            PayloadPreview = NormalizeOptional(record.PayloadPreview),
            Attributes = NormalizeAttributes(record.Attributes)
        };
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyDictionary<string, string> NormalizeAttributes(
        IReadOnlyDictionary<string, string>? attributes)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (attributes is null)
        {
            return normalized;
        }

        foreach (var (key, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Journal record attribute keys are required.");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Journal record attribute values are required.");
            }

            var normalizedKey = key.Trim();
            if (!normalized.TryAdd(normalizedKey, value.Trim()))
            {
                throw new ArgumentException(
                    $"Journal record attribute '{normalizedKey}' is declared more than once.");
            }
        }

        return normalized;
    }

    private static DateTimeOffset? ValidateRetention(JournalRetentionOptions options)
    {
        if (options.MaxRecords is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum journal records cannot be negative.");
        }

        if (options.MaxAge.HasValue && options.MaxAge.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum journal age must be positive.");
        }

        if (options.MaxAge.HasValue && !options.ReferenceTime.HasValue)
        {
            throw new ArgumentException(
                "Reference time is required when maximum journal age is set.",
                nameof(options));
        }

        var cutoff = options.DeleteBefore;
        if (options.MaxAge.HasValue)
        {
            var ageCutoff = options.ReferenceTime!.Value - options.MaxAge.Value;
            cutoff = cutoff.HasValue && cutoff.Value > ageCutoff ? cutoff.Value : ageCutoff;
        }

        return cutoff;
    }

    private sealed record JournalEntry(long Position, JournalRecord Record);
}
