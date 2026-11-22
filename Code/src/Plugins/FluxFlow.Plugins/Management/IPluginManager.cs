using System.Text.Json.Serialization;

namespace FluxFlow.Plugins.Management;

public interface IPluginManager
{
    const long MaxUploadBytes = 64 * 1024 * 1024;
    Task<PluginEntry[]> ListAsync();
    Task InstallAsync(string localPath);
    Task InstallArchiveAsync(string name, Stream archive);
    Task InstallFolderArchiveAsync(Stream archive);
    Task<PluginSource[]> GetSourcesAsync();
    Task<string[]> ListFolderAsync(PluginSource source);
    Task SaveSourcesAsync(PluginSourceChange change);
    Task InstallFeedAsync(PluginPackage package);
    Task RemoveAsync(string id);
    Task CancelAsync(string id);
}

public sealed record PluginEntry(string Id, string? Version, PluginState State)
{
    public string? Source { get; init; }
    public string? LoadError { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<PluginState>))]
public enum PluginState { Active, PendingInstall, PendingRemoval }

public sealed record PluginPackage(string Feed, string Id, string Version)
{
    public const string NuGetOrg = "https://api.nuget.org/v3/index.json";
}

public sealed record PluginSource(string Name, string Url)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PluginSourceKind Kind { get; init; }
    public static PluginSource Default { get; } = new("nuget.org", PluginPackage.NuGetOrg);
}

[JsonConverter(typeof(JsonStringEnumConverter<PluginSourceKind>))]
public enum PluginSourceKind { NuGet, Folder }

public sealed record PluginSourceChange(PluginSource[] Original, PluginSource[] Updated);

[JsonSerializable(typeof(PluginSource[]))]
[JsonSerializable(typeof(PluginSourceChange))]
[JsonSerializable(typeof(PluginPackage))]
[JsonSerializable(typeof(PluginEntry[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
public partial class PluginManagementJson : JsonSerializerContext;
