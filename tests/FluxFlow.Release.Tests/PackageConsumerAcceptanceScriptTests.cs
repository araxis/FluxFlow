using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

[Collection(ReleaseProcessCollection.Name)]
public sealed class PackageConsumerAcceptanceScriptTests
{
    private const string FixtureDirectory = "package-consumer-acceptance";
    private const string FixtureProject = "FluxFlow.PackageConsumerAcceptance.csproj";

    private static readonly IReadOnlyDictionary<string, string> ExpectedPackageVersions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FluxFlow.Engine"] = "$(FluxFlowEngineVersion)",
            ["FluxFlow.Engine.DurableInput.SqlFile"] = "$(FluxFlowDurableInputSqlFileVersion)",
            ["FluxFlow.Engine.DurableOutput.SqlFile"] = "$(FluxFlowDurableOutputSqlFileVersion)",
            ["FluxFlow.Fluent"] = "$(FluxFlowFluentVersion)"
        };

    private static readonly string[] RequiredAliases =
    [
        "nodes",
        "mapping",
        "composition",
        "engine",
        "fluent",
        "engine-durable-input",
        "engine-durable-input-sqlfile",
        "engine-durable-output",
        "engine-durable-output-sqlfile"
    ];

    [Fact]
    public void Acceptance_fixture_is_a_net8_package_only_consumer()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var projectPath = GetFixturePath(root, FixtureProject);

        File.Exists(projectPath).ShouldBeTrue();
        var project = XDocument.Load(projectPath);
        project.Descendants().Single(element => element.Name.LocalName == "TargetFramework")
            .Value.ShouldBe("net8.0");

        var packageVersions = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.Ordinal);

        packageVersions.ShouldBe(ExpectedPackageVersions);
        project.Descendants().ShouldNotContain(element => element.Name.LocalName == "ProjectReference");

        var projectText = File.ReadAllText(projectPath);
        projectText.ShouldNotContain("..\\");
        projectText.ShouldNotContain("../");
        projectText.ShouldNotContain("/src/");
        projectText.ShouldNotContain("\\src\\");
    }

    [Fact]
    public void Acceptance_fixture_exercises_engine_fluent_and_sql_file_reopen_paths()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var source = File.ReadAllText(GetFixturePath(root, "Program.cs"));

        CountOccurrences(source, "PACKAGE_ACCEPTANCE_ENGINE_OK=True").ShouldBe(1);
        CountOccurrences(source, "PACKAGE_ACCEPTANCE_FLUENT_OK=True").ShouldBe(1);
        CountOccurrences(source, "PACKAGE_ACCEPTANCE_DURABILITY_OK=True").ShouldBe(1);
        CountOccurrences(source, "PACKAGE_ACCEPTANCE_OK=True").ShouldBe(1);
        source.ShouldNotContain("System.Reflection");
        source.ShouldNotContain("Assembly.Load(");
        source.ShouldNotContain("GetAssemblies(");

        source.ShouldContain("ApplicationDefinitionJson.Deserialize(definitionJson)");
        source.ShouldContain("services.AddFluxFlow(");
        source.ShouldContain("GetRequiredService<FluxFlowApplication>()");
        source.ShouldContain("Ensure(started.IsApplied");
        source.IndexOf("Ports.ReceiveAsync<string>", StringComparison.Ordinal).ShouldBeLessThan(
            source.IndexOf("Ports.SendAsync(", StringComparison.Ordinal));
        source.ShouldContain("PortSendStatus.Accepted");
        source.ShouldContain("PortReceiveStatus.Received");
        source.ShouldContain("PACKAGE-JSON");

        source.ShouldContain("var graph = Flow");
        source.ShouldContain(".From(new SingleValueSource(");
        source.ShouldContain(".Then(new UppercaseNode())");
        source.ShouldContain(".To(new CollectSink(collector))");
        source.ShouldContain("await graph.StartAsync()");
        source.ShouldContain("await graph.Completion.WaitAsync(");
        source.ShouldContain("PACKAGE-FLUENT");

        source.ShouldContain("AddFluxFlowSqlFileDurableInput");
        source.ShouldContain("AddFluxFlowSqlFileDurableOutput");
        source.ShouldContain("GetRequiredService<IDurableInputStore>()");
        source.ShouldContain("GetRequiredService<IDurableOutputStore>()");
        source.ShouldContain("GetRequiredService<IDurableOutputDeliveryStore>()");
        source.ShouldContain("DurableInputEnqueueStatus.Enqueued");
        source.ShouldContain("DurableOutputEnqueueStatus.Enqueued");
        source.ShouldContain("DurableInputEnqueueStatus.AlreadyExists");
        source.ShouldContain("DurableOutputEnqueueStatus.AlreadyExists");
        source.IndexOf("await using (var writer", StringComparison.Ordinal).ShouldBeLessThan(
            source.IndexOf("await using (var reader", StringComparison.Ordinal));
        source.ShouldContain("inputStore.LeaseAsync(");
        source.ShouldContain("outputDelivery.TryLeaseAsync(");
        source.ShouldContain("inputLease.Envelope.Payload.GetRawText()");
        source.ShouldContain("persistedOutput.Envelope.Payload.GetRawText()");
    }

    [Fact]
    public void Acceptance_gate_is_part_of_the_complete_ci_rehearsal()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        CountOccurrences(
            workflow,
            "./eng/package-consumer-acceptance.ps1 -PackPackages").ShouldBe(1);
    }

    [Fact]
    public async Task Acceptance_script_prepare_only_resolves_exact_closure_without_mutation()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root, createCandidateSource: false);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            fixture.CreateEnvironment(),
            "-PackageSource",
            fixture.PackageSource,
            "-WorkDirectory",
            fixture.WorkDirectory,
            "-PrepareOnly");

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain("PACKAGE_ACCEPTANCE_PACK_PACKAGES=False");
        result.StandardOutput.ShouldContain("PACKAGE_ACCEPTANCE_PREPARED=True");
        ReadLines(result.StandardOutput)
            .Where(line => line.StartsWith("PACKAGE_ACCEPTANCE_CANDIDATE=", StringComparison.Ordinal))
            .ShouldBe(fixture.Packages.Select(package =>
                $"PACKAGE_ACCEPTANCE_CANDIDATE={package.Alias}|{package.PackageId}|{package.Version}"));

        var restoreCommand = ReadOutputValue(result.StandardOutput, "PACKAGE_ACCEPTANCE_RESTORE_COMMAND=");
        restoreCommand.ShouldContain(" --no-cache --packages ");
        restoreCommand.ShouldContain($"--packages {fixture.PackageCache}");
        restoreCommand.ShouldContain($"--configfile {Path.Combine(fixture.WorkDirectory, "NuGet.config")}");
        restoreCommand.ShouldNotContain("--source");
        AssertTopLevelVersionArguments(restoreCommand, fixture.Packages);

        Directory.Exists(fixture.PackageSource).ShouldBeFalse();
        Directory.Exists(fixture.WorkDirectory).ShouldBeFalse();
        File.Exists(fixture.DotnetArgumentsPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Acceptance_script_rejects_a_fixture_project_reference_before_process_execution()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root, createCandidateSource: false);
        var rejectedFixture = Directory.CreateDirectory(Path.Combine(fixture.Root, "rejected-fixture")).FullName;
        var project = XDocument.Load(GetFixturePath(root, FixtureProject));
        project.Root!.Add(new XElement(
            "ItemGroup",
            new XElement("ProjectReference", new XAttribute("Include", "repository.csproj"))));
        project.Save(Path.Combine(rejectedFixture, FixtureProject));
        File.Copy(GetFixturePath(root, "Program.cs"), Path.Combine(rejectedFixture, "Program.cs"));

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            fixture.CreateEnvironment(),
            "-FixturePath",
            rejectedFixture,
            "-PackageSource",
            fixture.PackageSource,
            "-WorkDirectory",
            fixture.WorkDirectory,
            "-PrepareOnly");

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("cannot contain ProjectReference");
        Directory.Exists(fixture.PackageSource).ShouldBeFalse();
        Directory.Exists(fixture.WorkDirectory).ShouldBeFalse();
        File.Exists(fixture.DotnetArgumentsPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData("nodes")]
    [InlineData("engine-durable-output-sqlfile")]
    public async Task Acceptance_script_rejects_incomplete_candidate_closure_before_restore(
        string missingAlias)
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root);
        var missing = fixture.Packages.Single(package => package.Alias == missingAlias);
        File.Delete(fixture.GetCandidateArchive(missing));

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            fixture.CreateEnvironment(),
            "-PackageSource",
            fixture.PackageSource,
            "-WorkDirectory",
            fixture.WorkDirectory);

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain($"{missing.PackageId}.{missing.Version}.nupkg");
        result.ToString().ShouldContain("must contain exactly one");
        result.StandardOutput.ShouldNotContain("PACKAGE_ACCEPTANCE_COMPLETE=True");
        File.Exists(fixture.DotnetArgumentsPath).ShouldBeFalse();
        Directory.Exists(fixture.WorkDirectory).ShouldBeFalse();
        Directory.Exists(fixture.PackageSource).ShouldBeTrue();
    }

    [Fact]
    public async Task Acceptance_script_restores_verifies_builds_and_runs_from_retained_isolated_workdir()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            fixture.CreateEnvironment(),
            "-PackageSource",
            fixture.PackageSource,
            "-WorkDirectory",
            fixture.WorkDirectory);

        result.ExitCode.ShouldBe(0, result.ToString());
        var invocations = File.ReadAllLines(fixture.DotnetArgumentsPath);
        invocations.Length.ShouldBe(3);
        invocations[0].ShouldStartWith("restore ");
        invocations[1].ShouldStartWith("build ");
        invocations[2].ShouldStartWith("run ");

        CountOccurrences(invocations[0], "--no-cache").ShouldBe(1);
        CountOccurrences(invocations[0], "--packages").ShouldBe(1);
        CountOccurrences(invocations[0], "--configfile").ShouldBe(1);
        invocations[0].ShouldContain($"--packages {fixture.PackageCache}");
        invocations[0].ShouldContain($"--configfile {Path.Combine(fixture.WorkDirectory, "NuGet.config")}");
        invocations[0].ShouldNotContain("--source");
        invocations[1].ShouldContain("--no-restore");
        invocations[2].ShouldContain("--no-build");
        invocations[2].ShouldContain("--no-restore");
        foreach (var invocation in invocations)
            AssertTopLevelVersionArguments(invocation, fixture.Packages);

        var sourceConfig = XDocument.Load(Path.Combine(fixture.WorkDirectory, "NuGet.config"));
        var packageSources = sourceConfig
            .Descendants("packageSources")
            .Single()
            .Elements()
            .ToArray();
        packageSources.Select(element => element.Name.LocalName).ShouldBe(["clear", "add", "add"]);
        packageSources.Skip(1).Select(element => element.Attribute("key")!.Value)
            .ShouldBe(["candidate", "public"]);
        packageSources.Skip(1).Select(element => element.Attribute("value")!.Value)
            .ShouldBe([fixture.PackageSource, "https://api.nuget.org/v3/index.json"]);

        ReadLines(result.StandardOutput)
            .Where(line => line.StartsWith("PACKAGE_ACCEPTANCE_VERIFIED=", StringComparison.Ordinal))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ShouldBe(fixture.Packages
                .Select(package => $"PACKAGE_ACCEPTANCE_VERIFIED={package.PackageId}/{package.Version}")
                .OrderBy(line => line, StringComparer.Ordinal));
        foreach (var marker in RequiredMarkers())
            CountOccurrences(result.StandardOutput, marker).ShouldBe(1);
        result.StandardOutput.ShouldContain("PACKAGE_ACCEPTANCE_COMPLETE=True");

        Directory.Exists(fixture.WorkDirectory).ShouldBeTrue();
        Directory.Exists(fixture.PackageSource).ShouldBeTrue();
        foreach (var package in fixture.Packages)
        {
            var cachedArchive = Path.Combine(
                fixture.PackageCache,
                package.PackageId.ToLowerInvariant(),
                package.Version.ToLowerInvariant(),
                $"{package.PackageId.ToLowerInvariant()}.{package.Version.ToLowerInvariant()}.nupkg");
            File.ReadAllBytes(cachedArchive).ShouldBe(File.ReadAllBytes(fixture.GetCandidateArchive(package)));
        }
    }

    public static TheoryData<string, string> InvalidResolutionModes =>
        new()
        {
            { "project", "resolved a project library" },
            { "hash", "does not match candidate" }
        };

    [Theory]
    [MemberData(nameof(InvalidResolutionModes))]
    public async Task Acceptance_script_rejects_project_or_non_candidate_resolution(
        string mode,
        string expectedFailureFragment)
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root);
        var environment = fixture.CreateEnvironment();
        environment["FAKE_DOTNET_PROJECT_LIBRARY"] = mode == "project" ? "true" : null;
        environment["FAKE_DOTNET_MISMATCH_PACKAGE"] = mode == "hash" ? "FluxFlow.Engine" : null;

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            environment,
            "-PackageSource",
            fixture.PackageSource,
            "-WorkDirectory",
            fixture.WorkDirectory);

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain(expectedFailureFragment);
        result.StandardOutput.ShouldNotContain("PACKAGE_ACCEPTANCE_COMPLETE=True");
        var invocations = File.ReadAllLines(fixture.DotnetArgumentsPath);
        invocations.ShouldHaveSingleItem().ShouldStartWith("restore ");
        Directory.Exists(fixture.WorkDirectory).ShouldBeTrue();
        Directory.Exists(fixture.PackageSource).ShouldBeTrue();
    }

    [Fact]
    public async Task Acceptance_script_cleans_owned_workdir_after_missing_marker_failure()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root);
        var environment = fixture.CreateEnvironment();
        environment["FAKE_DOTNET_OMIT_MARKER"] = "PACKAGE_ACCEPTANCE_DURABILITY_OK=True";
        var candidateBytes = fixture.Packages.ToDictionary(
            package => package.PackageId,
            package => File.ReadAllBytes(fixture.GetCandidateArchive(package)),
            StringComparer.Ordinal);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            environment,
            "-PackageSource",
            fixture.PackageSource);

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain("PACKAGE_ACCEPTANCE_DURABILITY_OK=True");
        result.ToString().ShouldContain("observed 0");
        result.StandardOutput.ShouldNotContain("PACKAGE_ACCEPTANCE_COMPLETE=True");
        File.ReadAllLines(fixture.DotnetArgumentsPath).Length.ShouldBe(3);

        var ownedWorkDirectory = ReadOutputValue(result.StandardOutput, "PACKAGE_ACCEPTANCE_WORK_DIR=");
        Directory.Exists(ownedWorkDirectory).ShouldBeFalse();
        Directory.Exists(fixture.PackageSource).ShouldBeTrue();
        foreach (var package in fixture.Packages)
            File.ReadAllBytes(fixture.GetCandidateArchive(package)).ShouldBe(candidateBytes[package.PackageId]);
    }

    [Fact]
    public async Task Acceptance_script_pack_mode_cleans_owned_source_and_workdir_after_success()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        using var fixture = AcceptanceTestFixture.Create(root, createCandidateSource: false);

        var result = await ReleaseScriptRunner.RunAsync(
            root,
            "package-consumer-acceptance.ps1",
            fixture.CreateEnvironment(),
            "-PackPackages");

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain("PACKAGE_ACCEPTANCE_PACK_PACKAGES=True");
        result.StandardOutput.ShouldContain("PACKAGE_ACCEPTANCE_COMPLETE=True");
        var invocations = File.ReadAllLines(fixture.DotnetArgumentsPath);
        invocations.Length.ShouldBe(12);
        invocations.Take(9).ShouldAllBe(invocation => invocation.StartsWith("pack ", StringComparison.Ordinal));
        invocations[9].ShouldStartWith("restore ");
        invocations[10].ShouldStartWith("build ");
        invocations[11].ShouldStartWith("run ");

        var ownedPackageSource = ReadOutputValue(result.StandardOutput, "PACKAGE_ACCEPTANCE_PACKAGE_SOURCE=");
        var ownedWorkDirectory = ReadOutputValue(result.StandardOutput, "PACKAGE_ACCEPTANCE_WORK_DIR=");
        Directory.Exists(ownedPackageSource).ShouldBeFalse();
        Directory.Exists(ownedWorkDirectory).ShouldBeFalse();
    }

    private static string GetFixturePath(string root, string fileName)
        => Path.Combine(root, "eng", FixtureDirectory, fileName);

    private static RequiredPackage[] ReadRequiredPackages(string root)
    {
        var manifest = PackageManifest.Read(root);
        return RequiredAliases.Select(alias =>
        {
            var entry = manifest.Single(package => package.Alias == alias);
            var projectPath = Path.Combine(root, NormalizePath(entry.Project));
            var project = XDocument.Load(projectPath);
            var version = project
                .Descendants()
                .Where(element => element.Name.LocalName == "Version")
                .Select(element => element.Value.Trim())
                .First(value => value.Length > 0);
            return new RequiredPackage(alias, entry.PackageId, version, projectPath);
        }).ToArray();
    }

    private static void AssertTopLevelVersionArguments(
        string invocation,
        IReadOnlyCollection<RequiredPackage> packages)
    {
        foreach (var expected in ExpectedPackageVersions)
        {
            var alias = expected.Key switch
            {
                "FluxFlow.Engine" => "engine",
                "FluxFlow.Fluent" => "fluent",
                "FluxFlow.Engine.DurableInput.SqlFile" => "engine-durable-input-sqlfile",
                "FluxFlow.Engine.DurableOutput.SqlFile" => "engine-durable-output-sqlfile",
                _ => throw new InvalidOperationException($"Unexpected top-level package '{expected.Key}'.")
            };
            var package = packages.Single(candidate => candidate.Alias == alias);
            var propertyName = expected.Value.TrimStart('$', '(').TrimEnd(')');
            var expectedArgument = $"-p:{propertyName}={package.Version}";
            var arguments = invocation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var exactCount = arguments.Count(argument => argument == expectedArgument);
            var commandWrapperCount = arguments
                .Zip(arguments.Skip(1))
                .Count(pair => pair.First == $"-p:{propertyName}" && pair.Second == package.Version);
            (exactCount + commandWrapperCount).ShouldBe(1, invocation);
        }
    }

    private static string[] RequiredMarkers()
        =>
        [
            "PACKAGE_ACCEPTANCE_ENGINE_OK=True",
            "PACKAGE_ACCEPTANCE_FLUENT_OK=True",
            "PACKAGE_ACCEPTANCE_DURABILITY_OK=True",
            "PACKAGE_ACCEPTANCE_OK=True"
        ];

    private static string[] ReadLines(string text)
        => text.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ReadOutputValue(string output, string prefix)
    {
        var line = ReadLines(output).Single(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..];
    }

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

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

    private sealed record RequiredPackage(
        string Alias,
        string PackageId,
        string Version,
        string ProjectPath);

    private sealed class AcceptanceTestFixture : IDisposable
    {
        private AcceptanceTestFixture(string repositoryRoot, bool createCandidateSource)
        {
            Root = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(),
                $"fluxflow-package-acceptance-test-{Guid.NewGuid():N}")).FullName;
            Packages = ReadRequiredPackages(repositoryRoot);
            PackageSource = Path.Combine(Root, "candidate-source");
            WorkDirectory = Path.Combine(Root, "consumer-work");
            PackageCache = Path.Combine(WorkDirectory, "packages");
            DotnetArgumentsPath = Path.Combine(Root, "dotnet-arguments.txt");
            DotnetCurrentArgumentsPath = Path.Combine(Root, "dotnet-current-arguments.txt");
            var metadataPath = Path.Combine(Root, "packages.json");
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(Packages));
            var driverPath = Path.Combine(Root, "fake-dotnet.ps1");
            File.WriteAllText(driverPath, FakeDotnetDriver);
            FakeDotnetDirectory = CreateFakeDotnetCommand(Root);
            Environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["FAKE_DOTNET_ARGUMENTS"] = DotnetArgumentsPath,
                ["FAKE_DOTNET_CURRENT_ARGUMENTS"] = DotnetCurrentArgumentsPath,
                ["FAKE_DOTNET_DRIVER"] = driverPath,
                ["FAKE_DOTNET_METADATA"] = metadataPath,
                ["PATH"] = FakeDotnetDirectory + Path.PathSeparator +
                    System.Environment.GetEnvironmentVariable("PATH")
            };

            if (createCandidateSource)
            {
                Directory.CreateDirectory(PackageSource);
                foreach (var package in Packages)
                {
                    File.WriteAllText(
                        GetCandidateArchive(package),
                        $"candidate:{package.PackageId}/{package.Version}");
                }
            }
        }

        public string Root { get; }

        public RequiredPackage[] Packages { get; }

        public string PackageSource { get; }

        public string WorkDirectory { get; }

        public string PackageCache { get; }

        public string DotnetArgumentsPath { get; }

        private string DotnetCurrentArgumentsPath { get; }

        private string FakeDotnetDirectory { get; }

        private Dictionary<string, string?> Environment { get; }

        public static AcceptanceTestFixture Create(
            string repositoryRoot,
            bool createCandidateSource = true)
            => new(repositoryRoot, createCandidateSource);

        public Dictionary<string, string?> CreateEnvironment()
            => new(Environment, StringComparer.OrdinalIgnoreCase);

        public string GetCandidateArchive(RequiredPackage package)
            => Path.Combine(PackageSource, $"{package.PackageId}.{package.Version}.nupkg");

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private static string CreateFakeDotnetCommand(string root)
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "fake-bin")).FullName;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.WriteAllText(Path.Combine(directory, "dotnet.cmd"), """
                    @echo off
                    type nul > "%FAKE_DOTNET_CURRENT_ARGUMENTS%"
                    :collect_arguments
                    if "%~1"=="" goto run_driver
                    >> "%FAKE_DOTNET_CURRENT_ARGUMENTS%" echo(%~1
                    shift
                    goto collect_arguments
                    :run_driver
                    pwsh -NoLogo -NoProfile -File "%FAKE_DOTNET_DRIVER%"
                    exit /b %ERRORLEVEL%
                    """);
                return directory;
            }

            var scriptPath = Path.Combine(directory, "dotnet");
            File.WriteAllText(scriptPath, """
                #!/usr/bin/env bash
                printf '%s\n' "$@" > "$FAKE_DOTNET_CURRENT_ARGUMENTS"
                exec pwsh -NoLogo -NoProfile -File "$FAKE_DOTNET_DRIVER"
                """.ReplaceLineEndings("\n"));
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return directory;
        }

        private const string FakeDotnetDriver = """
            $ErrorActionPreference = "Stop"
            $CommandArguments = @(Get-Content -LiteralPath $env:FAKE_DOTNET_CURRENT_ARGUMENTS)
            Add-Content -LiteralPath $env:FAKE_DOTNET_ARGUMENTS -Value ($CommandArguments -join " ")
            $packages = @(Get-Content -LiteralPath $env:FAKE_DOTNET_METADATA -Raw | ConvertFrom-Json)

            function Read-Argument {
                param([string] $Name)

                for ($index = 0; $index -lt $CommandArguments.Count - 1; $index++) {
                    if ($CommandArguments[$index] -eq $Name) {
                        return $CommandArguments[$index + 1]
                    }
                }

                throw "Fake dotnet did not receive '$Name'."
            }

            $operation = $CommandArguments[0]
            if ($operation -eq "pack") {
                $projectPath = [System.IO.Path]::GetFullPath($CommandArguments[1])
                $package = @($packages | Where-Object {
                    [string]::Equals(
                        [System.IO.Path]::GetFullPath($_.ProjectPath),
                        $projectPath,
                        [System.StringComparison]::OrdinalIgnoreCase)
                })[0]
                if ($null -eq $package) {
                    throw "Fake dotnet could not map pack project '$projectPath'."
                }

                $outputPath = Read-Argument "--output"
                New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
                Set-Content `
                    -LiteralPath (Join-Path $outputPath "$($package.PackageId).$($package.Version).nupkg") `
                    -Value "candidate:$($package.PackageId)/$($package.Version)" `
                    -NoNewline
                exit 0
            }

            if ($operation -eq "restore") {
                $projectPath = [System.IO.Path]::GetFullPath($CommandArguments[1])
                $workRoot = Split-Path -Parent $projectPath
                $packageCache = Read-Argument "--packages"
                $configPath = Read-Argument "--configfile"
                [xml] $config = Get-Content -LiteralPath $configPath -Raw
                $candidateSource = [string] (@($config.configuration.packageSources.add | Where-Object {
                    $_.key -eq "candidate"
                })[0].value)

                $libraries = [ordered]@{}
                if ($env:FAKE_DOTNET_PROJECT_LIBRARY -eq "true") {
                    $libraries["Repository.Project/1.0.0"] = [ordered]@{
                        type = "project"
                        path = "../src/Repository.Project.csproj"
                    }
                }

                foreach ($package in $packages) {
                    $lowerId = $package.PackageId.ToLowerInvariant()
                    $lowerVersion = $package.Version.ToLowerInvariant()
                    $relativePath = "$lowerId/$lowerVersion"
                    $libraries["$($package.PackageId)/$($package.Version)"] = [ordered]@{
                        type = "package"
                        path = $relativePath
                    }

                    $packageDirectory = Join-Path $packageCache $relativePath
                    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
                    $candidateArchive = Join-Path $candidateSource "$($package.PackageId).$($package.Version).nupkg"
                    $cachedArchive = Join-Path $packageDirectory "$lowerId.$lowerVersion.nupkg"
                    Copy-Item -LiteralPath $candidateArchive -Destination $cachedArchive
                    if ($env:FAKE_DOTNET_MISMATCH_PACKAGE -eq $package.PackageId) {
                        Add-Content -LiteralPath $cachedArchive -Value "mismatch"
                    }
                }

                $assetsDirectory = Join-Path $workRoot "obj"
                New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null
                [ordered]@{
                    version = 3
                    libraries = $libraries
                } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $assetsDirectory "project.assets.json")
                exit 0
            }

            if ($operation -eq "run") {
                $markers = @(
                    "PACKAGE_ACCEPTANCE_ENGINE_OK=True",
                    "PACKAGE_ACCEPTANCE_FLUENT_OK=True",
                    "PACKAGE_ACCEPTANCE_DURABILITY_OK=True",
                    "PACKAGE_ACCEPTANCE_OK=True"
                )
                foreach ($marker in $markers) {
                    if ($marker -ne $env:FAKE_DOTNET_OMIT_MARKER) {
                        Write-Output $marker
                    }
                }
            }

            exit 0
            """;
    }
}
