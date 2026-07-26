using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentDescriptorTests
{
    [Fact]
    public void Descriptor_normalizes_type_aliases_and_port_names()
    {
        var descriptor = new ComponentDescriptor(
            " data.map ",
            UnusedFactory,
            inputs: [ComponentPorts.Metadata<string>(" Input ")],
            outputs: [ComponentPorts.Metadata<string>(" Output ")],
            aliases: ["legacy.mapper", "flow.mapper"]);

        descriptor.Type.ShouldBe("data.map");
        descriptor.Aliases.ShouldBe(["flow.mapper", "legacy.mapper"]);
        descriptor.Inputs.Keys.ShouldBe(["Input"]);
        descriptor.Outputs.Keys.ShouldBe(["Output", "Events"]);
        descriptor.Outputs["Events"].MessageType.ShouldBe(typeof(ComponentEvent));
    }

    [Fact]
    public void Catalog_resolves_canonical_types_and_aliases_with_ordinal_comparison()
    {
        var descriptor = Descriptor("data.map", aliases: ["flow.mapper"]);
        var catalog = new ComponentCatalog([descriptor]);

        catalog.Components.Keys.ShouldBe(["data.map"]);
        catalog.TryResolveType(" flow.mapper ", out var canonicalType).ShouldBeTrue();
        canonicalType.ShouldBe("data.map");
        catalog.TryGetDescriptor("flow.mapper", out var resolved).ShouldBeTrue();
        resolved.ShouldBeSameAs(descriptor);
        catalog.TryResolveType("FLOW.MAPPER", out _).ShouldBeFalse();
    }

    [Fact]
    public void Catalog_accepts_repeated_registration_of_the_same_descriptor_instance()
    {
        var descriptor = Descriptor("data.map", aliases: ["flow.mapper"]);

        var catalog = new ComponentCatalog([descriptor, descriptor]);

        catalog.Descriptors.ShouldBe([descriptor]);
        catalog.Aliases.ContainsKey("flow.mapper").ShouldBeTrue();
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
    public void Catalog_rejects_alias_conflicts_deterministically()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            new ComponentCatalog(
            [
                Descriptor("second", aliases: ["shared"]),
                Descriptor("first", aliases: ["shared"])
            ]));

        exception.Message.ShouldContain("shared");
        exception.Message.ShouldContain("'first', 'second'");
    }

    [Fact]
    public void Catalog_rejects_an_alias_that_matches_a_canonical_type()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            new ComponentCatalog(
            [
                Descriptor("data.map", aliases: ["data.sink"]),
                Descriptor("data.sink")
            ]));

        exception.Message.ShouldContain("data.sink");
        exception.Message.ShouldContain("canonical component type");
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
    public void Service_registration_is_idempotent_for_the_same_descriptor_instance()
    {
        var descriptor = Descriptor("data.map");
        var services = new ServiceCollection();

        services.AddFluxFlowComponent(descriptor);
        services.AddFluxFlowComponent(descriptor);
        using var provider = services.BuildServiceProvider();

        provider.GetServices<ComponentDescriptor>().ShouldBe([descriptor]);
        provider.GetRequiredService<ComponentCatalog>().Descriptors.ShouldBe([descriptor]);
    }

    [Fact]
    public void Service_registration_rejects_conflicting_descriptors_immediately()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowComponent(Descriptor("data.map"));

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowComponent(Descriptor("data.map")));

        exception.Message.ShouldContain("data.map");
    }

    [Fact]
    public void Resource_type_aliases_are_immutable_and_reject_conflicts()
    {
        var catalog = new ComponentCatalog(
            resourceTypeAliases: [new ResourceTypeAliasDescriptor("old", "first")]);

        catalog.TryResolveResourceType(" old ", out var canonicalType).ShouldBeTrue();
        canonicalType.ShouldBe("first");
        Should.Throw<InvalidOperationException>(() =>
                new ComponentCatalog(
                    resourceTypeAliases:
                    [
                        new ResourceTypeAliasDescriptor("old", "first"),
                        new ResourceTypeAliasDescriptor("old", "second")
                    ]))
            .Message.ShouldContain("old");
    }

    [Fact]
    public void Resource_type_alias_registration_is_semantically_idempotent()
    {
        var services = new ServiceCollection();

        services.AddFluxFlowResourceTypeAlias("old", "current");
        services.AddFluxFlowResourceTypeAlias("old", "current");
        using var provider = services.BuildServiceProvider();

        provider.GetServices<ResourceTypeAliasDescriptor>().ShouldHaveSingleItem();
        provider.GetRequiredService<ComponentCatalog>()
            .ResourceTypeAliases["old"].ShouldBe("current");
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
    public void Descriptor_rejects_duplicate_aliases_and_ports()
    {
        Should.Throw<ArgumentException>(() =>
            new ComponentDescriptor(
                "test.node",
                UnusedFactory,
                aliases: ["alias", "alias"]))
            .Message.ShouldContain("alias");
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

    private static ComponentDescriptor Descriptor(
        string type,
        IEnumerable<string>? aliases = null)
        => new(type, UnusedFactory, aliases: aliases);

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
