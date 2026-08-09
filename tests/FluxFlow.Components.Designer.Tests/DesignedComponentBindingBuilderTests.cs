using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class DesignedComponentBindingBuilderTests
{
    [Fact]
    public void Designed_typed_ports_create_runtime_and_design_metadata_from_one_declaration()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowComponents().AddComponent("test.designed", component =>
        {
            component.WithDisplay(
                displayName: "Designed test",
                category: "Testing",
                summary: "Typed design metadata");
            component
                .UseFactory(CreateNode)
                .HasInput(
                    "Input",
                    SelectInput,
                    displayName: "Incoming value",
                    group: "Data",
                    order: 3,
                    summary: "Value to process.",
                    isPrimary: true,
                    linkCardinality: ComponentPortLinkCardinality.Single)
                .HasSignalInput(
                    "Signal",
                    SelectSignal,
                    displayName: "Control signal",
                    group: "Control",
                    order: 4,
                    summary: "Controls processing.")
                .HasOutput(
                    "Output",
                    SelectOutput,
                    displayName: "Processed value",
                    group: "Data",
                    order: 5,
                    summary: "Processed result.",
                    isPrimary: true)
                .HasEvents(
                    "Diagnostics",
                    SelectEvents,
                    displayName: "Diagnostic events",
                    group: "Observability",
                    order: 6,
                    summary: "Component diagnostics.");
            component.SetPortAttribute("Input", PortDirection.Input, "shape", "value");
            component.SetPortAttribute("Diagnostics", PortDirection.Output, "delivery", "best-effort");
        });

        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        var metadata = provider.GetRequiredService<ComponentDesignMetadataCatalog>()
            .All.ShouldHaveSingleItem();

        descriptor.Inputs.Keys.ShouldBe(["Input", "Signal"]);
        descriptor.Outputs.Keys.ShouldBe(["Output", "Diagnostics"]);
        descriptor.Inputs["Input"].MessageType.ShouldBe(typeof(string));
        descriptor.Inputs["Input"].LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
        descriptor.Inputs["Signal"].Kind.ShouldBe(ComponentPortKind.Signal);
        descriptor.Outputs["Output"].MessageType.ShouldBe(typeof(int));
        descriptor.Outputs["Diagnostics"].MessageType.ShouldBe(typeof(ComponentEvent));

        metadata.DisplayName?.Value.ShouldBe("Designed test");
        metadata.Category?.Value.ShouldBe("Testing");
        metadata.Summary?.Value.ShouldBe("Typed design metadata");
        metadata.Ports.Select(static port => port.Name.Value)
            .ShouldBe(["Input", "Signal", "Output", "Diagnostics"]);

        var input = metadata.Ports.Single(port => port.Name.Value == "Input");
        input.Direction.ShouldBe(PortDirection.Input);
        input.MessageType.ShouldBe(typeof(string));
        input.ValueType?.Value.ShouldBe(nameof(String));
        input.DisplayName?.Value.ShouldBe("Incoming value");
        input.Group?.Value.ShouldBe("Data");
        input.Order.ShouldBe(3);
        input.Summary?.Value.ShouldBe("Value to process.");
        input.IsPrimary.ShouldBeTrue();
        input.LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
        AttributeValue(input.Attributes, "shape").ShouldBe("value");

        var signal = metadata.Ports.Single(port => port.Name.Value == "Signal");
        signal.Direction.ShouldBe(PortDirection.Input);
        signal.Kind.ShouldBe(ComponentPortKind.Signal);
        signal.DisplayName?.Value.ShouldBe("Control signal");
        signal.Group?.Value.ShouldBe("Control");
        signal.Order.ShouldBe(4);

        var output = metadata.Ports.Single(port => port.Name.Value == "Output");
        output.Direction.ShouldBe(PortDirection.Output);
        output.MessageType.ShouldBe(typeof(int));
        output.ValueType?.Value.ShouldBe(nameof(Int32));
        output.DisplayName?.Value.ShouldBe("Processed value");
        output.Group?.Value.ShouldBe("Data");
        output.Order.ShouldBe(5);
        output.IsPrimary.ShouldBeTrue();

        var events = metadata.Ports.Single(port => port.Name.Value == "Diagnostics");
        events.Direction.ShouldBe(PortDirection.Output);
        events.MessageType.ShouldBe(typeof(ComponentEvent));
        events.ValueType?.Value.ShouldBe(nameof(ComponentEvent));
        events.DisplayName?.Value.ShouldBe("Diagnostic events");
        events.Group?.Value.ShouldBe("Observability");
        events.Order.ShouldBe(6);
        events.Summary?.Value.ShouldBe("Component diagnostics.");
        events.IsPrimary.ShouldBeFalse();
        AttributeValue(events.Attributes, "delivery").ShouldBe("best-effort");
    }

    [Fact]
    public void Designed_component_binding_builders_expose_only_Has_port_methods()
    {
        AssertCanonicalPortMethods(typeof(DesignedComponentBindingBuilder<>));
        AssertCanonicalPortMethods(typeof(DesignedComponentInstanceBindingBuilder));
    }

    [Fact]
    public void Designed_component_without_HasEvents_has_no_implicit_event_metadata()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowComponents().AddComponent("test.no-events", component =>
            component
                .UseFactory(CreateNode)
                .HasOutput("Output", SelectOutput, displayName: "Output"));

        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        var metadata = provider.GetRequiredService<ComponentDesignMetadataCatalog>()
            .All.ShouldHaveSingleItem();

        descriptor.Outputs.Keys.ShouldBe(["Output"]);
        descriptor.Outputs.ContainsKey("Events").ShouldBeFalse();
        metadata.Ports.Select(static port => port.Name.Value).ShouldBe(["Output"]);
        metadata.Ports.ShouldNotContain(port => port.Name.Value == "Events");
    }

    [Fact]
    public void Designed_advanced_factory_declares_metadata_without_activating_instance()
    {
        var services = new ServiceCollection();
        var factoryCalls = 0;
        services.AddFluxFlowComponents().AddComponent("test.advanced", component =>
            component
                .UseInstanceFactory(_ =>
                {
                    factoryCalls++;
                    throw new InvalidOperationException("Metadata registration must not activate the factory.");
                })
                .HasInput<string>(
                    "Input",
                    displayName: "Input",
                    linkCardinality: ComponentPortLinkCardinality.Single)
                .HasSignalInput("Signal", displayName: "Signal")
                .HasOutput<int>("Output", displayName: "Output")
                .HasEvents("Diagnostics", displayName: "Diagnostics"));

        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<ComponentDescriptor>();
        var metadata = provider.GetRequiredService<ComponentDesignMetadataCatalog>()
            .All.ShouldHaveSingleItem();

        factoryCalls.ShouldBe(0);
        descriptor.Inputs.Keys.ShouldBe(["Input", "Signal"]);
        descriptor.Outputs.Keys.ShouldBe(["Output", "Diagnostics"]);
        descriptor.Inputs["Input"].MessageType.ShouldBe(typeof(string));
        descriptor.Inputs["Input"].LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
        descriptor.Inputs["Signal"].Kind.ShouldBe(ComponentPortKind.Signal);
        descriptor.Outputs["Output"].MessageType.ShouldBe(typeof(int));
        descriptor.Outputs["Diagnostics"].MessageType.ShouldBe(typeof(ComponentEvent));
        metadata.Ports.Select(static port =>
                (port.Name.Value, port.Direction, port.Order, port.DisplayName?.Value))
            .ShouldBe(
            [
                ("Input", PortDirection.Input, 0, "Input"),
                ("Signal", PortDirection.Input, 1, "Signal"),
                ("Output", PortDirection.Output, 0, "Output"),
                ("Diagnostics", PortDirection.Output, 1, "Diagnostics")
            ]);
    }

    [Fact]
    public void Equivalent_designed_typed_registration_is_idempotent_and_changed_selector_conflicts()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents()
            .AddComponent("test.identity", ConfigureEquivalent)
            .AddComponent("test.identity", ConfigureEquivalent);

        services.Count(registration => registration.ServiceType == typeof(ComponentDescriptor))
            .ShouldBe(1);
        services.Count(registration => registration.ServiceType == typeof(ComponentDesignMetadataCatalog))
            .ShouldBe(1);

        var exception = Should.Throw<InvalidOperationException>(() =>
            builder.AddComponent("test.identity", ConfigureChangedSelector));

        exception.Message.ShouldContain("test.identity");
        exception.Message.ShouldContain("conflicting descriptor registration");
        services.Count(registration => registration.ServiceType == typeof(ComponentDescriptor))
            .ShouldBe(1);
    }

    private static void ConfigureEquivalent(ComponentRegistrationBuilder component)
        => component
            .UseFactory(CreateNode)
            .HasInput("Input", SelectInput, displayName: "Input")
            .HasOutput("Output", SelectOutput, displayName: "Output")
            .HasEvents("Diagnostics", SelectEvents, displayName: "Diagnostics");

    private static void ConfigureChangedSelector(ComponentRegistrationBuilder component)
        => component
            .UseFactory(CreateNode)
            .HasInput("Input", SelectAlternateInput, displayName: "Input")
            .HasOutput("Output", SelectOutput, displayName: "Output")
            .HasEvents("Diagnostics", SelectEvents, displayName: "Diagnostics");

    private static DesignedNode CreateNode(ComponentActivationContext _) => new();

    private static ITargetBlock<FlowMessage<string>> SelectInput(DesignedNode node) => node.Input;

    private static ITargetBlock<FlowMessage<string>> SelectAlternateInput(DesignedNode node)
        => node.AlternateInput;

    private static ISourceBlock<FlowMessage<int>> SelectOutput(DesignedNode node) => node.Output;

    private static ISourceBlock<FlowEvent> SelectEvents(DesignedNode node) => node.Events;

    private static IFlowSignalTarget SelectSignal(DesignedNode node) => node.Signal;

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

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

    private sealed class DesignedNode : IFlowNode
    {
        public BufferBlock<FlowMessage<string>> Input { get; } = new();

        public BufferBlock<FlowMessage<string>> AlternateInput { get; } = new();

        public BufferBlock<FlowMessage<int>> Output { get; } = new();

        public BufferBlock<FlowEvent> Events { get; } = new();

        public DesignedSignalTarget Signal { get; } = new();

        public Task Completion { get; } = Task.CompletedTask;

        public void Complete()
        {
            Input.Complete();
            AlternateInput.Complete();
            Output.Complete();
            Events.Complete();
            Signal.Complete();
        }

        public void Fault(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
        }

        public ValueTask DisposeAsync()
        {
            Complete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DesignedSignalTarget : IFlowSignalTarget
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
    }
}
