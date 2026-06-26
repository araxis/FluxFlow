using System.Diagnostics;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed partial class SampleDocumentationTests
{
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
                "fluent:",
                "devices/pump-02/state/reply -> ACK: offline"
            ]);
        await AssertSampleRunAsync(
            root,
            "samples/FluxFlow.SampleApp/FluxFlow.SampleApp.csproj",
            [
                "Workspace: sample-order-workspace",
                "priority: A-100 Harbor Market",
                "standard: A-101 Cedar Supply",
                "Events observed: 3"
            ]);
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
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(project);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException($"{project} did not finish within the sample smoke-test timeout.");
        }

        var output = await outputTask;
        var error = await errorTask;
        process.ExitCode.ShouldBe(
            0,
            $"""
            Sample project failed: {project}
            Output:
            {output}
            Error:
            {error}
            """);

        foreach (var expected in expectedOutput)
        {
            output.Contains(expected, StringComparison.Ordinal)
                .ShouldBeTrue($"sample output for {project} changed.");
        }
    }

    [GeneratedRegex(@"dotnet\s+run\s+--project\s+(?<project>samples[\\/][^\s`)]+\.csproj)")]
    private static partial Regex SampleRunProjectRegex();
}
