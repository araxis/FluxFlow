using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace FluxFlow.Environments;

public sealed record EnvironmentProfile
{
    public Dictionary<string, ResourceBinding> Resources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int StartupTimeoutSeconds { get; set; } = 60;

    public static string? StartupTimeoutError(int seconds) =>
        seconds is < 1 or > 600 ? "Startup timeout must be between 1 and 600 seconds." : null;

    public bool TrySetStartupTimeout(int seconds)
    {
        if (seconds == StartupTimeoutSeconds || StartupTimeoutError(seconds) is not null) return false;
        StartupTimeoutSeconds = seconds;
        return true;
    }

    public void Validate()
    {
        if (Resources is null || Resources.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null ||
            string.IsNullOrWhiteSpace(pair.Value.Provider) || pair.Value.Files is null || pair.Value.Variables is null || pair.Value.Mappings is null ||
            pair.Value.BrokerAccess is null || pair.Value.BrokerAccess.Values.Any(access => access is null || access.Users is null ||
                access.Users.Any(user => user is null || user.Name is null || user.Password is null || user.Read is null || user.Write is null))))
            throw new ArgumentException("Environment resources and their setup must be non-null objects.");
        if (Resources.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Resources.Count)
            throw new ArgumentException("Environment resource paths must be unique.");
        if (StartupTimeoutError(StartupTimeoutSeconds) is { } error) throw new ArgumentException(error);
        foreach (var binding in Resources.Values) binding.ValidateSnapshot();
    }
    public EnvironmentProfile Copy()
    {
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(this, EnvironmentJsonContext.Default.EnvironmentProfile), EnvironmentJsonContext.Default.EnvironmentProfile)!;
        copy.Resources = new(copy.Resources, StringComparer.OrdinalIgnoreCase);
        return copy;
    }
}

public record ResourceSetup
{
    public string Provider { get; set; } = "existing";
    // Fully qualified image names allow public registries, private registries and mirrors.
    public string? Image { get; set; }
    public bool PullIfMissing { get; set; } = true;
    public string Mappings { get; set; } = "[]";
    public Dictionary<string, BrokerAccess> BrokerAccess { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.Ordinal);

    public ResourceSetup CopySetup() => JsonSerializer.Deserialize(
        JsonSerializer.Serialize(this, EnvironmentJsonContext.Default.ResourceSetup), EnvironmentJsonContext.Default.ResourceSetup)!;
}

public sealed record ResourceBinding : ResourceSetup
{
    public ResourceProfileSnapshot? Profile { get; init; }

    public void ValidateSnapshot()
    {
        if (Profile is not { } profile) return;
        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name) || profile.Settings is null)
            throw new ArgumentException("A profile snapshot requires its identity and settings.");
        if (profile.SetupLocked && !JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(this, EnvironmentJsonContext.Default.ResourceSetup),
            JsonSerializer.SerializeToNode(profile.Settings, EnvironmentJsonContext.Default.ResourceSetup)))
            throw new ArgumentException($"Profile '{profile.Name}' has locked setup. Select another profile to change the service setup.");
    }
}

public sealed record ResourceProfileSnapshot(string Id, string Name, bool SetupLocked, ResourceSetup Settings);

public sealed record EnvironmentRequirement(string Path, string Type, string? Endpoint);
public sealed record PreparedService(string Resource, string Provider, string Endpoint, string? ContainerId);
public sealed record EnvironmentRunRequest(string Configuration, EnvironmentProfile Environment)
{
    public IReadOnlyList<ApplicationInstance> Instances { get; init; } = [];
    public void ValidateInstances()
    {
        if (Instances is null || Instances.Count > 32 || Instances.Any(instance => instance is null ||
            string.IsNullOrWhiteSpace(instance.Id) || string.IsNullOrWhiteSpace(instance.Name) || instance.Bindings is null ||
            instance.Bindings.Any(binding => string.IsNullOrWhiteSpace(binding.Key) || string.IsNullOrWhiteSpace(binding.Value)) ||
            instance.ExistingServices is null || instance.ExistingServices.Any(binding => string.IsNullOrWhiteSpace(binding.Key) || binding.Value is null)) ||
            Instances.Select(instance => instance.Id).Distinct(StringComparer.Ordinal).Count() != Instances.Count)
            throw new ArgumentException("Choose up to 32 application instances with unique identities and nonempty names.");
    }
}
public sealed record ApplicationInstance(string Id, string Name)
{
    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, InfrastructureService> ExistingServices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public sealed record EnvironmentRunStatus(Guid Id, string State, IReadOnlyList<PreparedService> Services, string? Error = null,
    [property: JsonIgnore] ExecutionLogSnapshot? Logs = null, [property: JsonIgnore] ExecutionLogSnapshot? Diagnostics = null, ExecutionOutputSnapshot? Outputs = null,
    [property: JsonIgnore] ExecutionTraceSnapshot? Traces = null);

public interface IEnvironmentRunner
{
    Task<EnvironmentCheckResult> CheckAsync(EnvironmentRunRequest request, CancellationToken cancellationToken);
    Task<EnvironmentRunStatus> StartAsync(Guid id, EnvironmentRunRequest request, CancellationToken cancellationToken);
    Task<EnvironmentRunStatus> GetAsync(Guid id, CancellationToken cancellationToken);
    Task StopAsync(Guid id, CancellationToken cancellationToken);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(EnvironmentProfile))]
[JsonSerializable(typeof(EnvironmentDefinition))]
[JsonSerializable(typeof(List<EnvironmentDefinition>))]
[JsonSerializable(typeof(EnvironmentRunRequest))]
[JsonSerializable(typeof(EnvironmentRunStatus))]
[JsonSerializable(typeof(ExecutionLogSnapshot))]
[JsonSerializable(typeof(ExecutionTraceSnapshot))]
[JsonSerializable(typeof(EnvironmentCheckResult))]
[JsonSerializable(typeof(ResourceBinding))]
[JsonSerializable(typeof(ResourceSetup))]
[JsonSerializable(typeof(EnvironmentDefaults))]
[JsonSerializable(typeof(ImageRegistry))]
[JsonSerializable(typeof(RegistrySignIn))]
[JsonSerializable(typeof(RegistryAccessResult))]
public partial class EnvironmentJsonContext : JsonSerializerContext;
