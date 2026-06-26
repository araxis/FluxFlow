using System.Text;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Hosting.Tests;

public sealed class CompositionRuntimeHostTests
{
    [Fact]
    public async Task Hosted_runtime_resolves_keyed_resources_and_runs_definition()
    {
        var collector = new TextCollector();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITextCollector>("primary", collector);
        services
            .AddFluxFlowComposition(CreateDefinition(["alpha", "beta"], includeResource: true))
            .RegisterNodes(RegisterTestNodes);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);
        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(CancellationToken.None);

        collector.Items.ShouldBe(["ALPHA", "BETA"]);
        host.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task Hosted_runtime_trims_configured_resource_keys()
    {
        var collector = new TextCollector();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITextCollector>("primary", collector);
        services
            .AddFluxFlowComposition(CreateDefinition(["spaced"], includeResource: true, resourceKey: " primary "))
            .RegisterNodes(RegisterTestNodes);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);
        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["SPACED"]);
        host.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Resource_helpers_trim_resource_slot_names_and_keys()
    {
        var collector = new TextCollector();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITextCollector>("primary", collector);
        using var provider = services.BuildServiceProvider();
        var context = new CompositionNodeFactoryContext(
            provider,
            "main",
            "sink",
            new NodeDefinition
            {
                Type = "test.hosting.sink",
                Resources =
                {
                    ["collector"] = " primary "
                }
            });

        context.GetRequiredResourceKey(" collector ").ShouldBe("primary");
        context.GetRequiredResource<ITextCollector>(" collector ").ShouldBeSameAs(collector);
        context.GetResource<ITextCollector>(" collector ").ShouldBeSameAs(collector);
    }

    [Fact]
    public async Task Hosted_runtime_loads_definition_from_configuration()
    {
        var collector = new TextCollector();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITextCollector>("primary", collector);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
        {
          "FluxFlow": {
            "Composition": {
              "workflows": {
                "main": {
                  "nodes": {
                    "source": {
                      "type": "test.hosting.source",
                      "configuration": {
                        "messages": [ "one", "two" ]
                      }
                    },
                    "sink": {
                      "type": "test.hosting.sink",
                      "resources": {
                        "collector": "primary"
                      }
                    }
                  },
                  "links": [
                    { "from": "source.Output", "to": "sink.Input" }
                  ]
                }
              }
            }
          }
        }
        """));

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        services
            .AddFluxFlowComposition(configuration)
            .RegisterNodes(RegisterTestNodes);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);
        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["ONE", "TWO"]);
    }

    [Fact]
    public async Task Hosted_runtime_can_build_without_starting()
    {
        var collector = new TextCollector();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITextCollector>("primary", collector);
        services
            .AddFluxFlowComposition(CreateDefinition(["manual"], includeResource: true))
            .RegisterNodes(RegisterTestNodes)
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);
        collector.Items.ShouldBeEmpty();

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldNotBeNull();
        await host.StartRuntimeAsync(CancellationToken.None);
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["MANUAL"]);
    }

    [Fact]
    public async Task Hosted_runtime_start_is_idempotent()
    {
        var source = new CountingSourceNode();
        var services = new ServiceCollection();
        services.AddSingleton(source);
        services
            .AddFluxFlowComposition(CreateLifecycleDefinition())
            .RegisterNodes(RegisterLifecycleNodes);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StartAsync(CancellationToken.None);

        source.StartCount.ShouldBe(1);
    }

    [Fact]
    public async Task Hosted_runtime_stop_is_idempotent()
    {
        var source = new CountingSourceNode();
        var services = new ServiceCollection();
        services.AddSingleton(source);
        services
            .AddFluxFlowComposition(CreateLifecycleDefinition())
            .RegisterNodes(RegisterLifecycleNodes);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);
        await hostedService.StartAsync(CancellationToken.None);

        source.StartCount.ShouldBe(1);
        source.CompleteCount.ShouldBe(1);
    }

    [Fact]
    public async Task Hosted_runtime_surfaces_diagnostics_without_throwing_when_configured()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node("missing", "missing.type"))
                .Build())
            .RegisterNodes(RegisterTestNodes)
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.UnknownNodeType);
    }

    [Fact]
    public async Task Missing_resource_reference_fails_build_with_host_exception()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CreateDefinition(["orphan"], includeResource: false))
            .RegisterNodes(RegisterTestNodes);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        var exception = await Should.ThrowAsync<CompositionHostingException>(
            () => hostedService.StartAsync(CancellationToken.None));

        exception.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed);
    }

    private static CompositionDefinition CreateDefinition(
        string[] messages,
        bool includeResource,
        string resourceKey = "primary")
        => CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow =>
            {
                workflow
                    .Node("source", "test.hosting.source", node => node.Configure("messages", messages))
                    .Node("sink", "test.hosting.sink", node =>
                    {
                        if (includeResource)
                            node.Resource("collector", resourceKey);
                    })
                    .Link("source.Output", "sink.Input");
            })
            .Build();

    private static CompositionDefinition CreateLifecycleDefinition()
        => CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow => workflow.Node("source", "test.hosting.lifecycle-source"))
            .Build();

    private static void RegisterTestNodes(CompositionNodeRegistry registry)
    {
        registry
            .Register(
                "test.hosting.source",
                context =>
                {
                    var options = context.BindConfiguration<SourceOptions>();
                    var node = new TextSourceNode(options.Messages);
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        outputs: [CompositionPorts.Output<string>("Output", node.Output)],
                        events: node.Events,
                        errors: node.Errors));
                },
                outputs: [CompositionPorts.Metadata<string>("Output")])
            .Register(
                "test.hosting.sink",
                context =>
                {
                    var collector = context.GetRequiredResource<ITextCollector>("collector");
                    var node = new UppercaseSinkNode(collector);
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        inputs: [CompositionPorts.Input<string>("Input", node.Input)],
                        events: node.Events,
                        errors: node.Errors));
                },
                inputs: [CompositionPorts.Metadata<string>("Input")]);
    }

    private static void RegisterLifecycleNodes(CompositionNodeRegistry registry)
    {
        registry.Register(
            "test.hosting.lifecycle-source",
            context =>
            {
                var node = context.Services.GetRequiredService<CountingSourceNode>();
                return ValueTask.FromResult(ComposedNode.Create(node));
            });
    }

    private sealed record SourceOptions
    {
        public string[] Messages { get; init; } = [];
    }

    private interface ITextCollector
    {
        IReadOnlyList<string> Items { get; }

        void Add(string value);
    }

    private sealed class TextCollector : ITextCollector
    {
        private readonly List<string> _items = [];

        public IReadOnlyList<string> Items
        {
            get
            {
                lock (_items)
                {
                    return _items.ToArray();
                }
            }
        }

        public void Add(string value)
        {
            lock (_items)
            {
                _items.Add(value);
            }
        }
    }

    private sealed class TextSourceNode(IReadOnlyList<string> messages) : FlowSource<string>
    {
        protected override Task RunAsync(CancellationToken cancellationToken)
        {
            foreach (var message in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Emit(FlowMessage.Create(message));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CountingSourceNode : IFlowSource
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _completeCount;
        private int _disposeCount;
        private int _startCount;

        public int CompleteCount => _completeCount;

        public int DisposeCount => _disposeCount;

        public int StartCount => _startCount;

        public Task Completion => _completion.Task;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _startCount);
            return Task.CompletedTask;
        }

        public void Complete()
        {
            Interlocked.Increment(ref _completeCount);
            _completion.TrySetResult();
        }

        public void Fault(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _completion.TrySetException(exception);
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            _completion.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UppercaseSinkNode(ITextCollector collector) : FlowNode<string, string>
    {
        protected override Task ProcessAsync(FlowMessage<string> message)
        {
            collector.Add(message.Payload.ToUpperInvariant());
            Emit(message);
            return Task.CompletedTask;
        }
    }
}
