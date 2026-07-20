using System.Collections.ObjectModel;
using FluxFlow.Data;

namespace FluxFlow.Components.Sessions.Contracts;

internal static class SessionContentContractMap
{
    public static IReadOnlyDictionary<string, string> CopyAttributes(
        IReadOnlyDictionary<string, string>? source)
    {
        var copy = source is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(source, StringComparer.Ordinal);
        return new ReadOnlyDictionary<string, string>(copy);
    }

    public static FlowContent CopyContent(FlowContent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.HasOriginalRepresentation)
        {
            throw new ArgumentException(
                "Session content requires an original byte representation.",
                nameof(source));
        }

        return FlowContent.FromBytes(
            source.OriginalBytes.AsSpan().ToArray(),
            source.ContentType,
            source.Encoding);
    }

    public static SessionContentRecord CopyRecord(SessionContentRecord source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source with
        {
            Content = CopyContent(source.Content),
            Attributes = CopyAttributes(source.Attributes)
        };
    }

    public static IReadOnlyList<SessionMetadata> CopySessions(
        IReadOnlyList<SessionMetadata>? source)
        => source is null || source.Count == 0
            ? Array.Empty<SessionMetadata>()
            : Array.AsReadOnly(source
                .Select(session => session with
                {
                    Tags = session.Tags is null
                        ? []
                        : new Dictionary<string, string>(session.Tags, StringComparer.Ordinal)
                })
                .ToArray());

    public static string? NormalizeOptional(string? value)
        => SessionContractNormalization.NormalizeOptional(value);
}
