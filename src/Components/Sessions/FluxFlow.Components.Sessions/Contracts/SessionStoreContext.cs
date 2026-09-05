namespace FluxFlow.Components.Sessions.Contracts;

/// <summary>
/// The context an <see cref="ISessionStoreFactory"/> receives when the host opens
/// a session store for composed or direct node use.
/// </summary>
public sealed record SessionStoreContext
{
    private string? _storeName;
    private string? _sessionId;
    private TimeProvider _clock = TimeProvider.System;

    public string? StoreName
    {
        get => _storeName;
        init => _storeName = Normalize(value);
    }

    public string? SessionId
    {
        get => _sessionId;
        init => _sessionId = Normalize(value);
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
