using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentDescriptorTests
{
    [Fact]
    public void Descriptor_normalizes_type_and_port_names_without_injecting_events()
    {
        var descriptor = new ComponentDescriptor(
            " data.map ",
            UnusedFactory,
            inputs: [ComponentPorts.Metadata<string>(" Input ")],
            outputs: [ComponentPorts.Metadata<string>(" Output ")]);

        descriptor.Type.ShouldBe("data.map");
        descriptor.Inputs.Keys.ShouldBe(["Input"]);
        descriptor.Outputs.Keys.ShouldBe(["Output"]);
        descriptor.Outputs.ContainsKey("Events").ShouldBeFalse();
    }

    [Fact]
    public void Catalog_resolves_only_canonical_types_with_ordinal_comparison()
    {
        var descriptor = Descriptor("data.map");
        var catalog = new ComponentCatalog([descriptor]);

        catalog.Components.Keys.ShouldBe(["data.map"]);
        catalog.TryGetDescriptor(" data.map ", out var resolved).ShouldBeTrue();
        resolved.ShouldBeSameAs(descriptor);
        catalog.TryGetDescriptor("flow.mapper", out _).ShouldBeFalse();
        catalog.TryGetDescriptor("DATA.MAP", out _).ShouldBeFalse();
    }

    [Fact]
    public void Catalog_accepts_repeated_registration_of_the_same_descriptor_instance()
    {
        var descriptor = Descriptor("data.map");

        var catalog = new ComponentCatalog([descriptor, descriptor]);

        catalog.Descriptors.ShouldBe([descriptor]);
    }

    [Fact]
    public void Catalog_rejects_conflicting_canonical_descriptors()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            new ComponentCatalog([Descriptor("data.map"), Descriptor("data.map")]));

        exception.Message.ShouldContain("data.map");
        exception.Message.ShouldContain("conflicting");
    }

    [Fact]
    public void Catalog_order_is_independent_and_immutable_after_construction()
    {
        var first = Descriptor("first");
        var second = Descriptor("second");
        var registrations = new List<ComponentDescriptor> { second, first };

        var catalog = new ComponentCatalog(registrations);
        registrations.Clear();

        catalog.Descriptors.ShouldBe([first, second]);
        catalog.Components.Keys.ShouldBe(["first", "second"]);
    }

    [Fact]
    public void Port_metadata_rejects_invalid_arguments()
    {
        Should.Throw<ArgumentNullException>(() =>
            new ComponentPortMetadata(null!, typeof(string)))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentException>(() =>
            new ComponentPortMetadata(" ", typeof(string)))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            new ComponentPortMetadata("Input", null!))
            .ParamName.ShouldBe("messageType");
    }

    [Fact]
    public void Port_metadata_supports_deconstruction()
    {
        var metadata = ComponentPortMetadata.Create<string>("Output");

        var (name, messageType) = metadata;

        name.ShouldBe("Output");
        messageType.ShouldBe(typeof(string));
    }

    [Fact]
    public void Port_metadata_defaults_to_multiple_links_and_supports_single_link_ports()
    {
        var multiple = ComponentPortMetadata.Create<string>("Output");
        var single = ComponentPorts.Metadata<string>(
            "Input",
            ComponentPortLinkCardinality.Single);

        multiple.LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Multiple);
        var (name, messageType, linkCardinality) = single;
        name.ShouldBe("Input");
        messageType.ShouldBe(typeof(string));
        linkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ComponentPortMetadata(
                "Input",
                typeof(string),
                (ComponentPortLinkCardinality)42));
    }

    [Fact]
    public void Signal_port_metadata_is_payload_independent()
    {
        var metadata = ComponentPorts.SignalMetadata(
            "Ack",
            ComponentPortLinkCardinality.Single);

        metadata.Name.ShouldBe("Ack");
        metadata.Kind.ShouldBe(ComponentPortKind.Signal);
        metadata.MessageType.ShouldBe(typeof(object));
        metadata.LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
        Should.Throw<ArgumentException>(() => new ComponentPortMetadata(
            "Ack",
            typeof(string),
            ComponentPortLinkCardinality.Multiple,
            ComponentPortKind.Signal));
    }

    [Fact]
    public void Port_metadata_dispatches_registered_message_types_without_reflection()
    {
        var visitor = new RecordingPortTypeVisitor();

        var typed = ComponentPorts.Metadata<string>("Input");
        typed.SupportsTypeVisit.ShouldBeTrue();
        typed.Accept(visitor);

        visitor.MessageType.ShouldBe(typeof(string));
        visitor.IsSignal.ShouldBeFalse();

        var signal = ComponentPorts.SignalMetadata("Ack");
        signal.Accept(visitor);

        visitor.MessageType.ShouldBe(typeof(object));
        visitor.IsSignal.ShouldBeTrue();
    }

    [Fact]
    public void Runtime_type_metadata_cannot_be_used_for_reflection_free_dispatch()
    {
        var metadata = new ComponentPortMetadata("Input", typeof(string));

        metadata.SupportsTypeVisit.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() =>
            metadata.Accept(new RecordingPortTypeVisitor()));
    }

    [Fact]
    public void Descriptor_rejects_duplicate_ports()
    {
        Should.Throw<ArgumentException>(() =>
            new ComponentDescriptor(
                "test.node",
                UnusedFactory,
                inputs:
                [
                    ComponentPorts.Metadata<string>("Input"),
                    ComponentPorts.Metadata<string>(" Input ")
                ]))
            .Message.ShouldContain("Input");
    }

    [Fact]
    public void Descriptor_rejects_null_port_metadata_entries()
    {
        var inputException = Should.Throw<ArgumentNullException>(() =>
            new ComponentDescriptor("test.node", UnusedFactory, inputs: [null!]));
        var outputException = Should.Throw<ArgumentNullException>(() =>
            new ComponentDescriptor("test.node", UnusedFactory, outputs: [null!]));

        inputException.ParamName.ShouldBe("port");
        outputException.ParamName.ShouldBe("port");
    }

    [Fact]
    public void Descriptor_owns_immutable_option_and_resource_schemas()
    {
        var options = new List<ComponentOptionMetadata>
        {
            ComponentOptions.Metadata<string>(" expression ", isRequired: true)
        };
        var resources = new List<ComponentResourceMetadata>
        {
            ComponentResources.Metadata<TimeProvider>(" clock ")
        };

        var descriptor = new ComponentDescriptor(
            "test.node",
            UnusedFactory,
            options: options,
            resources: resources);
        options.Clear();
        resources.Clear();

        descriptor.Options.Keys.ShouldBe(["expression"]);
        descriptor.Options["expression"].ValueType.ShouldBe(typeof(string));
        descriptor.Options["expression"].IsRequired.ShouldBeTrue();
        descriptor.Resources.Keys.ShouldBe(["clock"]);
        descriptor.Resources["clock"].ServiceType.ShouldBe(typeof(TimeProvider));
    }

    [Fact]
    public void Descriptor_rejects_duplicate_option_and_resource_names()
    {
        Should.Throw<ArgumentException>(() => new ComponentDescriptor(
                "test.node",
                UnusedFactory,
                options:
                [
                    ComponentOptions.Metadata<string>("value"),
                    ComponentOptions.Metadata<int>("value")
                ]))
            .Message.ShouldContain("value");
        Should.Throw<ArgumentException>(() => new ComponentDescriptor(
                "test.node",
                UnusedFactory,
                resources:
                [
                    ComponentResources.Metadata<TimeProvider>("clock"),
                    ComponentResources.Metadata<object>("clock")
                ]))
            .Message.ShouldContain("clock");
    }

    private static ComponentDescriptor Descriptor(string type)
        => new(type, UnusedFactory);

    private static ValueTask<ComponentInstance> UnusedFactory(ComponentActivationContext _)
        => throw new InvalidOperationException("Factory should not run.");

    private sealed class RecordingPortTypeVisitor : IComponentPortTypeVisitor
    {
        public Type? MessageType { get; private set; }

        public bool IsSignal { get; private set; }

        public void Visit<TMessage>(ComponentPortMetadata metadata)
        {
            MessageType = typeof(TMessage);
            IsSignal = false;
        }

        public void VisitSignal(ComponentPortMetadata metadata)
        {
            MessageType = typeof(object);
            IsSignal = true;
        }
    }
}
