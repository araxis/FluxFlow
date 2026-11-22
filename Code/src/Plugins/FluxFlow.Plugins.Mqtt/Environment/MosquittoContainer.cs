using FluxFlow.Environments;

namespace FluxFlow.Plugins.Mqtt;

public static class MosquittoContainer
{
    public static ContainerServiceDefinition Definition { get; } = new(MosquittoSetup.Provider, 1883, null, Validate)
    {
        Scheme = "mqtt", Prepare = Prepare
    };

    private static void Validate(ResourceBinding binding)
    {
        MosquittoAccess.Validate(binding);
        if (binding.Mappings != "[]") throw new ArgumentException("MQTT does not accept HTTP mappings.");
    }

    private static ContainerStartup Prepare(ResourceBinding binding)
    {
        return new() { Files = binding.BrokerAccess.GetValueOrDefault(Definition.Provider.Name) is { Enabled: true } access
            ? MosquittoAccess.Files(access) : new() { [MosquittoAccess.Config] = MosquittoSetup.Config } };
    }
}
