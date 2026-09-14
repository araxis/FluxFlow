using System.Text.Json;
using FluxFlow.Composition.Definition;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Internal.Snapshots;
using FluxFlow.Engine.Ports;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationRuntimePortBindingTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly ApplicationAddress Input = ApplicationAddress.WorkflowPort("Main", "Node", "Input");
    private static readonly ApplicationAddress Ack = ApplicationAddress.WorkflowPort("Main", "Node", "Ack");
    private static readonly ApplicationAddress Output = ApplicationAddress.WorkflowPort("Main", "Node", "Output");
    private static readonly ApplicationRuntimeComponentKey Key = new("Main", "Node");
    private static readonly ApplicationDefinition Definition = ApplicationDefinitionJson.Deserialize(
        """{"Resources":{},"Workflows":{"Main":{"Node":{"Type":"test.native"}}}}""");

    [Fact]
    public async Task Plain_metadata_builds_sorted_message_and_control_surface_without_activation()
    {
        var factory = SurfaceFactory();
        var surface = factory.Describe(Definition, SurfaceInstances(Descriptor()));
        surface.Select(entry => entry.Address).ShouldBe([Ack, Input, Output]);
        await using var runtime = factory.Create(surface);

        runtime.Ports.Count.ShouldBe(5);
        var input = runtime.Ports.Single(port => port.Address == Input);
        input.Direction.ShouldBe(ApplicationPortDirection.Input);
        input.Kind.ShouldBe(ApplicationPortKind.Message);
        input.PayloadType.ShouldBe(typeof(FlowValue));
        input.Capacity.ShouldBe(3);
        var ack = runtime.Ports.Single(port => port.Address == Ack);
        ack.Direction.ShouldBe(ApplicationPortDirection.Input);
        ack.Kind.ShouldBe(ApplicationPortKind.Signal);
        ack.PayloadType.ShouldBe(typeof(object));
        ack.Capacity.ShouldBe(3);
        var output = runtime.Ports.Single(port => port.Address == Output);
        output.Direction.ShouldBe(ApplicationPortDirection.Output);
        output.Kind.ShouldBe(ApplicationPortKind.Message);
        output.PayloadType.ShouldBe(typeof(FlowValue));
        output.Capacity.ShouldBe(5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Surface_rejects_noncanonical_business_metadata_before_activation(bool output)
    {
        var metadata = new ComponentPortMetadata(output ? "Output" : "Input", typeof(string));
        var descriptor = SurfaceDefinition(inputs: output ? [] : [metadata], outputs: output ? [metadata] : []);
        var error = Should.Throw<ApplicationRuntimeAssemblerException>(() =>
            SurfaceFactory().Describe(Definition, SurfaceInstances(descriptor)));
        error.Message.ShouldContain("Main.Node");
        error.Message.ShouldContain(metadata.Name);
        error.Message.ShouldContain("canonical workflow messages");
    }

    [Fact]
    public void Surface_rejects_signal_output_without_activation()
    {
        var descriptor = SurfaceDefinition(outputs: [ComponentPortMetadata.CreateSignal("Output")]);
        var factory = SurfaceFactory();
        var surface = factory.Describe(Definition, SurfaceInstances(descriptor));
        Should.Throw<ApplicationRuntimeAssemblerException>(() => factory.Create(surface))
            .Message.ShouldBe("Signal output 'Main.Node.Output' is unsupported.");
    }

    [Fact]
    public async Task Workflow_views_preserve_native_message_identity_and_do_not_own_components()
    {
        await using var node = new TestNode();
        await using var instance = Instance(node);
        var snapshots = new List<CompositionServiceProviderSnapshot>();
        ApplicationRuntimePortBinder.AddWorkflowSnapshots(Definition, "views", Components(instance), snapshots);
        await using var snapshot = snapshots.ShouldHaveSingleItem();
        snapshot.Name.ShouldBe("workflow:Main:views");
        snapshot.Boundary.ShouldBe(CompositionProviderBoundary.WorkflowRevision);
        var services = snapshot.Services;
        var address = ApplicationAddress.WorkflowComponent("Main", "Node").Value;
        services.GetRequiredKeyedService<ComponentInstance>(address).ShouldBeSameAs(instance);
        var nodeView = services.GetRequiredKeyedService<IFlowNode>(address);
        nodeView.Completion.ShouldBeSameAs(node.Completion);
        await nodeView.DisposeAsync();
        node.DisposeCalls.ShouldBe(0);

        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("native", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        using var timeout = new CancellationTokenSource(Deadline);
        (await services.GetRequiredKeyedService<ITargetBlock<FlowMessage>>(Input.Value)
            .SendAsync(message, timeout.Token)).ShouldBeTrue();
        (await node.Input.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        var control = services.GetRequiredKeyedService<ITargetBlock<FlowMessage>>(Ack.Value);
        control.Completion.ShouldBeSameAs(node.Ack.Completion);
        (await control.SendAsync(message, timeout.Token)).ShouldBeTrue();
        (await node.Ack.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            control.SendAsync(message, canceled.Token).WaitAsync(Deadline));
        node.Ack.Count.ShouldBe(0);
        var output = services.GetRequiredKeyedService<ISourceBlock<FlowMessage>>(Output.Value);
        output.Completion.ShouldBeSameAs(node.Output.Completion);
        var received = output.ReceiveAsync(Deadline);
        (await node.Output.SendAsync(message, timeout.Token)).ShouldBeTrue();
        (await received).ShouldBeSameAs(message);

        await snapshot.DisposeAsync();
        node.DisposeCalls.ShouldBe(0);
        node.Completion.IsCompleted.ShouldBeFalse();
        (await node.Input.SendAsync(message, timeout.Token)).ShouldBeTrue();
        (await node.Input.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        (await node.Ack.SendAsync(message, timeout.Token)).ShouldBeTrue();
        (await node.Ack.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        (await node.Output.SendAsync(message, timeout.Token)).ShouldBeTrue();
        (await node.Output.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
    }

    [Fact]
    public async Task Disposing_old_revision_keeps_new_native_message_control_and_output_bindings()
    {
        await using var first = new TestNode();
        await using var second = new TestNode();
        await using var firstInstance = Instance(first);
        await using var secondInstance = Instance(second);
        var factory = SurfaceFactory();
        await using var runtime = factory.Create(factory.Describe(Definition, SurfaceInstances(Descriptor())));
        await using var firstBuilder = runtime.CreateRevision("first");
        ApplicationRuntimePortBinder.ConfigureRevision(firstBuilder, Components(firstInstance));
        firstBuilder.SetLinks([], discardedOutputs: [Output]);
        await using var firstRevision = firstBuilder.Build();
        await using var firstLease = await firstRevision.ActivateAsync();
        await AssertDelivery(runtime, first, "before");

        await using var secondBuilder = runtime.CreateRevision("second");
        ApplicationRuntimePortBinder.ConfigureRevision(secondBuilder, Components(secondInstance));
        secondBuilder.SetLinks([], discardedOutputs: [Output]);
        await using var secondRevision = secondBuilder.Build();
        await using var secondLease = await secondRevision.ActivateAsync();
        await firstLease.DisposeAsync();
        runtime.CurrentRevision.ShouldNotBeNull().RevisionId.ShouldBe("second");
        await AssertDelivery(runtime, second, "after");
        first.Input.Count.ShouldBe(0);
        first.Ack.Count.ShouldBe(0);
        first.DisposeCalls.ShouldBe(0);
        second.DisposeCalls.ShouldBe(0);
        var detached = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("detached", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        first.Output.Post(detached).ShouldBeTrue();
        (await first.Output.ReceiveAsync(Deadline)).ShouldBeSameAs(detached);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Both_binding_paths_reject_wrong_input_block_kind(bool signal, bool snapshot)
    {
        await using var node = new TestNode();
        var metadata = signal ? ComponentPortMetadata.CreateSignal("Input")
            : new ComponentPortMetadata("Input", typeof(FlowValue));
        var descriptor = SurfaceDefinition(inputs: [metadata]);
        ComponentInputPort physical = signal ? ComponentPorts.Input("Input", node.Input)
            : ComponentPorts.SignalInput("Input", node.Ack);
        await using var instance = new ComponentInstance(node, inputs: [physical]);
        await using var runtime = signal ? new ApplicationPortRuntimeBuilder().AddSignalInput(Input, 3).Build()
            : new ApplicationPortRuntimeBuilder().AddInput(Input, 3).Build();
        await using var builder = runtime.CreateRevision("invalid");
        var components = new Dictionary<ApplicationRuntimeComponentKey, ComponentInstance>
        { [Key] = WithMetadata(instance, descriptor) };

        var error = Should.Throw<ApplicationRuntimeAssemblerException>(() =>
        {
            if (snapshot) ApplicationRuntimePortBinder.AddWorkflowSnapshots(Definition, "invalid", components, []);
            else ApplicationRuntimePortBinder.ConfigureRevision(builder, components);
        });
        error.Message.ShouldContain("Main.Node.Input");
        error.Message.ShouldContain("wrong kind");
        runtime.CurrentRevision.ShouldBeNull();
        node.DisposeCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Both_binding_paths_reject_wrong_output_block_kind(bool signal, bool snapshot)
    {
        await using var node = new TestNode();
        var metadata = signal ? ComponentPortMetadata.CreateSignal("Output")
            : new ComponentPortMetadata("Output", typeof(string));
        var descriptor = SurfaceDefinition(outputs: [metadata]);
        var physical = ComponentPorts.Output("Output", node.Output);
        await using var instance = new ComponentInstance(node, outputs: [physical]);
        await using var runtime = new ApplicationPortRuntimeBuilder().AddOutput(Output, 5).Build();
        await using var builder = runtime.CreateRevision("invalid");
        var components = new Dictionary<ApplicationRuntimeComponentKey, ComponentInstance>
        { [Key] = WithMetadata(instance, descriptor) };

        var error = Should.Throw<ApplicationRuntimeAssemblerException>(() =>
        {
            if (snapshot) ApplicationRuntimePortBinder.AddWorkflowSnapshots(Definition, "invalid", components, []);
            else ApplicationRuntimePortBinder.ConfigureRevision(builder, components);
        });
        error.Message.ShouldContain("Main.Node.Output");
        error.Message.ShouldContain(signal ? "unsupported" : "canonical workflow messages");
        runtime.CurrentRevision.ShouldBeNull();
        node.DisposeCalls.ShouldBe(0);
    }

    private static async Task AssertDelivery(ApplicationPortRuntime runtime, TestNode node, string value)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var inputResult = await runtime.SendAsync(Input, message, timeout.Token);
        inputResult.Status.ShouldBe(PortSendStatus.Accepted);
        inputResult.Port.ShouldBe(Input);
        (await node.Input.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        var controlResult = await runtime.SendAsync(Ack, message, timeout.Token);
        controlResult.Status.ShouldBe(PortSendStatus.Accepted);
        controlResult.Port.ShouldBe(Ack);
        (await node.Ack.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        var receive = runtime.ReceiveAsync<FlowValue>(Output, Deadline, timeout.Token);
        (await node.Output.SendAsync(message, timeout.Token)).ShouldBeTrue();
        var result = await receive;
        result.Status.ShouldBe(PortReceiveStatus.Received);
        result.Port.ShouldBe(Output);
        result.Message.ShouldNotBeNull().Value.ShouldBeSameAs(message.Value);
        result.Message.MessageId.ShouldBe(message.MessageId);
        result.Message.TraceId.ShouldBe(message.TraceId);
    }

    private static ApplicationRuntimePortSurfaceFactory SurfaceFactory()
        => new(new() { InputCapacity = 3, OutputCapacity = 5 }, null, null);

    private static ComponentInstance Descriptor()
        => SurfaceDefinition(
            inputs: [new("Input", typeof(FlowValue)),
                new("Ack", typeof(object), ComponentPortLinkCardinality.Multiple, ComponentPortKind.Signal)],
            outputs: [new("Output", typeof(FlowValue))]);

    private static ComponentInstance Instance(TestNode node)
        => new(node, inputs: [ComponentPorts.Input("Input", node.Input),
                ComponentPorts.SignalInput("Ack", node.Ack)],
            outputs: [ComponentPorts.Output("Output", node.Output)]);

    private static Dictionary<ApplicationRuntimeComponentKey, ComponentInstance> Components(ComponentInstance instance)
        => new() { [Key] = WithMetadata(instance, Descriptor()) };




    private static ComponentInstance WithMetadata(ComponentInstance instance, ComponentInstance registration)
    {
        instance.InputMetadata = registration.InputMetadata;
        instance.OutputMetadata = registration.OutputMetadata;
        return instance;
    }

    private static IReadOnlyDictionary<string, ComponentInstance> SurfaceInstances(ComponentInstance registration)
        => new Dictionary<string, ComponentInstance>
        {
            ["Main.Node"] = WithMetadata(new ComponentInstance(new MetadataNode()), registration)
        };

    private static ComponentInstance SurfaceDefinition(IEnumerable<ComponentPortMetadata>? inputs = null,
        IEnumerable<ComponentPortMetadata>? outputs = null)
        => new(new MetadataNode())
        {
            InputMetadata = (inputs ?? []).ToDictionary(p => p.Name, StringComparer.Ordinal),
            OutputMetadata = (outputs ?? []).ToDictionary(p => p.Name, StringComparer.Ordinal)
        };

    private sealed class MetadataNode : IFlowNode
    {
        public Task Completion => Task.CompletedTask;
        public void Complete() { }
        public void Fault(Exception error) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestNode : IFlowNode
    {
        public BufferBlock<FlowMessage> Input { get; } = new(new() { BoundedCapacity = 3 });
        public BufferBlock<FlowMessage> Ack { get; } = new(new() { BoundedCapacity = 3 });
        public BufferBlock<FlowMessage> Output { get; } = new(new() { BoundedCapacity = 5 });
        public Task Completion { get; }
        public int DisposeCalls { get; private set; }
        public TestNode() => Completion = Task.WhenAll(Input.Completion, Ack.Completion, Output.Completion);
        public void Complete() { Input.Complete(); Ack.Complete(); Output.Complete(); }
        public void Fault(Exception error)
        {
            ((IDataflowBlock)Input).Fault(error); ((IDataflowBlock)Ack).Fault(error);
            ((IDataflowBlock)Output).Fault(error);
        }
        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            Complete();
            Input.TryReceiveAll(out _); Ack.TryReceiveAll(out _); Output.TryReceiveAll(out _);
            await Completion.WaitAsync(Deadline);
        }
    }
}
