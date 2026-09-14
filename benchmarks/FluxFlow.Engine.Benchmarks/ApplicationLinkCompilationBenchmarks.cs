using Microsoft.Extensions.DependencyInjection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.Links;

namespace FluxFlow.Engine.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
public class ApplicationLinkCompilationBenchmarks
{
    private ApplicationSetup _definition = null!;
    private ApplicationLinkCompiler _compiler = null!;

    [Params(1, 32, 128)]
    public int LinkCount { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("main");
        EchoHandle? previous = null;

        for (var index = 0; index <= LinkCount; index++)
        {
            var current = workflow.AddComponent(
                $"echo-{index}",
                "benchmark.echo", BenchmarkComponents.CreateEcho, static _ => { }, static component => new EchoHandle(component));
            previous?.Output.LinkTo(current.Input);
            previous = current;
        }

        _definition = builder.Build();
        using var services = new ServiceCollection().BuildServiceProvider();
        var factory = FluxFlowRegistrationExtensions.CreateFactoryMap(services, _definition.ComponentFactories)["benchmark.echo"];
        foreach (var (name, component) in _definition.Definition.Workflows["main"].Components)
            _instances.Add("main." + name, await factory(new ComponentActivationContext(services, "main", name, component)));
        _compiler = new ApplicationLinkCompiler(_instances);
    }

    private readonly Dictionary<string, ComponentInstance> _instances = new();

    [GlobalCleanup]
    public async Task Cleanup()
    {
        foreach (var instance in _instances.Values) await instance.DisposeAsync();
        _instances.Clear();
    }

    [Benchmark]
    public ApplicationLinkCompilationResult Compile()
        => _compiler.Compile(_definition);
}
