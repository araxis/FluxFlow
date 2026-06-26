namespace FluxFlow.Components.Journal.Contracts;

/// <summary>
/// The context an <see cref="IJournalStoreFactory"/> receives when the host opens
/// a journal store for direct or adapter-owned use.
/// </summary>
public sealed record JournalStoreContext
{
    private string? _storeName;
    private TimeProvider _clock = TimeProvider.System;

    public string? StoreName
    {
        get => _storeName;
        init => _storeName = Normalize(value);
    }

    public TimeProvider Clock
    {
        get => _clock;
        init => _clock = value ?? TimeProvider.System;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
