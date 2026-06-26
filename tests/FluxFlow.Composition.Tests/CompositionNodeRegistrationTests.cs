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
