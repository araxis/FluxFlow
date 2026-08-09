using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class HealthReadinessConventionTests
{
    private const string ProjectDirectory = "FluxFlow.Engine.HealthChecks";

    [Fact]
    public void Health_integration_package_is_optional_standard_only_and_manifested()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "src",
            ProjectDirectory,
            "FluxFlow.Engine.HealthChecks.csproj");
        var project = XDocument.Load(projectPath);

        ReadProperty(project, "TargetFrameworks").ShouldBe("net8.0;net10.0");
        ReadProperty(project, "PackageId").ShouldBe("FluxFlow.Engine.HealthChecks");
        ReadProperty(project, "AssemblyName").ShouldBe("FluxFlow.Engine.HealthChecks");
        ReadProperty(project, "RootNamespace").ShouldBe("FluxFlow.Engine.HealthChecks");
        ReadProperty(project, "Version").ShouldBe("1.0.0-rc.1");
        ReadIncludes(project, "PackageReference")
            .ShouldBe(new[] { "Microsoft.Extensions.Diagnostics.HealthChecks" });
        ReadIncludes(project, "ProjectReference")
            .ShouldBe(new[] { @"..\FluxFlow.Engine\FluxFlow.Engine.csproj" });
        project.Descendants().ShouldNotContain(static element =>
            element.Name.LocalName == "FrameworkReference");

        var manifest = PackageManifest.Read(root);
        var entry = manifest.Single(static candidate =>
            candidate.Alias == "engine-healthchecks");
        entry.TagPrefix.ShouldBe("engine-healthchecks");
        entry.PackageId.ShouldBe("FluxFlow.Engine.HealthChecks");
        entry.Project.ShouldBe("src/FluxFlow.Engine.HealthChecks/FluxFlow.Engine.HealthChecks.csproj");
        entry.NotesName.ShouldBe("FluxFlow.Engine.HealthChecks");
        entry.BinaryCompatibilityBaseline.ShouldBeNull();
        manifest.Count(static candidate =>
            candidate.PackageId == "FluxFlow.Engine.HealthChecks").ShouldBe(1);
    }

    [Fact]
    public void Health_integration_public_surface_is_one_standard_builder_extension()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var extension = ReadSource(root, "FluxFlowHealthChecksBuilderExtensions.cs");
        var check = ReadSource(root, "FluxFlowApplicationHealthCheck.cs");

        extension.ShouldContain("public static class FluxFlowHealthChecksBuilderExtensions");
        CountOccurrences(
            extension,
            "public static IHealthChecksBuilder AddFluxFlowApplication(").ShouldBe(1);
        extension.ShouldContain("this IHealthChecksBuilder builder");
        extension.ShouldContain("return builder;");
        extension.ShouldContain("private sealed class FluxFlowApplicationHealthCheckRegistrationMarker;");
        extension.ShouldContain("provider.GetService<FluxFlowApplication>()");
        extension.ShouldContain("\"fluxflow.application\"");
        extension.ShouldContain("HealthStatus.Unhealthy");
        extension.ShouldContain("[\"fluxflow\", \"ready\"]");
        extension.ShouldNotContain("Options");
        extension.ShouldNotContain("public sealed class FluxFlowApplicationHealthCheck");

        check.ShouldContain("internal sealed class FluxFlowApplicationHealthCheck");
        check.ShouldContain(": IHealthCheck");
        check.ShouldContain("public Task<HealthCheckResult> CheckHealthAsync(");
        check.ShouldNotContain("public sealed");
        check.ShouldNotContain("public record");
        check.ShouldNotContain("public enum");
    }

    [Fact]
    public void Health_check_source_has_exact_mapping_bounded_data_and_no_polling_scanning_or_io()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var extension = ReadSource(root, "FluxFlowHealthChecksBuilderExtensions.cs");
        var check = ReadSource(root, "FluxFlowApplicationHealthCheck.cs");
        var source = extension + Environment.NewLine + check;

        check.ShouldContain("cancellationToken.ThrowIfCancellationRequested();");
        check.ShouldContain("state is ApplicationState.Stopping or ApplicationState.Stopped");
        check.ShouldContain("state is not (ApplicationState.Running or ApplicationState.Reloading)");
        check.ShouldContain("lastUpdate?.Status == ApplicationUpdateStatus.Rejected");
        check.ShouldContain("HealthCheckResult.Healthy(");
        check.ShouldContain("HealthCheckResult.Degraded(");
        check.ShouldContain("HealthCheckResult.Unhealthy(");
        check.ShouldContain("lastUpdate.Diagnostics.LastOrDefault()");

        var expectedKeys = new[]
        {
            "applicationState",
            "activeRevisionId",
            "activeSequence",
            "requestedRevisionId",
            "lastUpdateStatus",
            "diagnosticStage",
            "diagnosticCode"
        };
        foreach (var key in expectedKeys)
        {
            var expectedOccurrences = key == "applicationState" ? 2 : 1;
            CountOccurrences(check, $"[\"{key}\"]").ShouldBe(expectedOccurrences);
        }

        var forbidden = new[]
        {
            "System.Reflection",
            "Assembly.Load",
            "GetAssemblies(",
            "BackgroundService",
            "IHostedService",
            "PeriodicTimer",
            "System.Threading.Timer",
            "Task.Delay(",
            "Task.Run(",
            "Thread.Sleep(",
            "while (",
            "File.",
            "Directory.",
            "HttpClient",
            "DbConnection",
            "SqlConnection",
            "ReloadAsync(",
            "ApplyAsync(",
            "StartAsync(",
            "StopAsync("
        };
        foreach (var value in forbidden)
            source.ShouldNotContain(value);
    }

    [Fact]
    public void Health_check_reads_state_and_references_forward_then_reverse_and_fails_closed_on_a_torn_observation()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var source = ReadSource(root, "FluxFlowApplicationHealthCheck.cs")
            .ReplaceLineEndings("\n");

        AssertInOrder(
            source,
            "var stateBefore = application.State;",
            "var currentBefore = application.Current;",
            "var lastUpdateBefore = application.LastUpdate;",
            "var lastUpdateAfter = application.LastUpdate;",
            "var currentAfter = application.Current;",
            "var stateAfter = application.State;");
        source.ShouldContain("stateBefore != stateAfter");
        source.ShouldContain("!ReferenceEquals(currentBefore, currentAfter)");
        source.ShouldContain("!ReferenceEquals(lastUpdateBefore, lastUpdateAfter)");
        AssertInOrder(
            source,
            "if (stateBefore != stateAfter ||",
            "current: null,",
            "lastUpdateAfter));",
            "return Task.FromResult(CreateResult(\n            stateAfter,\n            currentAfter,");
    }

    [Fact]
    public void Health_readiness_docs_define_opt_in_registration_exact_mapping_and_host_owned_endpoint()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var dedicated = File.ReadAllText(Path.Combine(
            root,
            "docs",
            "42-application-health-readiness.md"));
        var hosting = File.ReadAllText(Path.Combine(
            root,
            "docs",
            "05-hosting-and-observability.md"));
        var packageReadme = File.ReadAllText(Path.Combine(
            root,
            "src",
            ProjectDirectory,
            "README.md"));
        var publicOverview = File.ReadAllText(Path.Combine(
            root,
            "docs",
            "14-public-api-overview.md"));
        var docsIndex = File.ReadAllText(Path.Combine(root, "docs", "README.md"));
        var rootReadme = File.ReadAllText(Path.Combine(root, "README.md"));

        dedicated.ShouldContain("standard .NET health-check");
        dedicated.ShouldContain("services.AddHealthChecks()");
        dedicated.ShouldContain(".AddFluxFlowApplication();");
        dedicated.ShouldContain("name: `fluxflow.application`");
        dedicated.ShouldContain("tags: `fluxflow`, `ready`");
        dedicated.ShouldContain("configured failure status: `Unhealthy`");
        dedicated.ShouldContain("| `Healthy` | `Running` or `Reloading`");
        dedicated.ShouldContain("| `Degraded` | `Running` or `Reloading`");
        dedicated.ShouldContain("| `Unhealthy` | FluxFlow is not registered");
        dedicated.ShouldContain("The result may contain only these seven keys:");
        dedicated.ShouldContain("Cancellation is propagated immediately with the caller's token.");
        dedicated.ShouldContain("app.MapHealthChecks(");
        dedicated.ShouldContain("The package itself has no ASP.NET Core dependency");
        dedicated.ShouldContain("adds no hosted service, worker, timer, polling loop");
        dedicated.ShouldContain("does not report:");
        dedicated.ShouldContain("process liveness");
        dedicated.ShouldContain("database, broker, HTTP endpoint");

        hosting.ShouldContain("## Optional Application Readiness");
        hosting.ShouldContain("at most lifecycle state");
        hosting.ShouldContain("performs no polling");
        packageReadme.ShouldContain("Optional standard .NET readiness integration");
        packageReadme.ShouldContain("It adds no worker,");
        publicOverview.ShouldContain("### FluxFlow.Engine.HealthChecks 1.x");
        publicOverview.ShouldContain("at most seven bounded metadata");
        docsIndex.ShouldContain("42. [Application Health Readiness](42-application-health-readiness.md)");
        rootReadme.ShouldContain("[Application Health Readiness](docs/42-application-health-readiness.md)");
    }

    private static string ReadSource(string root, string fileName)
        => File.ReadAllText(Path.Combine(root, "src", ProjectDirectory, fileName));

    private static string ReadProperty(XDocument project, string name)
        => project.Descendants()
            .Single(element => element.Name.LocalName == name)
            .Value.Trim();

    private static string[] ReadIncludes(XDocument project, string itemName)
        => project.Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => element.Attribute("Include")!.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static void AssertInOrder(string source, params string[] values)
    {
        var previous = -1;
        foreach (var value in values)
        {
            var current = source.IndexOf(value, StringComparison.Ordinal);
            current.ShouldBeGreaterThan(previous);
            previous = current;
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }
}
