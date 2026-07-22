using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class CompositionNodeRegistrationTests
{
    [Fact]
    public void Port_metadata_rejects_invalid_arguments()
    {
        Should.Throw<ArgumentNullException>(() =>
            new CompositionPortMetadata(null!, typeof(string)))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentException>(() =>
            new CompositionPortMetadata(" ", typeof(string)))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            new CompositionPortMetadata("Input", null!))
            .ParamName.ShouldBe("messageType");
    }

    [Fact]
    public void Port_metadata_supports_deconstruction()
    {
        var metadata = CompositionPortMetadata.Create<string>("Output");

        var (name, messageType) = metadata;

        name.ShouldBe("Output");
        messageType.ShouldBe(typeof(string));
    }

    [Fact]
    public void Port_metadata_defaults_to_multiple_links_and_supports_single_link_ports()
    {
        var multiple = CompositionPortMetadata.Create<string>("Output");
        var single = CompositionPorts.Metadata<string>(
            "Input",
            CompositionPortLinkCardinality.Single);

        multiple.LinkCardinality.ShouldBe(CompositionPortLinkCardinality.Multiple);
        var (name, messageType, linkCardinality) = single;
        name.ShouldBe("Input");
        messageType.ShouldBe(typeof(string));
        linkCardinality.ShouldBe(CompositionPortLinkCardinality.Single);
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new CompositionPortMetadata(
                "Input",
                typeof(string),
                (CompositionPortLinkCardinality)42));
    }

    [Fact]
    public void Signal_port_metadata_is_payload_independent()
    {
        var metadata = CompositionPorts.SignalMetadata(
            "Ack",
            CompositionPortLinkCardinality.Single);

        metadata.Name.ShouldBe("Ack");
        metadata.Kind.ShouldBe(CompositionPortKind.Signal);
        metadata.MessageType.ShouldBe(typeof(object));
        metadata.LinkCardinality.ShouldBe(CompositionPortLinkCardinality.Single);
        Should.Throw<ArgumentException>(() => new CompositionPortMetadata(
            "Ack",
            typeof(string),
            CompositionPortLinkCardinality.Multiple,
            CompositionPortKind.Signal));
    }

    [Fact]
    public void Port_metadata_dispatches_registered_message_types_without_reflection()
    {
        var visitor = new RecordingPortTypeVisitor();

        var typed = CompositionPorts.Metadata<string>("Input");
        typed.SupportsTypeVisit.ShouldBeTrue();
        typed.Accept(visitor);

        visitor.MessageType.ShouldBe(typeof(string));
        visitor.IsSignal.ShouldBeFalse();

        var signal = CompositionPorts.SignalMetadata("Ack");
        signal.Accept(visitor);

        visitor.MessageType.ShouldBe(typeof(object));
        visitor.IsSignal.ShouldBeTrue();
    }

    [Fact]
    public void Runtime_type_metadata_cannot_be_used_for_reflection_free_dispatch()
    {
        var metadata = new CompositionPortMetadata("Input", typeof(string));

        metadata.SupportsTypeVisit.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() =>
            metadata.Accept(new RecordingPortTypeVisitor()));
    }

    [Fact]
    public void Port_metadata_trims_names()
    {
        var metadata = CompositionPortMetadata.Create<string>(" Output ");

        metadata.Name.ShouldBe("Output");
    }

    [Fact]
    public void Node_registration_trims_type_and_port_metadata_names()
    {
        var registration = new CompositionNodeRegistration(
            " test.node ",
            static _ => throw new InvalidOperationException("Factory should not run."),
            inputs: [CompositionPorts.Metadata<string>(" Input ")],
            outputs: [CompositionPorts.Metadata<string>(" Output ")]);

        registration.Type.ShouldBe("test.node");
        registration.Inputs.Keys.ShouldBe(["Input"]);
        registration.Outputs.Keys.ShouldBe(["Output"]);
    }

    [Fact]
    public void Node_registry_uses_normalized_type_keys()
    {
        var registry = new CompositionNodeRegistry()
            .Register(
                " test.node ",
                static _ => throw new InvalidOperationException("Factory should not run."));

        registry.Registrations.Keys.ShouldBe(["test.node"]);
        registry.TryGetRegistration(" test.node ", out var registration).ShouldBeTrue();
        registration.Type.ShouldBe("test.node");

        var exception = Should.Throw<InvalidOperationException>(() =>
            registry.Register(
                "test.node",
                static _ => throw new InvalidOperationException("Factory should not run.")));
        exception.Message.ShouldContain("test.node");
    }

    [Fact]
    public void Node_registry_resolves_aliases_without_exposing_duplicate_registrations()
    {
        var registry = new CompositionNodeRegistry()
            .Register(
                "data.map",
                static _ => throw new InvalidOperationException("Factory should not run."))
            .RegisterAlias("flow.mapper", "data.map");

        registry.Registrations.Keys.ShouldBe(["data.map"]);
        registry.TryGetRegistration(" flow.mapper ", out var registration).ShouldBeTrue();
        registration.Type.ShouldBe("data.map");
    }

    [Fact]
    public void Node_registry_rejects_invalid_aliases_and_alias_collisions()
    {
        var registry = new CompositionNodeRegistry()
            .Register(
                "data.map",
                static _ => throw new InvalidOperationException("Factory should not run."));

        Should.Throw<InvalidOperationException>(() =>
                registry.RegisterAlias("flow.mapper", "missing.type"))
            .Message.ShouldContain("missing.type");
        Should.Throw<ArgumentException>(() => registry.RegisterAlias("data.map", "data.map"));

        registry.RegisterAlias("flow.mapper", "data.map");
        Should.Throw<InvalidOperationException>(() =>
            registry.RegisterAlias("flow.mapper", "data.map"));
        Should.Throw<InvalidOperationException>(() =>
            registry.Register(
                "flow.mapper",
                static _ => throw new InvalidOperationException("Factory should not run.")));
    }

    [Fact]
    public void Node_registration_rejects_duplicate_ports_after_trimming()
    {
        var exception = Should.Throw<ArgumentException>(() =>
            new CompositionNodeRegistration(
                "test.node",
                static _ => throw new InvalidOperationException("Factory should not run."),
                inputs:
                [
                    CompositionPorts.Metadata<string>("Input"),
                    CompositionPorts.Metadata<string>(" Input ")
                ]));

        exception.Message.ShouldContain("Input");
    }

    [Fact]
    public void Node_registration_rejects_null_port_metadata_entries()
    {
        var inputException = Should.Throw<ArgumentNullException>(() =>
            new CompositionNodeRegistration(
                "test.node",
                static _ => throw new InvalidOperationException("Factory should not run."),
                inputs: [null!]));
        var outputException = Should.Throw<ArgumentNullException>(() =>
            new CompositionNodeRegistration(
                "test.node",
                static _ => throw new InvalidOperationException("Factory should not run."),
                outputs: [null!]));

        inputException.ParamName.ShouldBe("port");
        outputException.ParamName.ShouldBe("port");
    }

    private sealed class RecordingPortTypeVisitor : ICompositionPortTypeVisitor
    {
        public Type? MessageType { get; private set; }

        public bool IsSignal { get; private set; }

        public void Visit<TMessage>(CompositionPortMetadata metadata)
        {
            MessageType = typeof(TMessage);
            IsSignal = false;
        }

        public void VisitSignal(CompositionPortMetadata metadata)
        {
            MessageType = typeof(object);
            IsSignal = true;
        }
    }
}
