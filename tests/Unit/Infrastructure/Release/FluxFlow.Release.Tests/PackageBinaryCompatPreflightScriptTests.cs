using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

[Collection(ReleaseProcessCollection.Name)]
public sealed class PackageBinaryCompatPreflightScriptTests
{
    private const string PublicPackageSource = "https://api.nuget.org/v3/index.json";

    [Fact]
    public async Task Binary_compat_preflight_script_prepares_package_validation_command()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetNodesPackage(root);
        var version = ReadProjectVersion(root, package);
        var baseline = package.BinaryCompatibilityBaseline.ShouldNotBeNull();
        var packageSource = Directory.CreateTempSubdirectory("fluxflow-binary-compat-source-").FullName;

        try
        {
            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "package-binary-compat-preflight.ps1",
                "-Package",
                package.Alias,
                "-Version",
                version,
                "-PackageSource",
                packageSource,
                "-PrepareOnly");

            result.ExitCode.ShouldBe(0, result.ToString());
            var outputLines = ReadLines(result.StandardOutput);
            result.StandardOutput.ShouldContain($"BINARY_COMPAT_PACKAGE_ALIAS={package.Alias}");
            result.StandardOutput.ShouldContain($"BINARY_COMPAT_PACKAGE_ID={package.PackageId}");
            result.StandardOutput.ShouldContain($"BINARY_COMPAT_PACKAGE_PROJECT={package.Project}");
            result.StandardOutput.ShouldContain($"BINARY_COMPAT_PACKAGE_VERSION={version}");
            outputLines.ShouldContain($"BINARY_COMPAT_MANIFEST_BASELINE_VERSION={baseline}");
            outputLines.ShouldContain($"BINARY_COMPAT_BASELINE_VERSION={baseline}");
            outputLines.ShouldContain("BINARY_COMPAT_INITIAL_RELEASE=False");
            result.StandardOutput.ShouldContain($"BINARY_COMPAT_PACKAGE_SOURCE={packageSource}");
            result.StandardOutput.ShouldContain("BINARY_COMPAT_PACK_COMMAND=dotnet pack");
            result.StandardOutput.ShouldContain("-p:EnablePackageValidation=true");
            result.StandardOutput.ShouldContain($"-p:PackageValidationBaselineName={package.PackageId}");
            result.StandardOutput.ShouldContain($"-p:PackageValidationBaselineVersion={baseline}");
            outputLines.ShouldContain("BINARY_COMPAT_BASELINE_RESTORE=True");
            outputLines.ShouldContain("BINARY_COMPAT_PREPARED=True");
        }
        finally
        {
            Directory.Delete(packageSource, recursive: true);
        }
    }

    [Fact]
    public async Task Binary_compat_preflight_script_rejects_version_mismatch()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetNodesPackage(root);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-binary-compat-preflight.ps1",
            "-Package",
            package.Alias,
            "-Version",
            "9.9.9",
            "-PrepareOnly");

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("does not match project version");
    }

    [Fact]
    public async Task Binary_compat_preflight_script_rejects_file_package_source()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetNodesPackage(root);
        var version = ReadProjectVersion(root, package);
        var packageSource = Path.Combine(Path.GetTempPath(), $"fluxflow-binary-compat-source-{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(packageSource, "");

            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "package-binary-compat-preflight.ps1",
                "-Package",
                package.Alias,
                "-Version",
                version,
                "-PackageSource",
                packageSource,
                "-PrepareOnly");

            result.ExitCode.ShouldNotBe(0);
            result.ToString().ShouldContain("must be a directory");
            result.ToString().ShouldContain("package source URL");
        }
        finally
        {
            if (File.Exists(packageSource))
                File.Delete(packageSource);
        }
    }

    [Fact]
    public async Task Binary_compat_preflight_script_requires_release_build_output()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = BinaryCompatFixture.CreateWithBaseline(buildOutput: false);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-binary-compat-preflight.ps1",
            "-Package",
            fixture.Alias,
            "-Version",
            fixture.Version,
            "-ManifestPath",
            fixture.ManifestPath);

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("Run the controlled Release build before binary compatibility preflight");
    }

    [Fact]
    public async Task Binary_compat_preflight_script_uses_manifest_baseline_and_requested_output()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = BinaryCompatFixture.CreateWithBaseline(buildOutput: true);
        var dotnetArgumentsPath = Path.Combine(fixture.Root, "dotnet-arguments.txt");
        var fakeDotnetDirectory = CreateFakeDotnet(fixture.Root);

        var environment = new Dictionary<string, string?>
        {
            ["DOTNET_ARGUMENTS_FILE"] = dotnetArgumentsPath,
            ["PATH"] = fakeDotnetDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
        };

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-binary-compat-preflight.ps1",
            environment,
            "-Package",
            fixture.Alias,
            "-Version",
            fixture.Version,
            "-ManifestPath",
            fixture.ManifestPath,
            "-PackageSource",
            PublicPackageSource,
            "-OutputPath",
            fixture.OutputPath);

        result.ExitCode.ShouldBe(0, result.ToString());
        var outputLines = ReadLines(result.StandardOutput);
        outputLines.ShouldContain($"BINARY_COMPAT_MANIFEST_BASELINE_VERSION={fixture.BaselineVersion}");
        outputLines.ShouldContain($"BINARY_COMPAT_BASELINE_VERSION={fixture.BaselineVersion}");
        outputLines.ShouldContain("BINARY_COMPAT_INITIAL_RELEASE=False");
        outputLines.ShouldContain("BINARY_COMPAT_BASELINE_RESTORE=True");
        outputLines.ShouldContain($"BINARY_COMPAT_PACKAGE_OUTPUT={fixture.OutputPath}");
        outputLines.ShouldContain($"BINARY_COMPAT_OK={fixture.PackageId}");

        var dotnetInvocations = File.ReadAllLines(dotnetArgumentsPath);
        dotnetInvocations.Length.ShouldBe(2);
        var restoreInvocation = dotnetInvocations[0];
        var packInvocation = dotnetInvocations[1];
        restoreInvocation.ShouldStartWith("restore ");
        restoreInvocation.ShouldContain(" --no-cache --packages ");
        CountOccurrences(restoreInvocation, "--no-cache").ShouldBe(1);
        CountOccurrences(restoreInvocation, "--packages").ShouldBe(1);
        CountOccurrences(restoreInvocation, PublicPackageSource).ShouldBe(1);

        var isolatedPackageRoot = ReadFollowingArgument(restoreInvocation, "--packages");
        Path.IsPathRooted(isolatedPackageRoot).ShouldBeTrue();
        Path.GetFileName(isolatedPackageRoot).ShouldBe("packages");
        Path.GetFileName(Path.GetDirectoryName(isolatedPackageRoot)).ShouldStartWith(
            "fluxflow-binary-compat-restore-");

        var normalizedPackageId = fixture.PackageId.ToLowerInvariant();
        var expectedBaselinePath = Path.Combine(
            isolatedPackageRoot,
            normalizedPackageId,
            fixture.BaselineVersion!,
            $"{normalizedPackageId}.{fixture.BaselineVersion}.nupkg");

        packInvocation.ShouldStartWith("pack ");
        packInvocation.ShouldContain("--output");
        packInvocation.ShouldContain(fixture.OutputPath);
        packInvocation.ShouldContain("-p:EnablePackageValidation=true");
        packInvocation.ShouldContain($"-p:PackageValidationBaselineName={fixture.PackageId}");
        packInvocation.ShouldContain($"-p:PackageValidationBaselineVersion={fixture.BaselineVersion}");
        CountOccurrences(packInvocation, "PackageValidationBaselinePath=").ShouldBe(1);
        packInvocation.ShouldContain($"-p:PackageValidationBaselinePath={expectedBaselinePath}");
    }

    [Fact]
    public async Task Binary_compat_preflight_script_supports_deliberate_baseline_override()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = BinaryCompatFixture.CreateWithBaseline(buildOutput: false);
        const string overrideVersion = "1.1.0";

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-binary-compat-preflight.ps1",
            "-Package",
            fixture.Alias,
            "-Version",
            fixture.Version,
            "-BaselineVersion",
            overrideVersion,
            "-ManifestPath",
            fixture.ManifestPath,
            "-PrepareOnly");

        result.ExitCode.ShouldBe(0, result.ToString());
        var outputLines = ReadLines(result.StandardOutput);
        outputLines.ShouldContain($"BINARY_COMPAT_MANIFEST_BASELINE_VERSION={fixture.BaselineVersion}");
        outputLines.ShouldContain($"BINARY_COMPAT_BASELINE_VERSION={overrideVersion}");
        result.StandardOutput.ShouldContain($"-p:PackageValidationBaselineVersion={overrideVersion}");
        outputLines.ShouldContain("BINARY_COMPAT_INITIAL_RELEASE=False");
        outputLines.ShouldContain("BINARY_COMPAT_BASELINE_RESTORE=True");
    }

    [Fact]
    public async Task Binary_compat_preflight_script_does_not_let_override_bypass_missing_manifest_policy()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = BinaryCompatFixture.CreateWithoutPolicy(buildOutput: false);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-binary-compat-preflight.ps1",
            "-Package",
            fixture.Alias,
            "-Version",
            fixture.Version,
            "-BaselineVersion",
            "1.1.0",
            "-ManifestPath",
            fixture.ManifestPath,
            "-PrepareOnly");

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("binaryCompatibilityBaseline");
        result.StandardOutput.ShouldNotContain("BINARY_COMPAT_PACK_COMMAND=");
        result.StandardOutput.ShouldNotContain("BINARY_COMPAT_PREPARED=True");
    }

    [Fact]
    public async Task Binary_compat_preflight_script_packages_explicit_initial_release_without_baseline_validation()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = BinaryCompatFixture.CreateInitialRelease(buildOutput: true);
        var dotnetArgumentsPath = Path.Combine(fixture.Root, "dotnet-arguments.txt");
        var fakeDotnetDirectory = CreateFakeDotnet(fixture.Root);
        var environment = new Dictionary<string, string?>
        {
            ["DOTNET_ARGUMENTS_FILE"] = dotnetArgumentsPath,
            ["PATH"] = fakeDotnetDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
        };

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-binary-compat-preflight.ps1",
            environment,
            "-Package",
            fixture.Alias,
            "-Version",
            fixture.Version,
            "-ManifestPath",
            fixture.ManifestPath,
            "-OutputPath",
            fixture.OutputPath);

        result.ExitCode.ShouldBe(0, result.ToString());
        var outputLines = ReadLines(result.StandardOutput);
        outputLines.ShouldContain("BINARY_COMPAT_MANIFEST_BASELINE_VERSION=");
        outputLines.ShouldContain("BINARY_COMPAT_BASELINE_VERSION=");
        outputLines.ShouldContain("BINARY_COMPAT_INITIAL_RELEASE=True");
        outputLines.ShouldContain("BINARY_COMPAT_BASELINE_RESTORE=False");
        outputLines.ShouldContain($"BINARY_COMPAT_PACKAGE_OUTPUT={fixture.OutputPath}");
        outputLines.ShouldContain($"BINARY_COMPAT_OK={fixture.PackageId}");
        result.StandardOutput.ShouldNotContain("BINARY_COMPAT_BASELINE_RESTORE_COMMAND=");

        var dotnetInvocations = File.ReadAllLines(dotnetArgumentsPath);
        dotnetInvocations.ShouldHaveSingleItem();
        dotnetInvocations[0].ShouldStartWith("pack ");
        dotnetInvocations[0].ShouldContain("--output");
        dotnetInvocations[0].ShouldContain(fixture.OutputPath);
        dotnetInvocations[0].ShouldNotContain("EnablePackageValidation");
        dotnetInvocations[0].ShouldNotContain("PackageValidationBaselineName");
        dotnetInvocations[0].ShouldNotContain("PackageValidationBaselineVersion");
        dotnetInvocations[0].ShouldNotContain("PackageValidationBaselinePath");
        dotnetInvocations[0].ShouldNotContain("--no-cache");
        dotnetInvocations[0].ShouldNotContain("--packages");
    }

    [Fact]
    public void Fake_dotnet_unix_script_uses_lf_line_endings()
    {
        var script = CreateFakeUnixDotnetScript();
        var bytes = Encoding.UTF8.GetBytes(script);

        script.ShouldStartWith("#!/usr/bin/env bash\n");
        bytes.ShouldNotContain((byte)'\r');
    }

    private static PackageManifestEntry GetNodesPackage(string root)
        => PackageManifest
            .Read(root)
            .Single(entry => entry.Alias == "nodes");

    private static string ReadProjectVersion(string root, PackageManifestEntry package)
    {
        var projectPath = Path.Combine(root, NormalizePath(package.Project));
        var project = XDocument.Load(projectPath);
        return project
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

    private static string ReadFollowingArgument(string command, string option)
    {
        var pattern =
            $$"""(?:^|\s){{Regex.Escape(option)}}\s+(?:"(?<doubleQuoted>[^"]+)"|'(?<singleQuoted>[^']+)'|(?<bare>\S+))""";
        var match = Regex.Match(command, pattern);

        match.Success.ShouldBeTrue($"Expected '{option} <value>' in '{command}'.");
        foreach (var groupName in new[] { "doubleQuoted", "singleQuoted", "bare" })
        {
            if (match.Groups[groupName].Success)
                return match.Groups[groupName].Value;
        }

        throw new InvalidOperationException($"Argument following '{option}' was not captured.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var startIndex = 0;

        while ((startIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }

    private static string CreateFakeDotnet(string root)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, "fake-bin")).FullName;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var path = Path.Combine(directory, "dotnet.cmd");
            File.WriteAllText(path, """
                @echo off
                echo %*>>"%DOTNET_ARGUMENTS_FILE%"
                exit /b 0
                """);
            return directory;
        }

        var scriptPath = Path.Combine(directory, "dotnet");
        File.WriteAllText(scriptPath, CreateFakeUnixDotnetScript());

        File.SetUnixFileMode(
            scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        return directory;
    }

    private static string CreateFakeUnixDotnetScript()
        => """
            #!/usr/bin/env bash
            printf '%s\n' "$*" >> "$DOTNET_ARGUMENTS_FILE"
            exit 0
            """.ReplaceLineEndings("\n");

    private sealed class BinaryCompatFixture : IDisposable
    {
        private BinaryCompatFixture(
            string root,
            string manifestPath,
            string alias,
            string packageId,
            string version,
            string? baselineVersion)
        {
            Root = root;
            ManifestPath = manifestPath;
            Alias = alias;
            PackageId = packageId;
            Version = version;
            BaselineVersion = baselineVersion;
            OutputPath = Path.Combine(root, "candidate packages");
        }

        public string Root { get; }

        public string ManifestPath { get; }

        public string Alias { get; }

        public string PackageId { get; }

        public string Version { get; }

        public string? BaselineVersion { get; }

        public string OutputPath { get; }

        public static BinaryCompatFixture CreateWithBaseline(bool buildOutput)
            => Create(
                buildOutput,
                ", \"binaryCompatibilityBaseline\": \"1.2.2\"",
                baselineVersion: "1.2.2");

        public static BinaryCompatFixture CreateInitialRelease(bool buildOutput)
            => Create(
                buildOutput,
                ", \"binaryCompatibilityBaseline\": null",
                baselineVersion: null);

        public static BinaryCompatFixture CreateWithoutPolicy(bool buildOutput)
            => Create(buildOutput, policyPropertyJson: "", baselineVersion: null);

        private static BinaryCompatFixture Create(
            bool buildOutput,
            string policyPropertyJson,
            string? baselineVersion)
        {
            var root = Directory.CreateTempSubdirectory("fluxflow-binary-compat-fixture-").FullName;
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root, "package")).FullName;
            const string alias = "test-package";
            const string packageId = "FluxFlow.Test.BinaryCompat";
            const string version = "1.2.3";
            var projectPath = Path.Combine(projectDirectory, $"{packageId}.csproj");
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

            if (buildOutput)
            {
                var outputDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "bin", "Release", "net8.0")).FullName;
                File.WriteAllText(Path.Combine(outputDirectory, $"{packageId}.dll"), "");
            }

            return new BinaryCompatFixture(root, manifestPath, alias, packageId, version, baselineVersion);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
