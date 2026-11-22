using System.Security.Cryptography;
using System.Text;

using FluxFlow.Environments;

namespace FluxFlow.Plugins.Mqtt;

internal static class MosquittoAccess
{
    internal const string Config = "/mosquitto/config/mosquitto.conf";
    internal const string Passwords = "/mosquitto/config/passwords";
    internal const string Acl = "/mosquitto/config/acl";

    internal static void Validate(ResourceBinding binding)
    {
        if (binding.BrokerAccess.GetValueOrDefault("mosquitto") is not { Enabled: true } access) return;
        access.Validate();
        if (new[] { Config, Passwords, Acl }.Any(binding.Files.ContainsKey))
            throw new ArgumentException("User settings conflict with the Mosquitto configuration, password or ACL file override. Reset the conflicting override or remove the configured users.");
        foreach (var user in access.Users)
            foreach (var topic in Topics(user.Read).Concat(Topics(user.Write)))
            {
                var levels = topic.Split('/');
                if (topic.Any(char.IsControl) || levels.Where((level, index) =>
                    (level.Contains('#') && (level != "#" || index != levels.Length - 1)) ||
                    (level.Contains('+') && level != "+")).Any())
                    throw new ArgumentException("Use valid MQTT topic filters, one per line, for Mosquitto permissions.");
            }
    }

    internal static Dictionary<string, string> Files(BrokerAccess access)
    {
        var passwords = new StringBuilder();
        var acl = new StringBuilder();
        foreach (var user in access.Users)
        {
            var salt = RandomNumberGenerator.GetBytes(12);
            var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(user.Password), salt, 100000, HashAlgorithmName.SHA512, 64);
            passwords.Append(user.Name).Append(":$7$100000$").Append(Convert.ToBase64String(salt)).Append('$').AppendLine(Convert.ToBase64String(hash));
            acl.Append("user ").AppendLine(user.Name);
            foreach (var topic in Topics(user.Read)) acl.Append("topic read ").AppendLine(topic);
            foreach (var topic in Topics(user.Write)) acl.Append("topic write ").AppendLine(topic);
        }
        return new()
        {
            [Config] = $"listener 1883\nallow_anonymous false\npersistence false\npassword_file {Passwords}\nacl_file {Acl}\n",
            [Passwords] = passwords.ToString().Replace("\r\n", "\n"),
            [Acl] = acl.ToString().Replace("\r\n", "\n")
        };
    }
    private static IEnumerable<string> Topics(string text) => text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
