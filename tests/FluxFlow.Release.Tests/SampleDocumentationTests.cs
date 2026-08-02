using System.Diagnostics;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

[Collection(ReleaseProcessCollection.Name)]
public sealed partial class SampleDocumentationTests
{
    private static readonly TimeSpan SampleTimeout = TimeSpan.FromMinutes(1);

#if DEBUG
    private const string CurrentConfiguration = "Debug";
#else
    private const string CurrentConfiguration = "Release";
#endif

    [Fact]
    public void Sample_projects_are_listed_in_solution_and_docs_inventory()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "FluxFlow.sln"));
        var docsReadme = File.ReadAllText(Path.Combine(root, "docs", "README.md"));
        var sampleProjects = Directory
            .EnumerateFiles(Path.Combine(root, "samples"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        sampleProjects.ShouldNotBeEmpty("the repository should keep at least one sample project.");

        foreach (var project in sampleProjects)
        {
            var sampleDirectory = Path.GetDirectoryName(project)!.Replace('\\', '/');

            solution.Contains(project.Replace('/', '\\'), StringComparison.Ordinal)
                .ShouldBeTrue($"{project} must be included in FluxFlow.sln.");
            docsReadme.Contains(sampleDirectory, StringComparison.Ordinal)
                .ShouldBeTrue($"docs/README.md must list {sampleDirectory} in the sample inventory.");
        }
    }

    [Fact]
    public void Documented_sample_run_commands_point_to_existing_projects()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var documents = Directory
            .EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOrBuildOutput(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var missingProjects = new List<string>();

        foreach (var document in documents)
        {
            var relativeDocument = Path.GetRelativePath(root, document).Replace('\\', '/');
            foreach (Match match in SampleRunProjectRegex().Matches(File.ReadAllText(document)))
            {
                var project = match.Groups["project"].Value.Replace('\\', '/');
                var projectPath = Path.Combine(root, project.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(projectPath))
                {
                    missingProjects.Add($"{relativeDocument}: {project}");
                }
            }
        }

        missingProjects.ShouldBeEmpty("documented sample run commands must target existing sample projects.");
    }

    [Fact]
    public async Task Non_server_samples_run_to_completion()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();

        await AssertSampleRunAsync(
            root,
            "samples/FluxFlow.CompositionSample/FluxFlow.CompositionSample.csproj",
            ["ALPHA", "BETA"]);
        await AssertSampleRunAsync(
            root,
            "samples/FluxFlow.MqttCompositionSample/FluxFlow.MqttCompositionSample.csproj",
            [
                "configuration:",
                "devices/pump-01/state/reply -> ACK: online",
                "definition:",
                "devices/pump-02/state/reply -> ACK: offline"
            ]);
        await AssertSampleRunAsync(
            root,
            "samples/FluxFlow.SampleApp/FluxFlow.SampleApp.csproj",
            [
                "Workspace: sample-order-workspace",
                "priority: A-100 Harbor Market",
                "standard: A-101 Cedar Supply",
                "Component events observed: 6"
            ]);
    }

    [Fact]
    public async Task Durability_operations_sample_runs_with_exact_output()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        const string project =
            "samples/FluxFlow.DurabilityOperationsSample/FluxFlow.DurabilityOperationsSample.csproj";
        var firstResult = await ReleaseTestProcess.RunAsync(
            CreateSampleStartInfo(root, project),
            SampleTimeout,
            $"first sample run '{project}'");
        var secondResult = await ReleaseTestProcess.RunAsync(
            CreateSampleStartInfo(root, project),
            SampleTimeout,
            $"second sample run '{project}'");

        firstResult.ExitCode.ShouldBe(
            0,
            $"First sample run failed. Output: {firstResult.StandardOutput} Error: {firstResult.StandardError}");
        firstResult.StandardError.ShouldBe(string.Empty);
        secondResult.ExitCode.ShouldBe(
            0,
            $"Second sample run failed. Output: {secondResult.StandardOutput} Error: {secondResult.StandardError}");
        secondResult.StandardError.ShouldBe(string.Empty);

        var firstOutput = NormalizeSampleOutput(firstResult.StandardOutput);
        firstOutput.ShouldBe(
            """
            Durability operations sample
            Before input status: pending=1 leased=0 delivered=0 dead_lettered=0
            Delivered value: HELLO DURABILITY
            Meter FluxFlow.Engine.DurableInput: fluxflow.durable_input.leases.acquired=1; fluxflow.durable_input.messages{outcome=delivered}=1; fluxflow.durable_input.processing.duration observed=1
            Activity FluxFlow.Engine.DurableInput: fluxflow.durable_input.process kind=Consumer stopped=1
            Meter FluxFlow.Engine.DurableOutput: fluxflow.durable_output.captures{result=enqueued}=1; fluxflow.durable_output.capture.duration observed=1; fluxflow.durable_output.leases.acquired=1; fluxflow.durable_output.handler.calls{result=succeeded}=1; fluxflow.durable_output.deliveries{outcome=completed,result=applied}=1; fluxflow.durable_output.delivery.duration observed=1
            Activity FluxFlow.Engine.DurableOutput: fluxflow.durable_output.capture kind=Producer outcome=enqueued stopped=1; fluxflow.durable_output.deliver kind=Consumer outcome=completed stopped=1
            After input status: pending=0 leased=0 delivered=1 dead_lettered=0
            After output status: pending=0 leased=0 completed=1 dead_lettered=0
            Status snapshots: explicit input=2 output=1; automatic polling=off
            """);
        NormalizeSampleOutput(secondResult.StandardOutput).ShouldBe(firstOutput);
    }

    [Fact]
    public void Durability_operations_sample_keeps_diagnostics_host_owned_and_status_explicit()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var sampleDirectory = Path.Combine(root, "samples", "FluxFlow.DurabilityOperationsSample");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(sampleDirectory, "*.cs")
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
        var project = File.ReadAllText(Path.Combine(
            sampleDirectory,
            "FluxFlow.DurabilityOperationsSample.csproj"));
        var programSource = File.ReadAllText(Path.Combine(sampleDirectory, "Program.cs"));
        var telemetrySource = File.ReadAllText(Path.Combine(
            sampleDirectory,
            "DurabilityTelemetry.cs"));
        var jsonContextSource = File.ReadAllText(Path.Combine(
            sampleDirectory,
            "SampleJsonContext.cs"));

        source.ShouldContain("new MeterListener");
        source.ShouldContain("new ActivityListener");
        source.ShouldContain("FluxFlow.Engine.DurableInput");
        source.ShouldContain("FluxFlow.Engine.DurableOutput");
        source.ShouldContain("IDurableInputStatusStore");
        source.ShouldContain("IDurableOutputStatusStore");
        CountOccurrences(source, "GetStatusAsync(").ShouldBe(3);
        programSource.ShouldContain("Host.CreateApplicationBuilder()");
        CountOccurrences(programSource, "host.StartAsync(").ShouldBe(1);
        CountOccurrences(programSource, "host.StopAsync(").ShouldBe(1);
        source.ShouldContain("using var telemetry = new DurabilityTelemetry()");
        telemetrySource.ShouldContain(
            "ShouldListenTo = static source => IsKnownSource(source.Name)");
        telemetrySource.ShouldContain("IsKnownSource(instrument.Meter.Name)");
        telemetrySource.ShouldContain("KnownInstruments.Contains(instrument.Name)");
        telemetrySource.ShouldContain("_activityListener.Dispose()");
        telemetrySource.ShouldContain("_meterListener.Dispose()");
        CountOccurrences(source, "SampleJsonContext.Default.String").ShouldBe(3);
        jsonContextSource.ShouldContain("[JsonSerializable(typeof(string))]");
        source.ShouldContain("finally");
        source.ShouldContain("Directory.Delete(dataDirectory, recursive: true)");

        foreach (var forbidden in new[]
                 {
                     "Task.Delay",
                     "Thread.Sleep",
                     "PeriodicTimer",
                     "CreateObservableGauge",
                     "AddHostedService",
                     "HttpListener",
                     "UseUrls(",
                     "TcpClient",
                     "TcpListener",
                     "System.Reflection",
                     "Activator.CreateInstance"
                 })
        {
            source.ShouldNotContain(forbidden);
        }

        project.ShouldNotContain("OpenTelemetry");
        project.ShouldNotContain("Exporter");
        CountOccurrences(project, "<PackageReference ").ShouldBe(1);
        project.ShouldContain("<PackageReference Include=\"Microsoft.Extensions.Hosting\" />");
        telemetrySource.ShouldNotContain("Console.");
        telemetrySource.ShouldNotContain("File.");
        telemetrySource.ShouldNotContain("Directory.");
        telemetrySource.ShouldNotContain("HttpClient");
        telemetrySource.ShouldNotContain(".Wait(");
        telemetrySource.ShouldNotContain(".Result");
    }

    [Fact]
    public void Sample_process_uses_current_configuration_without_build_or_restore()
    {
        var startInfo = CreateSampleStartInfo("repository-root", "samples/Sample/Sample.csproj");

        startInfo.FileName.ShouldBe("dotnet");
        startInfo.WorkingDirectory.ShouldBe("repository-root");
        startInfo.RedirectStandardOutput.ShouldBeTrue();
        startInfo.RedirectStandardError.ShouldBeTrue();
        startInfo.UseShellExecute.ShouldBeFalse();
        startInfo.ArgumentList.ShouldBe(
        [
            "run",
            "--project",
            "samples/Sample/Sample.csproj",
            "--configuration",
            CurrentConfiguration,
            "--no-build",
            "--no-restore"
        ]);
        startInfo.ArgumentList.ShouldNotContain("--disable-build-servers");
    }

    private static bool IsGeneratedOrBuildOutput(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.Contains("/bin/", StringComparison.Ordinal) ||
               relative.Contains("/obj/", StringComparison.Ordinal);
    }

    private static async Task AssertSampleRunAsync(
        string root,
        string project,
        IReadOnlyList<string> expectedOutput)
    {
        var startInfo = CreateSampleStartInfo(root, project);
        var result = await ReleaseTestProcess.RunAsync(
            startInfo,
            SampleTimeout,
            $"sample '{project}'");

        result.ExitCode.ShouldBe(
            0,
            $"""
            Sample project failed: {project}
            Output:
            {result.StandardOutput}
            Error:
            {result.StandardError}
            """);

        foreach (var expected in expectedOutput)
        {
            result.StandardOutput.Contains(expected, StringComparison.Ordinal)
                .ShouldBeTrue($"sample output for {project} changed.");
        }
    }

    private static ProcessStartInfo CreateSampleStartInfo(string root, string project)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(CurrentConfiguration);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-restore");
        return startInfo;
    }

    private static string NormalizeSampleOutput(string output)
        => output.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    private static int CountOccurrences(string value, string expected)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(expected, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += expected.Length;
        }

        return count;
    }

    [GeneratedRegex(@"dotnet\s+run\s+--project\s+(?<project>samples[\\/][^\s`)]+\.csproj)")]
    private static partial Regex SampleRunProjectRegex();
}
