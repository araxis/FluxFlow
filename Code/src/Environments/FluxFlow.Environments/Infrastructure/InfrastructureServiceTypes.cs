namespace FluxFlow.Environments;

public sealed record InfrastructureServiceType(string Type, string Name, string DefaultEndpoint, string Help, Func<Uri, bool> AcceptsEndpoint)
{
    public Func<InfrastructureService, IReadOnlyList<ServiceProperty>>? Fields { get; init; }
    public Action<InfrastructureService, Uri>? ValidateSettings { get; init; }
    /// <summary>Connection keys the service sets or clears in addition to its endpoint, even when values match the manual connection.</summary>
    public Func<InfrastructureService, IReadOnlyList<string>>? OverriddenSettings { get; init; }
    public static bool HasNoPath(Uri uri) => uri.AbsolutePath is "" or "/" && uri.Query.Length == 0;
    public string? Icon { get; init; }
    public bool RequiresApplicationHost { get; init; }
}

public static class InfrastructureServiceTypes
{
    public static InfrastructureServiceType? Find(string type, IEnumerable<IResourceEnvironmentAdapter>? extensions = null) =>
        extensions?.FirstOrDefault(item => item.Type == type)?.Infrastructure;
}
