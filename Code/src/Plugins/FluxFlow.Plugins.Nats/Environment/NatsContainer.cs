using FluxFlow.Environments;

namespace FluxFlow.Plugins.Nats;

public static class NatsContainer
{
    public static ContainerServiceDefinition Definition { get; } = new(NatsSetup.Provider, 4222, null, Validate)
    {
        Scheme = "nats", Prepare = Prepare, ReadyMessage = "Server is ready"
    };

    private static void Validate(ResourceBinding binding)
    {
        if (binding.Mappings != "[]" || binding.BrokerAccess.Count != 0)
            throw new ArgumentException("NATS permissions belong in nats.conf; MQTT access and HTTP mappings do not apply.");
        foreach (var key in new[] { "NATS_TOKEN", "NATS_USERNAME", "NATS_PASSWORD" })
        {
            if (binding.Variables.ContainsKey(key)) throw new ArgumentException("NATS credentials must use execution-host environment-variable references.");
            if (binding.Variables.TryGetValue(key + "_ENVIRONMENT_VARIABLE", out var reference) &&
                (string.IsNullOrWhiteSpace(reference) || reference.IndexOfAny(['=', '\0']) >= 0))
                throw new ArgumentException("Invalid NATS credential environment-variable reference.");
        }
        var user = binding.Variables.ContainsKey("NATS_USERNAME_ENVIRONMENT_VARIABLE");
        var password = binding.Variables.ContainsKey("NATS_PASSWORD_ENVIRONMENT_VARIABLE");
        if (user != password || user && binding.Variables.ContainsKey("NATS_TOKEN_ENVIRONMENT_VARIABLE"))
            throw new ArgumentException("Choose a token reference or both username and password references.");
    }

    private static ContainerStartup Prepare(ResourceBinding binding)
    {
        const string path = "/etc/nats/nats.conf";
        var startup = new ContainerStartup { Files = { [path] = NatsSetup.DefaultConfig(binding) }, Command = ["-c", path] };
        foreach (var key in new[] { "NATS_TOKEN", "NATS_USERNAME", "NATS_PASSWORD" }) startup.ResolveVariable(binding, key);
        return startup;
    }
}
