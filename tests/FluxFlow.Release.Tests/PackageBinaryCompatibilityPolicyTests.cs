using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

[Collection(ReleaseProcessCollection.Name)]
public sealed class PackageBinaryCompatibilityPolicyTests
{
    [Fact]
    public async Task Resolver_emits_manifest_baseline_and_noninitial_flag()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = PackageManifest.Read(root).Single(entry => entry.Alias == "nodes");
        var version = ReadProjectVersion(root, package.Project);
        var baseline = package.BinaryCompatibilityBaseline.ShouldNotBeNull();
        var environmentPath = Path.Combine(Path.GetTempPath(), $"fluxflow-binary-policy-{Guid.NewGuid():N}.env");

        try
        {
            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "resolve-package-release.ps1",
                "-Package",
                package.Alias,
                "-Version",
                version,
                "-ManifestPath",
                Path.Combine(root, "eng", "packages.json"),
                "-EnvironmentPath",
                environmentPath);

            result.ExitCode.ShouldBe(0, result.ToString());
            var outputLines = ReadLines(result.StandardOutput);
            outputLines.ShouldContain($"PACKAGE_BINARY_COMPATIBILITY_BASELINE={baseline}");
            outputLines.ShouldContain("PACKAGE_IS_INITIAL_RELEASE=False");

            var environmentLines = File.ReadAllLines(environmentPath);
            environmentLines.ShouldContain($"PACKAGE_BINARY_COMPATIBILITY_BASELINE={baseline}");
            environmentLines.ShouldContain("PACKAGE_IS_INITIAL_RELEASE=False");
        }
        finally
        {
            if (File.Exists(environmentPath))
                File.Delete(environmentPath);
        }
    }

    [Fact]
    public async Task Resolver_emits_explicit_initial_release_policy()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = ReleasePolicyFixture.Create(", \"binaryCompatibilityBaseline\": null");

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "resolve-package-release.ps1",
            "-Package",
            fixture.Alias,
            "-Version",
            fixture.Version,
            "-ManifestPath",
            fixture.ManifestPath,
            "-EnvironmentPath",
            fixture.EnvironmentPath);

        result.ExitCode.ShouldBe(0, result.ToString());
        var outputLines = ReadLines(result.StandardOutput);
        outputLines.ShouldContain("PACKAGE_BINARY_COMPATIBILITY_BASELINE=");
        outputLines.ShouldContain("PACKAGE_IS_INITIAL_RELEASE=True");

        var environmentLines = File.ReadAllLines(fixture.EnvironmentPath);
        environmentLines.ShouldContain("PACKAGE_BINARY_COMPATIBILITY_BASELINE=");
        environmentLines.ShouldContain("PACKAGE_IS_INITIAL_RELEASE=True");
    }

    [Theory]
    [InlineData("")]
    [InlineData(", \"binaryCompatibilityBaseline\": \"\"")]
    [InlineData(", \"binaryCompatibilityBaseline\": 42")]
    [InlineData(", \"binaryCompatibilityBaseline\": \"1.2\"")]
    public async Task Resolver_rejects_missing_or_invalid_binary_compatibility_policy(string policyPropertyJson)
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = ReleasePolicyFixture.Create(policyPropertyJson);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "resolve-package-release.ps1",
            "-Package",
            fixture.Alias,
            "-Version",
            fixture.Version,
            "-ManifestPath",
            fixture.ManifestPath);

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("binaryCompatibilityBaseline");
        result.StandardOutput.ShouldNotContain("PACKAGE_ALIAS=");
        result.StandardOutput.ShouldNotContain("PACKAGE_IS_INITIAL_RELEASE=");
    }

    private static string ReadProjectVersion(string root, string project)
    {
        var projectPath = Path.Combine(root, NormalizePath(project));
        return XDocument
            .Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "Version")
            .Select(element => element.Value.Trim())
            .First(value => value.Length > 0);
    }

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    private static string[] ReadLines(string text)
        => text.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed class ReleasePolicyFixture : IDisposable
    {
        private ReleasePolicyFixture(string root, string manifestPath, string alias, string version)
        {
            Root = root;
            ManifestPath = manifestPath;
            Alias = alias;
            Version = version;
            EnvironmentPath = Path.Combine(root, "release.env");
        }

        public string Root { get; }

        public string ManifestPath { get; }

        public string EnvironmentPath { get; }

        public string Alias { get; }

        public string Version { get; }

        public static ReleasePolicyFixture Create(string policyPropertyJson)
        {
            var root = Directory.CreateTempSubdirectory("fluxflow-binary-policy-").FullName;
            const string alias = "test-package";
            const string packageId = "FluxFlow.Test.BinaryPolicy";
            const string version = "1.2.3";
            var projectPath = Path.Combine(root, $"{packageId}.csproj");
            var manifestPath = Path.Combine(root, "packages.json");

            File.WriteAllText(
                projectPath,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>{packageId}</PackageId>
                    <Version>{version}</Version>
                  </PropertyGroup>
                </Project>
                """);

            File.WriteAllText(
                manifestPath,
                $$"""
                [
                  {
                    "alias": "{{alias}}",
                    "tagPrefix": "{{alias}}",
                    "packageId": "{{packageId}}",
                    "project": "{{projectPath.Replace("\\", "\\\\")}}",
                    "notesName": "{{packageId}}"{{policyPropertyJson}}
                  }
                ]
                """);

            return new ReleasePolicyFixture(root, manifestPath, alias, version);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
