using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class ComponentDesignDeclarationTests
{
    [Fact]
    public void CreateRange_pairs_exact_types_in_deterministic_ordinal_order()
    {
        var capitalizedDescriptor = CreateDescriptor("Test.alpha");
        var alphaDescriptor = CreateDescriptor("test.alpha");
        var betaDescriptor = CreateDescriptor("test.beta");
        var capitalizedMetadata = CreateMetadata(capitalizedDescriptor);
        var alphaMetadata = CreateMetadata(alphaDescriptor);
        var betaMetadata = CreateMetadata(betaDescriptor);

        var declarations = ComponentDesignDeclaration.CreateRange(
            [betaDescriptor, alphaDescriptor, capitalizedDescriptor],
            [alphaMetadata, capitalizedMetadata, betaMetadata])
            .ToArray();

        declarations.Select(static declaration => declaration.Descriptor.Type)
            .ShouldBe(["Test.alpha", "test.alpha", "test.beta"]);
        declarations[0].Descriptor.ShouldBeSameAs(capitalizedDescriptor);
        declarations[0].Metadata.ShouldBeSameAs(capitalizedMetadata);
        declarations[1].Descriptor.ShouldBeSameAs(alphaDescriptor);
        declarations[1].Metadata.ShouldBeSameAs(alphaMetadata);
        declarations[2].Descriptor.ShouldBeSameAs(betaDescriptor);
        declarations[2].Metadata.ShouldBeSameAs(betaMetadata);
    }

    [Fact]
    public void CreateRange_reports_all_missing_pairs_in_deterministic_order()
    {
        var descriptors = new[]
        {
            CreateDescriptor("test.zulu"),
            CreateDescriptor("test.bravo")
        };
        var metadata = new[]
        {
            CreateMetadata(CreateDescriptor("test.yankee")),
            CreateMetadata(CreateDescriptor("test.alpha"))
        };

        var act = () => ComponentDesignDeclaration.CreateRange(descriptors, metadata);

        act.ShouldThrow<ArgumentException>().Message.ShouldBe(
            "Component declarations must pair exactly. " +
            "Missing metadata: [test.bravo, test.zulu]. " +
            "Missing descriptors: [test.alpha, test.yankee].");
    }

    [Fact]
    public void CreateRange_rejects_duplicate_descriptor_types()
    {
        var descriptor = CreateDescriptor("test.duplicate");
        var act = () => ComponentDesignDeclaration.CreateRange(
            [descriptor, CreateDescriptor(descriptor.Type)],
            [CreateMetadata(descriptor)]);

        act.ShouldThrow<ArgumentException>().Message.ShouldContain("test.duplicate");
    }

    [Fact]
    public void CreateRange_rejects_duplicate_metadata_types()
    {
        var descriptor = CreateDescriptor("test.duplicate");
        var act = () => ComponentDesignDeclaration.CreateRange(
            [descriptor],
            [CreateMetadata(descriptor), CreateMetadata(descriptor)]);

        act.ShouldThrow<ArgumentException>().Message.ShouldContain("test.duplicate");
    }

    [Fact]
    public void CreateRange_rejects_null_collections()
    {
        Should.Throw<ArgumentNullException>(() => ComponentDesignDeclaration.CreateRange(
                null!,
                Array.Empty<ComponentDesignMetadata>()))
            .ParamName.ShouldBe("descriptors");
        Should.Throw<ArgumentNullException>(() => ComponentDesignDeclaration.CreateRange(
                Array.Empty<ComponentDescriptor>(),
                null!))
            .ParamName.ShouldBe("metadata");
    }

    [Fact]
    public void Range_registration_registers_every_declaration_and_returns_the_same_collection()
    {
        var alphaDescriptor = CreateDescriptor("test.alpha");
        var betaDescriptor = CreateDescriptor("test.beta");
        var alphaDeclaration = new ComponentDesignDeclaration(
            alphaDescriptor,
            CreateMetadata(alphaDescriptor));
        var betaDeclaration = new ComponentDesignDeclaration(
            betaDescriptor,
            CreateMetadata(betaDescriptor));
        var services = new ServiceCollection();

        var returned = services.AddComponentDesignDeclarations(
            [betaDeclaration, alphaDeclaration]);

        returned.ShouldBeSameAs(services);
        using var provider = services.BuildServiceProvider();
        provider.GetServices<ComponentDesignDeclaration>()
            .ShouldBe([betaDeclaration, alphaDeclaration]);
        provider.GetRequiredService<ComponentCatalog>().Descriptors
            .Select(static descriptor => descriptor.Type)
            .ShouldBe(["test.alpha", "test.beta"]);
    }

    [Fact]
    public void Range_registration_rejects_null_arguments_and_items()
    {
        Should.Throw<ArgumentNullException>(() =>
                ComponentDesignMetadataServiceCollectionExtensions.AddComponentDesignDeclarations(
                    null!,
                    Array.Empty<ComponentDesignDeclaration>()))
            .ParamName.ShouldBe("services");

        var services = new ServiceCollection();
        Should.Throw<ArgumentNullException>(() => services.AddComponentDesignDeclarations(null!))
            .ParamName.ShouldBe("declarations");
        Should.Throw<ArgumentNullException>(() => services.AddComponentDesignDeclarations([null!]))
            .ParamName.ShouldBe("declaration");
    }

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
        => new ComponentDesignMetadataBuilder(descriptor)
            .WithDisplay(displayName: "Test")
            .AddOption("enabled", OptionValueKind.Boolean)
            .AddResource("clock", valueType: "Incorrect")
            .AddInputPort("Input", valueType: "Incorrect")
            .AddOutputPort("Output", valueType: "Incorrect")
            .Build();
}
