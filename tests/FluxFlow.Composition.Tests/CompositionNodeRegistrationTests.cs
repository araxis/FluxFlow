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
}
