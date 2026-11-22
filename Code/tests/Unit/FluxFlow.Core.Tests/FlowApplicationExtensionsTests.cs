using FluxFlow.Application.Addressing;
using FluxFlow.Application.Execution;
using FluxFlow.Application;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;
public sealed class FlowApplicationExtensionsTests
{
    [Fact]
    public async Task Discovery_reads_only_declared_addresses_without_activating_components_or_ports()
    {
        var services = new ServiceCollection();
        services.AddFlowWorkflow("empty");
        var first = ComponentAddress.Parse("main.first");
        var second = ComponentAddress.Parse("other.second");
        foreach (var address in new[] { first, second })
        {
            services.AddComponentFactory(address, _ => throw new InvalidOperationException("Must remain deferred."));
            services.AddInputPort<Echo>(address, "Input", _ => throw new InvalidOperationException("Must remain deferred."));
            services.AddOutputPort<Echo>(address, "Output", _ => throw new InvalidOperationException("Must remain deferred."));
            services.AddOutputPort<Echo>(address, "Selected", _ => throw new InvalidOperationException("Must remain deferred."));
            services.AddEventPort<Echo>(address, "Diagnostics", _ => throw new InvalidOperationException("Must remain deferred."));
        }
        // Host services are not declared application components or ports, even with address-shaped keys.
        services.AddKeyedSingleton<IFlowNode>("host.node", (_, _) => throw new InvalidOperationException("Unrelated service."));
        services.AddKeyedSingleton<ITargetBlock<FlowMessage>>("host.node.Input", (_, _) => throw new InvalidOperationException("Unrelated service."));
        services.AddKeyedSingleton<ISourceBlock<FlowMessage>>("host.node.Output", (_, _) => throw new InvalidOperationException("Unrelated service."));

        await using var provider = services.BuildServiceProvider();
        for (var iteration = 0; iteration < 2; iteration++)
        {
            provider.GetComponentAddresses().ShouldBe([first, second]);
            provider.GetInputPortAddresses().ShouldBe([first.Port("Input"), second.Port("Input")]);
            provider.GetOutputPortAddresses().ShouldBe([
                first.Port("Output"), first.Port("Selected"), first.Port("Diagnostics"),
                second.Port("Output"), second.Port("Selected"), second.Port("Diagnostics")]);
        }
    }

    [Fact]
    public async Task Discovery_supports_empty_providers_and_workflows()
    {
        foreach (var includeWorkflow in new[] { false, true })
        {
            var services = new ServiceCollection();
            if (includeWorkflow) services.AddFlowWorkflow("empty");
            await using var provider = services.BuildServiceProvider();
            provider.GetComponentAddresses().ShouldBeEmpty();
            provider.GetInputPortAddresses().ShouldBeEmpty();
            provider.GetOutputPortAddresses().ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Output_probes_attach_and_detach_while_application_runs_including_event_outputs()
    {
        var services = new ServiceCollection();
        var address = ComponentAddress.Parse("main.source");
        services.AddComponentFactory(address, _ => new Echo())
            .AddInputPort<Echo>(address, "Input", node => node.Input)
            .AddOutputPort<Echo>(address, "Output", node => node.Output)
            .AddEventPort<Echo>(address, "Diagnostics", node => node.Events);
        await using var provider = services.BuildServiceProvider();
        await using var application = await provider.BuildApplicationAsync();
        await application.StartAsync();
        var source = (Echo)application.GetComponent(address);
        using var first = provider.ObserveOutput(address.Port("Output"));
        var message = Message(1);
        await source.Input.SendAsync(message);
        (await first.Messages.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(message);
        first.Dispose();
        await first.Messages.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        using var second = provider.ObserveOutput(address.Port("Output"));
        (await second.Messages.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(message);
        using var events = provider.ObserveOutput(address.Port("Diagnostics"));
        source.Events.Post(Message(2)).ShouldBeTrue();
        var observedEvent = await events.Messages.ReceiveAsync(TimeSpan.FromSeconds(5));
        observedEvent.Value.Json.GetInt32().ShouldBe(2);
        await source.Input.SendAsync(Message(3));
        (await second.Messages.ReceiveAsync(TimeSpan.FromSeconds(5))).Value.Json.GetInt32().ShouldBe(3);

        // An unread probe sample must not delay application shutdown.
        await source.Input.SendAsync(Message(4));
        await application.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        (await second.Messages.ReceiveAsync(TimeSpan.FromSeconds(5))).Value.Json.GetInt32().ShouldBe(4);
        await Task.WhenAll(second.Messages.Completion, events.Messages.Completion).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Direct_extensions_route_real_messages_with_captured_predicates_and_cross_workflow_ports()
    {
        var services = new ServiceCollection();
        var archiveNode = new Sink();
        var selectedNode = new Sink();
        var source = new Echo();
        services.AddComponentFactory(ComponentAddress.Parse("archive.sink"), _ => archiveNode).AddInputPort<Sink>(ComponentAddress.Parse("archive.sink"), "Input", node => node.Input).AddComponentFactory(ComponentAddress.Parse("main.selected"), _ => selectedNode).AddInputPort<Sink>(ComponentAddress.Parse("main.selected"), "Input", node => node.Input).AddComponentFactory(ComponentAddress.Parse("main.source"), _ => source).AddInputPort<Echo>(ComponentAddress.Parse("main.source"), "Input", node => node.Input).AddOutputPort<Echo>(ComponentAddress.Parse("main.source"), "Output", node => node.Output);
        var threshold = 5;
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("archive.sink.Input")).Output.ShouldBeSameAs(source.Output);
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.selected.Input"), message => message.Value.Json.GetInt32() > threshold);
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        var sourceAddress = provider.GetComponentAddresses().Single(address => address.ComponentName == "source");
        provider.GetWorkflowServices(sourceAddress.WorkflowName).GetRequiredKeyedService<IFlowNode>(sourceAddress.Value).ShouldBeSameAs(source);
        var outputAddress = provider.GetOutputPortAddresses().Single();
        provider.GetWorkflowServices(outputAddress.ComponentAddress!.WorkflowName).GetRequiredKeyedService<ISourceBlock<FlowMessage>>(outputAddress.Value).ShouldBeSameAs(source.Output);
        var selectedInput = provider.GetInputPortAddresses().Single(address => address.ComponentAddress!.ComponentName == "selected");
        provider.GetWorkflowServices(selectedInput.ComponentAddress!.WorkflowName).GetRequiredKeyedService<ITargetBlock<FlowMessage>>(selectedInput.Value).ShouldBeSameAs(selectedNode.Input);
        await source.Input.SendAsync(Message(3));
        await source.Input.SendAsync(Message(7));
        await appExecution.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        archiveNode.Values.ShouldBe([3, 7]);
        selectedNode.Values.ShouldBe([7]);
    }

    [Fact]
    public async Task Native_link_delegates_keep_fan_in_and_reject_late_routes()
    {
        var first = new Echo();
        var second = new Echo();
        var sinkNode = new Sink();
        var services = new ServiceCollection();
        foreach (var(name, node)in new[]
        {
            ("first", first),
            ("second", second)
        }

        )
            services.AddComponentFactory(ComponentAddress.Parse("main." + name), _ => node).AddInputPort<Echo>(ComponentAddress.Parse("main." + name), "Input", value => value.Input).AddOutputPort<Echo>(ComponentAddress.Parse("main." + name), "Output", value => value.Output);
        services.AddComponentFactory(ComponentAddress.Parse("main.sink"), _ => sinkNode).AddInputPort<Sink>(ComponentAddress.Parse("main.sink"), "Input", node => node.Input);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.first.Output"), PortAddress.Parse("main.sink.Input"));
            ports.Link(PortAddress.Parse("main.second.Output"), PortAddress.Parse("main.sink.Input"), message => message.Value.Json.GetInt32() > 0);
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await first.Input.SendAsync(Message(1));
        await second.Input.SendAsync(Message(-1));
        await second.Input.SendAsync(Message(2));
        await appExecution.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        sinkNode.Values.Order().ShouldBe([1, 2]);
        Should.Throw<InvalidOperationException>(() => provider.Link(PortAddress.Parse("main.first.Output"), PortAddress.Parse("main.sink.Input")));
    }

    private static FlowMessage Message(int value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));
    private sealed class Echo : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Echo()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => Task.FromResult<IEnumerable<FlowMessage>>([message]);
    }

    private sealed class Sink : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Sink()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        public List<int> Values { get; } = [];

        private Task ProcessAsync(FlowMessage message)
        {
            Values.Add(message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return Task.CompletedTask;
        }
    }
}
