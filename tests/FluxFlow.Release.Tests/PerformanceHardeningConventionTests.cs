using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PerformanceHardeningConventionTests
{
    [Fact]
    public void Benchmark_project_is_a_manual_non_packable_net10_suite_with_eight_cases()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var benchmarkDirectory = Path.Combine(
            root,
            "benchmarks",
            "FluxFlow.Engine.Benchmarks");
        var solution = File.ReadAllText(Path.Combine(root, "FluxFlow.sln"));
        var project = File.ReadAllText(Path.Combine(
            benchmarkDirectory,
            "FluxFlow.Engine.Benchmarks.csproj"));
        var program = File.ReadAllText(Path.Combine(benchmarkDirectory, "Program.cs"));

        solution.ShouldContain(
            @"benchmarks\FluxFlow.Engine.Benchmarks\FluxFlow.Engine.Benchmarks.csproj");
        project.ShouldContain("<TargetFramework>net10.0</TargetFramework>");
        project.ShouldContain("<IsPackable>false</IsPackable>");
        program.ShouldContain("BenchmarkSwitcher");
        program.ShouldContain(".FromAssembly(typeof(Program).Assembly)");
        program.ShouldContain(".Run(args);");

        var benchmarkFiles = Directory.GetFiles(
                benchmarkDirectory,
                "*Benchmarks.cs",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        benchmarkFiles.ShouldBe(new[]
        {
            "ApplicationLinkCompilationBenchmarks.cs",
            "ApplicationMessagingBenchmarks.cs",
            "ApplicationTopologyBenchmarks.cs"
        });

        var messaging = ReadBenchmarkSource(benchmarkDirectory, "ApplicationMessagingBenchmarks.cs");
        var topology = ReadBenchmarkSource(benchmarkDirectory, "ApplicationTopologyBenchmarks.cs");
        var compilation = ReadBenchmarkSource(
            benchmarkDirectory,
            "ApplicationLinkCompilationBenchmarks.cs");
        AssertBenchmarkClass(messaging, expectedBenchmarkMethods: 3);
        AssertBenchmarkClass(topology, expectedBenchmarkMethods: 1);
        AssertBenchmarkClass(compilation, expectedBenchmarkMethods: 1);
        messaging.ShouldContain("[GlobalCleanup]");
        topology.ShouldContain("[GlobalCleanup]");
        compilation.ShouldNotContain("[GlobalCleanup]");
        CountOccurrences(messaging, "[Params(").ShouldBe(0);
        CountOccurrences(topology, "[Params(").ShouldBe(1);
        CountOccurrences(compilation, "[Params(").ShouldBe(1);
        topology.ShouldContain("[Params(1, 8)]");
        compilation.ShouldContain("[Params(1, 32, 128)]");

        const int messagingCases = 3;
        const int topologyCases = 2;
        const int compilationCases = 3;
        (messagingCases + topologyCases + compilationCases).ShouldBe(8);
    }

    [Fact]
    public void Performance_baseline_is_indexed_and_keeps_timing_out_of_ci()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var documentName = "43-performance-concurrency-lifetime-baseline.md";
        var index = File.ReadAllText(Path.Combine(root, "docs", "README.md"));
        var baseline = File.ReadAllText(Path.Combine(root, "docs", documentName));

        index.ShouldContain($"({documentName})");
        baseline.ShouldContain("not CI");
        baseline.ShouldContain("pass/fail thresholds");
        baseline.ShouldContain("Timing is never used as correctness evidence.");
        baseline.ShouldContain("Benchmark jobs remain");
        baseline.ShouldContain("manual evidence");

        var workflowSources = Directory.GetFiles(
                Path.Combine(root, ".github", "workflows"),
                "*.yml",
                SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        workflowSources.ShouldNotBeEmpty();
        var combinedWorkflows = string.Join(Environment.NewLine, workflowSources);
        combinedWorkflows.ShouldNotContain("FluxFlow.Engine.Benchmarks");
        combinedWorkflows.ShouldNotContain("BenchmarkDotNet");
        combinedWorkflows.ShouldNotContain("--job Dry");
        combinedWorkflows.ShouldNotContain("--job Short");
    }

    [Fact]
    public void Unconditional_route_skips_the_only_condition_context_allocation()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FluxFlow.Engine",
            "Ports",
            "ApplicationOutputPort.cs"));
        const string guard = "if (!link.IsConditional)";
        const string context = "new FlowMapContext";
        const string variables = "new Dictionary<string, object?>";

        CountOccurrences(source, guard).ShouldBe(1);
        CountOccurrences(source, context).ShouldBe(1);
        CountOccurrences(source, variables).ShouldBe(1);
        var guardIndex = source.IndexOf(guard, StringComparison.Ordinal);
        var contextIndex = source.IndexOf(context, StringComparison.Ordinal);
        var variablesIndex = source.IndexOf(variables, StringComparison.Ordinal);
        guardIndex.ShouldBeLessThan(contextIndex);
        contextIndex.ShouldBeLessThan(variablesIndex);
        source[guardIndex..contextIndex].ShouldContain("return true;");
    }

    [Fact]
    public void Diagnostic_attribute_publisher_uses_fixed_arity_builders_without_linq_or_params()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FluxFlow.Engine",
            "Ports",
            "ApplicationPortEventPublisher.cs"));

        source.ShouldNotContain("params ");
        source.ShouldNotContain(".Where(");
        source.ShouldNotContain(".ToDictionary(");
        CountOccurrences(
            source,
            "private static Dictionary<string, string> CreateAttributes(").ShouldBe(2);
        source.ShouldContain("new Dictionary<string, string>(2, StringComparer.Ordinal)");
        source.ShouldContain("new Dictionary<string, string>(3, StringComparer.Ordinal)");
        CountOccurrences(source, "private static JsonElement CreateDetails(").ShouldBe(2);
        source.ShouldContain("AddAttribute(attributes, first);");
        source.ShouldContain("AddAttribute(attributes, second);");
        source.ShouldContain("AddAttribute(attributes, third);");
    }

    private static string ReadBenchmarkSource(string directory, string fileName)
        => File.ReadAllText(Path.Combine(directory, fileName));

    private static void AssertBenchmarkClass(string source, int expectedBenchmarkMethods)
    {
        CountOccurrences(source, "[MemoryDiagnoser]").ShouldBe(1);
        CountOccurrences(source, "[GlobalSetup]").ShouldBe(1);
        Regex.Matches(
                source,
                @"^\s*\[Benchmark(?:\(|\])",
                RegexOptions.CultureInvariant | RegexOptions.Multiline)
            .Count.ShouldBe(expectedBenchmarkMethods);
        Regex.Matches(
                source,
                @"\[Benchmark(?:\([^\]]*\))?\]\s+public\s+[^\r\n]+\r?\n\s*=>",
                RegexOptions.CultureInvariant)
            .Count.ShouldBe(expectedBenchmarkMethods);
    }

    private static int CountOccurrences(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;
}
