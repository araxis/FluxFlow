using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;

namespace FluxFlow.Engine.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
public class ApplicationLinkCompilationBenchmarks
{
    private ApplicationDefinition _definition = null!;
    private ApplicationLinkCompiler _compiler = null!;

    [Params(1, 32, 128)]
    public int LinkCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("main");
        EchoHandle? previous = null;

        for (var index = 0; index <= LinkCount; index++)
        {
            var current = workflow.AddComponent(
                $"echo-{index}",
                BenchmarkComponents.Echo);
            previous?.Output.ConnectTo(current.Input);
            previous = current;
        }

        _definition = builder.Build();
        _compiler = new ApplicationLinkCompiler(
            new ComponentCatalog([BenchmarkComponents.Echo.Descriptor]));
    }

    [Benchmark]
    public ApplicationLinkCompilationResult Compile()
        => _compiler.Compile(_definition);
}
