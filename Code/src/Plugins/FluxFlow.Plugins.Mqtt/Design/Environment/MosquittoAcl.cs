using FluxFlow.Components.Mqtt;
using FluxFlow.Environments;

namespace FluxFlow.Plugins.Mqtt;

internal static class MosquittoAcl
{
    private static readonly string[] Kinds = ["user", "topic", "pattern"];
    private static readonly string[] Permissions = ["read", "write", "readwrite", "deny"];

    public static ServiceDirectiveSchema Schema { get; } = new("Rules",
        "Topic rules apply to the preceding user, or to anonymous clients before the first user. Pattern rules apply to all users.",
        () => new() { Name = "topic", Value = "read " }, Fields, Validate);

    private static IReadOnlyList<DocumentField> Fields(DocumentPair row)
    {
        var kind = new DocumentChoice("Rule", () => row.Name, value =>
        {
            if (row.Name == value) return;
            if (row.Name == "user" || value == "user") row.Value = value == "user" ? "" : "read ";
            row.Name = value;
        }, Kinds);
        if (row.Name == "user")
            return [kind, new DocumentText("Username", () => row.Value, value => row.Value = value ?? "")];

        return [kind, new DocumentChoice("Access", () => Split(row.Value).Access ?? "readwrite", value =>
        {
            var (access, gap, filter) = Split(row.Value);
            if ((access ?? "readwrite") != value) SetFilter(row, value, gap, filter);
        }, Permissions), new DocumentText("Topic filter", () => Split(row.Value).Filter, value =>
        {
            var (access, gap, _) = Split(row.Value);
            SetFilter(row, access, gap, value ?? "");
        })];
    }

    private static void SetFilter(DocumentPair row, string? access, string gap, string filter)
    {
        // Keep the file's implicit read/write spelling whenever the filter fits in one token.
        var implicitAccess = access is null || (access == "readwrite" && row.OriginalValue is { } original && !original.Contains(' '));
        row.Value = implicitAccess && !filter.Contains(' ') ? filter
            : (access ?? "readwrite") + (gap.Length == 0 ? " " : gap) + filter;
    }

    // A lone token is a topic, even when it is literally "read" or "deny".
    private static (string? Access, string Gap, string Filter) Split(string arguments)
    {
        var space = arguments.IndexOf(' ');
        if (space < 0) return (null, "", arguments);
        var start = space + 1;
        while (start < arguments.Length && arguments[start] is ' ' or '\t') start++;
        return (arguments[..space], arguments[space..start], arguments[start..]);
    }

    private static void Validate(DocumentPair row)
    {
        if (!Kinds.Contains(row.Name)) throw new ArgumentException("Unknown ACL rule. Use Advanced text to edit this file.");
        if (row.Name == "user")
        {
            if (string.IsNullOrWhiteSpace(row.Value) || row.Value != row.Value.Trim(' ', '\t'))
                throw new ArgumentException("Enter a username without leading or trailing spaces.");
            return;
        }

        var (access, _, filter) = Split(row.Value);
        if (access is not null && !Permissions.Contains(access)) throw new ArgumentException("Unknown ACL permission. Use Advanced text to edit this file.");
        if (string.IsNullOrWhiteSpace(filter) || filter != filter.Trim(' ', '\t')) throw new ArgumentException("Enter a topic filter without leading or trailing spaces.");
        new MqttTopic { Topic = filter }.Validate();
        if (row.Name == "pattern" && filter.Split('/').Any(level =>
            (level.Contains("%c") || level.Contains("%u")) && level is not ("%c" or "%u")))
            throw new ArgumentException("Use %c or %u as a whole topic level.");
    }
}
