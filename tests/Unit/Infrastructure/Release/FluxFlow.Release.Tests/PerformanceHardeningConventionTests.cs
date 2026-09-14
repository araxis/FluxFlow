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
            "ApplicationLinkCompilationBenchmarks.cs",
            "ApplicationMessagingBenchmarks.cs",
            "ApplicationTopologyBenchmarks.cs",
            "ExpressionBindingBenchmarks.cs",
            "FlowValueDeliveryBenchmarks.cs",
            "GraphConditionBenchmarks.cs",
            "PayloadSizeDeliveryBenchmarks.cs"
        });

        var messaging = ReadBenchmarkSource(benchmarkDirectory, "ApplicationMessagingBenchmarks.cs");
        var topology = ReadBenchmarkSource(benchmarkDirectory, "ApplicationTopologyBenchmarks.cs");
        var canonical = ReadBenchmarkSource(benchmarkDirectory, "FlowValueDeliveryBenchmarks.cs");
        var binding = ReadBenchmarkSource(benchmarkDirectory, "ExpressionBindingBenchmarks.cs");
        var payload = ReadBenchmarkSource(benchmarkDirectory, "PayloadSizeDeliveryBenchmarks.cs");
        var conditions = ReadBenchmarkSource(benchmarkDirectory, "GraphConditionBenchmarks.cs");
        var compilation = ReadBenchmarkSource(
            benchmarkDirectory,
            "ApplicationLinkCompilationBenchmarks.cs");
        AssertBenchmarkClass(messaging, expectedBenchmarkMethods: 3);
        AssertBenchmarkClass(topology, expectedBenchmarkMethods: 1);
        AssertBenchmarkClass(compilation, expectedBenchmarkMethods: 1);
        AssertBenchmarkClass(canonical, expectedBenchmarkMethods: 2, expectedSetups: 2);
        AssertBenchmarkClass(binding, expectedBenchmarkMethods: 1);
        AssertBenchmarkClass(payload, expectedBenchmarkMethods: 2, expectedSetups: 2);
        AssertBenchmarkClass(conditions, expectedBenchmarkMethods: 4, expectedSetups: 4);
        conditions.ShouldContain("[GlobalCleanup]");
        foreach (var method in new[] { "Unconditional", "NativePredicate", "DocumentExpression", "ConfiguredExpression" })
            conditions.ShouldContain($"[GlobalSetup(Target = nameof({method}))]");
        messaging.ShouldContain("[GlobalCleanup]");
        topology.ShouldContain("[GlobalCleanup]");
        canonical.ShouldContain("[GlobalCleanup(Target = nameof(SendThroughApplicationGraph))]");
        canonical.ShouldContain("[GlobalCleanup(Target = nameof(SendThroughPipelineAndFanout))]");
        canonical.ShouldContain("[GlobalSetup(Target = nameof(SendThroughApplicationGraph))]");
        canonical.ShouldContain("[GlobalSetup(Target = nameof(SendThroughPipelineAndFanout))]");
        payload.ShouldContain("[GlobalCleanup]");
        payload.ShouldContain("[GlobalSetup(Target = nameof(ForwardStructuredValue))]");
        payload.ShouldContain("[GlobalSetup(Target = nameof(ForwardBinaryContent))]");
        compilation.ShouldContain("[GlobalCleanup]");
        CountOccurrences(messaging, "[Params(").ShouldBe(0);
        CountOccurrences(topology, "[Params(").ShouldBe(1);
        CountOccurrences(compilation, "[Params(").ShouldBe(1);
        topology.ShouldContain("[Params(1, 8, 32)]");
        compilation.ShouldContain("[Params(1, 32, 128)]");
        canonical.ShouldContain("[Params(1, 8, 32)]");
        canonical.ShouldContain("[Params(1, 2, 8)]");
        CountOccurrences(canonical, "[Params(").ShouldBe(2);
        binding.ShouldContain("[Params(8, 256)]");
        CountOccurrences(binding, "[Params(").ShouldBe(1);
        payload.ShouldContain("[Params(1, 8, 32)]");
        payload.ShouldContain("[Params(1, 2, 8)]");
        payload.ShouldContain("[Params(1024, 1048576)]");
        CountOccurrences(payload, "[Params(").ShouldBe(3);
        conditions.ShouldContain("[Params(1, 8, 32)]");
        CountOccurrences(conditions, "[Params(").ShouldBe(1);

        new[] { messaging, topology, compilation, canonical, binding, payload, conditions }
            .Sum(CountBenchmarkCases).ShouldBe(77);
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

    [Fact]
    public void Unconditional_route_skips_the_only_condition_context_allocation()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src", "Engine", "Runtime", "FluxFlow.Engine",
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
            "src", "Engine", "Runtime", "FluxFlow.Engine",
            "Ports",
            "ApplicationPortEventPublisher.cs"));

        source.ShouldNotContain("params ");
        source.ShouldNotContain(".Where(");
        source.ShouldNotContain(".ToDictionary(");
        CountOccurrences(
            source,
            "private static Dictionary<string, object?> CreateAttributes(").ShouldBe(2);
        source.ShouldContain("new Dictionary<string, object?>(2, StringComparer.Ordinal)");
        source.ShouldContain("new Dictionary<string, object?>(3, StringComparer.Ordinal)");
        CountOccurrences(source, "private static JsonElement CreateDetails(").ShouldBe(2);
        source.ShouldContain("AddAttribute(attributes, first);");
        source.ShouldContain("AddAttribute(attributes, second);");
        source.ShouldContain("AddAttribute(attributes, third);");
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
