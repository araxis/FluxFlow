using System.Text.Json;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

[Collection(ReleaseProcessCollection.Name)]
public sealed class PackageReleasePlanScriptTests
{
    [Fact]
    public async Task Release_plan_is_deterministic_and_respects_repository_dependencies()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "eng", "packages.json");

        var first = await RunPlanAsync(root, manifestPath, "mapping");
        var second = await RunPlanAsync(root, manifestPath, "mapping");

        first.ExitCode.ShouldBe(0, first.ToString());
        second.ExitCode.ShouldBe(0, second.ToString());
        var firstWaves = ParseWaves(first.StandardOutput);
        var secondWaves = ParseWaves(second.StandardOutput);

        firstWaves
            .Select(wave => $"{wave.Number}={string.Join(',', wave.Aliases)}")
            .ShouldBe(secondWaves.Select(wave => $"{wave.Number}={string.Join(',', wave.Aliases)}"));
        firstWaves.Select(wave => wave.Number).ShouldBe(Enumerable.Range(1, 5));
        first.StandardOutput.ShouldContain("PACKAGE_WAVE_COUNT=5");
        first.StandardOutput.ShouldContain("PACKAGE_REUSED=mapping");

        foreach (var wave in firstWaves)
        {
            wave.Aliases.ShouldBe(wave.Aliases.OrderBy(alias => alias, StringComparer.Ordinal));
        }

        var plannedAliases = firstWaves.SelectMany(wave => wave.Aliases).ToArray();
        var expectedAliases = PackageManifest.Read(root)
            .Select(package => package.Alias)
            .Where(alias => !alias.Equals("mapping", StringComparison.OrdinalIgnoreCase))
            .OrderBy(alias => alias, StringComparer.Ordinal)
            .ToArray();

        plannedAliases.ShouldNotContain("mapping");
        plannedAliases.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(plannedAliases.Length);
        plannedAliases.OrderBy(alias => alias, StringComparer.Ordinal).ShouldBe(expectedAliases);
        AssertDependenciesPrecedeDependents(root, firstWaves, ["mapping"]);
    }

    [Fact]
    public async Task Release_plan_rejects_unknown_already_available_alias()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "eng", "packages.json");

        var result = await RunPlanAsync(root, manifestPath, "does-not-exist");

        result.ExitCode.ShouldNotBe(0, result.ToString());
        result.ToString().ShouldContain("does-not-exist");
        result.ToString().ShouldContain("not present");
    }

    [Fact]
    public async Task Release_plan_rejects_missing_manifest_project()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var graph = new TemporaryPackageGraph();
        graph.WriteManifest(CreateEntry("missing", "src/Missing/Missing.csproj"));

        var result = await RunPlanAsync(root, graph.ManifestPath);

        result.ExitCode.ShouldNotBe(0, result.ToString());
        result.ToString().ShouldContain("src/Missing/Missing.csproj");
        result.ToString().ShouldContain("was not found");
    }

    [Fact]
    public async Task Release_plan_rejects_dependency_cycle()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var graph = new TemporaryPackageGraph();
        graph.WriteProject("src/A/A.csproj", "../B/B.csproj");
        graph.WriteProject("src/B/B.csproj", "../A/A.csproj");
        graph.WriteManifest(
            CreateEntry("a", "src/A/A.csproj"),
            CreateEntry("b", "src/B/B.csproj"));

        var result = await RunPlanAsync(root, graph.ManifestPath);

        result.ExitCode.ShouldNotBe(0, result.ToString());
        result.ToString().ShouldContain("cycle");
        result.ToString().ShouldContain("a");
        result.ToString().ShouldContain("b");
    }

    private static async Task<ReleaseScriptResult> RunPlanAsync(
        string root,
        string manifestPath,
        params string[] alreadyAvailable)
    {
        var arguments = new List<string>
        {
            "-ManifestPath",
            manifestPath
        };

        if (alreadyAvailable.Length > 0)
        {
            arguments.Add("-AlreadyAvailable");
            arguments.AddRange(alreadyAvailable);
        }

        return await ReleaseScriptRunner.RunAsync(
            root,
            "package-release-plan.ps1",
            [.. arguments]);
    }

    private static IReadOnlyList<ReleaseWave> ParseWaves(string output)
    {
        const string prefix = "PACKAGE_WAVE_";

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal)
                && line.Length > prefix.Length
                && char.IsAsciiDigit(line[prefix.Length]))
            .Select(line =>
            {
                var equalsIndex = line.IndexOf('=');
                equalsIndex.ShouldBeGreaterThan(prefix.Length, $"Malformed release wave: {line}");
                var number = int.Parse(line[prefix.Length..equalsIndex]);
                var aliases = line[(equalsIndex + 1)..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                aliases.ShouldNotBeEmpty($"Release wave {number} must contain at least one package.");
                return new ReleaseWave(number, aliases);
            })
            .OrderBy(wave => wave.Number)
            .ToArray();
    }

    private static void AssertDependenciesPrecedeDependents(
        string root,
        IReadOnlyList<ReleaseWave> waves,
        IReadOnlyCollection<string> alreadyAvailable)
    {
        var manifest = PackageManifest.Read(root);
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var aliasByProject = manifest.ToDictionary(
            package => Path.GetFullPath(Path.Combine(root, NormalizeSeparators(package.Project))),
            package => package.Alias,
            pathComparer);
        var waveByAlias = waves
            .SelectMany(wave => wave.Aliases.Select(alias => (alias, wave.Number)))
            .ToDictionary(item => item.alias, item => item.Number, StringComparer.OrdinalIgnoreCase);
        var available = alreadyAvailable.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var package in manifest.Where(package => !available.Contains(package.Alias)))
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizeSeparators(package.Project)));
            var document = XDocument.Load(projectPath);
            var references = document
                .Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include));

            foreach (var include in references)
            {
                var dependencyPath = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(projectPath)!,
                    NormalizeSeparators(include!)));
                if (!aliasByProject.TryGetValue(dependencyPath, out var dependencyAlias)
                    || available.Contains(dependencyAlias))
                {
                    continue;
                }

                (waveByAlias[dependencyAlias] < waveByAlias[package.Alias]).ShouldBeTrue(
                    $"Dependency '{dependencyAlias}' must precede dependent '{package.Alias}'.");
            }
        }
    }

    private static string NormalizeSeparators(string path)
        => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    private static PackageManifestEntry CreateEntry(string alias, string project)
        => new()
        {
            Alias = alias,
            TagPrefix = $"{alias}-v",
            PackageId = $"FluxFlow.Test.{alias}",
            Project = project,
            NotesName = alias
        };

    private sealed record ReleaseWave(int Number, string[] Aliases);

    private sealed class TemporaryPackageGraph : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "fluxflow-release-plan-tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryPackageGraph()
        {
            Directory.CreateDirectory(Path.Combine(_root, "eng"));
        }

        public string ManifestPath => Path.Combine(_root, "eng", "packages.json");

        public void WriteManifest(params PackageManifestEntry[] entries)
        {
            var json = JsonSerializer.Serialize(
                entries,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            File.WriteAllText(ManifestPath, json);
        }

        public void WriteProject(string relativePath, params string[] projectReferences)
        {
            var path = Path.Combine(_root, NormalizeSeparators(relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var itemGroup = projectReferences.Length == 0
                ? null
                : new XElement(
                    "ItemGroup",
                    projectReferences.Select(reference =>
                        new XElement("ProjectReference", new XAttribute("Include", reference))));
            var document = new XDocument(
                new XElement(
                    "Project",
                    new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup", new XElement("TargetFramework", "net8.0")),
                    itemGroup));
            document.Save(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
