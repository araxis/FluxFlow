using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class RuntimeComponentBindingBuilderTests
{
    [Fact]
    public void Typed_factory_registration_declares_metadata_without_running_factory_or_selectors()
    {
        var services = new ServiceCollection();
        var configurationCalls = 0;
        var factoryCalls = 0;
        var selectorCalls = 0;

        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.typed", component =>
        {
            configurationCalls++;
            component
                .UseFactory(_ =>
                {
                    factoryCalls++;
                    return new BindingNode();
                })
                .HasInput(
                    "Input",
                    node =>
                    {
                        selectorCalls++;
                        return node.Input;
                    },
                    ComponentPortLinkCardinality.Single)
                .HasSignalInput(
                    "Signal",
                    node =>
                    {
                        selectorCalls++;
                        return node.Signal;
                    })
                .HasOutput(
                    "Output",
                    node =>
                    {
                        selectorCalls++;
                        return node.Output;
                    })
                .HasEvents(
                    "Diagnostics",
                    node =>
                    {
                        selectorCalls++;
                        return node.Events;
                    });
        });

        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();

        configurationCalls.ShouldBe(1);
        factoryCalls.ShouldBe(0);
        selectorCalls.ShouldBe(0);
        descriptor.Inputs.Keys.ShouldBe(["Input", "Signal"]);
        descriptor.Inputs["Input"].MessageType.ShouldBe(typeof(string));
        descriptor.Inputs["Input"].LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
        descriptor.Inputs["Signal"].Kind.ShouldBe(ComponentPortKind.Signal);
        descriptor.Outputs.Keys.ShouldBe(["Output", "Diagnostics"]);
        descriptor.Outputs["Output"].MessageType.ShouldBe(typeof(int));
        descriptor.Outputs["Diagnostics"].MessageType.ShouldBe(typeof(ComponentEvent));
    }

    [Fact]
    public void Runtime_component_binding_builders_expose_only_Has_port_methods()
    {
        AssertCanonicalPortMethods(typeof(RuntimeComponentBindingBuilder<>));
        AssertCanonicalPortMethods(typeof(RuntimeComponentInstanceBindingBuilder));
    }

    [Fact]
    public async Task Sync_factory_infers_ports_and_binds_exact_node_members_once_in_role_order()
    {
        var services = new ServiceCollection();
        var selectorOrder = new List<string>();
        BindingNode? created = null;
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.typed", component =>
            component
                .UseFactory(_ => created = new BindingNode())
                .HasOutput("Output", node => Record("output", node.Output))
                .HasInput("First", node => Record("first", node.Input))
                .HasEvents("Diagnostics", node => Record("events", node.Events))
                .HasSignalInput("Signal", node => Record("signal", node.Signal))
                .HasInput("Second", node => Record("second", node.AlternateInput)));
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();

        var instance = await descriptor.Factory(Context(provider));

        created.ShouldNotBeNull();
        selectorOrder.ShouldBe(["first", "second", "signal", "output", "events"]);
        descriptor.Inputs.Keys.ShouldBe(["First", "Signal", "Second"]);
        descriptor.Outputs.Keys.ShouldBe(["Output", "Diagnostics"]);
        instance.Inputs["First"].ShouldBeOfType<ComponentInputPort<string>>()
            .Target.ShouldBeSameAs(created.Input);
        instance.Inputs["Second"].ShouldBeOfType<ComponentInputPort<string>>()
            .Target.ShouldBeSameAs(created.AlternateInput);
        instance.Inputs["Signal"].ShouldBeOfType<ComponentSignalInputPort>()
            .Target.ShouldBeSameAs(created.Signal);
        instance.Outputs["Output"].ShouldBeOfType<ComponentOutputPort<int>>()
            .Source.ShouldBeSameAs(created.Output);
        instance.Outputs["Diagnostics"].ShouldBeOfType<ComponentOutputPort<ComponentEvent>>();

        await instance.DisposeAsync();
        created.DisposeCount.ShouldBe(1);

        T Record<T>(string name, T value)
        {
            selectorOrder.Add(name);
            return value;
        }
    }

    [Fact]
    public async Task Async_factory_infers_ports_and_preserves_cardinality()
    {
        var services = new ServiceCollection();
        var factoryCalls = 0;
        BindingNode? created = null;
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.async", component =>
            component
                .UseFactory(async _ =>
                {
                    factoryCalls++;
                    await Task.Yield();
                    return created = new BindingNode();
                })
                .HasInput(
                    "Input",
                    static node => node.Input,
                    ComponentPortLinkCardinality.Single)
                .HasOutput(
                    "Output",
                    static node => node.Output,
                    ComponentPortLinkCardinality.Single));
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();

        factoryCalls.ShouldBe(0);
        var instance = await descriptor.Factory(Context(provider));

        factoryCalls.ShouldBe(1);
        created.ShouldNotBeNull();
        descriptor.Inputs["Input"].MessageType.ShouldBe(typeof(string));
        descriptor.Inputs["Input"].LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
        descriptor.Outputs["Output"].MessageType.ShouldBe(typeof(int));
        descriptor.Outputs["Output"].LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
        instance.Inputs["Input"].ShouldBeOfType<ComponentInputPort<string>>()
            .Target.ShouldBeSameAs(created.Input);
        instance.Outputs["Output"].ShouldBeOfType<ComponentOutputPort<int>>()
            .Source.ShouldBeSameAs(created.Output);

        await instance.DisposeAsync();
    }

    [Fact]
    public async Task Async_activation_preserves_explicit_completion_and_all_cleanup_owners()
    {
        var services = new ServiceCollection();
        var explicitCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerDisposeCount = 0;
        BindingNode? created = null;
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.async-activation", component =>
            component
                .UseFactory(Activate)
                .HasOutput("Output", static node => node.Output));
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();

        var instance = await descriptor.Factory(Context(provider));

        instance.Node.ShouldBeSameAs(created.ShouldNotBeNull());
        instance.Completion.ShouldBeSameAs(explicitCompletion.Task);
        await instance.DisposeAsync();
        created.DisposeCount.ShouldBe(1);
        ownerDisposeCount.ShouldBe(1);

        async ValueTask<ComponentNodeActivation<BindingNode>> Activate(
            ComponentActivationContext _)
        {
            await Task.Yield();
            return new ComponentNodeActivation<BindingNode>(
                created = new BindingNode(),
                explicitCompletion.Task,
                () =>
                {
                    ownerDisposeCount++;
                    return ValueTask.CompletedTask;
                });
        }
    }

    [Fact]
    public void Typed_component_without_HasEvents_has_no_implicit_event_output()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.no-events", component =>
            component
                .UseFactory(static _ => new BindingNode())
                .HasOutput("Output", static node => node.Output));

        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();

        descriptor.Outputs.Keys.ShouldBe(["Output"]);
        descriptor.Outputs.ContainsKey("Events").ShouldBeFalse();
    }

    [Fact]
    public async Task Normal_output_may_use_Events_when_no_event_port_uses_that_name()
    {
        var services = new ServiceCollection();
        BindingNode? created = null;
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.events-output", component =>
            component
                .UseFactory(_ => created = new BindingNode())
                .HasOutput("Events", static node => node.Output));
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();

        var instance = await descriptor.Factory(Context(provider));

        descriptor.Outputs.Keys.ShouldBe(["Events"]);
        descriptor.Outputs["Events"].MessageType.ShouldBe(typeof(int));
        instance.Outputs["Events"].ShouldBeOfType<ComponentOutputPort<int>>()
            .Source.ShouldBeSameAs(created.ShouldNotBeNull().Output);
        await instance.DisposeAsync();
    }

    [Fact]
    public async Task Multiple_named_event_ports_bind_their_selected_sources_independently()
    {
        var services = new ServiceCollection();
        BindingNode? created = null;
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.multiple-events", component =>
            component
                .UseFactory(_ => created = new BindingNode())
                .HasEvents("Diagnostics", static node => node.Events)
                .HasEvents("Audit", static node => node.AuditEvents));
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        var instance = await descriptor.Factory(Context(provider));
        var diagnostics = instance.Outputs["Diagnostics"]
            .ShouldBeOfType<ComponentOutputPort<ComponentEvent>>();
        var audit = instance.Outputs["Audit"]
            .ShouldBeOfType<ComponentOutputPort<ComponentEvent>>();
        var diagnosticReceive = diagnostics.Source.ReceiveAsync();
        var auditReceive = audit.Source.ReceiveAsync();

        var node = created.ShouldNotBeNull();
        node.Events.Post(new FlowEvent { Name = "diagnostic" }).ShouldBeTrue();
        node.AuditEvents.Post(new FlowEvent { Name = "audit" }).ShouldBeTrue();

        (await diagnosticReceive.WaitAsync(TimeSpan.FromSeconds(5))).Value.Name
            .ShouldBe("diagnostic");
        (await auditReceive.WaitAsync(TimeSpan.FromSeconds(5))).Value.Name
            .ShouldBe("audit");
        descriptor.Outputs.Keys.ShouldBe(["Diagnostics", "Audit"]);
        descriptor.Outputs.Values.All(port => port.MessageType == typeof(ComponentEvent))
            .ShouldBeTrue();

        await instance.DisposeAsync();
    }

    [Fact]
    public void Normal_and_event_outputs_share_one_duplicate_name_boundary()
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.duplicate", component =>
                component
                    .UseFactory(static _ => new BindingNode())
                    .HasOutput("Events", static node => node.Output)
                    .HasEvents("Events", static node => node.Events)));

        exception.Message.ShouldContain("output port 'Events'");
        exception.Message.ShouldContain("already registered");
        services.ShouldNotContain(registration => registration.ServiceType == typeof(ComponentDescriptor));
    }

    [Theory]
    [InlineData("input")]
    [InlineData("signal")]
    [InlineData("output")]
    [InlineData("events")]
    public void Typed_builder_rejects_null_selectors_before_registration(string role)
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.null-selector", component =>
            {
                var bindings = component.UseFactory(CreateNode);
                switch (role)
                {
                    case "input":
                        bindings.HasInput<string>("Input", null!);
                        break;
                    case "signal":
                        bindings.HasSignalInput("Signal", null!);
                        break;
                    case "output":
                        bindings.HasOutput<int>("Output", null!);
                        break;
                    case "events":
                        bindings.HasEvents("Events", null!);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(role));
                }
            }));

        exception.ParamName.ShouldBe("selector");
        services.ShouldNotContain(registration => registration.ServiceType == typeof(ComponentDescriptor));
    }

    [Fact]
    public void Registration_rejects_null_or_multiple_factory_modes_without_partial_state()
    {
        var nullFactoryServices = new ServiceCollection();
        var nullFactory = Should.Throw<ArgumentNullException>(() =>
            nullFactoryServices.AddFluxFlowComponents().Advanced.AddDynamicComponent(
                "test.null-factory",
                component => component.UseFactory<BindingNode>(
                    (Func<ComponentActivationContext, BindingNode>)null!)));

        nullFactory.ParamName.ShouldBe("value");
        nullFactoryServices.ShouldNotContain(registration =>
            registration.ServiceType == typeof(ComponentDescriptor));

        var multipleFactoryServices = new ServiceCollection();
        var multipleFactory = Should.Throw<InvalidOperationException>(() =>
            multipleFactoryServices.AddFluxFlowComponents().Advanced.AddDynamicComponent(
                "test.multiple-factories",
                component =>
                {
                    component.UseFactory(CreateNode);
                    component.UseInstanceFactory(static _ =>
                        throw new InvalidOperationException("Instance factory should not run."));
                }));

        multipleFactory.Message.ShouldContain("test.multiple-factories");
        multipleFactory.Message.ShouldContain("exactly one factory mode");
        multipleFactoryServices.ShouldNotContain(registration =>
            registration.ServiceType == typeof(ComponentDescriptor));
    }

    [Fact]
    public async Task Typed_factory_null_node_failure_identifies_component_and_cleans_no_owner()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.null-node", component =>
            component.UseFactory<BindingNode>(
                (Func<ComponentActivationContext, BindingNode>)(static _ => null!)));
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await descriptor.Factory(Context(provider)));

        exception.Message.ShouldContain("test.null-node");
        exception.Message.ShouldContain("null node");
    }

    [Fact]
    public async Task Typed_activation_failure_disposes_node_and_additional_owner_once()
    {
        var services = new ServiceCollection();
        var ownerDisposeCount = 0;
        BindingNode? created = null;
        services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.null-binding", component =>
            component
                .UseFactory(_ => new ComponentNodeActivation<BindingNode>(
                    created = new BindingNode(),
                    disposeAsync: () =>
                    {
                        ownerDisposeCount++;
                        return ValueTask.CompletedTask;
                    }))
                .HasInput<string>("Input", static _ => null!));
        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await descriptor.Factory(Context(provider)));

        exception.Message.ShouldContain("test.null-binding");
        exception.Message.ShouldContain("Input");
        exception.Message.ShouldContain("returned null");
        created.ShouldNotBeNull().DisposeCount.ShouldBe(1);
        ownerDisposeCount.ShouldBe(1);
    }

    [Fact]
    public void Equivalent_typed_registration_is_idempotent_and_changed_selector_conflicts()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents().Advanced
            .AddDynamicComponent("test.identity", ConfigureEquivalent)
            .AddDynamicComponent("test.identity", ConfigureEquivalent);

        services.Count(registration => registration.ServiceType == typeof(ComponentDescriptor))
            .ShouldBe(1);

        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.AddDynamicComponent("test.identity", ConfigureChangedSelector));

        exception.Message.ShouldContain("test.identity");
        exception.Message.ShouldContain("conflicting descriptor registration");
        services.Count(registration => registration.ServiceType == typeof(ComponentDescriptor))
            .ShouldBe(1);
    }

    private static void ConfigureEquivalent(RuntimeComponentRegistrationBuilder component)
        => component
            .UseFactory(CreateNode)
            .HasInput("Input", SelectInput)
            .HasOutput("Output", SelectOutput)
            .HasEvents("Diagnostics", SelectEvents);

    private static void ConfigureChangedSelector(RuntimeComponentRegistrationBuilder component)
        => component
            .UseFactory(CreateNode)
            .HasInput("Input", SelectAlternateInput)
            .HasOutput("Output", SelectOutput)
            .HasEvents("Diagnostics", SelectEvents);

    private static BindingNode CreateNode(ComponentActivationContext _) => new();

    private static ITargetBlock<FlowMessage<string>> SelectInput(BindingNode node) => node.Input;

    private static ITargetBlock<FlowMessage<string>> SelectAlternateInput(BindingNode node)
        => node.AlternateInput;

    private static ISourceBlock<FlowMessage<int>> SelectOutput(BindingNode node) => node.Output;

    private static ISourceBlock<FlowEvent> SelectEvents(BindingNode node) => node.Events;

    private static ComponentActivationContext Context(IServiceProvider services)
        => new(
            services,
            "Orders",
            "Worker",
            new ComponentDefinition("test.typed"));

    private static void AssertCanonicalPortMethods(Type builderType)
    {
        var methodNames = builderType
            .GetMethods(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.DeclaredOnly)
            .Select(static method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        methodNames.ShouldBe(
        [
            "HasEvents",
            "HasInput",
            "HasOutput",
            "HasSignalInput"
        ],
        $"{builderType.Name} must expose only the canonical Has port DSL.");
        methodNames.ShouldNotContain(
            static name => name.StartsWith("Add", StringComparison.Ordinal),
            $"{builderType.Name} must not retain a public Add port alias.");
    }

    private sealed class BindingNode : IFlowNode
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public BufferBlock<FlowMessage<string>> Input { get; } = new();

        public BufferBlock<FlowMessage<string>> AlternateInput { get; } = new();

        public BufferBlock<FlowMessage<int>> Output { get; } = new();

        public BufferBlock<FlowEvent> Events { get; } = new();

        public BufferBlock<FlowEvent> AuditEvents { get; } = new();

        public RecordingSignalTarget Signal { get; } = new();

        public Task Completion => _completion.Task;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Complete()
        {
            Input.Complete();
            AlternateInput.Complete();
            Output.Complete();
            Events.Complete();
            AuditEvents.Complete();
            Signal.Complete();
            _completion.TrySetResult();
        }

        public void Fault(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            ((IDataflowBlock)Input).Fault(exception);
            ((IDataflowBlock)AlternateInput).Fault(exception);
            ((IDataflowBlock)Output).Fault(exception);
            ((IDataflowBlock)Events).Fault(exception);
            ((IDataflowBlock)AuditEvents).Fault(exception);
            Signal.Fault(exception);
            _completion.TrySetException(exception);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Increment(ref _disposeCount) == 1)
                Complete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSignalTarget : IFlowSignalTarget
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public ValueTask<bool> SendAsync<T>(
            FlowMessage<T> signal,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);
    }
}
