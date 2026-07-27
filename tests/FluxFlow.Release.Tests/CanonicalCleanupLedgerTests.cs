using System.Text.Json;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class CanonicalCleanupLedgerTests
{
    [Fact]
    public void Cleanup_ledger_records_the_completed_canonical_boundary()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var path = Path.Combine(root, "eng", "canonical-vnext-cleanup-ledger.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var ledger = document.RootElement;

        ledger.GetProperty("schemaVersion").GetInt32().ShouldBe(2);
        ledger.GetProperty("status").GetString().ShouldBe("complete");
        ledger.GetProperty("removedSurfaces").GetArrayLength().ShouldBeGreaterThanOrEqualTo(6);
        ledger.GetProperty("preservedCapabilities").GetArrayLength().ShouldBeGreaterThan(0);
        ledger.GetProperty("verification").GetArrayLength().ShouldBeGreaterThan(0);

        var boundary = ledger.GetProperty("canonicalBoundary");
        boundary.GetProperty("documentShape").GetArrayLength().ShouldBe(2);
        RequiredString(boundary, "componentLookup");
        RequiredString(boundary, "resourceLookup");
        RequiredString(boundary, "lifecycle");
        RequiredString(boundary, "extensionPoint");

        var packageIds = PackageManifest.Read(root)
            .Select(static entry => entry.PackageId)
            .ToHashSet(StringComparer.Ordinal);
        var versionImpact = ledger.GetProperty("versionImpact");

        foreach (var property in versionImpact.EnumerateObject()
                     .Where(static property => property.Name.StartsWith("FluxFlow.", StringComparison.Ordinal)))
        {
            packageIds.ShouldContain(property.Name);
            string.IsNullOrWhiteSpace(property.Value.GetString()).ShouldBeFalse();
        }

        packageIds.ShouldNotContain("FluxFlow.Components.Resources");
        packageIds.ShouldNotContain("FluxFlow.Components.Secrets");
        packageIds.ShouldNotContain("FluxFlow.Components.Configuration");
        packageIds.ShouldNotContain("FluxFlow.Components.Journal");
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        string.IsNullOrWhiteSpace(value).ShouldBeFalse();
        return value!;
    }
}
