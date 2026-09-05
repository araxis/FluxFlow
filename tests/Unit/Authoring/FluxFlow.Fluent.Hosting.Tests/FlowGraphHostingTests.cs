using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Fluent.Hosting.Tests;

public sealed class FlowGraphHostingTests
{
    [Fact]
    public async Task AddFlowGraph_builds_starts_and_runs_the_graph_with_di_resolved_nodes()
    {
        var collector = new StringCollector();
        FlowGraph? built = null;

        var services = new ServiceCollection();
        services.AddSingleton(collector);
        services.AddFlowGraph(provider => built = Flow
            .From(new StringSourceNode(["hello", "world"]))
            .Then(new UppercaseNode())
            .To(new CollectSinkNode(provider.GetRequiredService<StringCollector>()))
            .Build());

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().Single();

        await hosted.StartAsync(CancellationToken.None);
        built.ShouldNotBeNull();
        await built!.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await hosted.StopAsync(CancellationToken.None);

        collector.Items.ShouldBe(["HELLO", "WORLD"]);
    }

    [Fact]
    public async Task Hosted_StopAsync_stops_an_unbounded_source()
    {
        FlowGraph? built = null;

        var services = new ServiceCollection();
        services.AddFlowGraph(_ => built = Flow
            .From(new TickingSourceNode())
            .To(new CollectSinkNode(new StringCollector()))
            .Build());

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().Single();

        await hosted.StartAsync(CancellationToken.None);
        await hosted.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        built.ShouldNotBeNull();
        built!.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Multiple_AddFlowGraph_registrations_each_run()
    {
        var first = new StringCollector();
        var second = new StringCollector();
        var graphs = new List<FlowGraph>();

        var services = new ServiceCollection();
        services.AddFlowGraph(_ =>
        {
            var graph = Flow.From(new StringSourceNode(["a"])).Then(new UppercaseNode()).To(new CollectSinkNode(first)).Build();
            graphs.Add(graph);
            return graph;
        });
        services.AddFlowGraph(_ =>
        {
            var graph = Flow.From(new StringSourceNode(["b"])).Then(new UppercaseNode()).To(new CollectSinkNode(second)).Build();
            graphs.Add(graph);
            return graph;
        });

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        hosted.Count.ShouldBe(2);

        foreach (var service in hosted)
        {
            await service.StartAsync(CancellationToken.None);
        }

        await Task.WhenAll(graphs.Select(graph => graph.Completion.WaitAsync(TimeSpan.FromSeconds(5))));

        first.Items.ShouldBe(["A"]);
        second.Items.ShouldBe(["B"]);
    }

    [Fact]
    public async Task Disposing_the_provider_disposes_the_graph()
    {
        FlowGraph? built = null;

        var services = new ServiceCollection();
        services.AddFlowGraph(_ => built = Flow
            .From(new StringSourceNode(["x"]))
            .To(new CollectSinkNode(new StringCollector()))
            .Build());

        var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().Single();
        await hosted.StartAsync(CancellationToken.None);
        built.ShouldNotBeNull();
        await built!.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // Disposing the provider disposes the singleton hosted service, which disposes the graph.
        await provider.DisposeAsync();

        // The graph's runtime is disposed; disposing again is a safe no-op.
        await Should.NotThrowAsync(async () => await built!.DisposeAsync());
    }

    [Fact]
    public void AddFlowGraph_rejects_a_null_build_delegate()
        => Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddFlowGraph(null!));
}
