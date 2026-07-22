using System.Text.Json;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class CanonicalCleanupLedgerTests
{
    [Fact]
    public void Cleanup_ledger_is_complete_and_references_manifest_packages()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var path = Path.Combine(root, "eng", "canonical-vnext-cleanup-ledger.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var rootElement = document.RootElement;

        rootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        rootElement.GetProperty("target").GetString().ShouldBe("canonical-vnext-next-major");
        rootElement.GetProperty("canonicalInvariants").GetArrayLength().ShouldBeGreaterThan(0);

        var packageAliases = PackageManifest.Read(root)
            .Select(static entry => entry.Alias)
            .ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var sourceFiles = new HashSet<string>(StringComparer.Ordinal);
        var entries = rootElement.GetProperty("entries").EnumerateArray().ToArray();

        entries.Length.ShouldBeGreaterThanOrEqualTo(15);
        foreach (var entry in entries)
        {
            var id = RequiredString(entry, "id");
            ids.Add(id).ShouldBeTrue($"Cleanup ledger id '{id}' must be unique.");
            RequiredString(entry, "kind");
            RequiredString(entry, "canonicalReplacement");
            RequiredString(entry, "readiness");
            RequiredString(entry, "versionImpact");
            RequiredString(entry, "migration");
            RequireNonEmptyArray(entry, "declarations");
            RequireNonEmptyArray(entry, "currentConsumers");
            RequireNonEmptyArray(entry, "uniqueBehavior");
            RequireNonEmptyArray(entry, "verification");

            foreach (var package in RequireNonEmptyArray(entry, "packages"))
            {
                packageAliases.Contains(package).ShouldBeTrue(
                    $"Cleanup ledger entry '{id}' references unknown package alias '{package}'.");
            }

            foreach (var sourceFile in RequireNonEmptyArray(entry, "sourceFiles"))
            {
                var normalized = sourceFile.Replace('\\', '/');
                sourceFiles.Add(normalized);
                File.Exists(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)))
                    .ShouldBeTrue($"Cleanup ledger source '{normalized}' does not exist.");
            }
        }

        var obsoleteFiles = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static file => File.ReadAllText(file).Contains("[Obsolete(", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .OrderBy(static file => file, StringComparer.Ordinal)
            .ToArray();

        foreach (var obsoleteFile in obsoleteFiles)
        {
            sourceFiles.Contains(obsoleteFile).ShouldBeTrue(
                $"Obsolete source '{obsoleteFile}' must be represented in the cleanup ledger.");
        }
    }

    private static string RequiredString(JsonElement entry, string propertyName)
    {
        var value = entry.GetProperty(propertyName).GetString();
        value.ShouldNotBeNullOrWhiteSpace();
        return value;
    }

    private static IReadOnlyList<string> RequireNonEmptyArray(
        JsonElement entry,
        string propertyName)
    {
        var values = entry.GetProperty(propertyName)
            .EnumerateArray()
            .Select(static value => value.GetString() ?? string.Empty)
            .ToArray();
        values.ShouldNotBeEmpty();
        values.ShouldAllBe(static value => !string.IsNullOrWhiteSpace(value));
        return values;
    }
}
