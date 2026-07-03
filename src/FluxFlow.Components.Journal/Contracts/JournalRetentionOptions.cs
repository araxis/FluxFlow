namespace FluxFlow.Components.Journal.Contracts;

public sealed record JournalRetentionOptions
{
    private TimeSpan? _maxAge;
    private int? _maxRecords;

    public DateTimeOffset? DeleteBefore { get; init; }
    public TimeSpan? MaxAge
    {
        get => _maxAge;
        init => _maxAge = ValidateMaxAge(value);
    }

    public DateTimeOffset? ReferenceTime { get; init; }
    public int? MaxRecords
    {
        get => _maxRecords;
        init => _maxRecords = ValidateMaxRecords(value);
    }

    private static TimeSpan? ValidateMaxAge(TimeSpan? value)
    {
        if (value.HasValue && value.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Maximum journal age must be positive.");
        }

        return value;
    }

    private static int? ValidateMaxRecords(int? value)
    {
        if (value is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Maximum journal records cannot be negative.");
        }

        return value;
    }
}
