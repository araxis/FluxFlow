using FluxFlow.Components.FileSystem.Composition;
using FluxFlow.Components.FileSystem.Designer;
using FluxFlow.Components.Sessions.Composition;
using FluxFlow.Components.Sessions.Designer;
using FluxFlow.Components.Timers.Composition;
using FluxFlow.Components.Timers.Designer;
using FluxFlow.Components.Payloads.Composition;
using FluxFlow.Components.Payloads.Designer;
using FluxFlow.Components.Projections.Composition;
using FluxFlow.Components.Projections.Designer;
using FluxFlow.Components.Observability.Composition;
using FluxFlow.Components.Observability.Designer;
using FluxFlow.Components.Metrics.Composition;
using FluxFlow.Components.Metrics.Designer;
using FluxFlow.Components.State.Composition;
using FluxFlow.Components.State.Designer;
using FluxFlow.Components.Storage.Composition;
using FluxFlow.Components.Storage.Designer;
using FluxFlow.Components.Sources.Composition;
using FluxFlow.Components.Sources.Designer;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class OptionalModulePresentationTests
{
    private sealed record Family(Type Registration, int Count,
        Func<FluxFlowRegistrationBuilder, FluxFlowRegistrationBuilder> Runtime,
        Func<FluxFlowRegistrationBuilder, FluxFlowRegistrationBuilder> Design);

    private static readonly IReadOnlyDictionary<string, Family> Families = new Dictionary<string, Family>
    {
        ["FileSystem"] = new(typeof(FileSystemServiceCollectionExtensions), 4,
            builder => builder.AddFileSystem(), builder => builder.AddFileSystemDesign()),
        ["Sessions"] = new(typeof(SessionsServiceCollectionExtensions), 3,
            builder => builder.AddSessions(), builder => builder.AddSessionsDesign()),
        ["Timers"] = new(typeof(TimersServiceCollectionExtensions), 5,
            builder => builder.AddTimers(), builder => builder.AddTimersDesign()),
        ["Payloads"] = new(typeof(PayloadsServiceCollectionExtensions), 1,
            builder => builder.AddPayloads(), builder => builder.AddPayloadsDesign()),
        ["Projections"] = new(typeof(ProjectionsServiceCollectionExtensions), 1,
            builder => builder.AddProjections(), builder => builder.AddProjectionsDesign()),
        ["Observability"] = new(typeof(ObservabilityServiceCollectionExtensions), 3,
            builder => builder.AddObservability(), builder => builder.AddObservabilityDesign()),
        ["Metrics"] = new(typeof(MetricsServiceCollectionExtensions), 1,
            builder => builder.AddMetrics(), builder => builder.AddMetricsDesign()),
        ["State"] = new(typeof(StateServiceCollectionExtensions), 1,
            builder => builder.AddState(), builder => builder.AddStateDesign()),
        ["Storage"] = new(typeof(StorageServiceCollectionExtensions), 4,
            builder => builder.AddStorage(), builder => builder.AddStorageDesign()),
        ["Sources"] = new(typeof(SourcesServiceCollectionExtensions), 2,
            builder => builder.AddSources(), builder => builder.AddSourcesDesign()),
    };

    [Theory]
    [InlineData("FileSystem")]
    [InlineData("Sessions")]
    [InlineData("Timers")]
    [InlineData("Payloads")]
    [InlineData("Projections")]
    [InlineData("Observability")]
    [InlineData("Metrics")]
    [InlineData("State")]
    [InlineData("Storage")]
    [InlineData("Sources")]
    public void Runtime_registration_is_presentation_free(string name)
    {
        var family = Families[name];
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();
        family.Runtime(family.Runtime(builder)).ShouldBeSameAs(builder);
        using var provider = services.BuildServiceProvider();
        family.Registration.Assembly.GetReferencedAssemblies()
            .ShouldNotContain(reference => reference.Name!.Contains("Designer", StringComparison.Ordinal));
        provider.GetService<ComponentDesignMetadataCatalog>().ShouldBeNull();
        services.ShouldNotContain(service => service.ServiceType.Assembly == typeof(ComponentDesignMetadataCatalog).Assembly);
        var descriptors = provider.GetRequiredService<ComponentCatalog>().Descriptors;
        descriptors.Count.ShouldBe(family.Count);
        descriptors.Select(descriptor => descriptor.Type).Distinct().Count().ShouldBe(family.Count);
        foreach (var descriptor in descriptors)
            descriptor.Outputs.Keys.Order().ShouldBe(["Events", "Output"]);
    }

    [Theory]
    [InlineData("FileSystem")]
    [InlineData("Sessions")]
    [InlineData("Timers")]
    [InlineData("Payloads")]
    [InlineData("Projections")]
    [InlineData("Observability")]
    [InlineData("Metrics")]
    [InlineData("State")]
    [InlineData("Storage")]
    [InlineData("Sources")]
    public void Design_registration_preserves_contracts_without_resources(string name)
    {
        var family = Families[name];
        var services = new ServiceCollection();
        var builder = family.Runtime(services.AddFluxFlowComponents());
        var descriptors = services.Where(service => service.ServiceType == typeof(ComponentDescriptor))
            .Select(service => (ComponentDescriptor)service.ImplementationInstance!).ToDictionary(descriptor => descriptor.Type);
        descriptors.Count.ShouldBe(family.Count);
        var factories = descriptors.ToDictionary(pair => pair.Key, pair => pair.Value.Factory);
        var resolutions = 0;
        foreach (var resource in descriptors.Values.SelectMany(descriptor => descriptor.Resources.Values)
            .DistinctBy(resource => (resource.Name, resource.ServiceType)))
            ((IServiceCollection)services).Add(ServiceDescriptor.KeyedSingleton(resource.ServiceType, resource.Name, (_, _) =>
            {
                resolutions++;
                throw new InvalidOperationException("Presentation must not resolve runtime resources.");
            }));

        family.Design(family.Design(builder)).ShouldBeSameAs(builder);
        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<ComponentCatalog>();
        var presentation = provider.GetRequiredService<ComponentDesignMetadataCatalog>();
        catalog.Descriptors.Count.ShouldBe(family.Count);
        presentation.All.Count.ShouldBe(family.Count);
        foreach (var descriptor in catalog.Descriptors)
        {
            descriptor.ShouldBeSameAs(descriptors[descriptor.Type]);
            descriptor.Factory.ShouldBeSameAs(factories[descriptor.Type]);
            var metadata = presentation.All.Single(item => item.Type.Value == descriptor.Type);
            metadata.ProcessingCapabilities.ShouldBe(descriptor.ProcessingCapabilities);
            metadata.DisplayName?.Value.ShouldNotBeNullOrWhiteSpace();
            metadata.Category?.Value.ShouldBe(name);
            ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
            var hidden = new HashSet<string>(["name", "MaxDegreeOfParallelism", "EnsureOrdered"], StringComparer.OrdinalIgnoreCase);
            metadata.Options.Where(option => option.Name.Value != "processing")
                .Select(option => (option.Name.Value, option.IsRequired)).OrderBy(option => option.Value)
                .ShouldBe(descriptor.Options.Values.Where(option => !hidden.Contains(option.Name))
                    .Select(option => (option.Name, option.IsRequired)).OrderBy(option => option.Name));
            metadata.Resources.Where(resource => resource.Name.Value != "processing")
                .Select(resource => (resource.Name.Value, resource.IsRequired)).OrderBy(resource => resource.Value)
                .ShouldBe(descriptor.Resources.Values.Select(resource => (resource.Name, resource.IsRequired)).OrderBy(resource => resource.Name));
            metadata.Options.Single(option => option.Name.Value == "processing").IsRequired.ShouldBeFalse();
            metadata.Resources.Single(resource => resource.Name.Value == "processing").IsRequired.ShouldBeFalse();
            metadata.Ports.Count.ShouldBe(descriptor.Inputs.Count + descriptor.Outputs.Count);
            foreach (var port in metadata.Ports)
            {
                var expected = (port.Direction == PortDirection.Input ? descriptor.Inputs : descriptor.Outputs)[port.Name.Value];
                port.MessageType.ShouldBe(expected.MessageType);
                port.Kind.ShouldBe(expected.Kind);
                port.LinkCardinality.ShouldBe(expected.LinkCardinality);
                port.InputShape.ShouldBeSameAs(expected.InputShape);
                port.Summary?.Value.ShouldNotBeNullOrWhiteSpace();
            }
        }
        resolutions.ShouldBe(0);
    }

    [Theory]
    [InlineData("FileSystem")]
    [InlineData("Sessions")]
    [InlineData("Timers")]
    [InlineData("Payloads")]
    [InlineData("Projections")]
    [InlineData("Observability")]
    [InlineData("Metrics")]
    [InlineData("State")]
    [InlineData("Storage")]
    [InlineData("Sources")]
    public void Design_registration_requires_runtime(string name)
    {
        var family = Families[name];
        var services = new ServiceCollection();
        var builder = services.AddFluxFlowComponents();
        var before = services.ToArray();
        Should.Throw<InvalidOperationException>(() => family.Design(builder))
            .Message.ShouldContain("must be registered");
        services.ShouldBe(before);
        Should.Throw<ArgumentNullException>(() => family.Design(null!)).ParamName.ShouldBe("builder");
    }
}

