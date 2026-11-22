namespace FluxFlow.Environments;

/// <summary>An installed service can request container provisioning from a host that supports it.</summary>
public sealed record ContainerServiceDefinition(EnvironmentProviderDescription Provider, ushort Port,
    string? HttpReadinessPath, Action<ResourceBinding> Validate,
    Func<Uri, ResourceBinding, CancellationToken, Task>? Initialize = null)
{
    public string Scheme { get; init; } = "http";
    public string? ReadyMessage { get; init; }
    public Func<ResourceBinding, ContainerStartup>? Prepare { get; init; }
}

public sealed class ContainerStartup
{
    public Dictionary<string, string> Files { get; init; } = [];
    public Dictionary<string, string> Variables { get; } = [];
    public string[] Command { get; init; } = [];

    public void ResolveVariable(ResourceBinding binding, string name)
    {
        if (binding.Variables.GetValueOrDefault(name + "_ENVIRONMENT_VARIABLE") is { } reference)
            Variables[name] = Environment.GetEnvironmentVariable(reference)
                ?? throw new InvalidOperationException($"Credential environment variable '{reference}' is unavailable on this execution host.");
    }
}
