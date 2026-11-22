using FluxFlow.Environments;

namespace FluxFlow.Plugins.Mqtt;

public static class MosquittoSetup
{
    public const string Config = "listener 1883\nallow_anonymous true\npersistence false\n";
    public static EnvironmentProviderDescription Provider { get; } = new("mosquitto", "mqtt.client", "Mosquitto", "eclipse-mosquitto:2.0.22")
    {
        Access = new("Subscribe topics", "Publish topics", "MQTT topic filters, one per line. Use sensors/# for a topic tree or + for one level. Empty fields deny access. Configured users disable anonymous access.", 2, "The users above are written to mosquitto.conf, the hashed password file and ACL when the container starts.") { Edits = MqttAccessEdits.Instance },
        StartupTitle = "Mosquitto startup files",
        StartupFiles =
        [
            new("Mosquitto configuration", "/mosquitto/config/mosquitto.conf", "Keep the MQTT listener on port 1883. Reference password and ACL files by their container paths.", _ => Config, ServiceDocumentKind.DirectiveLines, GeneratedWithUsers: true),
            new("Password file", "/mosquitto/config/passwords", "Import a hashed password file created with mosquitto_passwd. Set password_file /mosquitto/config/passwords in the configuration.", _ => "", ServiceDocumentKind.TextPreview, GeneratedWithUsers: true) { Preview = new("Usernames", ReadUsernames) },
            new("Access control list", "/mosquitto/config/acl", "Use user and topic read/write directives. Set acl_file /mosquitto/config/acl in the configuration.", _ => "", ServiceDocumentKind.DirectiveLines, GeneratedWithUsers: true) { Directives = MosquittoAcl.Schema },
        ]
    };

    private static IReadOnlyList<string> ReadUsernames(string source)
    {
        var usernames = new List<string>();
        using var lines = new StringReader(source);
        var first = true;
        while (lines.ReadLine() is { } line)
        {
            var record = line.AsSpan();
            if (first && record.StartsWith("\uFEFF", StringComparison.Ordinal)) record = record[1..];
            first = false;
            if (record.IsEmpty || record[0] == '#') continue;
            var colon = record.IndexOf(':');
            if (colon <= 0 || record[(colon + 1)..].Trim().IsEmpty) continue;
            // The broker trims ASCII whitespace from the UTF-8 field, not Unicode space characters.
            var username = record[..colon].Trim(" \t\v\f");
            if (!username.IsEmpty) usernames.Add(username.ToString());
        }
        return usernames;
    }
}
