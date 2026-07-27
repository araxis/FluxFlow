using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackageBinaryCompatPreflightScriptTests
{
    [Fact]
    public async Task Binary_compat_preflight_script_prepares_package_validation_command()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetDataPackage(root);
        var version = ReadProjectVersion(root, package);
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
            result.StandardOutput.ShouldContain($"BINARY_COMPAT_PACKAGE_ALIAS={package.Alias}");
            result.StandardOutput.ShouldContain($"BINARY_COMPAT_PACKAGE_ID={package.PackageId}");
            result.StandardOutput.ShouldContain($"BINARY_COMPAT_PACKAGE_PROJECT={package.Project}");
            result.StandardOutput.ShouldContain($"BINARY_COMPAT_PACKAGE_VERSION={version}");
            result.StandardOutput.ShouldContain($"BINARY_COMPAT_BASELINE_VERSION={version}");
            result.StandardOutput.ShouldContain($"BINARY_COMPAT_PACKAGE_SOURCE={packageSource}");
            result.StandardOutput.ShouldContain("BINARY_COMPAT_PACK_COMMAND=dotnet pack");
            result.StandardOutput.ShouldContain("-p:EnablePackageValidation=true");
            result.StandardOutput.ShouldContain($"-p:PackageValidationBaselineName={package.PackageId}");
            result.StandardOutput.ShouldContain($"-p:PackageValidationBaselineVersion={version}");
            result.StandardOutput.ShouldContain("BINARY_COMPAT_PREPARED=True");
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
        var package = GetDataPackage(root);

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
        var package = GetDataPackage(root);
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
        using var fixture = BinaryCompatFixture.Create(buildOutput: false);

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
    public async Task Binary_compat_preflight_script_prints_success_marker_after_pack()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = BinaryCompatFixture.Create(buildOutput: true);
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
            "-BaselineVersion",
            fixture.Version,
            "-ManifestPath",
            fixture.ManifestPath);

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain($"BINARY_COMPAT_OK={fixture.PackageId}");

        var dotnetArguments = File.ReadAllText(dotnetArgumentsPath);
        dotnetArguments.ShouldContain("pack");
        dotnetArguments.ShouldContain("-p:EnablePackageValidation=true");
        dotnetArguments.ShouldContain($"-p:PackageValidationBaselineName={fixture.PackageId}");
        dotnetArguments.ShouldContain($"-p:PackageValidationBaselineVersion={fixture.Version}");
    }

    [Fact]
    public void Fake_dotnet_unix_script_uses_lf_line_endings()
    {
        var script = CreateFakeUnixDotnetScript();
        var bytes = Encoding.UTF8.GetBytes(script);

        script.ShouldStartWith("#!/usr/bin/env bash\n");
        bytes.ShouldNotContain((byte)'\r');
    }

    private static PackageManifestEntry GetDataPackage(string root)
        => PackageManifest
            .Read(root)
            .Single(entry => entry.Alias == "data");

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
            string version)
        {
            Root = root;
            ManifestPath = manifestPath;
            Alias = alias;
            PackageId = packageId;
            Version = version;
        }

        public string Root { get; }

        public string ManifestPath { get; }

        public string Alias { get; }

        public string PackageId { get; }

        public string Version { get; }

        public static BinaryCompatFixture Create(bool buildOutput)
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
                    "notesName": "{{packageId}}"
                  }
                ]
                """);

            if (buildOutput)
            {
                var outputDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "bin", "Release", "net8.0")).FullName;
                File.WriteAllText(Path.Combine(outputDirectory, $"{packageId}.dll"), "");
            }

            return new BinaryCompatFixture(root, manifestPath, alias, packageId, version);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
