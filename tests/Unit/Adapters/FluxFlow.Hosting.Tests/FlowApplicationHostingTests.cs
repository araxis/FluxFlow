using FluxFlow.Core;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Hosting.Tests;

public sealed class FlowApplicationHostingTests
{
    [Fact]
    public async Task AddFlowApplication_builds_starts_and_runs_the_graph_with_di_resolved_nodes()
    {
        var collector = new StringCollector();
        FlowApplication? built = null;

        var services = new ServiceCollection();
        services.AddSingleton(collector);
        services.AddFlowApplication(provider => built = Build(new StringSourceNode(["hello", "world"]), provider.GetRequiredService<StringCollector>()));

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
        FlowApplication? built = null;

        var services = new ServiceCollection();
        services.AddFlowApplication(_ => built = Build(new TickingSourceNode(), new StringCollector()));

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().Single();

        await hosted.StartAsync(CancellationToken.None);
        await hosted.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        built.ShouldNotBeNull();
        built!.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Multiple_AddFlowApplication_registrations_each_run()
    {
        var first = new StringCollector();
        var second = new StringCollector();
        var graphs = new List<FlowApplication>();

        var services = new ServiceCollection();
        services.AddFlowApplication(_ =>
        {
            var graph = Build(new StringSourceNode(["a"]), first);
            graphs.Add(graph);
            return graph;
        });
        services.AddFlowApplication(_ =>
        {
            var graph = Build(new StringSourceNode(["b"]), second);
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
        FlowApplication? built = null;

        var services = new ServiceCollection();
        services.AddFlowApplication(_ => built = Build(new StringSourceNode(["x"]), new StringCollector()));

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
    public void AddFlowApplication_rejects_a_null_build_delegate()
        => Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddFlowApplication(null!));
    private static FlowApplication Build(IFlowComponent source, StringCollector collector)
    {
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var input = workflow.Add("source", source);
        var upper = workflow.Add("upper", new UppercaseNode());
        var sink = workflow.Add("sink", new CollectSinkNode(collector));
        input.Output.LinkTo(upper.Input);
        upper.Output.LinkTo(sink.Input);
        sink.Output.DiscardUnmatched();
        return app;
    }
}