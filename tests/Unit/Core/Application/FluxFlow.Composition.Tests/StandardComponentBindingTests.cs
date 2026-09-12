using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class StandardComponentBindingTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Standard_ports_share_native_blocks_without_owning_them()
    {
        await using var node = new ProbeComponent();
        var ports = FlowPorts.CreateStandard(node.NativeInput, node.NativeOutput, node.NativeEvents);
        ports.Inputs.Keys.ShouldBe(["Input"]);
        ports.Outputs.Keys.ShouldBe(["Output"]);
        ports.Inputs["Input"].ShouldBeSameAs(node.NativeInput);
        ports.Outputs["Output"].ShouldBeSameAs(node.NativeOutput);
        ports.Events.ShouldBeSameAs(node.NativeEvents);
        ports.Controls.ShouldBeEmpty();
        var message = FlowMessage.Create(FlowValue.From("shared"));
        (await ports.Inputs["Input"].SendAsync(message)).ShouldBeTrue();
        (await node.NativeInput.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        node.Completion.IsCompleted.ShouldBeFalse();
        node.DisposeCount.ShouldBe(0);

        await using var application = new FlowApplication();
        var component = application.AddWorkflow("main").Add("node", node, ComponentOwnership.Borrowed);
        component.Input.Address.ShouldBe("main.node.Input");
        component.Output.Address.ShouldBe("main.node.Output");
        component.Input.ShouldBeSameAs(component.Inputs["Input"]);
        component.Output.ShouldBeSameAs(component.Outputs["Output"]);
        application.GetInput("main.node.Input").ShouldBeSameAs(component.Input);
        application.GetOutput("main.node.Output").ShouldBeSameAs(component.Output);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("events")]
    public async Task Standard_port_construction_rejects_missing_blocks(string missing)
    {
        await using var node = new ProbeComponent();
        Should.Throw<ArgumentNullException>(() => FlowPorts.CreateStandard(
            missing == "input" ? null! : node.NativeInput,
            missing == "output" ? null! : node.NativeOutput,
            missing == "events" ? null! : node.NativeEvents)).ParamName.ShouldBe(missing);
        node.Completion.IsCompleted.ShouldBeFalse();
        node.DisposeCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Standard_metadata_does_not_construct_and_activation_binds_once(bool shaped)
    {
        var calls = 0;
        ProbeComponent? created = null;
        var shape = shaped ? FlowValueShape.String("Text") : null;
        var services = new ServiceCollection();
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.standard", registration =>
            registration.UseFactory(_ => { calls++; return created = new ProbeComponent(); }).HasStandardPorts(shape));
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        descriptor.Inputs.Keys.ShouldBe(["Input"]);
        descriptor.Outputs.Keys.Order(StringComparer.Ordinal).ShouldBe(["Events", "Output"]);
        descriptor.Inputs["Input"].InputShape.ShouldBeSameAs(shape);
        descriptor.Inputs["Input"].MessageType.ShouldBe(typeof(FlowValue));
        descriptor.Inputs["Input"].LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Multiple);
        descriptor.Outputs.Values.ShouldAllBe(port => port.MessageType == typeof(FlowValue));
        if (shaped)
        {
            descriptor.ValidateInputShape("Input", FlowValue.From("text")).ShouldNotBeNull().IsValid.ShouldBeTrue();
            descriptor.ValidateInputShape("Input", FlowValue.From(42)).ShouldNotBeNull().IsValid.ShouldBeFalse();
        }
        else descriptor.ValidateInputShape("Input", FlowValue.Null).ShouldBeNull();
        calls.ShouldBe(0);
        created.ShouldBeNull();

        var instance = await descriptor.Factory(Context(provider));
        var node = created.ShouldNotBeNull();
        try
        {
            calls.ShouldBe(1);
            instance.Node.ShouldBeSameAs(node);
            instance.Inputs["Input"].Target.ShouldBeSameAs(node.NativeInput);
            instance.Outputs["Output"].Source.ShouldBeSameAs(node.NativeOutput);
            var message = FlowMessage.Create(FlowValue.From("native"));
            (await instance.Inputs["Input"].Target.SendAsync(message)).ShouldBeTrue();
            (await node.NativeInput.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
            node.NativeOutput.Post(message).ShouldBeTrue();
            (await instance.Outputs["Output"].Source.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
            node.NativeEvents.Post(message).ShouldBeTrue();
            var observed = await instance.Outputs["Events"].Source.ReceiveAsync(Deadline);
            observed.Value.ShouldBeSameAs(message.Value);
            observed.MessageId.ShouldBe(message.MessageId);
            observed.Headers[FlowEventHeaders.Source].ShouldBe("main.node");
            node.DisposeCount.ShouldBe(0);
        }
        finally { await instance.DisposeAsync(); }
        node.DisposeCount.ShouldBe(1);
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Standard_async_factories_preserve_native_bindings_and_resource_ownership(bool additionalOwner)
    {
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        var ownerDisposals = 0;
        ProbeComponent? created = null;
        var shape = FlowValueShape.String("Text");
        var services = new ServiceCollection();
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.standard", registration =>
        {
            var binding = additionalOwner ? registration.UseFactory(ActivateOwned) : registration.UseFactory(Activate);
            binding.HasStandardPorts(shape);
        });
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        descriptor.Inputs["Input"].InputShape.ShouldBeSameAs(shape);
        descriptor.Outputs.Keys.Order(StringComparer.Ordinal).ShouldBe(["Events", "Output"]);
        factoryCalls.ShouldBe(0);
        created.ShouldBeNull();
        var pending = descriptor.Factory(Context(provider)).AsTask();
        var callsWhileWaiting = factoryCalls;
        var completedWhileWaiting = pending.IsCompleted;
        var createdWhileWaiting = created;
        releaseFactory.TrySetResult();
        var instance = await pending.WaitAsync(Deadline);
        var node = created.ShouldNotBeNull();
        try
        {
            callsWhileWaiting.ShouldBe(1);
            completedWhileWaiting.ShouldBeFalse();
            createdWhileWaiting.ShouldBeNull();
            factoryCalls.ShouldBe(1);
            instance.Node.ShouldBeSameAs(node);
            instance.Inputs["Input"].Target.ShouldBeSameAs(node.NativeInput);
            instance.Outputs["Output"].Source.ShouldBeSameAs(node.NativeOutput);
            instance.Completion.ShouldBeSameAs(additionalOwner ? ownerCompletion.Task : node.Completion);
            var message = FlowMessage.Create(FlowValue.From("async"));
            (await instance.Inputs["Input"].Target.SendAsync(message)).ShouldBeTrue();
            (await node.NativeInput.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
            node.NativeOutput.Post(message).ShouldBeTrue();
            (await instance.Outputs["Output"].Source.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
            node.DisposeCount.ShouldBe(0);
            ownerDisposals.ShouldBe(0);
        }
        finally { await instance.DisposeAsync(); }
        node.DisposeCount.ShouldBe(1);
        ownerDisposals.ShouldBe(additionalOwner ? 1 : 0);
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        instance.Completion.IsCompletedSuccessfully.ShouldBeTrue();

        async ValueTask<ProbeComponent> Activate(ComponentActivationContext _)
        {
            factoryCalls++;
            await releaseFactory.Task;
            return created = new ProbeComponent();
        }

        async ValueTask<ComponentNodeActivation<ProbeComponent>> ActivateOwned(ComponentActivationContext context)
            => new(await Activate(context), ownerCompletion.Task, () =>
            {
                ownerDisposals++;
                ownerCompletion.TrySetResult();
                return ValueTask.CompletedTask;
            });
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Standard_async_binding_failure_cleans_every_acquired_owner(
        bool additionalOwner, bool nodeCleanupFails, bool ownerCleanupFails)
    {
        var factoryCalls = 0;
        var ownerDisposals = 0;
        var ownerFailure = new InvalidOperationException("expected resource cleanup failure");
        ProbeComponent? created = null;
        var services = new ServiceCollection();
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.standard", registration =>
        {
            var binding = additionalOwner ? registration.UseFactory(ActivateOwned) : registration.UseFactory(Activate);
            binding.HasStandardPorts();
        });
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        factoryCalls.ShouldBe(0);
        created.ShouldBeNull();
        if (nodeCleanupFails || ownerCleanupFails)
        {
            var failure = await Should.ThrowAsync<AggregateException>(async () =>
                await descriptor.Factory(Context(provider)));
            var errors = failure.Flatten().InnerExceptions;
            errors.Count.ShouldBe(1 + (nodeCleanupFails ? 1 : 0) + (ownerCleanupFails ? 1 : 0));
            errors[0].ShouldBeOfType<InvalidOperationException>().Message.ShouldContain("use explicit bindings for custom ports");
            if (nodeCleanupFails) errors.ShouldContain(created.ShouldNotBeNull().CleanupFailure);
            if (ownerCleanupFails) errors.ShouldContain(ownerFailure);
        }
        else
        {
            var failure = await Should.ThrowAsync<InvalidOperationException>(async () =>
                await descriptor.Factory(Context(provider)));
            failure.Message.ShouldContain("use explicit bindings for custom ports");
        }
        factoryCalls.ShouldBe(1);
        created.ShouldNotBeNull().DisposeCount.ShouldBe(1);
        created.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        ownerDisposals.ShouldBe(additionalOwner ? 1 : 0);

        async ValueTask<ProbeComponent> Activate(ComponentActivationContext _)
        {
            factoryCalls++;
            await Task.Yield();
            return created = new ProbeComponent("extraOutput", nodeCleanupFails);
        }

        async ValueTask<ComponentNodeActivation<ProbeComponent>> ActivateOwned(ComponentActivationContext context)
            => new(await Activate(context), disposeAsync: () =>
            {
                ownerDisposals++;
                return ownerCleanupFails ? ValueTask.FromException(ownerFailure) : ValueTask.CompletedTask;
            });
    }

    [Theory]
    [InlineData("inputName")]
    [InlineData("outputName")]
    [InlineData("extraInput")]
    [InlineData("extraOutput")]
    [InlineData("noInput")]
    [InlineData("noOutput")]
    [InlineData("control")]
    [InlineData("noEvents")]
    [InlineData("noPorts")]
    public async Task Standard_binding_rejects_custom_ports_and_disposes_once(string difference)
    {
        var node = new ProbeComponent(difference);
        var services = new ServiceCollection();
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.standard", registration =>
            registration.UseFactory(_ => node).HasStandardPorts());
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        var error = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await descriptor.Factory(Context(provider)));
        error.Message.ShouldContain("use explicit bindings for custom ports");
        node.DisposeCount.ShouldBe(1);
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Standard_binding_preserves_cleanup_failures()
    {
        var node = new ProbeComponent("extraOutput", failCleanup: true);
        var services = new ServiceCollection();
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.standard", registration =>
            registration.UseFactory(_ => node).HasStandardPorts());
        using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<AggregateException>(async () =>
            await provider.GetRequiredService<ComponentDescriptor>().Factory(Context(provider)));
        error.InnerExceptions.Count.ShouldBe(2);
        error.InnerExceptions[0].ShouldBeOfType<InvalidOperationException>().Message
            .ShouldContain("use explicit bindings for custom ports");
        error.InnerExceptions[1].ShouldBeSameAs(node.CleanupFailure);
        node.DisposeCount.ShouldBe(1);
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void Standard_binding_rejects_a_missing_registration_builder()
        => Should.Throw<ArgumentNullException>(() =>
            StandardComponentBindingExtensions.HasStandardPorts<ProbeComponent>(null!)).ParamName.ShouldBe("builder");

    private static ComponentActivationContext Context(IServiceProvider provider)
        => new(provider, "main", "node", new ComponentDefinition("test.standard"));

    private sealed class ProbeComponent : IFlowComponent
    {
        private readonly bool _failCleanup;
        public BufferBlock<FlowMessage> NativeInput { get; } = new(new DataflowBlockOptions { BoundedCapacity = 2 });
        public BufferBlock<FlowMessage> NativeOutput { get; } = new(new DataflowBlockOptions { BoundedCapacity = 2 });
        public BufferBlock<FlowMessage> NativeEvents { get; } = new(new DataflowBlockOptions { BoundedCapacity = 2 });
        public Exception CleanupFailure { get; } = new InvalidOperationException("expected cleanup failure");
        public int DisposeCount { get; private set; }

        public ProbeComponent(string? difference = null, bool failCleanup = false)
        {
            _failCleanup = failCleanup;
            var inputs = new Dictionary<string, ITargetBlock<FlowMessage>>
                { [difference == "inputName" ? "Other" : "Input"] = NativeInput };
            var outputs = new Dictionary<string, ISourceBlock<FlowMessage>>
                { [difference == "outputName" ? "Other" : "Output"] = NativeOutput };
            if (difference == "extraInput") inputs.Add("Other", NativeInput);
            if (difference == "extraOutput") outputs.Add("Other", NativeOutput);
            if (difference == "noInput") inputs.Clear();
            if (difference == "noOutput") outputs.Clear();
            Ports = difference == "noPorts" ? null! : new FlowPorts(inputs, outputs,
                difference == "noEvents" ? null : NativeEvents,
                difference == "control" ? new Dictionary<string, ITargetBlock<FlowMessage>> { ["Stop"] = NativeInput } : null);
            Completion = Task.WhenAll(NativeInput.Completion, NativeOutput.Completion, NativeEvents.Completion);
        }

        public FlowPorts Ports { get; }
        public Task Completion { get; }
        public void Complete() { NativeInput.Complete(); NativeOutput.Complete(); NativeEvents.Complete(); }
        public void Fault(Exception exception)
        {
            ((IDataflowBlock)NativeInput).Fault(exception);
            ((IDataflowBlock)NativeOutput).Fault(exception);
            ((IDataflowBlock)NativeEvents).Fault(exception);
        }
        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            Complete();
            await Completion.WaitAsync(Deadline);
            if (_failCleanup) throw CleanupFailure;
        }
    }
}
