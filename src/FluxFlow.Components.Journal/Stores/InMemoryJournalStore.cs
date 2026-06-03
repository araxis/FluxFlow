using FluxFlow.Components.Journal.Contracts;

namespace FluxFlow.Components.Journal.Stores;

public sealed class InMemoryJournalStore : IJournalStore
{
    private readonly object gate = new();
    private readonly List<JournalEntry> entries = [];
    private long nextPosition;

    public ValueTask<JournalAppendResult> AppendAsync(
        JournalRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = NormalizeRecord(record);
        lock (gate)
        {
            if (entries.Any(entry => StringComparer.Ordinal.Equals(entry.Record.Id, normalized.Id)))
            {
                throw new InvalidOperationException($"Journal record '{normalized.Id}' already exists.");
            }

            var position = nextPosition++;
            entries.Add(new JournalEntry(position, normalized));
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

        ValidateQuery(query);
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
                entries.RemoveAll(entry => entry.Record.Timestamp < cutoff.Value);
            }

            if (options.MaxRecords.HasValue && entries.Count > options.MaxRecords.Value)
            {
                var excess = entries.Count - options.MaxRecords.Value;
                entries.RemoveRange(0, excess);
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
        => string.IsNullOrWhiteSpace(value) ? null : value;

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

            normalized[key] = value;
        }

        return normalized;
    }

    private static void ValidateQuery(JournalQuery query)
    {
        if (query.Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Journal query offset cannot be negative.");
        }

        if (query.Limit is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Journal query limit must be positive.");
        }
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
