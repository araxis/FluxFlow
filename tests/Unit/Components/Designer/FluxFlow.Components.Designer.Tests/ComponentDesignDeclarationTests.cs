using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class ComponentDesignDeclarationTests
{
    [Fact]
    public void Declaration_rejects_descriptor_metadata_type_mismatch()
    {
        var descriptor = CreateDescriptor("test.descriptor");
        var metadata = CreateMetadata(CreateDescriptor("test.metadata"));

        var act = () => new ComponentDesignDeclaration(descriptor, metadata);

        var exception = act.ShouldThrow<ArgumentException>();
        exception.ParamName.ShouldBe("metadata");
        exception.Message.ShouldContain("test.descriptor");
        exception.Message.ShouldContain("test.metadata");
    }

    private static ComponentDescriptor CreateDescriptor(string type = "test.component")
        => new(
            type,
            static _ => throw new NotSupportedException(),
            inputs: [ComponentPorts.Metadata<string>("Input")],
            outputs: [ComponentPorts.Metadata<int>("Output")],
            processingCapabilities: CompositionProcessingCapabilities.ParallelRelaxedOrder,
            options: [ComponentOptions.Metadata<bool>("enabled", isRequired: true)],
            resources: [ComponentResources.Metadata<TimeProvider>("clock", isRequired: true)]);

    private static ComponentDesignMetadata CreateMetadata(ComponentDescriptor descriptor)
        => new()
        {
            Type = new ComponentType(descriptor.Type),
            DisplayName = new ComponentMetadataText("Test"),
            Options =
            [
                new OptionDesignMetadata
                {
                    Name = new ComponentOptionName("enabled"),
                    Kind = OptionValueKind.Boolean
                }
            ],
            Resources =
            [
                new ResourceDesignMetadata
                {
                    Name = new ComponentResourceName("clock"),
                    ValueType = new ComponentValueTypeHint("Incorrect")
                }
            ],
            Ports =
            [
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Input"),
                    Direction = PortDirection.Input,
                    ValueType = new ComponentValueTypeHint("Incorrect")
                },
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Output"),
                    Direction = PortDirection.Output,
                    ValueType = new ComponentValueTypeHint("Incorrect")
                }
            ]
        };
}
