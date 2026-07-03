using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class CompositionRuntimeBuilderTests
{
    [Fact]
    public async Task Runtime_runs_fluent_definition()
    {
        var collector = new StringCollector();
        var services = new TestServiceProvider().Add(collector);
        var definition = CreateTextPipeline(["hello", "world"]);

        var result = await new CompositionRuntimeBuilder(TestCompositionRegistry.Create())
            .BuildAsync(definition, services);

        result.Succeeded.ShouldBeTrue(string.Join(Environment.NewLine, result.Diagnostics));
        await using var runtime = result.Runtime.ShouldNotBeNull();

        await runtime.StartAsync();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        collector.Items.ShouldBe(["HELLO", "WORLD"]);
    }

    [Fact]
    public async Task Config_and_fluent_definitions_run_equivalent_workflows()
    {
        var fluentCollector = new StringCollector();
        var configCollector = new StringCollector();
        var registry = TestCompositionRegistry.Create();
        var builder = new CompositionRuntimeBuilder(registry);

        var fluentResult = await builder.BuildAsync(
            CreateTextPipeline(["alpha", "beta"]),
            new TestServiceProvider().Add(fluentCollector));
        var configResult = await builder.BuildAsync(
            CreateConfigEquivalentDefinition(),
            new TestServiceProvider().Add(configCollector));

        await using var fluentRuntime = fluentResult.Runtime.ShouldNotBeNull();
        await using var configRuntime = configResult.Runtime.ShouldNotBeNull();

        await fluentRuntime.StartAsync();
        await configRuntime.StartAsync();
        await fluentRuntime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await configRuntime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        configCollector.Items.ShouldBe(fluentCollector.Items);
    }

    [Fact]
    public async Task Runtime_stop_completes_running_source()
    {
        var collector = new StringCollector();
        var definition = CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow => workflow
                .Node("source", TestNodeTypes.TickingSource)
                .Node("sink", TestNodeTypes.Sink)
                .Link("source.Output", "sink.Input"))
            .Build();

        var result = await new CompositionRuntimeBuilder(TestCompositionRegistry.Create())
            .BuildAsync(definition, new TestServiceProvider().Add(collector));

        await using var runtime = result.Runtime.ShouldNotBeNull();
        await runtime.StartAsync();
        await runtime.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        runtime.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Factory_failure_disposes_nodes_that_were_already_created()
    {
        var tracker = new BuildTracker();
        var definition = CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow => workflow
                .Node("created", TestNodeTypes.TrackedSource)
                .Node("failing", TestNodeTypes.Failing))
            .Build();

        var result = await new CompositionRuntimeBuilder(TestCompositionRegistry.Create(tracker))
            .BuildAsync(definition);

        result.Succeeded.ShouldBeFalse();
        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed);
        tracker.DisposedNodes.ShouldBe(1);
    }

    [Fact]
    public async Task Build_cancellation_disposes_nodes_that_were_already_created()
    {
        var tracker = new BuildTracker();
        using var cancellation = new CancellationTokenSource();
        var registry = new CompositionNodeRegistry()
            .Register(
                "test.cancel-after-created",
                _ =>
                {
                    var node = new TrackedSourceNode(tracker);
                    cancellation.Cancel();
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        outputs: [CompositionPorts.Output<string>("Output", node.Output)],
                        events: node.Events,
                        errors: node.Errors));
                },
                outputs: [CompositionPorts.Metadata<string>("Output")])
            .Register(
                "test.never-created",
                _ => throw new InvalidOperationException("factory should not run"));
        var definition = CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow => workflow
                .Node("created", "test.cancel-after-created")
                .Node("never", "test.never-created"))
            .Build();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await new CompositionRuntimeBuilder(registry)
                .BuildAsync(definition, cancellationToken: cancellation.Token));

        tracker.DisposedNodes.ShouldBe(1);
    }

    [Fact]
    public async Task Factory_cancellation_propagates_when_build_token_is_canceled()
    {
        using var cancellation = new CancellationTokenSource();
        var registry = new CompositionNodeRegistry()
            .Register(
                "test.canceled-factory",
                _ =>
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                });
        var definition = CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow => workflow.Node("source", "test.canceled-factory"))
            .Build();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await new CompositionRuntimeBuilder(registry)
                .BuildAsync(definition, cancellationToken: cancellation.Token));
    }

    private static CompositionDefinition CreateTextPipeline(string[] messages)
        => CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow => workflow
                .Node("source", TestNodeTypes.Source, node => node.Configure("messages", messages))
                .Node("upper", TestNodeTypes.Uppercase)
                .Node("sink", TestNodeTypes.Sink)
                .Link("source.Output", "upper.Input")
                .Link("upper.Output", "sink.Input"))
            .Build();

    private static CompositionDefinition CreateConfigEquivalentDefinition()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""
        {
          "workflows": {
            "main": {
              "nodes": {
                "source": {
                  "type": "test.source",
                  "configuration": {
                    "messages": [ "alpha", "beta" ]
                  }
                },
                "upper": {
                  "type": "test.uppercase"
                },
                "sink": {
                  "type": "test.sink"
                }
              },
              "links": [
                { "from": "source.Output", "to": "upper.Input" },
                { "from": "upper.Output", "to": "sink.Input" }
              ]
            }
          }
        }
        """));

        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        return new CompositionConfigurationLoader().Load(configuration, sectionName: "");
    }
}
