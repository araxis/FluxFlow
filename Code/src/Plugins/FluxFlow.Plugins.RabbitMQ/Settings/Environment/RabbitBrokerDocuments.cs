namespace FluxFlow.Plugins.RabbitMQ;

public static class RabbitBrokerDocuments
{
    // Compile-time schema: MQTT and AMQP can each ship the broker editor without loading the other plugin.
    public const int MaxPluginNameLength = 255; // Erlang atom limit; visual names are ASCII.
    public const string PluginNamePattern = "[a-z][a-zA-Z0-9_]*";
    public const string Labels = """{"vhosts":"Virtual hosts","auto_delete":"Auto-delete","vhost":"Virtual host","hashing_algorithm":"Hash algorithm"}""";
    public const string Definitions = """
        {
            "vhosts": {"name":"/"},
            "users": {"name":"","password_hash":"","hashing_algorithm":"rabbit_password_hashing_sha256","tags":""},
            "permissions": {"user":"","vhost":"/","configure":"","write":"","read":""},
            "exchanges": {"name":"","vhost":"/","type":"direct","durable":true,"auto_delete":false,"internal":false,"arguments":{}},
            "queues": {"name":"","vhost":"/","durable":true,"auto_delete":false,"arguments":{}},
            "bindings": {"source":"","vhost":"/","destination":"","destination_type":"queue","routing_key":"","arguments":{}}
        }
        """;
}
