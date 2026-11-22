using FluxFlow.Environments;

namespace FluxFlow.Plugins.Nats;

public static class NatsSetup
{
    public static string DefaultConfig(ResourceBinding binding) => "port: 4222\n" +
        (binding.Variables.ContainsKey("NATS_TOKEN_ENVIRONMENT_VARIABLE") ? "authorization { token: $NATS_TOKEN }\n" :
        binding.Variables.ContainsKey("NATS_USERNAME_ENVIRONMENT_VARIABLE") ? "authorization { users: [{ user: $NATS_USERNAME, password: $NATS_PASSWORD }] }\n" : "");
    public static EnvironmentProviderDescription Provider { get; } = new("nats", "nats.connection", "NATS", "nats:latest")
    {
        AccessHelp = "Use host-owned credential references. Choose a token or a username/password pair. Empty references create a disposable broker without authentication, bound to loopback.",
        AccessTitle = "NATS access",
        Variables =
        [
            new("NATS_USERNAME_ENVIRONMENT_VARIABLE", "Username variable", "Username environment variable"),
            new("NATS_PASSWORD_ENVIRONMENT_VARIABLE", "Password variable", "Password environment variable"),
            new("NATS_TOKEN_ENVIRONMENT_VARIABLE", "Token variable", "Token environment variable"),
        ],
        StartupTitle = "NATS startup configuration",
        StartupFiles =
        [
            new("NATS configuration", "/etc/nats/nats.conf", "Keep the plaintext listener on 4222. Use NATS configuration syntax for subject permissions and accounts. Enable JetStream explicitly with jetstream { store_dir: /tmp/jetstream }. Managed container storage is temporary and removed on Stop. Reference $NATS_USERNAME, $NATS_PASSWORD or $NATS_TOKEN; do not put secrets in project files. Custom TLS listeners belong in an existing-service connection.", DefaultConfig, ServiceDocumentKind.TextPreview)
                { Preview = new("Server settings", NatsConfigPreview.Read) },
        ]
    };
}
