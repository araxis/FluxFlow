using FluxFlow.Components.Assertions.Composition;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Expectations.Composition;
using FluxFlow.Components.FileSystem.Composition;
using FluxFlow.Components.Http.Composition;
using FluxFlow.Components.Mapping.Composition;
using FluxFlow.Components.Metrics.Composition;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Observability.Composition;
using FluxFlow.Components.Payloads.Composition;
using FluxFlow.Components.Projections.Composition;
using FluxFlow.Components.Resilience.Composition;
using FluxFlow.Components.Routing.Composition;
using FluxFlow.Components.Serialization.Composition;
using FluxFlow.Components.Sessions.Composition;
using FluxFlow.Components.Sources.Composition;
using FluxFlow.Components.State.Composition;
using FluxFlow.Components.Storage.Composition;
using FluxFlow.Components.Timers.Composition;
using FluxFlow.Components.Validation.Composition;
using FluxFlow.Composition;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System.Xml.Linq;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class ComponentFamilyRegistrationMatrixTests
{
    private const string ProcessingName = "processing";

    private static readonly IReadOnlyList<FamilyCase> Families =
    [
        new(
            "Assertions",
            "FluxFlow.Components.Assertions.Composition",
            static services => services.AddFluxFlowComponents().AddAssertions(),
            [AssertionsComponentDefinition.Types.Assertion]),
        new(
            "Expectations",
            "FluxFlow.Components.Expectations.Composition",
            static services => services.AddFluxFlowComponents().AddExpectations(),
            [ExpectationsComponentDefinition.Types.EventExpectation]),
        new(
            "FileSystem",
            "FluxFlow.Components.FileSystem.Composition",
            static services => services.AddFluxFlowComponents().AddFileSystem(),
            [
                FileSystemComponentDefinition.Types.Read,
                FileSystemComponentDefinition.Types.Write,
                FileSystemComponentDefinition.Types.DirectoryEnumerate,
                FileSystemComponentDefinition.Types.Watch
            ]),
        new(
            "Http",
            "FluxFlow.Components.Http.Composition",
            static services => services.AddFluxFlowComponents().AddHttp(),
            [HttpComponentDefinition.Types.Client]),
        new(
            "Mapping",
            "FluxFlow.Components.Mapping.Composition",
            static services => services.AddFluxFlowComponents().AddMapping(),
            [MappingComponentDefinition.Types.Mapper]),
        new(
            "Metrics",
            "FluxFlow.Components.Metrics.Composition",
            static services => services.AddFluxFlowComponents().AddMetrics(),
            [MetricsComponentDefinition.Types.Aggregate]),
        new(
            "Mqtt",
            "FluxFlow.Components.Mqtt.Composition",
            static services => services.AddFluxFlowComponents().AddMqtt(),
            [
                MqttComponentDefinition.Types.Control,
                MqttComponentDefinition.Types.Publish,
                MqttComponentDefinition.Types.Trigger,
                MqttComponentDefinition.Types.Events
            ]),
        new(
            "Observability",
            "FluxFlow.Components.Observability.Composition",
            static services => services.AddFluxFlowComponents().AddObservability(),
            [
                ObservabilityComponentDefinition.Types.Counter,
                ObservabilityComponentDefinition.Types.Logger,
                ObservabilityComponentDefinition.Types.Metrics
            ]),
        new(
            "Payloads",
            "FluxFlow.Components.Payloads.Composition",
            static services => services.AddFluxFlowComponents().AddPayloads(),
            [PayloadsComponentDefinition.Types.Inspect]),
        new(
            "Projections",
            "FluxFlow.Components.Projections.Composition",
            static services => services.AddFluxFlowComponents().AddProjections(),
            [ProjectionsComponentDefinition.Types.EventProjection]),
        new(
            "Resilience",
            "FluxFlow.Components.Resilience.Composition",
            static services => services.AddFluxFlowComponents().AddResilience(),
            [ResilienceComponentDefinition.Types.Retry]),
        new(
            "Routing",
            "FluxFlow.Components.Routing.Composition",
            static services => services.AddFluxFlowComponents().AddRouting(),
            [
                RoutingComponentDefinition.Types.Window,
                RoutingComponentDefinition.Types.Correlation,
                RoutingComponentDefinition.Types.Join
            ]),
        new(
            "Serialization",
            "FluxFlow.Components.Serialization.Composition",
            static services => services.AddFluxFlowComponents().AddSerialization(),
            [
                SerializationComponentDefinition.Types.JsonParse,
                SerializationComponentDefinition.Types.JsonStringify,
                SerializationComponentDefinition.Types.TextEncode,
                SerializationComponentDefinition.Types.TextDecode,
                SerializationComponentDefinition.Types.Base64Encode,
                SerializationComponentDefinition.Types.Base64Decode
            ]),
        new(
            "Sessions",
            "FluxFlow.Components.Sessions.Composition",
            static services => services.AddFluxFlowComponents().AddSessions(),
            [
                SessionsComponentDefinition.Types.Recorder,
                SessionsComponentDefinition.Types.Replay,
                SessionsComponentDefinition.Types.Query
            ]),
        new(
            "Sources",
            "FluxFlow.Components.Sources.Composition",
            static services => services.AddFluxFlowComponents().AddSources(),
            [
                SourcesComponentDefinition.Types.Generated,
                SourcesComponentDefinition.Types.Sequence
            ]),
        new(
            "State",
            "FluxFlow.Components.State.Composition",
            static services => services.AddFluxFlowComponents().AddState(),
            [StateComponentDefinition.Types.Reducer]),
        new(
            "Storage",
            "FluxFlow.Components.Storage.Composition",
            static services => services.AddFluxFlowComponents().AddStorage(),
            [
                StorageComponentDefinition.Types.Put,
                StorageComponentDefinition.Types.Get,
                StorageComponentDefinition.Types.Query,
                StorageComponentDefinition.Types.Delete
            ]),
        new(
            "Timers",
            "FluxFlow.Components.Timers.Composition",
            static services => services.AddFluxFlowComponents().AddTimers(),
            [
                TimersComponentDefinition.Types.Interval,
                TimersComponentDefinition.Types.Schedule,
                TimersComponentDefinition.Types.Delay,
                TimersComponentDefinition.Types.Throttle,
                TimersComponentDefinition.Types.Debounce
            ]),
        new(
            "Validation",
            "FluxFlow.Components.Validation.Composition",
            static services => services.AddFluxFlowComponents().AddValidation(),
            [ValidationComponentDefinition.Types.JsonSchemaValidator])
    ];

    [Fact]
    public void Explicit_family_matrix_registers_all_44_declarations_idempotently()
    {
        var allTypes = new List<string>();

        foreach (var family in Families)
        {
            var services = new ServiceCollection();

            family.Register(services).Services.ShouldBeSameAs(services);
            family.Register(services).Services.ShouldBeSameAs(services);

            var descriptors = ReadDescriptors(services);
            using var provider = services.BuildServiceProvider();
            var componentCatalog = provider.GetRequiredService<ComponentCatalog>();
            var designCatalog = provider.GetRequiredService<ComponentDesignMetadataCatalog>();

            descriptors.Select(static descriptor => descriptor.Type)
                .ShouldBe(family.ExpectedTypes, $"{family.Name} declaration order changed.");
            componentCatalog.Components.Keys.ShouldBe(
                family.ExpectedTypes.OrderBy(static type => type, StringComparer.Ordinal),
                $"{family.Name} runtime catalog must contain exactly the declared types.");
            designCatalog.All.Select(static metadata => metadata.Type.Value)
                .ShouldBe(family.ExpectedTypes, $"{family.Name} design catalog order changed.");

            foreach (var descriptor in descriptors)
            {
                componentCatalog.TryGetDescriptor(descriptor.Type, out var registered)
                    .ShouldBeTrue($"{family.Name} did not register '{descriptor.Type}'.");
                registered.ShouldBeSameAs(
                    descriptor,
                    $"{family.Name} must share one authoritative descriptor instance.");
            }

            allTypes.AddRange(descriptors.Select(static descriptor => descriptor.Type));
        }

        Families.Count.ShouldBe(19);
        allTypes.Count.ShouldBe(44);
        allTypes.Distinct(StringComparer.Ordinal).Count().ShouldBe(44);
    }

    [Fact]
    public void Explicit_family_matrix_keeps_all_44_explicit_event_outputs()
    {
        var eventOutputs = new List<(string Family, string Type)>();

        foreach (var family in Families)
        {
            var services = new ServiceCollection();
            family.Register(services);
            using var provider = services.BuildServiceProvider();
            var designCatalog = provider.GetRequiredService<ComponentDesignMetadataCatalog>();

            foreach (var descriptor in ReadDescriptors(services))
            {
                descriptor.Outputs.TryGetValue("Events", out var events).ShouldBeTrue(
                    $"{family.Name} '{descriptor.Type}' must explicitly retain its established Events output.");
                events.ShouldNotBeNull().MessageType.ShouldBe(typeof(ComponentEvent));
                events.Kind.ShouldBe(ComponentPortKind.Message);

                designCatalog.TryGet(new ComponentType(descriptor.Type), out var metadata)
                    .ShouldBeTrue($"{family.Name} '{descriptor.Type}' has no matching design metadata.");
                var designedEvents = metadata.ShouldNotBeNull().Ports
                    .Single(port => port.Name.Value == "Events");
                designedEvents.Direction.ShouldBe(PortDirection.Output);
                designedEvents.MessageType.ShouldBe(typeof(ComponentEvent));
                designedEvents.ValueType?.Value.ShouldBe(nameof(ComponentEvent));
                eventOutputs.Add((family.Name, descriptor.Type));
            }
        }

        Families.Count.ShouldBe(19);
        eventOutputs.Count.ShouldBe(44);
        eventOutputs.Select(static item => item.Type)
            .Distinct(StringComparer.Ordinal).Count().ShouldBe(44);
    }

    [Fact]
    public void Reliable_capacity_options_use_positive_ranges_and_output_delivery_guidance()
    {
        var services = new ServiceCollection();
        foreach (var family in Families)
            family.Register(services);

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<ComponentDesignMetadataCatalog>();
        catalog.All.Count.ShouldBe(44);
        var capacityOptions = catalog.All
            .SelectMany(metadata => metadata.Options
                .Where(option => IsReliableCapacityOption(
                    metadata.Type.Value,
                    option.Name.Value))
                .Select(option => (Type: metadata.Type.Value, Option: option)))
            .ToArray();
        var boundedOptions = capacityOptions
            .Where(candidate => string.Equals(
                candidate.Option.Name.Value,
                "boundedCapacity",
                StringComparison.Ordinal))
            .ToArray();

        boundedOptions.ShouldNotBeEmpty(
            "registered boundedCapacity options must remain discoverable in the catalog.");
        capacityOptions
            .Where(candidate => string.Equals(
                candidate.Type,
                ResilienceComponentDefinition.Types.Retry,
                StringComparison.Ordinal))
            .ShouldHaveSingleItem().Option.Name.Value.ShouldBe(
                ResilienceComponentDefinition.Options.Capacity);
        capacityOptions
            .Where(candidate => candidate.Type.StartsWith("mqtt.", StringComparison.Ordinal))
            .Select(candidate => (candidate.Type, candidate.Option.Name.Value))
            .ShouldBe(
                [
                    (MqttComponentDefinition.Types.Control, MqttComponentDefinition.Options.MaximumPendingRequests),
                    (MqttComponentDefinition.Types.Publish, MqttComponentDefinition.Options.MaximumPendingRequests),
                    (MqttComponentDefinition.Types.Trigger, MqttComponentDefinition.Options.MaximumPendingMessages),
                    (MqttComponentDefinition.Types.Events, MqttComponentDefinition.Options.MaximumPendingEvents)
                ],
                ignoreOrder: true);

        foreach (var (type, option) in capacityOptions)
        {
            option.Min.ShouldBe(1d, $"{type}.{option.Name.Value} must reject non-positive capacity.");
            var helperText = option.HelperText.ShouldNotBeNull().Value;
            helperText.Contains("reliable", StringComparison.OrdinalIgnoreCase)
                .ShouldBeTrue($"{type}.{option.Name.Value} must describe reliable delivery.");
            helperText.Contains("normal-data", StringComparison.OrdinalIgnoreCase)
                .ShouldBeTrue($"{type}.{option.Name.Value} must describe normal-data behavior.");
            (helperText.Contains("output", StringComparison.OrdinalIgnoreCase) ||
             helperText.Contains("delivery", StringComparison.OrdinalIgnoreCase))
                .ShouldBeTrue($"{type}.{option.Name.Value} must describe output or delivery, not input alone.");
        }

        var concurrencyOption = catalog.All
            .Single(metadata => metadata.Type.Value == MqttComponentDefinition.Types.Control)
            .Options.Single(option =>
                option.Name.Value == MqttComponentDefinition.Options.MaximumConcurrentRequests);
        capacityOptions.ShouldNotContain(candidate =>
            ReferenceEquals(candidate.Option, concurrencyOption));
    }

    [Fact]
    public void Explicit_family_matrix_builds_descriptor_authoritative_designer_catalogs()
    {
        foreach (var family in Families)
        {
            var services = new ServiceCollection();
            family.Register(services);
            using var provider = services.BuildServiceProvider();
            var descriptors = ReadDescriptors(services);
            var componentCatalog = provider.GetRequiredService<ComponentCatalog>();
            var designCatalog = provider.GetRequiredService<ComponentDesignMetadataCatalog>();

            designCatalog.All.Select(static metadata => metadata.Type.Value)
                .ShouldBe(family.ExpectedTypes, $"{family.Name} Designer catalog order changed.");

            foreach (var descriptor in descriptors)
            {
                designCatalog.TryGet(new ComponentType(descriptor.Type), out var metadata)
                    .ShouldBeTrue($"{family.Name} has no Designer metadata for '{descriptor.Type}'.");
                metadata.ShouldNotBeNull();
                ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();

                AssertFinalPresentationMatchesDescriptor(family, descriptor, metadata);

                componentCatalog.TryGetDescriptor(descriptor.Type, out var registered).ShouldBeTrue();
                registered.ShouldBeSameAs(descriptor);
            }
        }
    }

    [Fact]
    public void Explicit_family_matrix_uses_ordinal_exact_type_lookup()
    {
        foreach (var family in Families)
        {
            var services = new ServiceCollection();
            family.Register(services);
            using var provider = services.BuildServiceProvider();
            var componentCatalog = provider.GetRequiredService<ComponentCatalog>();
            var designCatalog = provider.GetRequiredService<ComponentDesignMetadataCatalog>();

            foreach (var type in family.ExpectedTypes)
            {
                componentCatalog.TryGetDescriptor(type, out _).ShouldBeTrue();
                designCatalog.TryGet(new ComponentType(type), out _).ShouldBeTrue();

                var changedCase = ChangeCase(type);
                componentCatalog.TryGetDescriptor(changedCase, out _).ShouldBeFalse();
                designCatalog.TryGet(new ComponentType(changedCase), out _).ShouldBeFalse();
            }

            componentCatalog.TryGetDescriptor("unknown.component", out _).ShouldBeFalse();
            designCatalog.TryGet(new ComponentType("unknown.component"), out _).ShouldBeFalse();
        }
    }

    [Fact]
    public void Explicit_family_matrix_registration_and_catalog_order_are_repeatable()
    {
        foreach (var family in Families)
        {
            var first = BuildOrderSnapshot(family);
            var second = BuildOrderSnapshot(family);

            first.DeclarationTypes.ShouldBe(family.ExpectedTypes);
            first.DesignTypes.ShouldBe(family.ExpectedTypes);
            first.RuntimeTypes.ShouldBe(
                family.ExpectedTypes.OrderBy(static type => type, StringComparer.Ordinal));
            second.DeclarationTypes.ShouldBe(
                first.DeclarationTypes,
                $"{family.Name} declaration order was not repeatable.");
            second.DesignTypes.ShouldBe(
                first.DesignTypes,
                $"{family.Name} Designer catalog order was not repeatable.");
            second.RuntimeTypes.ShouldBe(
                first.RuntimeTypes,
                $"{family.Name} runtime catalog order was not repeatable.");
        }

        Families.SelectMany(static family => family.ExpectedTypes).Count().ShouldBe(44);
    }

    [Fact]
    public void Removed_designer_registration_helpers_do_not_reappear()
    {
        Type[] definitions =
        [
            typeof(AssertionsComponentDefinition),
            typeof(ExpectationsComponentDefinition),
            typeof(FileSystemComponentDefinition),
            typeof(HttpComponentDefinition),
            typeof(MappingComponentDefinition),
            typeof(MetricsComponentDefinition),
            typeof(MqttComponentDefinition),
            typeof(ObservabilityComponentDefinition),
            typeof(PayloadsComponentDefinition),
            typeof(ProjectionsComponentDefinition),
            typeof(ResilienceComponentDefinition),
            typeof(RoutingComponentDefinition),
            typeof(SerializationComponentDefinition),
            typeof(SessionsComponentDefinition),
            typeof(SourcesComponentDefinition),
            typeof(StateComponentDefinition),
            typeof(StorageComponentDefinition),
            typeof(TimersComponentDefinition),
            typeof(ValidationComponentDefinition)
        ];

        definitions.Length.ShouldBe(19);
        definitions.All(definition => definition.GetMethod("CreateMetadata") is null)
            .ShouldBeTrue();
        typeof(ComponentRegistrationExtensions).GetMethod("AddDesignerCatalog").ShouldBeNull();
        typeof(ComponentDesignMetadataCatalog).GetMethod("Add").ShouldBeNull();
        typeof(ComponentDesignMetadataCatalog).GetMethod("AddRange").ShouldBeNull();
        typeof(ComponentDesignMetadataCatalog).GetMethod("FromDeclarations").ShouldBeNull();
        typeof(ComponentRegistrationBuilder).GetMethod("DescribeInput").ShouldBeNull();
        typeof(ComponentRegistrationBuilder).GetMethod("DescribeOutput").ShouldBeNull();
        typeof(ComponentRegistrationBuilder).GetMethod("DescribeOption").ShouldBeNull();
        typeof(ComponentRegistrationBuilder).GetMethod("DescribeResource").ShouldBeNull();
        typeof(ComponentRegistrationBuilder).GetMethod("SetOptionRange").ShouldBeNull();
        var declarationType = typeof(ComponentRegistrationExtensions).Assembly.GetType(
            "FluxFlow.Components.Designer.ComponentDesignDeclaration");
        declarationType.ShouldNotBeNull();
        declarationType.IsPublic.ShouldBeFalse();
        typeof(ComponentRegistrationExtensions).Assembly.GetType(
            "FluxFlow.Components.Designer.ComponentDesignMetadataServiceCollectionExtensions")
            .ShouldBeNull();
        typeof(ComponentRegistrationExtensions).Assembly.GetType(
            "FluxFlow.Components.Designer.ComponentDesignMetadataBuilder")
            .ShouldBeNull();
        typeof(ComponentRegistrationExtensions).Assembly.GetType(
            "FluxFlow.Components.Designer.Contracts.OptionDesignMetadataFactory")
            .ShouldBeNull();
        typeof(ComponentRegistrationExtensions).Assembly.GetType(
            "FluxFlow.Components.Designer.Contracts.ResourceDesignMetadataFactory")
            .ShouldBeNull();
    }

    [Fact]
    public void Explicit_family_matrix_rejects_conflicting_registrations_clearly()
    {
        foreach (var family in Families)
        {
            var services = new ServiceCollection();
            family.Register(services);
            var original = ReadDescriptors(services).First();
            var action = () => services.AddFluxFlowComponents().Advanced.AddDynamicComponent(
                original.Type,
                component => component.UseFactory(static _ => new ConflictingRegistrationNode()));

            var exception = action.ShouldThrow<InvalidOperationException>();
            exception.Message.ShouldContain(original.Type);
            exception.Message.ShouldContain("conflicting descriptor registration");
        }
    }

    [Fact]
    public void Explicit_family_matrix_matches_active_release_manifest()
    {
        var expectedPackages = Families
            .Select(static family => family.PackageId)
            .OrderBy(static packageId => packageId, StringComparer.Ordinal)
            .ToArray();
        var manifestPackages = PackageManifest.Read(ReleaseTestPaths.FindRepositoryRoot())
            .Select(static entry => entry.PackageId)
            .Where(static packageId =>
                packageId.StartsWith("FluxFlow.Components.", StringComparison.Ordinal) &&
                packageId.EndsWith(".Composition", StringComparison.Ordinal))
            .OrderBy(static packageId => packageId, StringComparer.Ordinal)
            .ToArray();

        expectedPackages.ShouldBe(manifestPackages);
        expectedPackages.Length.ShouldBe(19);
    }

    [Fact]
    public void Component_family_package_boundaries_match_flat_registration_ownership()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");

        foreach (var family in Families)
        {
            var project = Directory.EnumerateFiles(
                    sourceRoot,
                    $"{family.PackageId}.csproj",
                    SearchOption.AllDirectories)
                .ShouldHaveSingleItem();

            ProjectReferences(project).ShouldContain(
                "FluxFlow.Components.Designer",
                $"{family.Name} must own its designed AddComponent registration dependency.");
        }

        var compositionProject = Path.Combine(
            sourceRoot,
            "FluxFlow.Composition",
            "FluxFlow.Composition.csproj");
        ProjectReferences(compositionProject).ShouldNotContain("FluxFlow.Components.Designer");

        var designerProject = Path.Combine(
            sourceRoot,
            "FluxFlow.Components.Designer",
            "FluxFlow.Components.Designer.csproj");
        ProjectReferences(designerProject).ShouldContain("FluxFlow.Composition");
        ProjectReferences(designerProject).ShouldNotContain("FluxFlow.Engine");
    }

    [Theory]
    [InlineData(@"..\..\src\FluxFlow.Components.Designer\FluxFlow.Components.Designer.csproj")]
    [InlineData("../../src/FluxFlow.Components.Designer/FluxFlow.Components.Designer.csproj")]
    public void Project_reference_name_is_separator_neutral(string include)
        => ProjectReferenceName(include).ShouldBe("FluxFlow.Components.Designer");

    private static void AssertFinalPresentationMatchesDescriptor(
        FamilyCase family,
        ComponentDescriptor descriptor,
        ComponentDesignMetadata metadata)
    {
        metadata.Type.Value.ShouldBe(descriptor.Type);
        metadata.ProcessingCapabilities.ShouldBe(descriptor.ProcessingCapabilities);

        var expectedResourceNames = descriptor.Resources.Keys
            .Append(ProcessingName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal);
        metadata.Resources.Select(static resource => resource.Name.Value)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ShouldBe(
                expectedResourceNames,
                $"{family.Name} '{descriptor.Type}' finalized resources changed.");

        foreach (var structural in descriptor.Resources.Values)
        {
            var resource = metadata.Resources.Single(candidate =>
                string.Equals(candidate.Name.Value, structural.Name, StringComparison.Ordinal));
            resource.IsRequired.ShouldBe(structural.IsRequired);
            if (structural.ValueTypeHint is not null)
                resource.ValueType?.Value.ShouldBe(structural.ValueTypeHint);
            else
                resource.ValueType?.Value.ShouldNotBeNullOrWhiteSpace();
        }

        var expectedPorts = descriptor.Inputs.Count + descriptor.Outputs.Count;
        metadata.Ports.Count.ShouldBe(expectedPorts);
        foreach (var structural in descriptor.Inputs.Values)
            AssertPort(metadata, structural, PortDirection.Input);
        foreach (var structural in descriptor.Outputs.Values)
            AssertPort(metadata, structural, PortDirection.Output);

        var processingOption = metadata.Options.Single(option =>
            string.Equals(option.Name.Value, ProcessingName, StringComparison.Ordinal));
        processingOption.Kind.ShouldBe(OptionValueKind.Text);
        processingOption.IsRequired.ShouldBeFalse();
        AttributeValue(processingOption.Attributes, OptionDesignMetadataAttributeNames.Section)
            .ShouldBe("Runtime");
        AttributeValue(processingOption.Attributes, OptionDesignMetadataAttributeNames.Importance)
            .ShouldBe(OptionDesignMetadataAttributeValues.Advanced);
        AttributeValue(processingOption.Attributes, OptionDesignMetadataAttributeNames.Editor)
            .ShouldBe(OptionDesignMetadataAttributeValues.Text);
        AttributeValue(processingOption.Attributes, OptionDesignMetadataAttributeNames.RelatedResource)
            .ShouldBe(ProcessingName);

        var processingResource = metadata.Resources.Single(resource =>
            string.Equals(resource.Name.Value, ProcessingName, StringComparison.Ordinal));
        processingResource.IsRequired.ShouldBeFalse();
        processingResource.ValueType?.Value.ShouldBe("CompositionProcessingProfile");
        AttributeValue(processingResource.Attributes, ResourceDesignMetadataAttributeNames.Ownership)
            .ShouldBe(ResourceDesignMetadataAttributeValues.HostOwned);
        AttributeValue(processingResource.Attributes, ResourceDesignMetadataAttributeNames.PickerKind)
            .ShouldBe(ResourceDesignMetadataAttributeValues.ProcessingProfile);
        AttributeValue(processingResource.Attributes, ResourceDesignMetadataAttributeNames.KeyPattern)
            .ShouldBe("Resources.{name}");
        AttributeValue(processingResource.Attributes, ResourceDesignMetadataAttributeNames.Option)
            .ShouldBe(ProcessingName);

        var events = metadata.Ports.Single(port =>
            string.Equals(port.Name.Value, "Events", StringComparison.Ordinal));
        events.Direction.ShouldBe(PortDirection.Output);
        events.MessageType.ShouldBe(typeof(ComponentEvent));
        events.ValueType?.Value.ShouldBe(nameof(ComponentEvent));
        events.Group?.Value.ShouldBe("Diagnostics");
    }

    private static void AssertPort(
        ComponentDesignMetadata metadata,
        ComponentPortMetadata structural,
        PortDirection direction)
    {
        var port = metadata.Ports.Single(candidate =>
            candidate.Direction == direction &&
            string.Equals(candidate.Name.Value, structural.Name, StringComparison.Ordinal));

        port.Direction.ShouldBe(direction);
        port.MessageType.ShouldBe(structural.MessageType);
        port.ValueType?.Value.ShouldBe(ToValueTypeHint(structural.MessageType));
        port.Kind.ShouldBe(structural.Kind);
        port.LinkCardinality.ShouldBe(structural.LinkCardinality);
    }

    private static void AssertNestedSnapshotsAreFresh(
        ComponentDesignMetadata first,
        ComponentDesignMetadata second)
    {
        for (var index = 0; index < first.Options.Count; index++)
        {
            first.Options[index].ShouldNotBeSameAs(second.Options[index]);
            if (first.Options[index].Choices.Count > 0)
            {
                first.Options[index].Choices.ShouldNotBeSameAs(second.Options[index].Choices);
                for (var choiceIndex = 0;
                     choiceIndex < first.Options[index].Choices.Count;
                     choiceIndex++)
                {
                    first.Options[index].Choices[choiceIndex]
                        .ShouldNotBeSameAs(second.Options[index].Choices[choiceIndex]);
                    if (first.Options[index].Choices[choiceIndex].Attributes.Count > 0)
                    {
                        first.Options[index].Choices[choiceIndex].Attributes.ShouldNotBeSameAs(
                            second.Options[index].Choices[choiceIndex].Attributes);
                    }
                }
            }

            if (first.Options[index].Attributes.Count > 0)
                first.Options[index].Attributes.ShouldNotBeSameAs(second.Options[index].Attributes);
        }

        for (var index = 0; index < first.Resources.Count; index++)
        {
            first.Resources[index].ShouldNotBeSameAs(second.Resources[index]);
            if (first.Resources[index].Attributes.Count > 0)
            {
                first.Resources[index].Attributes.ShouldNotBeSameAs(
                    second.Resources[index].Attributes);
            }
        }

        for (var index = 0; index < first.Ports.Count; index++)
        {
            first.Ports[index].ShouldNotBeSameAs(second.Ports[index]);
            if (first.Ports[index].Attributes.Count > 0)
                first.Ports[index].Attributes.ShouldNotBeSameAs(second.Ports[index].Attributes);
        }
    }

    private static RegistrationOrderSnapshot BuildOrderSnapshot(FamilyCase family)
    {
        var services = new ServiceCollection();
        family.Register(services);
        using var provider = services.BuildServiceProvider();
        var descriptors = ReadDescriptors(services);
        var componentCatalog = provider.GetRequiredService<ComponentCatalog>();
        var designCatalog = provider.GetRequiredService<ComponentDesignMetadataCatalog>();
        return new RegistrationOrderSnapshot(
            descriptors
                .Select(static descriptor => descriptor.Type)
                .ToArray(),
            designCatalog.All
                .Select(static metadata => metadata.Type.Value)
                .ToArray(),
            componentCatalog.Components.Keys.ToArray());
    }

    private static ComponentDescriptor[] ReadDescriptors(IServiceCollection services)
        => services
            .Where(static descriptor =>
                descriptor.ServiceType == typeof(ComponentDescriptor))
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<ComponentDescriptor>()
            .ToArray();

    private static bool IsReliableCapacityOption(string componentType, string optionName)
    {
        if (string.Equals(optionName, "boundedCapacity", StringComparison.Ordinal))
            return true;
        if (string.Equals(
                componentType,
                ResilienceComponentDefinition.Types.Retry,
                StringComparison.Ordinal) &&
            string.Equals(
                optionName,
                ResilienceComponentDefinition.Options.Capacity,
                StringComparison.Ordinal))
        {
            return true;
        }

        return componentType.StartsWith("mqtt.", StringComparison.Ordinal) &&
               (string.Equals(
                    optionName,
                    MqttComponentDefinition.Options.MaximumPendingRequests,
                    StringComparison.Ordinal) ||
                string.Equals(
                    optionName,
                    MqttComponentDefinition.Options.MaximumPendingMessages,
                    StringComparison.Ordinal) ||
                string.Equals(
                    optionName,
                    MqttComponentDefinition.Options.MaximumPendingEvents,
                    StringComparison.Ordinal));
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private static string[] ProjectReferences(string projectPath)
        => XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(static include => include is not null)
            .Select(static include => ProjectReferenceName(include!))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    private static string ProjectReferenceName(string include)
        => Path.GetFileNameWithoutExtension(include.Replace('\\', '/'));

    private static string ChangeCase(string value)
    {
        var characters = value.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (!char.IsLetter(characters[index]))
                continue;

            characters[index] = char.IsUpper(characters[index])
                ? char.ToLowerInvariant(characters[index])
                : char.ToUpperInvariant(characters[index]);
            return new string(characters);
        }

        throw new InvalidOperationException($"Component type '{value}' has no alphabetic character.");
    }

    private static string ToValueTypeHint(Type type)
    {
        if (type.IsArray)
            return $"{ToValueTypeHint(type.GetElementType()!)}[]";
        if (!type.IsGenericType)
            return type.Name;

        var tick = type.Name.IndexOf('`', StringComparison.Ordinal);
        var name = tick < 0 ? type.Name : type.Name[..tick];
        return $"{name}<{string.Join(",", type.GetGenericArguments().Select(ToValueTypeHint))}>";
    }

    private static string MetadataSignature(ComponentDesignMetadata metadata)
    {
        var values = new List<string>
        {
            $"type:{metadata.Type.Value}",
            $"display:{metadata.DisplayName?.Value}",
            $"category:{metadata.Category?.Value}",
            $"summary:{metadata.Summary?.Value}",
            $"icon:{metadata.IconKey?.Value}",
            $"node:{metadata.PreferredNodeName?.Value}",
            $"width:{metadata.SuggestedEditorWidth}",
            $"capabilities:{metadata.ProcessingCapabilities}",
            $"attributes:{AttributeSignature(metadata.Attributes)}"
        };

        values.AddRange(metadata.Options.Select(option => string.Join('|',
            "option",
            option.Name.Value,
            option.Kind,
            option.DisplayName?.Value,
            option.HelperText?.Value,
            option.IsRequired,
            option.DefaultValue?.GetType().FullName,
            option.DefaultValue,
            option.Min,
            option.Max,
            AttributeSignature(option.Attributes),
            string.Join(';', option.Choices.Select(choice => string.Join(',',
                choice.Value.Value,
                choice.DisplayName?.Value,
                choice.HelperText?.Value,
                AttributeSignature(choice.Attributes)))))));
        values.AddRange(metadata.Resources.Select(resource => string.Join('|',
            "resource",
            resource.Name.Value,
            resource.DisplayName?.Value,
            resource.Order,
            resource.Summary?.Value,
            resource.ValueType?.Value,
            resource.IsRequired,
            AttributeSignature(resource.Attributes))));
        values.AddRange(metadata.Ports.Select(port => string.Join('|',
            "port",
            port.Name.Value,
            port.Direction,
            port.DisplayName?.Value,
            port.Group?.Value,
            port.Order,
            port.Summary?.Value,
            port.ValueType?.Value,
            port.MessageType?.AssemblyQualifiedName,
            port.Kind,
            port.LinkCardinality,
            port.IsPrimary,
            AttributeSignature(port.Attributes))));

        return string.Join('\n', values);
    }

    private static string AttributeSignature(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes)
        => string.Join(';', attributes
            .OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Key.Value}={pair.Value.Value}"));

    private sealed record FamilyCase(
        string Name,
        string PackageId,
        Func<IServiceCollection, FluxFlowRegistrationBuilder> Register,
        IReadOnlyList<string> ExpectedTypes);

    private sealed record RegistrationOrderSnapshot(
        IReadOnlyList<string> DeclarationTypes,
        IReadOnlyList<string> DesignTypes,
        IReadOnlyList<string> RuntimeTypes);

    private sealed class ConflictingRegistrationNode : IFlowNode
    {
        public Task Completion { get; } = Task.CompletedTask;

        public void Complete()
        {
        }

        public void Fault(Exception exception)
            => ArgumentNullException.ThrowIfNull(exception);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
