using FluxFlow.Composition.Definition;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentPortMetadataTests
{
    [Theory]
    [InlineData(ComponentPortLinkCardinality.Single)]
    [InlineData(ComponentPortLinkCardinality.Multiple)]
    public void Canonical_metadata_factory_preserves_existing_metadata_contract(ComponentPortLinkCardinality cardinality)
    {
        var metadata = ComponentPortMetadata.Create(" Input ", cardinality);
        metadata.ShouldBe(ComponentPortMetadata.Create<global::FluxFlow.Data.FlowValue>(" Input ", cardinality));
        var (name, messageType, links) = metadata;
        name.ShouldBe("Input");
        messageType.ShouldBe(typeof(global::FluxFlow.Data.FlowValue));
        links.ShouldBe(cardinality);
        metadata.Kind.ShouldBe(ComponentPortKind.Message);
        ComponentPortMetadata.Create("Input").LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Multiple);
        var signal = ComponentPortMetadata.CreateSignal("Stop", cardinality);
        signal.Kind.ShouldBe(ComponentPortKind.Signal);
        signal.MessageType.ShouldBe(typeof(object));
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

    [Theory]
    [InlineData(ComponentPortLinkCardinality.Multiple)]
    [InlineData(ComponentPortLinkCardinality.Single)]
    public void Port_metadata_constructor_and_factories_have_identical_structural_values(
        ComponentPortLinkCardinality cardinality)
    {
        var metadata = new ComponentPortMetadata(" Input ", typeof(FlowValue), cardinality);
        metadata.ShouldBe(ComponentPortMetadata.Create<FlowValue>("Input", cardinality));
        metadata.Name.ShouldBe("Input");
        metadata.MessageType.ShouldBe(typeof(FlowValue));
        metadata.Kind.ShouldBe(ComponentPortKind.Message);
        metadata.LinkCardinality.ShouldBe(cardinality);
        new ComponentPortMetadata("Input", typeof(string), cardinality)
            .ShouldBe(ComponentPorts.Metadata<string>("Input", cardinality));
        new ComponentPortMetadata(" Ack ", typeof(object), cardinality, ComponentPortKind.Signal)
            .ShouldBe(ComponentPorts.SignalMetadata("Ack", cardinality));
    }

    [Fact]
    public void Port_registration_rejects_invalid_kind()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ComponentPortMetadata(
            "Input", typeof(FlowValue), ComponentPortLinkCardinality.Multiple, (ComponentPortKind)42))
            .ParamName.ShouldBe("kind");
    }

}
