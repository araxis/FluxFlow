using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PerformanceHardeningConventionTests
{
    [Fact]
    public void Benchmark_project_is_manual_non_packable_and_covers_supported_runtimes_and_conditions()
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
        project.ShouldContain("<TargetFrameworks>net8.0;net10.0</TargetFrameworks>");
        project.ShouldContain("<IsPackable>false</IsPackable>");
        program.ShouldContain("BenchmarkSwitcher");
        program.ShouldContain(".FromAssembly(typeof(Program).Assembly)");
        program.ShouldContain(".Run(args,");
        program.ShouldContain("Job.Default.DontEnforcePowerPlan().AsMutator()");
        program.ShouldNotContain(".AsDefault()");

        var benchmarkFiles = Directory.GetFiles(
                benchmarkDirectory,
                "*Benchmarks.cs",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        benchmarkFiles.ShouldBe(new[]
        {
            "FlowValueDeliveryBenchmarks.cs",
            "PayloadSizeDeliveryBenchmarks.cs"
        });

        var canonical = ReadBenchmarkSource(benchmarkDirectory, "FlowValueDeliveryBenchmarks.cs");
        var payload = ReadBenchmarkSource(benchmarkDirectory, "PayloadSizeDeliveryBenchmarks.cs");
        AssertBenchmarkClass(canonical, expectedBenchmarkMethods: 2, expectedSetups: 2);
        AssertBenchmarkClass(payload, expectedBenchmarkMethods: 2, expectedSetups: 2);
        canonical.ShouldContain("[GlobalCleanup(Target = nameof(SendThroughApplicationGraph))]");
        canonical.ShouldContain("[GlobalCleanup(Target = nameof(SendThroughPipelineAndFanout))]");
        canonical.ShouldContain("[GlobalSetup(Target = nameof(SendThroughApplicationGraph))]");
        canonical.ShouldContain("[GlobalSetup(Target = nameof(SendThroughPipelineAndFanout))]");
        payload.ShouldContain("[GlobalCleanup]");
        payload.ShouldContain("[GlobalSetup(Target = nameof(ForwardStructuredValue))]");
        payload.ShouldContain("[GlobalSetup(Target = nameof(ForwardBinaryContent))]");
        canonical.ShouldContain("[Params(1, 8, 32)]");
        canonical.ShouldContain("[Params(1, 2, 8)]");
        CountOccurrences(canonical, "[Params(").ShouldBe(2);
        payload.ShouldContain("[Params(1, 8, 32)]");
        payload.ShouldContain("[Params(1, 2, 8)]");
        payload.ShouldContain("[Params(1024, 1048576)]");
        CountOccurrences(payload, "[Params(").ShouldBe(3);

        new[] { canonical, payload }
            .Sum(CountBenchmarkCases).ShouldBe(54);
        var scopedSolution = File.ReadAllText(Path.Combine(root, "benchmarks", "FluxFlow.Benchmarks.sln"));
        scopedSolution.ShouldContain(@"FluxFlow.Engine.Benchmarks\FluxFlow.Engine.Benchmarks.csproj");
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

    private static string ReadBenchmarkSource(string directory, string fileName)
        => File.ReadAllText(Path.Combine(directory, fileName));

    private static void AssertBenchmarkClass(string source, int expectedBenchmarkMethods, int expectedSetups = 1)
    {
        CountOccurrences(source, "[MemoryDiagnoser]").ShouldBe(1);
        Regex.Matches(source, @"^\s*\[GlobalSetup(?:\(|\])", RegexOptions.Multiline)
            .Count.ShouldBe(expectedSetups);
        Regex.Matches(
                source,
                @"^\s*\[Benchmark(?:\(|\])",
                RegexOptions.CultureInvariant | RegexOptions.Multiline)
            .Count.ShouldBe(expectedBenchmarkMethods);
        Regex.Matches(
                source,
                @"\[Benchmark(?:\([^\]]*\))?\]\s+public\s+[^\r\n(]+\([^)]*\)\s*(?:=>|\{)",
                RegexOptions.CultureInvariant)
            .Count.ShouldBe(expectedBenchmarkMethods);
    }

    private static int CountOccurrences(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;

    private static int CountBenchmarkCases(string source)
    {
        var methods = Regex.Matches(source, @"^\s*\[Benchmark(?:\(|\])",
            RegexOptions.CultureInvariant | RegexOptions.Multiline).Count;
        return Regex.Matches(source, @"\[Params\(([^)]*)\)\]", RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Aggregate(methods, (count, parameters) =>
                count * parameters.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
