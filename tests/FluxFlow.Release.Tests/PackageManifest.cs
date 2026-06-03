using System.Text.Json;
using Shouldly;

namespace FluxFlow.Release.Tests;

internal static class PackageManifest
{
    public static IReadOnlyList<PackageManifestEntry> Read(string root)
    {
        var manifestPath = Path.Combine(root, "eng", "packages.json");
        var entries = JsonSerializer.Deserialize<IReadOnlyList<PackageManifestEntry>>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        entries.ShouldNotBeNull();
        return entries;
    }
}

internal sealed record PackageManifestEntry
{
    public required string Alias { get; init; }
    public required string TagPrefix { get; init; }
    public required string PackageId { get; init; }
    public required string Project { get; init; }
    public required string NotesName { get; init; }
}
