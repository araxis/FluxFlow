using System.Text;
using DotNet.Testcontainers.Builders;

namespace FluxFlow.Environments.Testcontainers;

public sealed class ConfiguredContainerProvider(ContainerServiceDefinition definition) : ContainerProvider
{
    public override string Name => definition.Provider.Name;
    public override string ResourceType => definition.Provider.ResourceType;
    protected override string DefaultImage => definition.Provider.Image;
    protected override int Port => definition.Port;

    protected override string Scheme => definition.Scheme;

    public override void Validate(ResourceBinding binding)
    {
        base.Validate(binding);
        definition.Validate(binding);
    }

    protected override ContainerBuilder Configure(ContainerBuilder builder, ResourceBinding binding)
    {
        if (definition.Prepare?.Invoke(binding) is { } startup)
        {
            foreach (var (path, content) in startup.Files)
                if (!binding.Files.ContainsKey(path)) builder = builder.WithResourceMapping(Encoding.UTF8.GetBytes(content), path);
            foreach (var (name, value) in startup.Variables) builder = builder.WithEnvironment(name, value);
            if (startup.Command.Length > 0) builder = builder.WithCommand(startup.Command);
        }
        if (definition.HttpReadinessPath is { } pathToCheck)
            return builder.WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
                request.ForPath(pathToCheck).ForPort(definition.Port)));
        if (definition.ReadyMessage is { } message)
            return builder.WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged(message).UntilInternalTcpPortIsAvailable(Port));
        return builder;
    }

    protected override Task InitializeAsync(Uri endpoint, ResourceBinding binding, CancellationToken cancellationToken) =>
        definition.Initialize?.Invoke(endpoint, binding, cancellationToken) ?? Task.CompletedTask;
}
