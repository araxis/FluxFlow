using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class ComponentDesignDeclarationTests
{
    [Fact]
    public void Registration_is_explicit_and_idempotent_for_the_same_declaration()
    {
        var descriptor = CreateDescriptor();
        var declaration = new ComponentDesignDeclaration(
            descriptor,
            CreateMetadata(descriptor));
        var services = new ServiceCollection();

        services.AddComponentDesignDeclaration(declaration);
        services.AddComponentDesignDeclaration(declaration);
        services.AddComponentDesignMetadataCatalog();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentCatalog>()
            .Descriptors.ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
        provider.GetServices<ComponentDesignDeclaration>()
            .ShouldHaveSingleItem().ShouldBeSameAs(declaration);

        var metadata = provider.GetRequiredService<ComponentDesignMetadataCatalog>()
            .All.ShouldHaveSingleItem();
        metadata.Type.ShouldBe(new ComponentType(descriptor.Type));
        metadata.ProcessingCapabilities.ShouldBe(descriptor.ProcessingCapabilities);
        metadata.Options.Single(option => option.Name.Value == "enabled")
            .IsRequired.ShouldBeTrue();
        metadata.Resources.ShouldContain(resource =>
            resource.Name.Value == "clock" &&
            resource.ValueType.HasValue &&
            resource.ValueType.Value.Value == nameof(TimeProvider) &&
            resource.IsRequired);
        metadata.Ports.ShouldContain(port =>
            port.Name.Value == "Input" &&
            port.MessageType == typeof(string));
    }

    [Fact]
    public void Registration_rejects_conflicting_declarations_for_the_same_type()
    {
        var firstDescriptor = CreateDescriptor();
        var secondDescriptor = CreateDescriptor();
        var services = new ServiceCollection()
            .AddComponentDesignDeclaration(new ComponentDesignDeclaration(
                firstDescriptor,
                CreateMetadata(firstDescriptor)));

        var act = () => services.AddComponentDesignDeclaration(
            new ComponentDesignDeclaration(
                secondDescriptor,
                CreateMetadata(secondDescriptor)));

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldContain("conflicting design declarations");
    }

    [Fact]
    public void Catalog_rejects_presentation_for_an_undeclared_option()
    {
        var descriptor = CreateDescriptor();
        var metadata = new ComponentDesignMetadataBuilder(descriptor)
            .AddOption("other", OptionValueKind.Text)
            .AddResource("clock")
            .AddInputPort("Input")
            .AddOutputPort("Output")
            .Build();
        var declaration = new ComponentDesignDeclaration(descriptor, metadata);

        var act = () => ComponentDesignMetadataCatalog.FromDeclarations(
            new ComponentCatalog([descriptor]),
            [declaration]);

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldContain("does not match a registered component option");
    }

    private static ComponentDescriptor CreateDescriptor()
        => new(
            "test.component",
            static _ => throw new NotSupportedException(),
            inputs: [ComponentPorts.Metadata<string>("Input")],
            outputs: [ComponentPorts.Metadata<int>("Output")],
            processingCapabilities: CompositionProcessingCapabilities.ParallelRelaxedOrder,
            options: [ComponentOptions.Metadata<bool>("enabled", isRequired: true)],
            resources: [ComponentResources.Metadata<TimeProvider>("clock", isRequired: true)]);

    private static ComponentDesignMetadata CreateMetadata(ComponentDescriptor descriptor)
        => new ComponentDesignMetadataBuilder(descriptor)
            .WithDisplay(displayName: "Test")
            .AddOption("enabled", OptionValueKind.Boolean)
            .AddResource("clock", valueType: "Incorrect")
            .AddInputPort("Input", valueType: "Incorrect")
            .AddOutputPort("Output", valueType: "Incorrect")
            .Build();
}
