using FluxFlow.Components.Assertions.Composition;
using FluxFlow.Components.Assertions.Designer;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Expectations.Composition;
using FluxFlow.Components.Expectations.Designer;
using FluxFlow.Components.Resilience.Composition;
using FluxFlow.Components.Resilience.Designer;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class OptionalOutcomePresentationTests
{
    private sealed record Family(
        Type RegistrationType,
        string ComponentType,
        Func<FluxFlowRegistrationBuilder, FluxFlowRegistrationBuilder> Runtime,
        Func<FluxFlowRegistrationBuilder, FluxFlowRegistrationBuilder> Design);

    private static readonly IReadOnlyDictionary<string, Family> Families = new Dictionary<string, Family>
    {
        ["Assertions"] = new(typeof(AssertionsServiceCollectionExtensions), AssertionsComponentDefinition.Types.Assertion,
            builder => builder.AddAssertions(), builder => builder.AddAssertionsDesign()),
        ["Expectations"] = new(typeof(ExpectationsServiceCollectionExtensions), ExpectationsComponentDefinition.Types.EventExpectation,
            builder => builder.AddExpectations(), builder => builder.AddExpectationsDesign()),
        ["Resilience"] = new(typeof(ResilienceServiceCollectionExtensions), ResilienceComponentDefinition.Types.Retry,
            builder => builder.AddResilience(), builder => builder.AddResilienceDesign())
    };

    [Theory]
    [InlineData("Assertions")]
    [InlineData("Expectations")]
    [InlineData("Resilience")]
    public void Runtime_registration_does_not_require_presentation(string name)
    {
        var family = Families[name];
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();
        family.Runtime(family.Runtime(builder)).ShouldBeSameAs(builder);
        using var provider = services.BuildServiceProvider();

        family.RegistrationType.Assembly.GetReferencedAssemblies()
            .ShouldNotContain(assembly => assembly.Name!.Contains("Designer", StringComparison.Ordinal));
        provider.GetService<ComponentDesignMetadataCatalog>().ShouldBeNull();
        services.ShouldNotContain(service => service.ServiceType.Assembly == typeof(ComponentDesignMetadataCatalog).Assembly);
        var descriptor = provider.GetRequiredService<ComponentCatalog>().Descriptors.ShouldHaveSingleItem();
        descriptor.Type.ShouldBe(family.ComponentType);
        descriptor.Inputs.ContainsKey("Input").ShouldBeTrue();
        descriptor.Outputs.Keys.Order().ShouldBe(["Events", "Output"]);
    }

    [Theory]
    [InlineData("Assertions")]
    [InlineData("Expectations")]
    [InlineData("Resilience")]
    public void Optional_presentation_preserves_structural_contracts_without_activation(string name)
    {
        var family = Families[name];
        var services = new ServiceCollection();
        var builder = family.Runtime(services.AddFluxFlowComponents());
        var descriptor = services.Where(service => service.ServiceType == typeof(ComponentDescriptor))
            .Select(service => (ComponentDescriptor)service.ImplementationInstance!).ShouldHaveSingleItem();
        var factory = descriptor.Factory;
        var resolutions = 0;
        foreach (var resource in descriptor.Resources.Values)
            ((IServiceCollection)services).Add(ServiceDescriptor.KeyedSingleton(resource.ServiceType, resource.Name, (_, _) =>
            {
                resolutions++;
                throw new InvalidOperationException("Presentation must not resolve host resources.");
            }));

        family.Design(family.Design(builder)).ShouldBeSameAs(builder);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ComponentCatalog>().Descriptors.ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
        descriptor.Factory.ShouldBeSameAs(factory);
        var metadata = provider.GetRequiredService<ComponentDesignMetadataCatalog>().All.ShouldHaveSingleItem();
        metadata.Type.Value.ShouldBe(family.ComponentType);
        metadata.ProcessingCapabilities.ShouldBe(descriptor.ProcessingCapabilities);
        metadata.Options.Where(option => option.Name.Value != "processing")
            .Select(option => (option.Name.Value, option.IsRequired)).OrderBy(option => option.Value)
            .ShouldBe(descriptor.Options.Values.Where(option => option.Name != "name")
                .Select(option => (option.Name, option.IsRequired)).OrderBy(option => option.Name));
        metadata.Options.Single(option => option.Name.Value == "processing").IsRequired.ShouldBeFalse();
        metadata.Resources.Single(resource => resource.Name.Value == "processing").IsRequired.ShouldBeFalse();
        metadata.Resources.Where(resource => resource.Name.Value != "processing")
            .Select(resource => (resource.Name.Value, resource.IsRequired)).OrderBy(resource => resource.Value)
            .ShouldBe(descriptor.Resources.Values.Select(resource => (resource.Name, resource.IsRequired)).OrderBy(resource => resource.Name));
        metadata.Ports.Count.ShouldBe(descriptor.Inputs.Count + descriptor.Outputs.Count);
        foreach (var port in metadata.Ports)
        {
            var structural = (port.Direction == PortDirection.Input ? descriptor.Inputs : descriptor.Outputs)[port.Name.Value];
            port.MessageType.ShouldBe(structural.MessageType);
            port.Kind.ShouldBe(structural.Kind);
            port.LinkCardinality.ShouldBe(structural.LinkCardinality);
            port.InputShape.ShouldBeSameAs(structural.InputShape);
            port.Summary?.Value.ShouldNotBeNullOrWhiteSpace();
        }
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        resolutions.ShouldBe(0);
    }

    [Theory]
    [InlineData("Assertions")]
    [InlineData("Expectations")]
    [InlineData("Resilience")]
    public void Optional_presentation_requires_registered_component(string name)
    {
        var family = Families[name];
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();
        var before = services.ToArray();

        Should.Throw<InvalidOperationException>(() => family.Design(builder))
            .Message.ShouldContain(family.ComponentType);
        services.ShouldBe(before);
        services.ShouldNotContain(service => service.ServiceType == typeof(ComponentDescriptor));
        Should.Throw<ArgumentNullException>(() => family.Design(null!));
    }
}
