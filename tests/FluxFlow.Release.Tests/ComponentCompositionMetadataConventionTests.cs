using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed partial class ComponentCompositionMetadataConventionTests
{
    private static readonly string[] RemovedComponentTypeNames =
    [
        "flow.mapper",
        "flow.assert",
        "json.schema-validator",
        "state.reducer",
        "event.expectation",
        "event.projection",
        "metrics.aggregate",
        "flow.counter",
        "flow.logger",
        "flow.metrics",
        "flow.correlation",
        "source.generated",
        "directory.enumerate",
        "http.client",
        "session.recorder",
        "mqtt.control",
        "mqtt.trigger"
    ];

    [Fact]
    public void Extracted_factory_methods_are_resolved_for_metadata_conventions()
    {
        const string implementation = """
            internal static class ExtractedFactories
            {
                internal static ValueTask<ComponentInstance> CreateAsync(
                    ComponentActivationContext context)
                {
                    var options = context.BindConfiguration<ExampleOptions>();
                    throw new NotSupportedException();
                }
            }
            """;
        const string registration = """
            internal static ComponentDescriptor ExampleDescriptor { get; } = new(
                ExampleComponentDefinition.Types.Example,
                ExtractedFactories.CreateAsync);
            """;

        var factories = ReadFactoryOptionTypes(implementation, "fixture");
        factories["CreateAsync"].ShouldBe(["ExampleOptions"]);

        var match = ComponentDescriptorRegistrationRegex().Match(registration);
        match.Success.ShouldBeTrue();
        match.Groups["factory"].Value.ShouldBe("ExtractedFactories.CreateAsync");
    }

    [Fact]
    public void Component_composition_packages_ship_one_authoritative_definition()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        entries.ShouldNotBeEmpty("component composition packages should be listed in the release manifest.");

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var definitionFile = ReadSingleDefinitionFile(projectDirectory, entry.PackageId);
            var definitionContent = File.ReadAllText(definitionFile);
            var implementationContent = ReadCompositionImplementationContent(projectDirectory);
            definitionContent.Contains("CreateMetadata()", StringComparison.Ordinal)
                .ShouldBeFalse(
                    $"{entry.PackageId} definition must not retain a parallel metadata factory.");
            Regex.IsMatch(
                    definitionContent,
                    @"CreateOptions\s*\(\s*string\s+type\s*\)",
                    RegexOptions.CultureInvariant)
                .ShouldBeFalse(
                    $"{entry.PackageId} must use exact per-component option declarations instead of string dispatch.");
            Regex.IsMatch(
                    definitionContent,
                    @"CreateResources\s*\(\s*string\s+type\s*\)",
                    RegexOptions.CultureInvariant)
                .ShouldBeFalse(
                    $"{entry.PackageId} must use exact per-component resource declarations instead of string dispatch.");
            Regex.IsMatch(
                    implementationContent,
                    @"Lazy\s*<\s*IReadOnlyCollection\s*<\s*ComponentDesignDeclaration\s*>\s*>",
                    RegexOptions.CultureInvariant)
                .ShouldBeFalse(
                    $"{entry.PackageId} must register explicit declarations without lazy declaration caches.");
            Regex.IsMatch(
                    implementationContent,
                    @"ComponentDesignDeclaration\s*\.\s*CreateRange\s*\(",
                    RegexOptions.CultureInvariant)
                .ShouldBeFalse(
                    $"{entry.PackageId} must pair explicit descriptors and metadata directly without CreateRange.");
            Directory.EnumerateFiles(projectDirectory, "*ComponentDesignMetadataProvider.cs")
                .ShouldBeEmpty($"{entry.PackageId} must not retain a parallel metadata provider.");

            var project = LoadProject(root, entry);
            var referencedPackageIds = ReadReferencedPackageIds(project, projectDirectory)
                .ToArray();

            referencedPackageIds.ShouldContain(
                "FluxFlow.Components.Designer",
                $"{entry.PackageId} must reference Designer for its component definition.");
            referencedPackageIds.ShouldNotContain(
                "FluxFlow.Engine",
                $"{entry.PackageId} must stay engine-free.");
        }
    }

    [Fact]
    public void Component_composition_definitions_validate_at_runtime()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var metadata = metadataItems;

            metadata.Count.ShouldBeGreaterThan(
                0,
                $"{entry.PackageId} definition must return at least one metadata entry.");

            foreach (var item in metadata)
            {
                var errors = ComponentDesignMetadataValidator.Validate(item);
                errors.ShouldBeEmpty(
                    $"{entry.PackageId} definition emitted invalid metadata for '{item.Type}'.");
            }

            var catalog = BuildDefaultDesignerCatalog(assembly, entry.PackageId);

            foreach (var item in metadata)
            {
                catalog.TryGet(item.Type, out _)
                    .ShouldBeTrue($"{entry.PackageId} catalog must contain declared metadata for '{item.Type}'.");
            }
        }
    }

    [Fact]
    public void Component_composition_designer_metadata_is_usable_for_palette_and_inspectors()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);

            foreach (var metadata in metadataItems)
            {
                var componentType = metadata.Type.ToString();
                AssertRequiredDesignerText(
                    metadata.DisplayName,
                    $"{entry.PackageId} Designer metadata for '{componentType}' must include a display name.");
                AssertRequiredDesignerText(
                    metadata.Category?.Value,
                    $"{entry.PackageId} Designer metadata for '{componentType}' must include a category.");
                AssertRequiredDesignerText(
                    metadata.Summary,
                    $"{entry.PackageId} Designer metadata for '{componentType}' must include a summary.");

                foreach (var option in metadata.Options)
                {
                    AssertRequiredDesignerText(
                        option.DisplayName,
                        $"{entry.PackageId} Designer metadata for '{componentType}' option '{option.Name}' must include a display name.");
                    AssertRequiredDesignerText(
                        option.HelperText,
                        $"{entry.PackageId} Designer metadata for '{componentType}' option '{option.Name}' must include helper text.");

                    foreach (var choice in option.Choices)
                    {
                        AssertRequiredDesignerText(
                            choice.DisplayName,
                            $"{entry.PackageId} Designer metadata for '{componentType}' option '{option.Name}' choice '{choice.Value}' must include a display name.");
                    }
                }

                foreach (var resource in metadata.Resources)
                {
                    AssertRequiredDesignerText(
                        resource.DisplayName,
                        $"{entry.PackageId} Designer metadata for '{componentType}' resource '{resource.Name.Value}' must include a display name.");
                    AssertRequiredDesignerText(
                        resource.Summary,
                        $"{entry.PackageId} Designer metadata for '{componentType}' resource '{resource.Name.Value}' must include a summary.");
                    AssertRequiredDesignerText(
                        resource.ValueType?.Value,
                        $"{entry.PackageId} Designer metadata for '{componentType}' resource '{resource.Name.Value}' must include a value type.");
                    AssertHostOwnedResourcePickerMetadata(
                        entry,
                        componentType,
                        resource);
                }

                foreach (var port in metadata.Ports)
                {
                    AssertRequiredDesignerText(
                        port.DisplayName,
                        $"{entry.PackageId} Designer metadata for '{componentType}' {port.Direction.ToString().ToLowerInvariant()} port '{port.Name}' must include a display name.");
                    AssertRequiredDesignerText(
                        port.Summary,
                        $"{entry.PackageId} Designer metadata for '{componentType}' {port.Direction.ToString().ToLowerInvariant()} port '{port.Name}' must include a summary.");
                    AssertRequiredDesignerText(
                        port.ValueType?.Value,
                        $"{entry.PackageId} Designer metadata for '{componentType}' {port.Direction.ToString().ToLowerInvariant()} port '{port.Name}' must include a value type.");
                }
            }
        }
    }

    [Fact]
    public void Component_composition_designer_metadata_is_palette_ready()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var preferredNodeNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var metadata in metadataItems)
            {
                var componentType = metadata.Type.ToString();
                AssertRequiredDesignerText(
                    metadata.IconKey?.Value,
                    $"{entry.PackageId} Designer metadata for '{componentType}' must include an icon key.");
                AssertRequiredDesignerText(
                    metadata.PreferredNodeName?.Value,
                    $"{entry.PackageId} Designer metadata for '{componentType}' must include a preferred node name.");

                metadata.SuggestedEditorWidth.HasValue
                    .ShouldBeTrue(
                        $"{entry.PackageId} Designer metadata for '{componentType}' must include a suggested editor width.");
                metadata.SuggestedEditorWidth.GetValueOrDefault()
                    .ShouldBeGreaterThan(
                        319,
                        $"{entry.PackageId} Designer metadata for '{componentType}' suggested editor width should support usable editors.");
                metadata.SuggestedEditorWidth.GetValueOrDefault()
                    .ShouldBeLessThan(
                        721,
                        $"{entry.PackageId} Designer metadata for '{componentType}' suggested editor width should avoid oversized inspectors.");

                preferredNodeNames
                    .Add(metadata.PreferredNodeName.GetValueOrDefault().Value)
                    .ShouldBeTrue(
                        $"{entry.PackageId} Designer metadata preferred node name '{metadata.PreferredNodeName}' is duplicated.");
            }
        }
    }

    [Fact]
    public void Component_composition_designer_metadata_option_hints_follow_designer_contract()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);

            foreach (var metadata in metadataItems)
            {
                var componentType = metadata.Type.ToString();
                var resourceNames = metadata.Resources
                    .Select(resource => resource.Name.Value)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var option in metadata.Options)
                {
                    AssertDesignerOptionHintMetadata(
                        entry,
                        componentType,
                        option,
                        resourceNames);
                }
            }
        }
    }

    [Fact]
    public void Component_composition_definitions_use_named_collection_helpers()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var providerFile = ReadSingleDefinitionFile(projectDirectory, entry.PackageId);
            var providerContent = File.ReadAllText(providerFile);
            var inlineCollectionAssignments = InlineMetadataCollectionAssignmentRegex()
                .Matches(providerContent)
                .Select(match => match.Groups["property"].Value)
                .ToArray();

            inlineCollectionAssignments.ShouldBeEmpty(
                $"{entry.PackageId} definition must assign presentation collections through named helpers or variables instead of inline collection expressions.");
        }
    }

    [Fact]
    public void Component_composition_definitions_do_not_retain_competing_metadata_paths()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var providerFile = ReadSingleDefinitionFile(projectDirectory, entry.PackageId);
            var providerContent = File.ReadAllText(providerFile);
            var directMetadataConstruction = DirectComponentMetadataConstructionRegex()
                .Matches(providerContent)
                .Select(match => match.Value)
                .ToArray();

            providerContent.Contains(
                    "ComponentDesignMetadataBuilder",
                    StringComparison.Ordinal)
                .ShouldBeFalse(
                    $"{entry.PackageId} definition must not retain the competing metadata builder path.");
            directMetadataConstruction.ShouldBeEmpty(
                $"{entry.PackageId} definition must not manually construct {nameof(ComponentDesignMetadata)}.");
        }
    }

    [Fact]
    public void Component_composition_designer_metadata_ordering_is_stable()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);

            foreach (var metadata in metadataItems)
            {
                var componentType = metadata.Type.ToString();
                AssertStableMetadataOrder(
                    metadata.Resources.Select(resource => (resource.Name.Value, resource.Order)),
                    $"{entry.PackageId} Designer metadata for '{componentType}' resources");
                AssertStableMetadataOrder(
                    metadata.Ports
                        .Where(port => port.Direction == PortDirection.Input)
                        .Select(port => (port.Name.ToString(), port.Order)),
                    $"{entry.PackageId} Designer metadata for '{componentType}' input ports");
                AssertStableMetadataOrder(
                    metadata.Ports
                        .Where(port => port.Direction == PortDirection.Output)
                        .Select(port => (port.Name.ToString(), port.Order)),
                    $"{entry.PackageId} Designer metadata for '{componentType}' output ports");
            }
        }
    }

    [Fact]
    public void Component_composition_designer_metadata_matches_component_catalog()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var componentCatalog = BuildDefaultComponentCatalog(assembly, entry.PackageId);
            var metadataByType = BuildDefaultDesignerCatalog(assembly, entry.PackageId)
                .All
                .ToDictionary(metadata => metadata.Type.ToString(), StringComparer.Ordinal);

            componentCatalog.Components.Keys
                .Order(StringComparer.Ordinal)
                .ShouldBe(
                    metadataByType.Keys.Order(StringComparer.Ordinal),
                    $"{entry.PackageId} Designer metadata component types must match default DI descriptor registrations.");

            foreach (var (componentType, registration) in componentCatalog.Components.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var metadata = metadataByType[componentType];

                foreach (var input in registration.Inputs.Keys.Order(StringComparer.Ordinal))
                {
                    metadata.Ports.Any(port =>
                            port.Direction == PortDirection.Input &&
                            string.Equals(port.Name.ToString(), input, StringComparison.Ordinal))
                        .ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{componentType}' must expose input port '{input}'.");
                }

                foreach (var output in registration.Outputs.Keys.Order(StringComparer.Ordinal))
                {
                    metadata.Ports.Any(port =>
                            port.Direction == PortDirection.Output &&
                            string.Equals(port.Name.ToString(), output, StringComparison.Ordinal))
                        .ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{componentType}' must expose output port '{output}'.");
                }
            }
        }
    }

    [Fact]
    public void Component_composition_concrete_port_value_types_match_descriptor_message_types()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var componentCatalog = BuildDefaultComponentCatalog(assembly, entry.PackageId);
            var metadataByType = BuildDefaultDesignerCatalog(assembly, entry.PackageId)
                .All
                .ToDictionary(metadata => metadata.Type.ToString(), StringComparer.Ordinal);

            foreach (var (componentType, registration) in componentCatalog.Components.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var metadata = metadataByType[componentType];

                foreach (var input in registration.Inputs.Values.OrderBy(input => input.Name, StringComparer.Ordinal))
                {
                    AssertConcretePortValueType(
                        entry.PackageId,
                        componentType,
                        metadata,
                        input,
                        PortDirection.Input);
                }

                foreach (var output in registration.Outputs.Values.OrderBy(output => output.Name, StringComparer.Ordinal))
                {
                    AssertConcretePortValueType(
                        entry.PackageId,
                        componentType,
                        metadata,
                        output,
                        PortDirection.Output);
                }
            }
        }
    }

    [Fact]
    public void Component_composition_service_collection_extensions_are_discoverable_and_idempotent()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var componentTypesFile = ReadSingleComponentTypesFile(projectDirectory, entry.PackageId);
            var componentTypeValues = PublicStringConstantWithValueRegex()
                .Matches(ReadDefinitionSection(File.ReadAllText(componentTypesFile), "Types"))
                .Select(match => match.Groups["value"].Value)
                .ToHashSet(StringComparer.Ordinal);
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);

            componentTypeValues.ShouldNotBeEmpty(
                $"{entry.PackageId} component-type file should expose at least one component type constant.");

            var publicRegistrationSurface = assembly
                .GetTypes()
                .Where(type => type is { IsAbstract: true, IsSealed: true } &&
                    type.Name.EndsWith("ServiceCollectionExtensions", StringComparison.Ordinal))
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(method =>
                    method.Name.StartsWith("Add", StringComparison.Ordinal) &&
                    method.IsDefined(typeof(ExtensionAttribute), inherit: false) &&
                    method.GetParameters() is [{ ParameterType: var firstParameter }, ..] &&
                    (firstParameter == typeof(FluxFlowRegistrationBuilder) ||
                     firstParameter == typeof(IServiceCollection)))
                .ToArray();
            var method = publicRegistrationSurface.ShouldHaveSingleItem(
                $"{entry.PackageId} must expose only one flat family registration extension.");
            method.Name.StartsWith("Add", StringComparison.Ordinal).ShouldBeTrue();
            method.Name.EndsWith("Components", StringComparison.Ordinal).ShouldBeFalse();
            method.IsDefined(typeof(ExtensionAttribute), inherit: false)
                .ShouldBeTrue($"{entry.PackageId} registration method '{method.Name}' must be an extension method.");
            method.ReturnType.ShouldBe(typeof(FluxFlowRegistrationBuilder));
            method.GetParameters().Select(parameter => parameter.ParameterType)
                .ShouldBe([typeof(FluxFlowRegistrationBuilder)]);

            var services = new ServiceCollection();
            InvokeComponentRegistrationMethod(method, services, entry.PackageId)
                .Services.ShouldBeSameAs(services);
            InvokeComponentRegistrationMethod(method, services, entry.PackageId)
                .Services.ShouldBeSameAs(services);
            var catalog = BuildComponentCatalog(services);

            catalog.Components.ShouldNotBeEmpty(
                $"{entry.PackageId} registration method '{method.Name}' must register component descriptors.");
            catalog.Components.Keys.All(componentTypeValues.Contains).ShouldBeTrue(
                $"{entry.PackageId} must register only package component-type constants as canonical types.");
            foreach (var componentType in componentTypeValues)
            {
                catalog.TryGetDescriptor(componentType, out _).ShouldBeTrue(
                    $"{entry.PackageId} canonical component type '{componentType}' must resolve.");
            }

            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<ComponentDesignMetadataCatalog>().All.Count
                .ShouldBe(
                    catalog.Components.Count,
                    $"{entry.PackageId} must automatically expose one design entry per component descriptor.");
            services.Count(descriptor => descriptor.ServiceType == typeof(ComponentDescriptor))
                .ShouldBe(catalog.Components.Count,
                    $"{entry.PackageId} repeated family registration must not duplicate descriptors.");
        }
    }

    [Fact]
    public void Component_type_catalogs_expose_only_canonical_names()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var componentTypesFile = ReadSingleComponentTypesFile(projectDirectory, entry.PackageId);
            var legacyNodeTypes = PublicStringConstantWithValueRegex()
                .Matches(ReadDefinitionSection(File.ReadAllText(componentTypesFile), "Types"))
                .Where(match => match.Groups["name"].Value.StartsWith("Legacy", StringComparison.Ordinal))
                .Select(match => match.Groups["value"].Value)
                .ToArray();

            legacyNodeTypes.ShouldBeEmpty(
                $"{entry.PackageId} must not expose Legacy* component-type constants.");
        }

        typeof(ComponentDescriptor).GetProperty("Aliases").ShouldBeNull();
        typeof(ComponentCatalog).GetProperty("Aliases").ShouldBeNull();
        typeof(ComponentCatalog).GetMethod("TryResolveType").ShouldBeNull();
        typeof(ComponentCatalog).GetMethod("TryResolveResourceType").ShouldBeNull();
    }

    [Fact]
    public void Component_composition_service_collection_extensions_are_documented()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var publicApiOverview = File.ReadAllText(Path.Combine(root, "docs", "14-public-api-overview.md"));
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var readmePath = Path.Combine(projectDirectory, "README.md");
            File.Exists(readmePath)
                .ShouldBeTrue($"{entry.PackageId} must include a package README.");

            var readme = File.ReadAllText(readmePath);
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var registrationMethods = ReadComponentRegistrationMethods(assembly, entry.PackageId)
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            registrationMethods.ShouldNotBeEmpty(
                $"{entry.PackageId} must expose a component registration extension method.");

            foreach (var registrationMethod in registrationMethods)
            {
                readme.Contains(registrationMethod, StringComparison.Ordinal)
                    .ShouldBeTrue($"{entry.PackageId} README must document registration method {registrationMethod}.");
                publicApiOverview.Contains(registrationMethod, StringComparison.Ordinal)
                    .ShouldBeTrue($"{entry.PackageId} public API overview must document {registrationMethod}.");
            }
        }
    }

    [Fact]
    public void Component_composition_designer_metadata_is_documented()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var publicApiOverview = File.ReadAllText(Path.Combine(root, "docs", "14-public-api-overview.md"));
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var project = LoadProject(root, entry);
            var version = ReadRequiredProperty(project, "Version", entry.PackageId);
            var readmePath = Path.Combine(projectDirectory, "README.md");
            var definitionFile = ReadSingleDefinitionFile(projectDirectory, entry.PackageId);
            var definitionName = Path.GetFileNameWithoutExtension(definitionFile);

            File.Exists(readmePath)
                .ShouldBeTrue($"{entry.PackageId} must include a package README.");
            var readme = File.ReadAllText(readmePath);

            readme.Contains(entry.PackageId, StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} README must name the package.");
            readme.Contains(definitionName, StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} README must document {definitionName}.");
            publicApiOverview.Contains(entry.PackageId, StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} must be listed in the public API overview.");
            publicApiOverview.Contains(definitionName, StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} public API overview must document {definitionName}.");
            changelog.Contains($"## {entry.PackageId} {version}", StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} {version} must have a changelog entry.");
        }
    }

    [Fact]
    public void Canonical_component_types_are_exposed_by_designer_catalog()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var componentTypesFile = ReadSingleComponentTypesFile(projectDirectory, entry.PackageId);
            var componentTypesContent = ReadDefinitionSection(
                File.ReadAllText(componentTypesFile),
                "Types");
            var componentTypeConstants = PublicStringConstantWithValueRegex()
                .Matches(componentTypesContent)
                .Select(match => (
                    Name: match.Groups["name"].Value,
                    Value: match.Groups["value"].Value))
                .ToArray();

            componentTypeConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} component-type file should expose at least one component type constant.");

            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var catalog = BuildDefaultDesignerCatalog(assembly, entry.PackageId);

            foreach (var componentTypeConstant in componentTypeConstants)
            {
                catalog.TryGet(new ComponentType(componentTypeConstant.Value), out _)
                    .ShouldBeTrue(
                        $"{entry.PackageId} Designer catalog must resolve component type constant '{componentTypeConstant.Name}'.");
            }
        }
    }

    [Fact]
    public void Removed_component_type_names_are_not_registered_or_exposed_by_designer()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();

        foreach (var entry in ReadComponentCompositionPackages(root))
        {
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var runtimeCatalog = BuildDefaultComponentCatalog(assembly, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var designerCatalog = BuildDefaultDesignerCatalog(assembly, entry.PackageId);

            foreach (var removedType in RemovedComponentTypeNames)
            {
                runtimeCatalog.TryGetDescriptor(removedType, out _).ShouldBeFalse(
                    $"{entry.PackageId} must reject removed component type '{removedType}'.");
                designerCatalog.TryGet(new ComponentType(removedType), out _).ShouldBeFalse(
                    $"{entry.PackageId} Designer metadata must reject removed component type '{removedType}'.");
            }
        }
    }

    [Fact]
    public void Component_composition_types_are_registered_by_service_collection_extensions()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var componentTypesFile = ReadSingleComponentTypesFile(projectDirectory, entry.PackageId);
            var componentTypesContent = ReadDefinitionSection(
                File.ReadAllText(componentTypesFile),
                "Types");
            var componentTypeConstants = PublicStringConstantWithValueRegex()
                .Matches(componentTypesContent)
                .Select(match => (
                    Name: match.Groups["name"].Value,
                    Value: match.Groups["value"].Value))
                .ToArray();

            componentTypeConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} component-type file should expose at least one component type constant.");

            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var catalog = BuildDefaultComponentCatalog(assembly, entry.PackageId);

            foreach (var componentTypeConstant in componentTypeConstants)
            {
                catalog.TryGetDescriptor(componentTypeConstant.Value, out _).ShouldBeTrue(
                    $"{entry.PackageId} must register component type constant '{componentTypeConstant.Name}'.");
                catalog.Components.ContainsKey(componentTypeConstant.Value).ShouldBeTrue(
                    $"{entry.PackageId} catalog must expose canonical type constant '{componentTypeConstant.Name}'.");
            }
        }
    }

    [Fact]
    public void Component_composition_resource_names_are_exposed_by_designer_metadata()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var resourceNamesFiles = ReadOptionalResourceNamesFiles(projectDirectory, entry.PackageId);

            if (resourceNamesFiles.Length == 0)
                continue;

            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var metadataResources = metadataItems
                .SelectMany(metadata => metadata.Resources)
                .ToArray();
            var resourceConstants = PublicStringConstantWithValueRegex()
                .Matches(ReadDefinitionSection(
                    File.ReadAllText(resourceNamesFiles[0]),
                    "Resources"))
                .Select(match => new ResourceConstant(
                    match.Groups["name"].Value,
                    match.Groups["value"].Value))
                .ToArray();

            resourceConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} resource-name file should expose at least one resource constant.");
            metadataResources.ShouldNotBeEmpty(
                $"{entry.PackageId} definition must expose Designer resource metadata.");

            foreach (var resource in resourceConstants)
            {
                metadataResources.Any(metadata => ResourceMetadataMatchesConstant(
                        metadata.Name.Value,
                        resource.Value,
                        resource.Name))
                    .ShouldBeTrue(
                        $"{entry.PackageId} Designer metadata must expose resource '{resource.Value}'.");
            }
        }
    }

    [Fact]
    public void Component_composition_resource_names_are_used_by_registry_extensions()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var resourceNamesFiles = ReadOptionalResourceNamesFiles(projectDirectory, entry.PackageId);

            if (resourceNamesFiles.Length == 0)
                continue;

            var registryContent = ReadCompositionImplementationContent(projectDirectory);
            var resourceContent = ReadDefinitionSection(
                File.ReadAllText(resourceNamesFiles[0]),
                "Resources");
            var resourceTypeName =
                $"{Path.GetFileNameWithoutExtension(resourceNamesFiles[0])}.Resources";
            var resourceConstants = PublicStringConstantRegex()
                .Matches(resourceContent)
                .Select(match => match.Groups["name"].Value)
                .ToArray();

            resourceConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} resource-name file should expose at least one resource constant.");

            foreach (var resourceConstant in resourceConstants)
            {
                ResourceReferenceIsUsedByRegistry(
                        registryContent,
                        resourceContent,
                        resourceTypeName,
                        resourceConstant)
                    .ShouldBeTrue(
                        $"{entry.PackageId} registry extensions must resolve resource '{resourceTypeName}.{resourceConstant}' directly or through a resource-name helper.");
            }
        }
    }

    [Fact]
    public void Component_composition_resource_requiredness_matches_factory_lookups()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var resourceNamesFiles = ReadOptionalResourceNamesFiles(projectDirectory, entry.PackageId);

            if (resourceNamesFiles.Length == 0)
                continue;

            var registryContent = ReadCompositionImplementationContent(projectDirectory);
            var resourceContent = ReadDefinitionSection(
                File.ReadAllText(resourceNamesFiles[0]),
                "Resources");
            var resourceTypeName =
                $"{Path.GetFileNameWithoutExtension(resourceNamesFiles[0])}.Resources";
            var resourceConstants = PublicStringConstantWithValueRegex()
                .Matches(resourceContent)
                .Select(match => new ResourceConstant(
                    match.Groups["name"].Value,
                    match.Groups["value"].Value))
                .ToArray();
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var metadataResources = metadataItems
                .SelectMany(metadata => metadata.Resources)
                .ToArray();

            resourceConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} resource-name file should expose at least one resource constant.");

            foreach (var resource in resourceConstants)
            {
                var lookupUsage = ReadResourceLookupUsage(
                    registryContent,
                    resourceContent,
                    resourceTypeName,
                    resource.Name);
                var matchingResources = metadataResources
                    .Where(metadata => ResourceMetadataMatchesConstant(
                        metadata.Name.Value,
                        resource.Value,
                        resource.Name))
                    .ToArray();

                lookupUsage.IsReferenced.ShouldBeTrue(
                    $"{entry.PackageId} registry extensions must resolve resource '{resourceTypeName}.{resource.Name}'.");
                matchingResources.ShouldNotBeEmpty(
                    $"{entry.PackageId} Designer metadata must expose resource '{resource.Value}'.");

                if (lookupUsage.UsesRequiredLookup)
                {
                    matchingResources
                        .Any(ResourceMetadataDocumentsRequiredness)
                        .ShouldBeTrue(
                            $"{entry.PackageId} resource '{resource.Value}' uses GetRequiredResource and must be marked required or document conditional requiredness.");
                }
                else
                {
                    matchingResources
                        .Any(resourceMetadata => resourceMetadata.IsRequired)
                        .ShouldBeFalse(
                            $"{entry.PackageId} resource '{resource.Value}' only uses optional GetResource and must not be marked required.");
                }
            }
        }
    }

    [Fact]
    public void Component_composition_port_names_are_exposed_by_designer_metadata()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var portNamesFiles = ReadOptionalPortNamesFiles(projectDirectory, entry.PackageId);

            if (portNamesFiles.Length == 0)
                continue;

            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var componentCatalog = BuildDefaultComponentCatalog(assembly, entry.PackageId);
            var defaultPortNames = componentCatalog
                .Components
                .Values
                .SelectMany(registration => registration.Inputs.Keys.Concat(registration.Outputs.Keys))
                .ToHashSet(StringComparer.Ordinal);
            var metadataPorts = BuildDefaultDesignerCatalog(assembly, entry.PackageId)
                .All
                .SelectMany(metadata => metadata.Ports)
                .ToArray();
            var portConstants = PublicStringConstantWithValueRegex()
                .Matches(ReadDefinitionSection(
                    File.ReadAllText(portNamesFiles[0]),
                    "Ports"))
                .Select(match => new PortConstant(
                    match.Groups["name"].Value,
                    match.Groups["value"].Value))
                .ToArray();

            portConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} port-name file should expose at least one port constant.");
            metadataPorts.ShouldNotBeEmpty(
                $"{entry.PackageId} definition must expose Designer port metadata.");

            foreach (var portConstant in portConstants)
            {
                if (!defaultPortNames.Contains(portConstant.Value))
                    continue;

                metadataPorts.Any(metadata => string.Equals(
                        metadata.Name.Value,
                        portConstant.Value,
                        StringComparison.Ordinal))
                    .ShouldBeTrue(
                        $"{entry.PackageId} Designer metadata must expose port '{portConstant.Value}'.");
            }
        }
    }

    [Fact]
    public void Component_composition_port_names_are_used_by_registry_extensions()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var portNamesFiles = ReadOptionalPortNamesFiles(projectDirectory, entry.PackageId);

            if (portNamesFiles.Length == 0)
                continue;

            var registryContent = ReadCompositionImplementationContent(projectDirectory);
            var portTypeName =
                $"{Path.GetFileNameWithoutExtension(portNamesFiles[0])}.Ports";
            var portConstants = PublicStringConstantRegex()
                .Matches(ReadDefinitionSection(
                    File.ReadAllText(portNamesFiles[0]),
                    "Ports"))
                .Select(match => match.Groups["name"].Value)
                .ToArray();

            portConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} port-name file should expose at least one port constant.");

            foreach (var portConstant in portConstants)
            {
                var portReference = $"{portTypeName}.{portConstant}";
                registryContent.Contains(portReference, StringComparison.Ordinal)
                    .ShouldBeTrue($"{entry.PackageId} registry extensions must expose port '{portReference}'.");
            }
        }
    }

    [Fact]
    public void Component_composition_bound_options_are_described_or_explicitly_omitted()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var boundOptionTypesByNodeType = ReadDefaultComponentOptionTypes(projectDirectory, entry.PackageId);

            foreach (var metadata in metadataItems)
            {
                var componentType = metadata.Type.ToString();
                boundOptionTypesByNodeType.TryGetValue(componentType, out var optionTypeNames)
                    .ShouldBeTrue($"{entry.PackageId} must map default component type '{componentType}' to bound option types.");

                var boundProperties = ReadBoundOptionProperties(
                    assembly,
                    optionTypeNames!,
                    entry.PackageId);

                boundProperties.ShouldNotBeEmpty(
                    $"{entry.PackageId} Designer metadata for '{componentType}' should have bound option properties.");

                foreach (var optionName in boundProperties.Keys.Order(StringComparer.Ordinal))
                {
                    MetadataDescribesOrOmitsOption(metadata, optionName)
                        .ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{componentType}' must describe bound option '{optionName}' or declare it in omittedOptions.");
                }
            }
        }
    }

    [Fact]
    public void Component_composition_omitted_designer_options_match_bound_configuration()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var configurationKeys = ReadConfigurationKeys(
                    assembly,
                    projectDirectory,
                    entry.PackageId)
                .ToArray();

            foreach (var metadata in metadataItems)
            {
                var componentType = metadata.Type.ToString();

                foreach (var omittedOption in ReadOmittedOptions(metadata).Order(StringComparer.Ordinal))
                {
                    ConfigurationKeysContainOption(configurationKeys, omittedOption)
                        .ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{componentType}' omits option '{omittedOption}', but no bound options property or explicit configuration read owns that key.");

                    metadata.Options.Any(option =>
                            string.Equals(option.Name.Value, omittedOption, StringComparison.Ordinal))
                        .ShouldBeFalse(
                            $"{entry.PackageId} Designer metadata for '{componentType}' option '{omittedOption}' cannot be both editable and declared in omittedOptions.");
                }
            }
        }
    }

    [Fact]
    public void Component_composition_designer_metadata_options_match_bound_configuration()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var configurationKeys = ReadConfigurationKeys(
                    assembly,
                    projectDirectory,
                    entry.PackageId)
                .ToArray();

            configurationKeys.ShouldNotBeEmpty(
                $"{entry.PackageId} must expose bound or explicitly read configuration keys.");

            foreach (var metadata in metadataItems)
            {
                foreach (var option in metadata.Options)
                {
                    (string.Equals(option.Name.Value, "processing", StringComparison.Ordinal) ||
                     ConfigurationKeysContainOption(configurationKeys, option.Name.Value))
                        .ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{metadata.Type}' exposes option '{option.Name}', but no bound options property or explicit configuration read owns that key.");
                }
            }
        }
    }

    [Fact]
    public void Component_composition_designer_metadata_defaults_match_bound_option_defaults()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var boundOptionTypesByNodeType = ReadDefaultComponentOptionTypes(projectDirectory, entry.PackageId);

            foreach (var metadata in metadataItems)
            {
                var componentType = metadata.Type.ToString();
                boundOptionTypesByNodeType.TryGetValue(componentType, out var optionTypeNames)
                    .ShouldBeTrue($"{entry.PackageId} must map default component type '{componentType}' to bound option types.");

                var simpleDefaults = ReadSimpleBoundOptionDefaults(
                    assembly,
                    optionTypeNames!,
                    entry.PackageId);

                foreach (var option in metadata.Options.Where(option => option.DefaultValue is not null))
                {
                    if (!simpleDefaults.TryGetValue(option.Name.Value, out var expected))
                        continue;

                    MetadataDefaultMatches(option.DefaultValue, expected.Value)
                        .ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{componentType}' option '{option.Name}' default '{option.DefaultValue}' must match bound option default '{expected.Value}' from {expected.OptionType}.{expected.PropertyName}.");
                }
            }
        }
    }

    [Fact]
    public void Component_composition_required_bound_options_are_required_in_designer_metadata()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var boundOptionTypesByNodeType = ReadDefaultComponentOptionTypes(projectDirectory, entry.PackageId);

            foreach (var metadata in metadataItems)
            {
                var componentType = metadata.Type.ToString();
                boundOptionTypesByNodeType.TryGetValue(componentType, out var optionTypeNames)
                    .ShouldBeTrue($"{entry.PackageId} must map default component type '{componentType}' to bound option types.");

                foreach (var optionTypeName in optionTypeNames!)
                {
                    var optionType = ResolveReferencedType(assembly, optionTypeName, entry.PackageId);

                    foreach (var requiredOption in ReadRequiredOptionProperties(optionType))
                    {
                        var option = metadata.Options.SingleOrDefault(option =>
                            string.Equals(option.Name.Value, requiredOption.ConfigurationKey, StringComparison.Ordinal));

                        option.ShouldNotBeNull(
                            $"{entry.PackageId} Designer metadata for '{componentType}' must expose required bound option '{requiredOption.ConfigurationKey}' from {optionType.Name}.{requiredOption.Name}.");
                        option.IsRequired.ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{componentType}' option '{requiredOption.ConfigurationKey}' must be marked required because {optionType.Name}.{requiredOption.Name} is a C# required member.");
                    }
                }
            }
        }
    }

    [Fact]
    public void Component_composition_numeric_metadata_bounds_are_accepted_by_bound_options()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var boundOptionTypesByNodeType = ReadDefaultComponentOptionTypes(projectDirectory, entry.PackageId);

            foreach (var metadata in metadataItems)
            {
                var componentType = metadata.Type.ToString();
                boundOptionTypesByNodeType.TryGetValue(componentType, out var optionTypeNames)
                    .ShouldBeTrue($"{entry.PackageId} must map default component type '{componentType}' to bound option types.");

                var boundProperties = ReadBoundOptionProperties(
                    assembly,
                    optionTypeNames!,
                    entry.PackageId);

                foreach (var option in metadata.Options.Where(option =>
                    option.Kind == OptionValueKind.Number &&
                    (option.Min.HasValue || option.Max.HasValue)))
                {
                    if (!boundProperties.TryGetValue(option.Name.Value, out var boundProperty) ||
                        !IsNumericOptionProperty(boundProperty.Property.PropertyType))
                    {
                        continue;
                    }

                    if (option.Min.HasValue)
                    {
                        AssertNumericMetadataBoundIsAccepted(
                            entry.PackageId,
                            componentType,
                            option,
                            boundProperty,
                            option.Min.Value,
                            nameof(OptionDesignMetadata.Min));
                    }

                    if (option.Max.HasValue)
                    {
                        AssertNumericMetadataBoundIsAccepted(
                            entry.PackageId,
                            componentType,
                            option,
                            boundProperty,
                            option.Max.Value,
                            nameof(OptionDesignMetadata.Max));
                    }
                }
            }
        }
    }

    [Fact]
    public void Component_composition_bound_option_metadata_kinds_match_simple_clr_types()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");
        var sourceFiles = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .ToArray();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var definitionOptionKinds = CreateComponentMetadata(assembly, entry.PackageId)
                .SelectMany(metadata => metadata.Options)
                .GroupBy(option => option.Name.Value, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(option => option.Kind.ToString())
                        .ToHashSet(StringComparer.Ordinal),
                    StringComparer.Ordinal);
            var boundOptionTypes = ReadBoundOptionTypes(projectDirectory, entry.PackageId);

            foreach (var optionType in boundOptionTypes)
            {
                var optionTypeFile = sourceFiles
                    .SingleOrDefault(file =>
                        string.Equals(
                            Path.GetFileNameWithoutExtension(file),
                            optionType,
                            StringComparison.Ordinal));
                optionTypeFile.ShouldNotBeNull(
                    $"{entry.PackageId} binds unknown option type '{optionType}'.");

                var optionContent = File.ReadAllText(optionTypeFile);
                var optionProperties = OptionPropertyWithTypeRegex()
                    .Matches(optionContent)
                    .Concat(ValidatedOptionPropertyWithTypeRegex().Matches(optionContent))
                    .Select(match => new
                    {
                        Name = match.Groups["name"].Value,
                        ConfigurationKey = ToConfigurationKey(match.Groups["name"].Value),
                        ClrType = match.Groups["type"].Value.Trim()
                    })
                    .DistinctBy(option => option.ConfigurationKey, StringComparer.Ordinal)
                    .ToArray();

                foreach (var option in optionProperties)
                {
                    var expectedKind = ExpectedOptionKind(option.ClrType);
                    if (expectedKind is null || !definitionOptionKinds.TryGetValue(option.ConfigurationKey, out var actualKinds))
                        continue;

                    actualKinds.Contains(expectedKind)
                        .ShouldBeTrue(
                            $"{entry.PackageId} option '{optionType}.{option.Name}' has CLR type '{option.ClrType}' and must use OptionValueKind.{expectedKind}.");
                }
            }
        }
    }

    [Fact]
    public void Component_composition_bound_enum_options_expose_all_enum_choices()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var metadataItems = CreateComponentMetadata(assembly, entry.PackageId);
            var definitionOptionsByName = metadataItems
                .SelectMany(metadata => metadata.Options)
                .GroupBy(option => option.Name.Value, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.Ordinal);

            foreach (var optionTypeName in ReadBoundOptionTypes(projectDirectory, entry.PackageId))
            {
                var optionType = ResolveReferencedType(assembly, optionTypeName, entry.PackageId);

                foreach (var option in ReadEnumOptionProperties(optionType))
                {
                    definitionOptionsByName.TryGetValue(option.ConfigurationKey, out var definitionOptions)
                        .ShouldBeTrue(
                            $"{entry.PackageId} must describe enum option '{optionType.Name}.{option.Name}'.");

                    var expectedChoices = Enum
                        .GetNames(option.EnumType)
                        .Order(StringComparer.Ordinal)
                        .ToArray();

                    foreach (var definitionOption in definitionOptions!)
                    {
                        definitionOption.Kind.ShouldBe(
                            OptionValueKind.Enum,
                            $"{entry.PackageId} option '{optionType.Name}.{option.Name}' has enum CLR type '{option.EnumType.Name}' and must use OptionValueKind.Enum.");
                        var actualChoices = definitionOption.Choices
                            .Select(choice => choice.Value.Value)
                            .Order(StringComparer.Ordinal)
                            .ToArray();

                        actualChoices.ShouldBe(
                            expectedChoices,
                            $"{entry.PackageId} option '{optionType.Name}.{option.Name}' choices must match enum '{option.EnumType.Name}'.");

                        if (definitionOption.DefaultValue is null)
                            continue;

                        var defaultValue = definitionOption.DefaultValue is Enum enumValue
                            ? enumValue.ToString()
                            : definitionOption.DefaultValue.ToString();

                        expectedChoices.Contains(defaultValue)
                            .ShouldBeTrue(
                                $"{entry.PackageId} option '{optionType.Name}.{option.Name}' default value '{defaultValue}' must match enum '{option.EnumType.Name}'.");
                    }
                }
            }
        }
    }

    private static bool IsComponentCompositionPackage(PackageManifestEntry entry)
        => entry.PackageId.StartsWith("FluxFlow.Components.", StringComparison.Ordinal)
            && entry.PackageId.EndsWith(".Composition", StringComparison.Ordinal);

    private static PackageManifestEntry[] ReadComponentCompositionPackages(string root)
        => PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

    private static string ReadProjectPath(
        string root,
        PackageManifestEntry entry)
        => Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));

    private static string ReadProjectDirectory(
        string root,
        PackageManifestEntry entry)
        => Path.GetDirectoryName(ReadProjectPath(root, entry)).ShouldNotBeNull();

    private static XDocument LoadProject(
        string root,
        PackageManifestEntry entry)
        => XDocument.Load(ReadProjectPath(root, entry));

    private static string ReadSingleDefinitionFile(
        string projectDirectory,
        string packageId)
        => Directory
            .EnumerateFiles(
                projectDirectory,
                "*ComponentDefinition.cs",
                SearchOption.TopDirectoryOnly)
            .ShouldHaveSingleItem(
                $"{packageId} must ship exactly one package-owned component definition.");

    private static string ReadSingleComponentTypesFile(
        string projectDirectory,
        string packageId)
        => ReadSingleDefinitionFile(projectDirectory, packageId);

    private static string ReadDefinitionSection(string source, string sectionName)
    {
        var marker = $"public static class {sectionName}";
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        markerIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            $"Component definition must declare nested {sectionName} constants.");

        var openingBrace = source.IndexOf('{', markerIndex + marker.Length);
        openingBrace.ShouldBeGreaterThan(
            markerIndex,
            $"Component definition nested {sectionName} section must have a body.");

        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[(openingBrace + 1)..index];
        }

        throw new InvalidOperationException(
            $"Component definition nested {sectionName} section is not closed.");
    }

    private static string ReadSingleServiceCollectionExtensionsFile(
        string projectDirectory,
        string packageId)
        => Directory
            .EnumerateFiles(
                projectDirectory,
                "*ServiceCollectionExtensions.cs",
                SearchOption.TopDirectoryOnly)
            .ShouldHaveSingleItem(
                $"{packageId} must keep component service registration in one file.");

    private static string ReadCompositionImplementationContent(string projectDirectory)
        => string.Join(
            Environment.NewLine,
            Directory
                .EnumerateFiles(projectDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var fileName = Path.GetFileName(path);
                    return !fileName.EndsWith("ComponentDefinition.cs", StringComparison.Ordinal) &&
                        !fileName.EndsWith("ComponentDesignMetadataProvider.cs", StringComparison.Ordinal) &&
                        !fileName.EndsWith("ComponentTypes.cs", StringComparison.Ordinal) &&
                        !fileName.EndsWith("ComponentPortNames.cs", StringComparison.Ordinal) &&
                        !fileName.EndsWith("ComponentResourceNames.cs", StringComparison.Ordinal);
                })
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

    private static string[] ReadOptionalResourceNamesFiles(
        string projectDirectory,
        string packageId)
        => [ReadSingleDefinitionFile(projectDirectory, packageId)];

    private static string[] ReadOptionalPortNamesFiles(
        string projectDirectory,
        string packageId)
        => [ReadSingleDefinitionFile(projectDirectory, packageId)];

    private static string[] ReadBoundOptionTypes(
        string projectDirectory,
        string packageId)
        => BindConfigurationRegex()
            .Matches(ReadCompositionImplementationContent(projectDirectory))
            .Select(match => match.Groups["type"].Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> ReadConfigurationKeys(
        Assembly assembly,
        string projectDirectory,
        string packageId)
    {
        foreach (var optionTypeName in ReadBoundOptionTypes(projectDirectory, packageId))
        {
            var optionType = ResolveReferencedType(assembly, optionTypeName, packageId);

            foreach (var property in optionType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.SetMethod?.IsPublic == true)
                    yield return ToConfigurationKey(property.Name);
            }
        }

        var implementationContent = ReadCompositionImplementationContent(projectDirectory);
        var stringConstants = StringConstantWithValueRegex()
            .Matches(implementationContent)
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => match.Groups["value"].Value,
                StringComparer.Ordinal);

        foreach (Match match in ExplicitConfigurationValueRegex().Matches(implementationContent))
        {
            var argument = match.Groups["argument"].Value.Trim();
            if (argument.Length >= 2 && argument[0] == '"' && argument[^1] == '"')
            {
                yield return argument[1..^1];
                continue;
            }

            if (stringConstants.TryGetValue(argument, out var constantValue))
                yield return constantValue;
        }
    }

    private static IReadOnlyDictionary<string, string[]> ReadDefaultComponentOptionTypes(
        string projectDirectory,
        string packageId)
    {
        var registrationContent = File.ReadAllText(
            ReadSingleServiceCollectionExtensionsFile(projectDirectory, packageId));
        var componentTypesContent = ReadDefinitionSection(
            File.ReadAllText(ReadSingleComponentTypesFile(projectDirectory, packageId)),
            "Types");
        var componentTypeConstants = PublicStringConstantWithValueRegex()
            .Matches(componentTypesContent)
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => match.Groups["value"].Value,
                StringComparer.Ordinal);
        var optionTypesByFactory = ReadFactoryOptionTypes(
            ReadCompositionImplementationContent(projectDirectory),
            packageId);
        var optionTypesByNodeType = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (Match match in FlatComponentRegistrationRegex().Matches(registrationContent))
        {
            var componentTypeConstant = match.Groups["componentType"].Value.Split('.')[^1];
            componentTypeConstants.TryGetValue(componentTypeConstant, out var componentType)
                .ShouldBeTrue(
                    $"{packageId} flat component type constant '{componentTypeConstant}' must resolve.");

            var configureMethod = match.Groups["configure"].Value;
            var configureBody = ReadMethodBody(registrationContent, configureMethod, packageId);
            var matchingFactories = optionTypesByFactory.Keys
                .Where(factoryName => Regex.IsMatch(
                    configureBody,
                    $@"\b(?:\w+\.)*{Regex.Escape(factoryName)}\b",
                    RegexOptions.CultureInvariant))
                .ToArray();
            matchingFactories.ShouldHaveSingleItem(
                $"{packageId} configuration method '{configureMethod}' for '{componentType}' must select one factory that binds configuration.");

            optionTypesByNodeType[componentType!] = optionTypesByFactory[matchingFactories[0]];
        }

        optionTypesByNodeType.ShouldNotBeEmpty(
            $"{packageId} must expose component descriptor option mappings.");
        return optionTypesByNodeType;
    }

    private static string ReadMethodBody(
        string source,
        string methodName,
        string packageId)
    {
        var marker = $"void {methodName}(";
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        markerIndex.ShouldBeGreaterThanOrEqualTo(
            0,
            $"{packageId} must declare component configuration method '{methodName}'.");

        var openingBrace = source.IndexOf('{', markerIndex + marker.Length);
        var expressionBody = source.IndexOf("=>", markerIndex + marker.Length, StringComparison.Ordinal);
        if (expressionBody >= 0 && (openingBrace < 0 || expressionBody < openingBrace))
        {
            var semicolon = source.IndexOf(';', expressionBody + 2);
            semicolon.ShouldBeGreaterThan(
                expressionBody,
                $"{packageId} component configuration method '{methodName}' expression body must be terminated.");
            return source[(expressionBody + 2)..semicolon];
        }

        openingBrace.ShouldBeGreaterThan(
            markerIndex,
            $"{packageId} component configuration method '{methodName}' must have a body.");

        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[(openingBrace + 1)..index];
        }

        throw new InvalidOperationException(
            $"{packageId} component configuration method '{methodName}' is not closed.");
    }

    private static IReadOnlyDictionary<string, string[]> ReadFactoryOptionTypes(
        string implementationContent,
        string packageId)
    {
        var methodBodies = ReadFactoryMethodBodies(implementationContent);
        var optionTypesByFactory = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var factoryName in methodBodies.Keys.Order(StringComparer.Ordinal))
        {
            var optionTypes = ReadFactoryOptionTypes(
                factoryName,
                methodBodies,
                []);

            if (optionTypes.Length > 0)
                optionTypesByFactory[factoryName] = optionTypes;
        }

        optionTypesByFactory.ShouldNotBeEmpty(
            $"{packageId} component factories must bind configuration.");
        return optionTypesByFactory;
    }

    private static Dictionary<string, string> ReadFactoryMethodBodies(string registryContent)
    {
        var methodBodies = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in PrivateFactoryMethodBlockRegex().Matches(registryContent))
            methodBodies[match.Groups["name"].Value] = match.Groups["body"].Value;

        foreach (Match match in PrivateFactoryExpressionMethodRegex().Matches(registryContent))
            methodBodies[match.Groups["name"].Value] = match.Groups["body"].Value;

        return methodBodies;
    }

    private static string[] ReadFactoryOptionTypes(
        string factoryName,
        IReadOnlyDictionary<string, string> methodBodies,
        HashSet<string> visiting)
    {
        if (!visiting.Add(factoryName) ||
            !methodBodies.TryGetValue(factoryName, out var body))
        {
            return [];
        }

        var directOptionTypes = BindConfigurationRegex()
            .Matches(body)
            .Select(match => match.Groups["type"].Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (directOptionTypes.Length > 0)
            return directOptionTypes;

        foreach (Match match in FactoryMethodCallRegex().Matches(body))
        {
            var helperName = match.Groups["name"].Value;
            if (!methodBodies.ContainsKey(helperName))
                continue;

            var helperOptionTypes = ReadFactoryOptionTypes(
                helperName,
                methodBodies,
                visiting);
            if (helperOptionTypes.Length > 0)
                return helperOptionTypes;
        }

        return [];
    }

    private static Dictionary<string, BoundOptionDefault> ReadSimpleBoundOptionDefaults(
        Assembly assembly,
        IReadOnlyCollection<string> optionTypeNames,
        string packageId)
    {
        var defaults = new Dictionary<string, BoundOptionDefault>(StringComparer.Ordinal);

        foreach (var optionTypeName in optionTypeNames)
        {
            var optionType = ResolveReferencedType(assembly, optionTypeName, packageId);
            var optionInstance = Activator.CreateInstance(optionType)
                .ShouldNotBeNull($"{packageId} option type '{optionType.Name}' must be default constructible.");

            foreach (var property in optionType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.SetMethod?.IsPublic != true ||
                    !IsComparableDefaultType(property.PropertyType))
                {
                    continue;
                }

                var key = ToConfigurationKey(property.Name);
                var defaultValue = property.GetValue(optionInstance) ??
                    ReadNamedEffectiveDefault(optionType, property);
                var candidate = new BoundOptionDefault(optionType.Name, property.Name, defaultValue);

                if (defaults.TryGetValue(key, out var existing))
                {
                    MetadataDefaultMatches(existing.Value, candidate.Value)
                        .ShouldBeTrue(
                            $"{packageId} option key '{key}' has inconsistent defaults in {existing.OptionType}.{existing.PropertyName} and {candidate.OptionType}.{candidate.PropertyName}.");
                    continue;
                }

                defaults.Add(key, candidate);
            }
        }

        return defaults;
    }

    private static Dictionary<string, BoundOptionProperty> ReadBoundOptionProperties(
        Assembly assembly,
        IReadOnlyCollection<string> optionTypeNames,
        string packageId)
    {
        var properties = new Dictionary<string, BoundOptionProperty>(StringComparer.Ordinal);

        foreach (var optionTypeName in optionTypeNames)
        {
            var optionType = ResolveReferencedType(assembly, optionTypeName, packageId);

            foreach (var property in optionType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.SetMethod?.IsPublic != true)
                    continue;

                var key = ToConfigurationKey(property.Name);
                var candidate = new BoundOptionProperty(optionType, property);

                if (properties.TryGetValue(key, out var existing))
                {
                    existing.Property.PropertyType.ShouldBe(
                        candidate.Property.PropertyType,
                        $"{packageId} option key '{key}' must not map to incompatible property types in {existing.OptionType.Name} and {candidate.OptionType.Name}.");
                    continue;
                }

                properties.Add(key, candidate);
            }
        }

        return properties;
    }

    private static object? ReadNamedEffectiveDefault(
        Type optionType,
        PropertyInfo property)
    {
        var field = optionType.GetField(
            $"Default{property.Name}",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        if (field is null || !IsComparableDefaultType(field.FieldType))
            return null;

        return field.IsLiteral
            ? field.GetRawConstantValue()
            : field.GetValue(null);
    }

    private static RequiredOptionProperty[] ReadRequiredOptionProperties(Type optionType)
        => optionType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property =>
                property.SetMethod?.IsPublic == true &&
                property.GetCustomAttribute<RequiredMemberAttribute>() is not null)
            .Select(property => new RequiredOptionProperty(
                property.Name,
                ToConfigurationKey(property.Name)))
            .OrderBy(option => option.ConfigurationKey, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> ReadReferencedPackageIds(
        XDocument project,
        string projectDirectory)
    {
        foreach (var reference in project
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var referencePath = Path.GetFullPath(
                Path.Combine(projectDirectory, NormalizePath(reference!)));
            var referencedProject = XDocument.Load(referencePath);
            var packageId = referencedProject
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageId")
                .Select(element => element.Value.Trim())
                .FirstOrDefault(value => value.Length > 0);

            if (!string.IsNullOrWhiteSpace(packageId))
                yield return packageId;
        }
    }

    private static string ReadRequiredProperty(
        XDocument project,
        string name,
        string packageId)
    {
        var value = project
            .Descendants()
            .Where(element => element.Name.LocalName == name)
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

        string.IsNullOrWhiteSpace(value).ShouldBeFalse($"{packageId} must define {name}.");
        return value!;
    }

    private static string? ReadOptionalProperty(
        XDocument project,
        string name)
        => project
            .Descendants()
            .Where(element => element.Name.LocalName == name)
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

    private static Assembly LoadPackageAssembly(
        XDocument project,
        string packageId)
    {
        var assemblyName = ReadOptionalProperty(project, "AssemblyName") ?? packageId;
        return Assembly.Load(new AssemblyName(assemblyName));
    }

    private static Type ResolveReferencedType(
        Assembly assembly,
        string typeName,
        string packageId)
    {
        var normalizedTypeName = NormalizeClrType(typeName);
        var matchingTypes = ReadFluxFlowAssemblyClosure(assembly)
            .SelectMany(SafeGetTypes)
            .Where(type =>
                string.Equals(type.Name, normalizedTypeName, StringComparison.Ordinal) ||
                string.Equals(type.FullName, typeName, StringComparison.Ordinal))
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        matchingTypes.Length.ShouldBe(
            1,
            $"{packageId} must resolve bound option type '{typeName}' to exactly one CLR type.");

        return matchingTypes[0];
    }

    private static Assembly[] ReadFluxFlowAssemblyClosure(Assembly assembly)
    {
        var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var queue = new Queue<Assembly>();

        AddAssembly(assembly);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var reference in current.GetReferencedAssemblies())
            {
                if (reference.Name is null ||
                    !reference.Name.StartsWith("FluxFlow.", StringComparison.Ordinal))
                {
                    continue;
                }

                AddAssembly(Assembly.Load(reference));
            }
        }

        return assemblies.Values.ToArray();

        void AddAssembly(Assembly candidate)
        {
            if (!assemblies.TryAdd(candidate.FullName.ShouldNotBeNull(), candidate))
                return;

            queue.Enqueue(candidate);
        }
    }

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types
                .Where(type => type is not null)
                .Cast<Type>()
                .ToArray();
        }
    }

    private static IReadOnlyCollection<ComponentDesignMetadata> CreateComponentMetadata(
        Assembly assembly,
        string packageId)
    {
        var services = new ServiceCollection();
        foreach (var method in ReadComponentRegistrationMethods(assembly, packageId))
            InvokeComponentRegistrationMethod(method, services, packageId);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ComponentDesignMetadataCatalog>().All;
    }

    private static ComponentDesignMetadataCatalog BuildDefaultDesignerCatalog(
        Assembly assembly,
        string packageId)
    {
        var services = new ServiceCollection();
        foreach (var method in ReadComponentRegistrationMethods(assembly, packageId))
            InvokeComponentRegistrationMethod(method, services, packageId);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ComponentDesignMetadataCatalog>();
    }

    private static ComponentCatalog BuildDefaultComponentCatalog(
        Assembly assembly,
        string packageId)
    {
        var services = new ServiceCollection();
        foreach (var method in ReadComponentRegistrationMethods(assembly, packageId))
            InvokeComponentRegistrationMethod(method, services, packageId);

        return BuildComponentCatalog(services);
    }

    private static ComponentCatalog BuildComponentCatalog(IServiceCollection services)
        => new(
            services
                .Where(descriptor => descriptor.ServiceType == typeof(ComponentDescriptor))
                .Select(descriptor => descriptor.ImplementationInstance as ComponentDescriptor ??
                    throw new InvalidOperationException(
                        "Component descriptors must be registered as explicit singleton instances.")));

    private static MethodInfo[] ReadComponentRegistrationMethods(
        Assembly assembly,
        string packageId)
    {
        var registrationMethods = assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: true, IsSealed: true } &&
                type.Name.EndsWith("ServiceCollectionExtensions", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method =>
                method.Name.StartsWith("Add", StringComparison.Ordinal) &&
                !method.Name.EndsWith("Components", StringComparison.Ordinal) &&
                method.ReturnType == typeof(FluxFlowRegistrationBuilder) &&
                method.GetParameters() is [{ ParameterType: var firstParameter }, ..] &&
                firstParameter == typeof(FluxFlowRegistrationBuilder))
            .OrderBy(method => method.DeclaringType?.FullName, StringComparer.Ordinal)
            .ThenBy(method => method.MetadataToken)
            .ToArray();

        registrationMethods.ShouldNotBeEmpty(
            $"{packageId} must expose a component service registration extension method.");
        return registrationMethods;
    }

    private static FluxFlowRegistrationBuilder InvokeComponentRegistrationMethod(
        MethodInfo method,
        IServiceCollection services,
        string packageId)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != 1 || parameters[0].ParameterType != typeof(FluxFlowRegistrationBuilder))
        {
            throw new InvalidOperationException(
                $"{packageId} registration method '{method.Name}' must accept only FluxFlowRegistrationBuilder.");
        }

        var builder = services.AddFluxFlowComponents();
        return method.Invoke(null, [builder]) as FluxFlowRegistrationBuilder ??
            throw new InvalidOperationException(
                $"{packageId} registration method '{method.Name}' returned null or a different registration builder.");
    }

    private static EnumOptionProperty[] ReadEnumOptionProperties(Type optionType)
        => optionType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod?.IsPublic == true)
            .Select(property => new
            {
                Property = property,
                EnumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType
            })
            .Where(option => option.EnumType.IsEnum)
            .Select(option => new EnumOptionProperty(
                option.Property.Name,
                ToConfigurationKey(option.Property.Name),
                option.EnumType))
            .OrderBy(option => option.ConfigurationKey, StringComparer.Ordinal)
            .ToArray();

    private static void AssertRequiredDesignerText(
        ComponentMetadataText? value,
        string message)
        => AssertRequiredDesignerText(value?.Value, message);

    private static void AssertRequiredDesignerText(
        string? value,
        string message)
        => string.IsNullOrWhiteSpace(value).ShouldBeFalse(message);

    private static void AssertDesignerOptionHintMetadata(
        PackageManifestEntry entry,
        string componentType,
        OptionDesignMetadata option,
        IReadOnlySet<string> resourceNames)
    {
        var optionName = option.Name.ToString();
        var section = ReadRequiredDesignerAttribute(
            option.Attributes,
            OptionDesignMetadataAttributeNames.Section,
            $"{entry.PackageId} Designer metadata for '{componentType}' option '{optionName}' must declare an option section.");

        AssertRequiredDesignerText(
            section,
            $"{entry.PackageId} Designer metadata for '{componentType}' option '{optionName}' must declare a non-empty option section.");

        var importance = ReadRequiredDesignerAttribute(
            option.Attributes,
            OptionDesignMetadataAttributeNames.Importance,
            $"{entry.PackageId} Designer metadata for '{componentType}' option '{optionName}' must declare option importance.");

        AssertAllowedDesignerAttributeValue(
            importance,
            OptionImportanceAttributeValues,
            $"{entry.PackageId} Designer metadata for '{componentType}' option '{optionName}' uses unsupported option importance '{importance}'.");

        AssertOptionalDesignerAttributeValue(
            option.Attributes,
            OptionDesignMetadataAttributeNames.Editor,
            OptionEditorAttributeValues,
            $"{entry.PackageId} Designer metadata for '{componentType}' option '{optionName}' uses unsupported option editor.");

        AssertOptionalDesignerAttributeValue(
            option.Attributes,
            OptionDesignMetadataAttributeNames.Syntax,
            OptionEditorAttributeValues,
            $"{entry.PackageId} Designer metadata for '{componentType}' option '{optionName}' uses unsupported option syntax.");

        var relatedResourceKey = new ComponentAttributeName(OptionDesignMetadataAttributeNames.RelatedResource);
        if (!option.Attributes.TryGetValue(relatedResourceKey, out var relatedResource))
            return;

        AssertRequiredDesignerText(
            relatedResource.Value,
            $"{entry.PackageId} Designer metadata for '{componentType}' option '{optionName}' must declare a non-empty related resource.");
        resourceNames.Contains(relatedResource.Value)
            .ShouldBeTrue(
                $"{entry.PackageId} Designer metadata for '{componentType}' option '{optionName}' related resource '{relatedResource.Value}' must match a resource on the same metadata node.");
    }

    private static void AssertHostOwnedResourcePickerMetadata(
        PackageManifestEntry entry,
        string componentType,
        ResourceDesignMetadata resource)
    {
        var ownershipKey = new ComponentAttributeName(ResourceDesignMetadataAttributeNames.Ownership);
        var pickerKindKey = new ComponentAttributeName(ResourceDesignMetadataAttributeNames.PickerKind);

        resource.Attributes.TryGetValue(ownershipKey, out var ownership)
            .ShouldBeTrue(
                $"{entry.PackageId} Designer metadata for '{componentType}' resource '{resource.Name.Value}' must declare host-owned resource ownership.");
        ownership!.Value.ShouldBe(
            ResourceDesignMetadataAttributeValues.HostOwned,
            $"{entry.PackageId} Designer metadata for '{componentType}' resource '{resource.Name.Value}' must use host-owned resource ownership.");

        resource.Attributes.TryGetValue(pickerKindKey, out var pickerKind)
            .ShouldBeTrue(
                $"{entry.PackageId} Designer metadata for '{componentType}' resource '{resource.Name.Value}' must declare a resource picker kind.");
        AssertRequiredDesignerText(
            pickerKind.Value,
            $"{entry.PackageId} Designer metadata for '{componentType}' resource '{resource.Name.Value}' must declare a non-empty resource picker kind.");

        var keyPatternKey = new ComponentAttributeName(ResourceDesignMetadataAttributeNames.KeyPattern);
        resource.Attributes.TryGetValue(keyPatternKey, out var keyPattern)
            .ShouldBeTrue(
                $"{entry.PackageId} Designer metadata for '{componentType}' resource '{resource.Name.Value}' must declare a resource key pattern.");
        AssertRequiredDesignerText(
            keyPattern!.Value,
            $"{entry.PackageId} Designer metadata for '{componentType}' resource '{resource.Name.Value}' must declare a non-empty resource key pattern.");
        keyPattern.Value.Contains("{name}", StringComparison.Ordinal)
            .ShouldBeTrue(
                $"{entry.PackageId} Designer metadata for '{componentType}' resource '{resource.Name.Value}' key pattern '{keyPattern.Value}' must include the host resource name placeholder.");
        (
            keyPattern.Value.Contains(pickerKind.Value, StringComparison.Ordinal) ||
            string.Equals(keyPattern.Value, resource.Name.Value, StringComparison.Ordinal) ||
            keyPattern.Value.StartsWith("Resources.", StringComparison.Ordinal)
        )
            .ShouldBeTrue(
                $"{entry.PackageId} Designer metadata for '{componentType}' resource '{resource.Name.Value}' key pattern '{keyPattern.Value}' must align with picker kind '{pickerKind.Value}' or its named resource pattern.");
    }

    private static string ReadRequiredDesignerAttribute(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name,
        string message)
    {
        attributes.TryGetValue(new ComponentAttributeName(name), out var value)
            .ShouldBeTrue(message);
        AssertRequiredDesignerText(value!.Value, message);

        return value.Value;
    }

    private static void AssertOptionalDesignerAttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name,
        IReadOnlyCollection<string> allowedValues,
        string message)
    {
        if (!attributes.TryGetValue(new ComponentAttributeName(name), out var value))
            return;

        AssertRequiredDesignerText(value.Value, message);
        AssertAllowedDesignerAttributeValue(value.Value, allowedValues, $"{message} Value: '{value.Value}'.");
    }

    private static void AssertAllowedDesignerAttributeValue(
        string value,
        IReadOnlyCollection<string> allowedValues,
        string message)
        => allowedValues.Contains(value, StringComparer.Ordinal)
            .ShouldBeTrue(message);

    private static void AssertStableMetadataOrder(
        IEnumerable<(string Name, int Order)> items,
        string scope)
    {
        var metadataItems = items.ToArray();

        foreach (var item in metadataItems)
        {
            item.Order.ShouldBeGreaterThan(
                -1,
                $"{scope} item '{item.Name}' must not use a negative order.");
        }

        var duplicateOrders = metadataItems
            .GroupBy(item => item.Order)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        duplicateOrders.ShouldBeEmpty(
            $"{scope} must not reuse order values: {string.Join(", ", duplicateOrders)}.");

        metadataItems
            .Select(item => item.Name)
            .ShouldBe(
                metadataItems
                    .OrderBy(item => item.Order)
                    .Select(item => item.Name)
                    .ToArray(),
                $"{scope} must be declared in ascending order.");
    }

    private static void AssertConcretePortValueType(
        string packageId,
        string componentType,
        ComponentDesignMetadata metadata,
        ComponentPortMetadata port,
        PortDirection direction)
    {
        if (!ShouldValidateConcretePortType(port.MessageType))
            return;

        var designerPort = metadata.Ports.SingleOrDefault(designerPort =>
            designerPort.Direction == direction &&
            string.Equals(designerPort.Name.ToString(), port.Name, StringComparison.Ordinal));

        designerPort.ShouldNotBeNull(
            $"{packageId} Designer metadata for '{componentType}' must expose {direction.ToString().ToLowerInvariant()} port '{port.Name}'.");

        string.IsNullOrWhiteSpace(designerPort.ValueType?.Value)
            .ShouldBeFalse(
                $"{packageId} Designer metadata for '{componentType}.{port.Name}' must expose a ValueType for concrete port type '{port.MessageType.FullName}'.");

        var expectedValueType = ToDesignerValueType(port.MessageType);
        NormalizeValueType(designerPort.ValueType?.Value!)
            .ShouldBe(
                NormalizeValueType(expectedValueType),
                $"{packageId} Designer metadata for '{componentType}.{port.Name}' must use ValueType '{expectedValueType}' for registry type '{port.MessageType.FullName}'.");
    }

    private static bool ShouldValidateConcretePortType(Type type)
        => !type.ContainsGenericParameters;

    private static string ToDesignerValueType(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var genericTickIndex = type.Name.IndexOf('`', StringComparison.Ordinal);
        var name = genericTickIndex < 0
            ? type.Name
            : type.Name[..genericTickIndex];
        var arguments = string.Join(
            ", ",
            type.GetGenericArguments().Select(ToDesignerValueType));

        return $"{name}<{arguments}>";
    }

    private static string NormalizeValueType(string valueType)
        => new(valueType.Where(character => !char.IsWhiteSpace(character)).ToArray());

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    private static bool ConfigurationKeysContainOption(
        IReadOnlyCollection<string> configurationKeys,
        string optionName)
        => configurationKeys.Any(key =>
            string.Equals(key, optionName, StringComparison.Ordinal) ||
            optionName.StartsWith($"{key}.", StringComparison.Ordinal));

    private static bool MetadataDescribesOrOmitsOption(
        ComponentDesignMetadata metadata,
        string optionName)
        => metadata.Options.Any(option =>
                string.Equals(option.Name.Value, optionName, StringComparison.Ordinal)) ||
            ReadOmittedOptions(metadata).Contains(optionName);

    private static HashSet<string> ReadOmittedOptions(ComponentDesignMetadata metadata)
    {
        if (!metadata.Attributes.TryGetValue(new ComponentAttributeName("omittedOptions"), out var omittedOptions) ||
            string.IsNullOrWhiteSpace(omittedOptions.Value))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return omittedOptions.Value
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void AssertNumericMetadataBoundIsAccepted(
        string packageId,
        string componentType,
        OptionDesignMetadata option,
        BoundOptionProperty boundProperty,
        double bound,
        string boundName)
    {
        TryConvertDesignerNumericBound(
                bound,
                boundProperty.Property.PropertyType,
                out var convertedBound)
            .ShouldBeTrue(
                $"{packageId} Designer metadata for '{componentType}' option '{option.Name}' {boundName} '{bound}' must be representable as {boundProperty.OptionType.Name}.{boundProperty.Property.Name} type '{boundProperty.Property.PropertyType.Name}'.");

        BoundOptionAcceptsValue(
                boundProperty.OptionType,
                boundProperty.Property,
                convertedBound)
            .ShouldBeTrue(
                $"{packageId} Designer metadata for '{componentType}' option '{option.Name}' {boundName} '{bound}' must be accepted by bound option {boundProperty.OptionType.Name}.{boundProperty.Property.Name}.");
    }

    private static bool BoundOptionAcceptsValue(
        Type optionType,
        PropertyInfo property,
        object? value)
    {
        var instance = Activator.CreateInstance(optionType);

        try
        {
            property.SetValue(instance, value);
            return true;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryConvertDesignerNumericBound(
        double bound,
        Type propertyType,
        out object? value)
    {
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        value = null;

        if (targetType == typeof(byte))
            return TryConvertWholeNumber(bound, byte.MinValue, byte.MaxValue, out value, number => (byte)number);
        if (targetType == typeof(short))
            return TryConvertWholeNumber(bound, short.MinValue, short.MaxValue, out value, number => (short)number);
        if (targetType == typeof(int))
            return TryConvertWholeNumber(bound, int.MinValue, int.MaxValue, out value, number => (int)number);
        if (targetType == typeof(long))
            return TryConvertWholeNumber(bound, long.MinValue, long.MaxValue, out value, number => (long)number);
        if (targetType == typeof(float))
        {
            value = (float)bound;
            return true;
        }

        if (targetType == typeof(double))
        {
            value = bound;
            return true;
        }

        if (targetType == typeof(decimal))
        {
            value = (decimal)bound;
            return true;
        }

        return false;
    }

    private static bool TryConvertWholeNumber(
        double bound,
        long min,
        long max,
        out object? value,
        Func<long, object> convert)
    {
        value = null;
        if (bound < min ||
            bound > max ||
            Math.Truncate(bound) != bound)
        {
            return false;
        }

        value = convert((long)bound);
        return true;
    }

    private static bool IsNumericOptionProperty(Type type)
        => IsNumericType(Nullable.GetUnderlyingType(type) ?? type);

    private static bool MetadataDefaultMatches(
        object? actual,
        object? expected)
    {
        if (actual is null || expected is null)
            return actual is null && expected is null;

        if (actual is Enum || expected is Enum)
        {
            return string.Equals(
                actual.ToString(),
                expected.ToString(),
                StringComparison.Ordinal);
        }

        var actualType = Nullable.GetUnderlyingType(actual.GetType()) ?? actual.GetType();
        var expectedType = Nullable.GetUnderlyingType(expected.GetType()) ?? expected.GetType();
        if (IsNumericType(actualType) && IsNumericType(expectedType))
            return Convert.ToDecimal(actual) == Convert.ToDecimal(expected);

        return Equals(actual, expected) ||
            string.Equals(actual.ToString(), expected.ToString(), StringComparison.Ordinal);
    }

    private static bool IsComparableDefaultType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType == typeof(string) ||
            underlyingType == typeof(bool) ||
            underlyingType == typeof(TimeSpan) ||
            underlyingType.IsEnum ||
            IsNumericType(underlyingType);
    }

    private static bool IsNumericType(Type type)
        => type == typeof(byte) ||
            type == typeof(short) ||
            type == typeof(int) ||
            type == typeof(long) ||
            type == typeof(float) ||
            type == typeof(double) ||
            type == typeof(decimal);

    private static string ToConfigurationKey(string propertyName)
        => $"{char.ToLowerInvariant(propertyName[0])}{propertyName[1..]}";

    private static bool ResourceReferenceIsUsedByRegistry(
        string registryContent,
        string resourceContent,
        string resourceTypeName,
        string resourceConstant)
    {
        var directReference = $"{resourceTypeName}.{resourceConstant}";
        if (registryContent.Contains(directReference, StringComparison.Ordinal))
            return true;

        foreach (Match match in PublicStaticStringMethodRegex().Matches(resourceContent))
        {
            var methodName = match.Groups["name"].Value;
            if (!registryContent.Contains($"{resourceTypeName}.{methodName}(", StringComparison.Ordinal))
                continue;

            if (ResourceHelperMentionsConstant(resourceContent, match.Index, resourceConstant))
                return true;
        }

        return false;
    }

    private static ResourceLookupUsage ReadResourceLookupUsage(
        string registryContent,
        string resourceContent,
        string resourceTypeName,
        string resourceConstant)
    {
        var usage = new ResourceLookupUsage(false, false);

        foreach (Match match in ResourceLookupRegex().Matches(registryContent))
        {
            var lookupContent = match.Value;
            if (!ResourceLookupMentionsConstant(
                    lookupContent,
                    resourceContent,
                    resourceTypeName,
                    resourceConstant))
            {
                continue;
            }

            usage = match.Groups["required"].Success
                ? usage with { UsesRequiredLookup = true }
                : usage with { UsesOptionalLookup = true };
        }

        return usage.IsReferenced
            ? usage
            : ReadResourceHelperLookupUsage(
                registryContent,
                resourceContent,
                resourceTypeName,
                resourceConstant);
    }

    private static bool ResourceLookupMentionsConstant(
        string lookupContent,
        string resourceContent,
        string resourceTypeName,
        string resourceConstant)
    {
        var directReference = $"{resourceTypeName}.{resourceConstant}";
        if (lookupContent.Contains(directReference, StringComparison.Ordinal))
            return true;

        foreach (Match match in PublicStaticStringMethodRegex().Matches(resourceContent))
        {
            var methodName = match.Groups["name"].Value;
            if (!lookupContent.Contains($"{resourceTypeName}.{methodName}(", StringComparison.Ordinal))
                continue;

            if (ResourceHelperMentionsConstant(resourceContent, match.Index, resourceConstant))
                return true;
        }

        return false;
    }

    private static ResourceLookupUsage ReadResourceHelperLookupUsage(
        string registryContent,
        string resourceContent,
        string resourceTypeName,
        string resourceConstant)
    {
        var usage = new ResourceLookupUsage(false, false);

        foreach (Match match in PublicStaticStringMethodRegex().Matches(resourceContent))
        {
            if (!ResourceHelperMentionsConstant(resourceContent, match.Index, resourceConstant))
                continue;

            var methodName = match.Groups["name"].Value;
            var helperReference = $"{resourceTypeName}.{methodName}(";
            var helperIndex = registryContent.IndexOf(helperReference, StringComparison.Ordinal);
            if (helperIndex < 0)
                continue;

            var registryMethodContent = ReadContainingRegistryMethod(registryContent, helperIndex);
            if (registryMethodContent.Contains(".GetRequiredResource<", StringComparison.Ordinal) ||
                registryMethodContent.Contains(".GetRequiredResourceKey(", StringComparison.Ordinal))
                usage = usage with { UsesRequiredLookup = true };
            if (registryMethodContent.Contains(".GetResource<", StringComparison.Ordinal))
                usage = usage with { UsesOptionalLookup = true };
        }

        return usage;
    }

    private static string ReadContainingRegistryMethod(
        string registryContent,
        int memberIndex)
    {
        var methodStart = registryContent.LastIndexOf(
            "\n    private static",
            memberIndex,
            StringComparison.Ordinal);
        if (methodStart < 0)
        {
            methodStart = registryContent.LastIndexOf(
                "\n    public static",
                memberIndex,
                StringComparison.Ordinal);
        }

        if (methodStart < 0)
            methodStart = 0;

        var nextMethodIndex = registryContent.IndexOf(
            "\n    private static",
            memberIndex + 1,
            StringComparison.Ordinal);
        var nextPublicMethodIndex = registryContent.IndexOf(
            "\n    public static",
            memberIndex + 1,
            StringComparison.Ordinal);
        if (nextPublicMethodIndex >= 0 &&
            (nextMethodIndex < 0 || nextPublicMethodIndex < nextMethodIndex))
        {
            nextMethodIndex = nextPublicMethodIndex;
        }

        var methodLength = nextMethodIndex < 0
            ? registryContent.Length - methodStart
            : nextMethodIndex - methodStart;
        return registryContent.Substring(methodStart, methodLength);
    }

    private static bool ResourceMetadataMatchesConstant(
        string resourceName,
        string resourceValue,
        string resourceConstant)
    {
        if (string.Equals(resourceName, resourceValue, StringComparison.Ordinal))
            return true;

        return resourceConstant.EndsWith("Prefix", StringComparison.Ordinal) &&
            resourceName.StartsWith(resourceValue, StringComparison.Ordinal);
    }

    private static bool ResourceMetadataDocumentsRequiredness(
        ResourceDesignMetadata resource)
        => resource.IsRequired ||
            resource.Attributes.Keys.Any(key =>
                key.Value.StartsWith("requiredWhen", StringComparison.OrdinalIgnoreCase)) ||
            resource.Attributes.ContainsKey(new ComponentAttributeName(ResourceDesignMetadataAttributeNames.Option));

    private static bool ResourceHelperMentionsConstant(
        string resourceContent,
        int methodStartIndex,
        string resourceConstant)
    {
        if (methodStartIndex < 0 || methodStartIndex >= resourceContent.Length)
            return false;

        var nextMemberIndex = resourceContent.IndexOf(
            "\n    public",
            methodStartIndex + 1,
            StringComparison.Ordinal);
        var methodLength = nextMemberIndex < 0
            ? resourceContent.Length - methodStartIndex
            : nextMemberIndex - methodStartIndex;
        var methodContent = resourceContent.Substring(methodStartIndex, methodLength);

        return methodContent.Contains(resourceConstant, StringComparison.Ordinal);
    }

    private static string? ExpectedOptionKind(string clrType)
    {
        var type = NormalizeClrType(clrType);

        return type switch
        {
            "bool" => "Boolean",
            "byte" or "short" or "int" or "long" or "float" or "double" or "decimal" => "Number",
            "TimeSpan" => "Duration",
            "JsonDocument" or "JsonElement" or "JsonNode" or "JsonObject" => "Json",
            _ when type.Contains("Dictionary", StringComparison.Ordinal) => "Json",
            _ => null
        };
    }

    private static string NormalizeClrType(string clrType)
    {
        var type = clrType
            .Replace("global::", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (type.EndsWith('?'))
            type = type[..^1];

        var nullableMatch = NullableTypeRegex().Match(type);
        if (nullableMatch.Success)
            type = nullableMatch.Groups["type"].Value.Trim();

        var lastDotIndex = type.LastIndexOf('.');
        return lastDotIndex >= 0 ? type[(lastDotIndex + 1)..] : type;
    }

    private sealed record ResourceConstant(string Name, string Value);

    private sealed record PortConstant(string Name, string Value);

    private sealed record EnumOptionProperty(
        string Name,
        string ConfigurationKey,
        Type EnumType);

    private sealed record BoundOptionDefault(
        string OptionType,
        string PropertyName,
        object? Value);

    private sealed record BoundOptionProperty(
        Type OptionType,
        PropertyInfo Property);

    private sealed record RequiredOptionProperty(
        string Name,
        string ConfigurationKey);

    private sealed record ResourceLookupUsage(
        bool UsesRequiredLookup,
        bool UsesOptionalLookup)
    {
        public bool IsReferenced => UsesRequiredLookup || UsesOptionalLookup;
    }

    private static readonly string[] OptionImportanceAttributeValues =
    [
        OptionDesignMetadataAttributeValues.Primary,
        OptionDesignMetadataAttributeValues.Advanced
    ];

    private static readonly string[] OptionEditorAttributeValues =
    [
        OptionDesignMetadataAttributeValues.Text,
        OptionDesignMetadataAttributeValues.Number,
        OptionDesignMetadataAttributeValues.Expression,
        OptionDesignMetadataAttributeValues.Json
    ];

    [GeneratedRegex(@"public\s+const\s+string\s+(?<name>\w+)\s*=")]
    private static partial Regex PublicStringConstantRegex();

    [GeneratedRegex(@"public\s+const\s+string\s+(?<name>\w+)\s*=\s*""(?<value>[^""]*)""\s*;")]
    private static partial Regex PublicStringConstantWithValueRegex();

    [GeneratedRegex(@"const\s+string\s+(?<name>\w+)\s*=\s*""(?<value>[^""]*)""\s*;")]
    private static partial Regex StringConstantWithValueRegex();

    [GeneratedRegex(@"public\s+static\s+string\s+(?<name>\w+)\s*\(")]
    private static partial Regex PublicStaticStringMethodRegex();

    [GeneratedRegex(@"\.Get(?<required>Required)?Resource(?:\s*<[^;]+?>|Key)\s*\([^;]+?;", RegexOptions.Singleline)]
    private static partial Regex ResourceLookupRegex();

    [GeneratedRegex(@"BindConfiguration<(?<type>[^>]+)>")]
    private static partial Regex BindConfigurationRegex();

    [GeneratedRegex(@"internal\s+static\s+ComponentDescriptor\s+\w+\s*\{\s*get;\s*\}\s*=\s*(?:new|Create\w*(?:<[^>]+>)?)\s*\(\s*(?<componentType>\w+ComponentDefinition\.Types\.\w+)\s*,\s*(?<factory>[\w.]+)", RegexOptions.Singleline)]
    private static partial Regex ComponentDescriptorRegistrationRegex();

    [GeneratedRegex(@"\.AddComponent\s*\(\s*(?<componentType>[\w.]+)\s*,\s*(?<configure>\w+)\s*\)")]
    private static partial Regex FlatComponentRegistrationRegex();

    [GeneratedRegex(@"(?:private|internal)\s+static\s+(?:async\s+)?ValueTask<ComponentInstance>\s+(?<name>\w+)(?:<[^>]+>)?\s*\([^)]*\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline)]
    private static partial Regex PrivateFactoryMethodBlockRegex();

    [GeneratedRegex(@"(?:private|internal)\s+static\s+(?:async\s+)?ValueTask<ComponentInstance>\s+(?<name>\w+)(?:<[^>]+>)?\s*\([^)]*\)\s*=>\s*(?<body>.*?);", RegexOptions.Singleline)]
    private static partial Regex PrivateFactoryExpressionMethodRegex();

    [GeneratedRegex(@"(?<name>\w+)(?:<[^>]+>)?\s*\(")]
    private static partial Regex FactoryMethodCallRegex();

    [GeneratedRegex(@"GetConfigurationValue<[^>]+>\(\s*(?<argument>[^)]+?)\s*\)")]
    private static partial Regex ExplicitConfigurationValueRegex();

    [GeneratedRegex(@"public\s+(?:required\s+)?[^\r\n{]+\s+(?<name>\w+)\s*\{\s*get;\s*init;\s*\}")]
    private static partial Regex OptionPropertyRegex();

    [GeneratedRegex(@"public\s+(?:required\s+)?[^\r\n{]+\s+(?<name>\w+)\s*\{\s*get\s*=>[^{};]+;\s*init\s*=>[^{};]+;\s*\}")]
    private static partial Regex ValidatedOptionPropertyRegex();

    [GeneratedRegex(@"public\s+(?:required\s+)?(?<type>[^\r\n{;=]+?)\s+(?<name>\w+)\s*\{\s*get;\s*init;\s*\}")]
    private static partial Regex OptionPropertyWithTypeRegex();

    [GeneratedRegex(@"public\s+(?:required\s+)?(?<type>[^\r\n{;=]+?)\s+(?<name>\w+)\s*\{\s*get\s*=>[^{};]+;\s*init\s*=>[^{};]+;\s*\}")]
    private static partial Regex ValidatedOptionPropertyWithTypeRegex();

    [GeneratedRegex(@"Nullable<(?<type>[^>]+)>")]
    private static partial Regex NullableTypeRegex();

    [GeneratedRegex(@"\b(?<property>Options|Resources|Ports)\s*=\s*\[")]
    private static partial Regex InlineMetadataCollectionAssignmentRegex();

    [GeneratedRegex(@"\bnew\s+ComponentDesignMetadata\s*(?:\(|\{)")]
    private static partial Regex DirectComponentMetadataConstructionRegex();
}
