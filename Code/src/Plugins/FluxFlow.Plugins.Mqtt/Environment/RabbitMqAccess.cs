using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using FluxFlow.Environments;

namespace FluxFlow.Plugins.Mqtt;

internal static class RabbitMqAccess
{
    internal const string Config = "/etc/rabbitmq/rabbitmq.conf";
    internal const string Definitions = "/etc/rabbitmq/definitions.json";
    internal static void Validate(ResourceBinding binding)
    {
        if (binding.BrokerAccess.GetValueOrDefault("rabbitmq-mqtt") is not { Enabled: true } access) return;
        access.Validate();
        if (binding.Files.ContainsKey(Config) || binding.Files.ContainsKey(Definitions))
            throw new ArgumentException("User settings conflict with the RabbitMQ configuration or definitions file override. Reset the conflicting override or remove the configured users.");
        if (access.Users.Any(user => user.Read.Contains('\n') || user.Write.Contains('\n')))
            throw new ArgumentException("RabbitMQ topic permissions use one regular expression per field.");
    }

    internal static Dictionary<string, string> Files(BrokerAccess access)
    {
        var users = new JsonArray();
        var permissions = new JsonArray();
        var topics = new JsonArray();
        foreach (var user in access.Users)
        {
            var salt = RandomNumberGenerator.GetBytes(4);
            var hash = SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(user.Password)]);
            users.Add(new JsonObject { ["name"] = user.Name, ["password_hash"] = Convert.ToBase64String([.. salt, .. hash]),
                ["hashing_algorithm"] = "rabbit_password_hashing_sha256", ["tags"] = new JsonArray() });
            permissions.Add(new JsonObject { ["user"] = user.Name, ["vhost"] = "/",
                ["configure"] = "^mqtt-subscription-.*", ["read"] = "^(amq\\.topic|mqtt-subscription-.*)$", ["write"] = "^(amq\\.topic|mqtt-subscription-.*)$" });
            topics.Add(new JsonObject { ["user"] = user.Name, ["vhost"] = "/", ["exchange"] = "amq.topic",
                ["read"] = string.IsNullOrWhiteSpace(user.Read) ? "(?!)" : user.Read,
                ["write"] = string.IsNullOrWhiteSpace(user.Write) ? "(?!)" : user.Write });
        }
        var definitions = new JsonObject { ["vhosts"] = new JsonArray(new JsonObject { ["name"] = "/" }),
            ["users"] = users, ["permissions"] = permissions, ["topic_permissions"] = topics };
        return new()
        {
            [Config] = $"mqtt.listeners.tcp.default = 1883\nmqtt.allow_anonymous = false\nmqtt.vhost = /\nmqtt.exchange = amq.topic\ndefinitions.import_backend = local_filesystem\ndefinitions.local.path = {Definitions}\n",
            [Definitions] = definitions.ToJsonString()
        };
    }
}
