using System.Globalization;
using System.Text.RegularExpressions;

namespace FluxFlow.Plugins.Nats;

internal static class NatsConfigPreview
{
    private const string Reference = @"(\$[A-Za-z_][A-Za-z0-9_]*)";
    private static readonly Regex Port = Line(@"port[ \t]*:[ \t]*([1-9][0-9]{0,4})");
    private static readonly Regex Token = Line(@"authorization[ \t]+\{[ \t]*token[ \t]*:[ \t]*" + Reference + @"[ \t]*\}");
    private static readonly Regex Users = Line(@"authorization[ \t]+\{[ \t]*users[ \t]*:[ \t]*\[[ \t]*\{[ \t]*user[ \t]*:[ \t]*" +
        Reference + @"[ \t]*,[ \t]*password[ \t]*:[ \t]*" + Reference + @"[ \t]*\}[ \t]*\][ \t]*\}");
    private static readonly Regex JetStream = Line(@"jetstream(?:[ \t]*:[ \t]*(enabled|true|disabled|false)|[ \t]+\{[ \t]*store_dir[ \t]*:[ \t]*/[A-Za-z0-9_./-]+[ \t]*\})");
    private static Regex Line(string pattern) => new(@"\A" + pattern + @"\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static IReadOnlyList<string> Read(string source)
    {
        // Recognize whole settings, never infer security from part of a custom configuration.
        const string custom = "Custom configuration. See Advanced text for the full settings.";
        if (source.Contains('\0')) return [custom];
        using var reader = new StringReader(source);
        string? port = null, auth = null;
        bool? jetStream = null;
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim(' ', '\t');
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (port is null && Port.Match(line) is { Success: true } p &&
                ushort.TryParse(p.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                port = p.Groups[1].Value;
            else if (auth is null && Token.Match(line) is { Success: true } token)
                auth = $"Token ({token.Groups[1].Value})";
            else if (auth is null && Users.Match(line) is { Success: true } users)
                auth = $"Username/password ({users.Groups[1].Value}, {users.Groups[2].Value})";
            else if (jetStream is null && JetStream.Match(line) is { Success: true } js)
                jetStream = js.Groups[1].Value is not ("disabled" or "false");
            else return [custom];
        }

        return port is null ? [custom] :
            [$"Listener port: {port}", $"Authentication: {auth ?? "None"}", $"JetStream: {(jetStream == true ? "Enabled" : "Disabled")}"];
    }
}
