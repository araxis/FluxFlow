namespace FluxFlow.Components.Sessions.Contracts;

internal static class SessionContractNormalization
{
    public static string NormalizeRequired(string? value)
        => NormalizeOptional(value) ?? string.Empty;

    public static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public static Dictionary<string, string> CopyMap(Dictionary<string, string>? source)
        => source is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

    public static SessionMetadata? CopySession(SessionMetadata? session)
        => session is null
            ? null
            : session with
            {
                Tags = CopyMap(session.Tags)
            };

    public static SessionRecordInput? CopyInput(SessionRecordInput? input)
        => input is null
            ? null
            : input with
            {
                Attributes = CopyMap(input.Attributes)
            };

    public static IReadOnlyList<SessionMetadata> CopySessions(
        IEnumerable<SessionMetadata>? sessions)
        => sessions is null
            ? []
            : sessions
                .Select(session => CopySession(session)!)
                .ToArray();
}
