using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed partial class ComponentCompositionMetadataConventionTests
{
    [Fact]
    public void Component_composition_packages_ship_designer_metadata_providers()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        entries.ShouldNotBeEmpty("component composition packages should be listed in the release manifest.");

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var providerFile = ReadSingleProviderFile(projectDirectory, entry.PackageId);
            var providerContent = File.ReadAllText(providerFile);
            providerContent.Contains(
                    "IComponentDesignMetadataProvider",
                    StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} provider must implement IComponentDesignMetadataProvider.");

            var project = LoadProject(root, entry);
            var referencedPackageIds = ReadReferencedPackageIds(project, projectDirectory)
                .ToArray();

            referencedPackageIds.ShouldContain(
                "FluxFlow.Components.Designer",
                $"{entry.PackageId} must reference Designer for its metadata provider.");
            referencedPackageIds.ShouldNotContain(
                "FluxFlow.Engine",
                $"{entry.PackageId} must stay engine-free.");
        }
    }

    [Fact]
    public void Component_composition_designer_metadata_providers_validate_at_runtime()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var metadata = provider.GetMetadata();

            metadata.Count.ShouldBeGreaterThan(
                0,
                $"{entry.PackageId} provider must return at least one metadata entry.");

            foreach (var item in metadata)
            {
                var errors = ComponentDesignMetadataValidator.Validate(item);
                errors.ShouldBeEmpty(
                    $"{entry.PackageId} provider emitted invalid metadata for '{item.Type}'.");
            }

            var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

            foreach (var item in metadata)
            {
                catalog.TryGet(item.Type, out _)
                    .ShouldBeTrue($"{entry.PackageId} catalog must contain provider metadata for '{item.Type}'.");
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
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);

            foreach (var metadata in provider.GetMetadata())
            {
                var nodeType = metadata.Type.ToString();
                AssertRequiredDesignerText(
                    metadata.DisplayName,
                    $"{entry.PackageId} Designer metadata for '{nodeType}' must include a display name.");
                AssertRequiredDesignerText(
                    metadata.Category,
                    $"{entry.PackageId} Designer metadata for '{nodeType}' must include a category.");
                AssertRequiredDesignerText(
                    metadata.Summary,
                    $"{entry.PackageId} Designer metadata for '{nodeType}' must include a summary.");

                foreach (var option in metadata.Options)
                {
                    AssertRequiredDesignerText(
                        option.DisplayName,
                        $"{entry.PackageId} Designer metadata for '{nodeType}' option '{option.Name}' must include a display name.");
                    AssertRequiredDesignerText(
                        option.HelperText,
                        $"{entry.PackageId} Designer metadata for '{nodeType}' option '{option.Name}' must include helper text.");

                    foreach (var choice in option.Choices)
                    {
                        AssertRequiredDesignerText(
                            choice.DisplayName,
                            $"{entry.PackageId} Designer metadata for '{nodeType}' option '{option.Name}' choice '{choice.Value}' must include a display name.");
                    }
                }

                foreach (var resource in metadata.Resources)
                {
                    AssertRequiredDesignerText(
                        resource.DisplayName,
                        $"{entry.PackageId} Designer metadata for '{nodeType}' resource '{resource.Name.Value}' must include a display name.");
                    AssertRequiredDesignerText(
                        resource.Summary,
                        $"{entry.PackageId} Designer metadata for '{nodeType}' resource '{resource.Name.Value}' must include a summary.");
                    AssertRequiredDesignerText(
                        resource.ValueType,
                        $"{entry.PackageId} Designer metadata for '{nodeType}' resource '{resource.Name.Value}' must include a value type.");
                }

                foreach (var port in metadata.Ports)
                {
                    AssertRequiredDesignerText(
                        port.DisplayName,
                        $"{entry.PackageId} Designer metadata for '{nodeType}' {port.Direction.ToString().ToLowerInvariant()} port '{port.Name}' must include a display name.");
                    AssertRequiredDesignerText(
                        port.Summary,
                        $"{entry.PackageId} Designer metadata for '{nodeType}' {port.Direction.ToString().ToLowerInvariant()} port '{port.Name}' must include a summary.");
                    AssertRequiredDesignerText(
                        port.ValueType,
                        $"{entry.PackageId} Designer metadata for '{nodeType}' {port.Direction.ToString().ToLowerInvariant()} port '{port.Name}' must include a value type.");
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
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var preferredNodeNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var metadata in provider.GetMetadata())
            {
                var nodeType = metadata.Type.ToString();
                AssertRequiredDesignerText(
                    metadata.IconKey,
                    $"{entry.PackageId} Designer metadata for '{nodeType}' must include an icon key.");
                AssertRequiredDesignerText(
                    metadata.PreferredNodeName,
                    $"{entry.PackageId} Designer metadata for '{nodeType}' must include a preferred node name.");

                metadata.SuggestedEditorWidth.HasValue
                    .ShouldBeTrue(
                        $"{entry.PackageId} Designer metadata for '{nodeType}' must include a suggested editor width.");
                metadata.SuggestedEditorWidth.GetValueOrDefault()
                    .ShouldBeGreaterThan(
                        319,
                        $"{entry.PackageId} Designer metadata for '{nodeType}' suggested editor width should support usable editors.");
                metadata.SuggestedEditorWidth.GetValueOrDefault()
                    .ShouldBeLessThan(
                        721,
                        $"{entry.PackageId} Designer metadata for '{nodeType}' suggested editor width should avoid oversized inspectors.");

                preferredNodeNames
                    .Add(metadata.PreferredNodeName!)
                    .ShouldBeTrue(
                        $"{entry.PackageId} Designer metadata preferred node name '{metadata.PreferredNodeName}' is duplicated.");
            }
        }
    }

    [Fact]
    public void Component_composition_designer_metadata_providers_use_named_collection_helpers()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var providerFile = ReadSingleProviderFile(projectDirectory, entry.PackageId);
            var providerContent = File.ReadAllText(providerFile);
            var inlineCollectionAssignments = InlineMetadataCollectionAssignmentRegex()
                .Matches(providerContent)
                .Select(match => match.Groups["property"].Value)
                .ToArray();

            inlineCollectionAssignments.ShouldBeEmpty(
                $"{entry.PackageId} provider must assign Options, Resources, and Ports through named helpers or variables instead of inline collection expressions.");
        }
    }

    [Fact]
    public void Component_composition_designer_metadata_providers_use_shared_builder()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var providerFile = ReadSingleProviderFile(projectDirectory, entry.PackageId);
            var providerContent = File.ReadAllText(providerFile);
            var directMetadataConstruction = DirectComponentMetadataConstructionRegex()
                .Matches(providerContent)
                .Select(match => match.Value)
                .ToArray();

            providerContent.Contains(
                    nameof(ComponentDesignMetadataBuilder),
                    StringComparison.Ordinal)
                .ShouldBeTrue(
                    $"{entry.PackageId} provider must author metadata through {nameof(ComponentDesignMetadataBuilder)}.");
            directMetadataConstruction.ShouldBeEmpty(
                $"{entry.PackageId} provider must not manually construct {nameof(ComponentDesignMetadata)}.");
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
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);

            foreach (var metadata in provider.GetMetadata())
            {
                var nodeType = metadata.Type.ToString();
                AssertStableMetadataOrder(
                    metadata.Resources.Select(resource => (resource.Name.Value, resource.Order)),
                    $"{entry.PackageId} Designer metadata for '{nodeType}' resources");
                AssertStableMetadataOrder(
                    metadata.Ports
                        .Where(port => port.Direction == PortDirection.Input)
                        .Select(port => (port.Name.ToString(), port.Order)),
                    $"{entry.PackageId} Designer metadata for '{nodeType}' input ports");
                AssertStableMetadataOrder(
                    metadata.Ports
                        .Where(port => port.Direction == PortDirection.Output)
                        .Select(port => (port.Name.ToString(), port.Order)),
                    $"{entry.PackageId} Designer metadata for '{nodeType}' output ports");
            }
        }
    }

    [Fact]
    public void Component_composition_designer_metadata_matches_default_registry_metadata()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var metadataByType = provider
                .GetMetadata()
                .ToDictionary(metadata => metadata.Type.ToString(), StringComparer.Ordinal);
            var registry = BuildDefaultRegistry(assembly, entry.PackageId);

            registry.Registrations.Keys
                .Order(StringComparer.Ordinal)
                .ShouldBe(
                    metadataByType.Keys.Order(StringComparer.Ordinal),
                    $"{entry.PackageId} Designer metadata node types must match default registry registrations.");

            foreach (var (nodeType, registration) in registry.Registrations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var metadata = metadataByType[nodeType];

                foreach (var input in registration.Inputs.Keys.Order(StringComparer.Ordinal))
                {
                    metadata.Ports.Any(port =>
                            port.Direction == PortDirection.Input &&
                            string.Equals(port.Name.ToString(), input, StringComparison.Ordinal))
                        .ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{nodeType}' must expose input port '{input}'.");
                }

                foreach (var output in registration.Outputs.Keys.Order(StringComparer.Ordinal))
                {
                    metadata.Ports.Any(port =>
                            port.Direction == PortDirection.Output &&
                            string.Equals(port.Name.ToString(), output, StringComparison.Ordinal))
                        .ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{nodeType}' must expose output port '{output}'.");
                }
            }
        }
    }

    [Fact]
    public void Component_composition_concrete_port_value_types_match_registry_message_types()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var metadataByType = provider
                .GetMetadata()
                .ToDictionary(metadata => metadata.Type.ToString(), StringComparer.Ordinal);
            var registry = BuildDefaultRegistry(assembly, entry.PackageId);

            foreach (var (nodeType, registration) in registry.Registrations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var metadata = metadataByType[nodeType];

                foreach (var input in registration.Inputs.Values.OrderBy(input => input.Name, StringComparer.Ordinal))
                {
                    AssertConcretePortValueType(
                        entry.PackageId,
                        nodeType,
                        metadata,
                        input,
                        PortDirection.Input);
                }

                foreach (var output in registration.Outputs.Values.OrderBy(output => output.Name, StringComparer.Ordinal))
                {
                    AssertConcretePortValueType(
                        entry.PackageId,
                        nodeType,
                        metadata,
                        output,
                        PortDirection.Output);
                }
            }
        }
    }

    [Fact]
    public void Component_composition_registry_extensions_are_discoverable_and_default_invokable()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var nodeTypesFile = ReadSingleNodeTypesFile(projectDirectory, entry.PackageId);
            var nodeTypeValues = PublicStringConstantWithValueRegex()
                .Matches(File.ReadAllText(nodeTypesFile))
                .Select(match => match.Groups["value"].Value)
                .ToHashSet(StringComparer.Ordinal);
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);

            nodeTypeValues.ShouldNotBeEmpty(
                $"{entry.PackageId} node-type file should expose at least one node type constant.");

            foreach (var method in ReadRegistryMethods(assembly, entry.PackageId))
            {
                method.Name.StartsWith("Register", StringComparison.Ordinal)
                    .ShouldBeTrue($"{entry.PackageId} registry method '{method.Name}' must use the Register* naming convention.");
                method.IsDefined(typeof(ExtensionAttribute), inherit: false)
                    .ShouldBeTrue($"{entry.PackageId} registry method '{method.Name}' must be an extension method.");

                var parameters = method.GetParameters();
                parameters[0].ParameterType.ShouldBe(
                    typeof(CompositionNodeRegistry),
                    $"{entry.PackageId} registry method '{method.Name}' must extend CompositionNodeRegistry.");

                foreach (var parameter in parameters.Skip(1))
                {
                    parameter.HasDefaultValue.ShouldBeTrue(
                        $"{entry.PackageId} registry method '{method.Name}' parameter '{parameter.Name}' must have a default value so default registration is discoverable.");

                    if (parameter.ParameterType != typeof(string))
                        continue;

                    parameter.Name.ShouldBe(
                        "nodeType",
                        $"{entry.PackageId} registry method '{method.Name}' string parameter must be the optional nodeType override.");
                    parameter.DefaultValue.ShouldBeOfType<string>(
                        $"{entry.PackageId} registry method '{method.Name}' nodeType default must be a node-type constant value.");
                    nodeTypeValues.Contains((string)parameter.DefaultValue)
                        .ShouldBeTrue(
                            $"{entry.PackageId} registry method '{method.Name}' nodeType default '{parameter.DefaultValue}' must come from its CompositionNodeTypes constants.");
                }

                var registry = new CompositionNodeRegistry();
                InvokeRegistryMethod(method, registry, entry.PackageId);

                registry.Registrations.ShouldNotBeEmpty(
                    $"{entry.PackageId} registry method '{method.Name}' must register at least one default node type.");
                registry.Registrations.Keys.All(nodeTypeValues.Contains)
                    .ShouldBeTrue(
                        $"{entry.PackageId} registry method '{method.Name}' must register only package node-type constants by default.");

                foreach (var nodeTypeParameter in parameters.Skip(1).Where(parameter => parameter.Name == "nodeType"))
                {
                    registry.Registrations.ContainsKey((string)nodeTypeParameter.DefaultValue!)
                        .ShouldBeTrue(
                            $"{entry.PackageId} registry method '{method.Name}' must register its default nodeType '{nodeTypeParameter.DefaultValue}'.");
                }
            }
        }
    }

    [Fact]
    public void Component_composition_registry_extension_methods_are_documented()
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
            var registryMethods = ReadRegistryMethods(assembly, entry.PackageId)
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            registryMethods.ShouldNotBeEmpty(
                $"{entry.PackageId} must expose at least one registry extension method.");

            foreach (var registryMethod in registryMethods)
            {
                readme.Contains(registryMethod, StringComparison.Ordinal)
                    .ShouldBeTrue($"{entry.PackageId} README must document registry method {registryMethod}.");
                publicApiOverview.Contains(registryMethod, StringComparison.Ordinal)
                    .ShouldBeTrue($"{entry.PackageId} public API overview entry must document registry method {registryMethod}.");
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
            var providerFile = ReadSingleProviderFile(projectDirectory, entry.PackageId);
            var providerName = Path.GetFileNameWithoutExtension(providerFile);

            File.Exists(readmePath)
                .ShouldBeTrue($"{entry.PackageId} must include a package README.");
            var readme = File.ReadAllText(readmePath);

            readme.Contains(entry.PackageId, StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} README must name the package.");
            readme.Contains(providerName, StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} README must document {providerName}.");
            publicApiOverview.Contains(entry.PackageId, StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} must be listed in the public API overview.");
            publicApiOverview.Contains(providerName, StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} public API overview must document {providerName}.");
            changelog.Contains($"## {entry.PackageId} {version}", StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} {version} must have a changelog entry.");
        }
    }

    [Fact]
    public void Component_composition_node_types_are_exposed_by_designer_metadata()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var nodeTypesFile = ReadSingleNodeTypesFile(projectDirectory, entry.PackageId);
            var providerFile = ReadSingleProviderFile(projectDirectory, entry.PackageId);
            var providerContent = File.ReadAllText(providerFile);
            var nodeTypeName = Path.GetFileNameWithoutExtension(nodeTypesFile);
            var nodeTypeConstants = PublicStringConstantRegex()
                .Matches(File.ReadAllText(nodeTypesFile))
                .Select(match => match.Groups["name"].Value)
                .ToArray();

            nodeTypeConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} node-type file should expose at least one node type constant.");

            foreach (var nodeTypeConstant in nodeTypeConstants)
            {
                var nodeTypeReference = $"{nodeTypeName}.{nodeTypeConstant}";
                providerContent.Contains(nodeTypeReference, StringComparison.Ordinal)
                    .ShouldBeTrue($"{entry.PackageId} provider must expose node type '{nodeTypeReference}'.");
            }
        }
    }

    [Fact]
    public void Component_composition_node_types_are_used_by_registry_extensions()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = ReadComponentCompositionPackages(root);

        foreach (var entry in entries)
        {
            var projectDirectory = ReadProjectDirectory(root, entry);
            var nodeTypesFile = ReadSingleNodeTypesFile(projectDirectory, entry.PackageId);
            var registryFile = ReadSingleRegistryFile(projectDirectory, entry.PackageId);
            var registryContent = File.ReadAllText(registryFile);
            var nodeTypeName = Path.GetFileNameWithoutExtension(nodeTypesFile);
            var nodeTypeConstants = PublicStringConstantRegex()
                .Matches(File.ReadAllText(nodeTypesFile))
                .Select(match => match.Groups["name"].Value)
                .ToArray();

            nodeTypeConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} node-type file should expose at least one node type constant.");

            foreach (var nodeTypeConstant in nodeTypeConstants)
            {
                var nodeTypeReference = $"{nodeTypeName}.{nodeTypeConstant}";
                registryContent.Contains(nodeTypeReference, StringComparison.Ordinal)
                    .ShouldBeTrue($"{entry.PackageId} registry extensions must register node type '{nodeTypeReference}'.");
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
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var metadataResources = provider
                .GetMetadata()
                .SelectMany(metadata => metadata.Resources)
                .ToArray();
            var resourceConstants = PublicStringConstantWithValueRegex()
                .Matches(File.ReadAllText(resourceNamesFiles[0]))
                .Select(match => new ResourceConstant(
                    match.Groups["name"].Value,
                    match.Groups["value"].Value))
                .ToArray();

            resourceConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} resource-name file should expose at least one resource constant.");
            metadataResources.ShouldNotBeEmpty(
                $"{entry.PackageId} provider must expose Designer resource metadata.");

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

            var registryFile = ReadSingleRegistryFile(projectDirectory, entry.PackageId);
            var registryContent = File.ReadAllText(registryFile);
            var resourceContent = File.ReadAllText(resourceNamesFiles[0]);
            var resourceTypeName = Path.GetFileNameWithoutExtension(resourceNamesFiles[0]);
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

            var registryFile = ReadSingleRegistryFile(projectDirectory, entry.PackageId);
            var registryContent = File.ReadAllText(registryFile);
            var resourceContent = File.ReadAllText(resourceNamesFiles[0]);
            var resourceTypeName = Path.GetFileNameWithoutExtension(resourceNamesFiles[0]);
            var resourceConstants = PublicStringConstantWithValueRegex()
                .Matches(resourceContent)
                .Select(match => new ResourceConstant(
                    match.Groups["name"].Value,
                    match.Groups["value"].Value))
                .ToArray();
            var project = LoadProject(root, entry);
            var assembly = LoadPackageAssembly(project, entry.PackageId);
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var metadataResources = provider
                .GetMetadata()
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
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var metadataPorts = provider
                .GetMetadata()
                .SelectMany(metadata => metadata.Ports)
                .ToArray();
            var providerFile = ReadSingleProviderFile(projectDirectory, entry.PackageId);
            var providerContent = File.ReadAllText(providerFile);
            var portTypeName = Path.GetFileNameWithoutExtension(portNamesFiles[0]);
            var portConstants = PublicStringConstantWithValueRegex()
                .Matches(File.ReadAllText(portNamesFiles[0]))
                .Select(match => new PortConstant(
                    match.Groups["name"].Value,
                    match.Groups["value"].Value))
                .ToArray();

            portConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} port-name file should expose at least one port constant.");
            metadataPorts.ShouldNotBeEmpty(
                $"{entry.PackageId} provider must expose Designer port metadata.");

            foreach (var portConstant in portConstants)
            {
                var portReference = $"{portTypeName}.{portConstant.Name}";
                providerContent.Contains(portReference, StringComparison.Ordinal)
                    .ShouldBeTrue($"{entry.PackageId} provider must expose port '{portReference}'.");
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

            var registryFile = ReadSingleRegistryFile(projectDirectory, entry.PackageId);
            var registryContent = File.ReadAllText(registryFile);
            var portTypeName = Path.GetFileNameWithoutExtension(portNamesFiles[0]);
            var portConstants = PublicStringConstantRegex()
                .Matches(File.ReadAllText(portNamesFiles[0]))
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
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var boundOptionTypesByNodeType = ReadDefaultNodeOptionTypes(projectDirectory, entry.PackageId);

            foreach (var metadata in provider.GetMetadata())
            {
                var nodeType = metadata.Type.ToString();
                boundOptionTypesByNodeType.TryGetValue(nodeType, out var optionTypeNames)
                    .ShouldBeTrue($"{entry.PackageId} must map default node type '{nodeType}' to bound option types.");

                var boundProperties = ReadBoundOptionProperties(
                    assembly,
                    optionTypeNames!,
                    entry.PackageId);

                boundProperties.ShouldNotBeEmpty(
                    $"{entry.PackageId} Designer metadata for '{nodeType}' should have bound option properties.");

                foreach (var optionName in boundProperties.Keys.Order(StringComparer.Ordinal))
                {
                    MetadataDescribesOrOmitsOption(metadata, optionName)
                        .ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{nodeType}' must describe bound option '{optionName}' or declare it in omittedOptions.");
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
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var configurationKeys = ReadConfigurationKeys(
                    assembly,
                    projectDirectory,
                    entry.PackageId)
                .ToArray();

            foreach (var metadata in provider.GetMetadata())
            {
                var nodeType = metadata.Type.ToString();

                foreach (var omittedOption in ReadOmittedOptions(metadata).Order(StringComparer.Ordinal))
                {
                    ConfigurationKeysContainOption(configurationKeys, omittedOption)
                        .ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{nodeType}' omits option '{omittedOption}', but no bound options property or explicit configuration read owns that key.");

                    metadata.Options.Any(option =>
                            string.Equals(option.Name, omittedOption, StringComparison.Ordinal))
                        .ShouldBeFalse(
                            $"{entry.PackageId} Designer metadata for '{nodeType}' option '{omittedOption}' cannot be both editable and declared in omittedOptions.");
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
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var configurationKeys = ReadConfigurationKeys(
                    assembly,
                    projectDirectory,
                    entry.PackageId)
                .ToArray();

            configurationKeys.ShouldNotBeEmpty(
                $"{entry.PackageId} must expose bound or explicitly read configuration keys.");

            foreach (var metadata in provider.GetMetadata())
            {
                foreach (var option in metadata.Options)
                {
                    ConfigurationKeysContainOption(configurationKeys, option.Name)
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
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var boundOptionTypesByNodeType = ReadDefaultNodeOptionTypes(projectDirectory, entry.PackageId);

            foreach (var metadata in provider.GetMetadata())
            {
                var nodeType = metadata.Type.ToString();
                boundOptionTypesByNodeType.TryGetValue(nodeType, out var optionTypeNames)
                    .ShouldBeTrue($"{entry.PackageId} must map default node type '{nodeType}' to bound option types.");

                var simpleDefaults = ReadSimpleBoundOptionDefaults(
                    assembly,
                    optionTypeNames!,
                    entry.PackageId);

                foreach (var option in metadata.Options.Where(option => option.DefaultValue is not null))
                {
                    if (!simpleDefaults.TryGetValue(option.Name, out var expected))
                        continue;

                    MetadataDefaultMatches(option.DefaultValue, expected.Value)
                        .ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{nodeType}' option '{option.Name}' default '{option.DefaultValue}' must match bound option default '{expected.Value}' from {expected.OptionType}.{expected.PropertyName}.");
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
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var boundOptionTypesByNodeType = ReadDefaultNodeOptionTypes(projectDirectory, entry.PackageId);

            foreach (var metadata in provider.GetMetadata())
            {
                var nodeType = metadata.Type.ToString();
                boundOptionTypesByNodeType.TryGetValue(nodeType, out var optionTypeNames)
                    .ShouldBeTrue($"{entry.PackageId} must map default node type '{nodeType}' to bound option types.");

                foreach (var optionTypeName in optionTypeNames!)
                {
                    var optionType = ResolveReferencedType(assembly, optionTypeName, entry.PackageId);

                    foreach (var requiredOption in ReadRequiredOptionProperties(optionType))
                    {
                        var option = metadata.Options.SingleOrDefault(option =>
                            string.Equals(option.Name, requiredOption.ConfigurationKey, StringComparison.Ordinal));

                        option.ShouldNotBeNull(
                            $"{entry.PackageId} Designer metadata for '{nodeType}' must expose required bound option '{requiredOption.ConfigurationKey}' from {optionType.Name}.{requiredOption.Name}.");
                        option.IsRequired.ShouldBeTrue(
                            $"{entry.PackageId} Designer metadata for '{nodeType}' option '{requiredOption.ConfigurationKey}' must be marked required because {optionType.Name}.{requiredOption.Name} is a C# required member.");
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
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var boundOptionTypesByNodeType = ReadDefaultNodeOptionTypes(projectDirectory, entry.PackageId);

            foreach (var metadata in provider.GetMetadata())
            {
                var nodeType = metadata.Type.ToString();
                boundOptionTypesByNodeType.TryGetValue(nodeType, out var optionTypeNames)
                    .ShouldBeTrue($"{entry.PackageId} must map default node type '{nodeType}' to bound option types.");

                var boundProperties = ReadBoundOptionProperties(
                    assembly,
                    optionTypeNames!,
                    entry.PackageId);

                foreach (var option in metadata.Options.Where(option =>
                    option.Kind == OptionValueKind.Number &&
                    (option.Min.HasValue || option.Max.HasValue)))
                {
                    if (!boundProperties.TryGetValue(option.Name, out var boundProperty) ||
                        !IsNumericOptionProperty(boundProperty.Property.PropertyType))
                    {
                        continue;
                    }

                    if (option.Min.HasValue)
                    {
                        AssertNumericMetadataBoundIsAccepted(
                            entry.PackageId,
                            nodeType,
                            option,
                            boundProperty,
                            option.Min.Value,
                            nameof(OptionDesignMetadata.Min));
                    }

                    if (option.Max.HasValue)
                    {
                        AssertNumericMetadataBoundIsAccepted(
                            entry.PackageId,
                            nodeType,
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
            var providerFile = ReadSingleProviderFile(projectDirectory, entry.PackageId);
            var providerContent = File.ReadAllText(providerFile);
            var providerOptionKinds = ReadProviderOptionKinds(providerContent);
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
                    if (expectedKind is null || !providerOptionKinds.TryGetValue(option.ConfigurationKey, out var actualKinds))
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
            var provider = CreateSingleMetadataProvider(assembly, entry.PackageId);
            var providerOptionsByName = provider
                .GetMetadata()
                .SelectMany(metadata => metadata.Options)
                .GroupBy(option => option.Name, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.Ordinal);

            foreach (var optionTypeName in ReadBoundOptionTypes(projectDirectory, entry.PackageId))
            {
                var optionType = ResolveReferencedType(assembly, optionTypeName, entry.PackageId);

                foreach (var option in ReadEnumOptionProperties(optionType))
                {
                    providerOptionsByName.TryGetValue(option.ConfigurationKey, out var providerOptions)
                        .ShouldBeTrue(
                            $"{entry.PackageId} must describe enum option '{optionType.Name}.{option.Name}'.");

                    var expectedChoices = Enum
                        .GetNames(option.EnumType)
                        .Order(StringComparer.Ordinal)
                        .ToArray();

                    foreach (var providerOption in providerOptions!)
                    {
                        providerOption.Kind.ShouldBe(
                            OptionValueKind.Enum,
                            $"{entry.PackageId} option '{optionType.Name}.{option.Name}' has enum CLR type '{option.EnumType.Name}' and must use OptionValueKind.Enum.");
                        var actualChoices = providerOption.Choices
                            .Select(choice => choice.Value)
                            .Order(StringComparer.Ordinal)
                            .ToArray();

                        actualChoices.ShouldBe(
                            expectedChoices,
                            $"{entry.PackageId} option '{optionType.Name}.{option.Name}' choices must match enum '{option.EnumType.Name}'.");

                        if (providerOption.DefaultValue is null)
                            continue;

                        var defaultValue = providerOption.DefaultValue is Enum enumValue
                            ? enumValue.ToString()
                            : providerOption.DefaultValue.ToString();

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

    private static string ReadSingleProviderFile(
        string projectDirectory,
        string packageId)
        => Directory
            .EnumerateFiles(
                projectDirectory,
                "*ComponentDesignMetadataProvider.cs",
                SearchOption.TopDirectoryOnly)
            .ShouldHaveSingleItem(
                $"{packageId} must ship exactly one package-owned Designer metadata provider.");

    private static string ReadSingleNodeTypesFile(
        string projectDirectory,
        string packageId)
        => Directory
            .EnumerateFiles(
                projectDirectory,
                "*CompositionNodeTypes.cs",
                SearchOption.TopDirectoryOnly)
            .ShouldHaveSingleItem($"{packageId} must keep node-type constants in one file.");

    private static string ReadSingleRegistryFile(
        string projectDirectory,
        string packageId)
        => Directory
            .EnumerateFiles(
                projectDirectory,
                "*CompositionNodeRegistryExtensions.cs",
                SearchOption.TopDirectoryOnly)
            .ShouldHaveSingleItem($"{packageId} must keep registry extensions in one file.");

    private static string[] ReadOptionalResourceNamesFiles(
        string projectDirectory,
        string packageId)
    {
        var files = Directory
            .EnumerateFiles(
                projectDirectory,
                "*CompositionResourceNames.cs",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

        files.Length.ShouldBeLessThanOrEqualTo(
            1,
            $"{packageId} must keep resource-name constants in one file.");

        return files;
    }

    private static string[] ReadOptionalPortNamesFiles(
        string projectDirectory,
        string packageId)
    {
        var files = Directory
            .EnumerateFiles(
                projectDirectory,
                "*CompositionPortNames.cs",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

        files.Length.ShouldBeLessThanOrEqualTo(
            1,
            $"{packageId} must keep port-name constants in one file.");

        return files;
    }

    private static string[] ReadBoundOptionTypes(
        string projectDirectory,
        string packageId)
        => BindConfigurationRegex()
            .Matches(File.ReadAllText(ReadSingleRegistryFile(projectDirectory, packageId)))
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

        var registryContent = File.ReadAllText(ReadSingleRegistryFile(projectDirectory, packageId));
        var stringConstants = StringConstantWithValueRegex()
            .Matches(registryContent)
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => match.Groups["value"].Value,
                StringComparer.Ordinal);

        foreach (Match match in ExplicitConfigurationValueRegex().Matches(registryContent))
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

    private static IReadOnlyDictionary<string, string[]> ReadDefaultNodeOptionTypes(
        string projectDirectory,
        string packageId)
    {
        var registryContent = File.ReadAllText(ReadSingleRegistryFile(projectDirectory, packageId));
        var nodeTypeConstants = PublicStringConstantWithValueRegex()
            .Matches(File.ReadAllText(ReadSingleNodeTypesFile(projectDirectory, packageId)))
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => match.Groups["value"].Value,
                StringComparer.Ordinal);
        var optionTypesByFactory = ReadFactoryOptionTypes(registryContent, packageId);
        var optionTypesByNodeType = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (Match match in PublicRegistryMethodBlockRegex().Matches(registryContent))
        {
            var parameters = match.Groups["parameters"].Value;
            var body = match.Groups["body"].Value;
            var nodeTypeMatch = DefaultNodeTypeParameterRegex().Match(parameters);
            var registrationMatches = RegistryRegistrationReferenceRegex().Matches(body);
            registrationMatches.Count.ShouldBeGreaterThan(
                0,
                $"{packageId} registry method must pass a named node type and factory method to registry.Register.");

            foreach (Match registrationMatch in registrationMatches)
            {
                var registeredNodeType = registrationMatch.Groups["nodeType"].Value;
                var nodeTypeReference = nodeTypeMatch.Success &&
                    string.Equals(registeredNodeType, "nodeType", StringComparison.Ordinal)
                        ? nodeTypeMatch.Groups["constant"].Value
                        : registeredNodeType;
                var nodeTypeConstant = nodeTypeReference.Split('.')[^1];
                nodeTypeConstants.TryGetValue(nodeTypeConstant, out var nodeType)
                    .ShouldBeTrue($"{packageId} registry node type constant '{nodeTypeConstant}' must resolve.");

                var factoryName = registrationMatch.Groups["factory"].Value;
                optionTypesByFactory.TryGetValue(factoryName, out var optionTypes)
                    .ShouldBeTrue($"{packageId} factory '{factoryName}' for '{nodeType}' must bind configuration.");

                optionTypesByNodeType[nodeType!] = optionTypes!;
            }
        }

        optionTypesByNodeType.ShouldNotBeEmpty($"{packageId} must expose default registry node option mappings.");
        return optionTypesByNodeType;
    }

    private static IReadOnlyDictionary<string, string[]> ReadFactoryOptionTypes(
        string registryContent,
        string packageId)
    {
        var methodBodies = ReadFactoryMethodBodies(registryContent);
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

        optionTypesByFactory.ShouldNotBeEmpty($"{packageId} registry factories must bind configuration.");
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

    private static IComponentDesignMetadataProvider CreateSingleMetadataProvider(
        Assembly assembly,
        string packageId)
    {
        var providerTypes = assembly
            .GetTypes()
            .Where(type =>
                type is { IsAbstract: false, IsClass: true } &&
                typeof(IComponentDesignMetadataProvider).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        providerTypes.Length.ShouldBe(
            1,
            $"{packageId} must expose exactly one runtime-loadable Designer metadata provider.");

        var providerType = providerTypes[0];
        providerType.GetConstructor(Type.EmptyTypes)
            .ShouldNotBeNull($"{packageId} provider must have a public parameterless constructor.");

        return (IComponentDesignMetadataProvider)Activator.CreateInstance(providerType).ShouldNotBeNull();
    }

    private static CompositionNodeRegistry BuildDefaultRegistry(
        Assembly assembly,
        string packageId)
    {
        var registry = new CompositionNodeRegistry();

        foreach (var method in ReadRegistryMethods(assembly, packageId))
        {
            InvokeRegistryMethod(method, registry, packageId);
        }

        return registry;
    }

    private static MethodInfo[] ReadRegistryMethods(
        Assembly assembly,
        string packageId)
    {
        var registryMethods = assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: true, IsSealed: true } &&
                type.Name.EndsWith("CompositionNodeRegistryExtensions", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method =>
                method.ReturnType == typeof(CompositionNodeRegistry) &&
                method.GetParameters() is [{ ParameterType: var firstParameter }, ..] &&
                firstParameter == typeof(CompositionNodeRegistry))
            .OrderBy(method => method.DeclaringType?.FullName, StringComparer.Ordinal)
            .ThenBy(method => method.MetadataToken)
            .ToArray();

        registryMethods.ShouldNotBeEmpty($"{packageId} must expose registry extension methods.");
        return registryMethods;
    }

    private static void InvokeRegistryMethod(
        MethodInfo method,
        CompositionNodeRegistry registry,
        string packageId)
    {
        var executableMethod = method;
        if (method.IsGenericMethodDefinition)
        {
            method.GetGenericArguments().Length
                .ShouldBeLessThanOrEqualTo(
                    RegistryGenericArgumentTypes.Length,
                    $"{packageId} registry method '{method.Name}' has more generic arguments than the release convention test supports.");

            var genericTypes = method
                .GetGenericArguments()
                .Select((_, index) => RegistryGenericArgumentTypes[index])
                .ToArray();
            executableMethod = method.MakeGenericMethod(genericTypes);
        }

        var arguments = executableMethod
            .GetParameters()
            .Select(parameter =>
            {
                if (parameter.ParameterType == typeof(CompositionNodeRegistry))
                    return registry;

                if (parameter.HasDefaultValue)
                    return parameter.DefaultValue;

                throw new InvalidOperationException(
                    $"{packageId} registry method '{method.Name}' has unsupported required parameter '{parameter.Name}'.");
            })
            .ToArray();

        executableMethod.Invoke(null, arguments);
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
        string? value,
        string message)
        => string.IsNullOrWhiteSpace(value).ShouldBeFalse(message);

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
        string nodeType,
        ComponentDesignMetadata metadata,
        CompositionPortMetadata port,
        PortDirection direction)
    {
        if (!ShouldValidateConcretePortType(port.MessageType))
            return;

        var designerPort = metadata.Ports.SingleOrDefault(designerPort =>
            designerPort.Direction == direction &&
            string.Equals(designerPort.Name.ToString(), port.Name, StringComparison.Ordinal));

        designerPort.ShouldNotBeNull(
            $"{packageId} Designer metadata for '{nodeType}' must expose {direction.ToString().ToLowerInvariant()} port '{port.Name}'.");

        string.IsNullOrWhiteSpace(designerPort.ValueType)
            .ShouldBeFalse(
                $"{packageId} Designer metadata for '{nodeType}.{port.Name}' must expose a ValueType for concrete port type '{port.MessageType.FullName}'.");

        var expectedValueType = ToDesignerValueType(port.MessageType);
        NormalizeValueType(designerPort.ValueType!)
            .ShouldBe(
                NormalizeValueType(expectedValueType),
                $"{packageId} Designer metadata for '{nodeType}.{port.Name}' must use ValueType '{expectedValueType}' for registry type '{port.MessageType.FullName}'.");
    }

    private static bool ShouldValidateConcretePortType(Type type)
        => !type.ContainsGenericParameters &&
            !ContainsRegistryGenericArgument(type);

    private static bool ContainsRegistryGenericArgument(Type type)
    {
        if (RegistryGenericArgumentTypes.Contains(type))
            return true;

        if (type.HasElementType && type.GetElementType() is { } elementType)
            return ContainsRegistryGenericArgument(elementType);

        return type.IsGenericType &&
            type.GetGenericArguments().Any(ContainsRegistryGenericArgument);
    }

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
                string.Equals(option.Name, optionName, StringComparison.Ordinal)) ||
            ReadOmittedOptions(metadata).Contains(optionName);

    private static HashSet<string> ReadOmittedOptions(ComponentDesignMetadata metadata)
    {
        if (!metadata.Attributes.TryGetValue("omittedOptions", out var omittedOptions) ||
            string.IsNullOrWhiteSpace(omittedOptions))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return omittedOptions
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void AssertNumericMetadataBoundIsAccepted(
        string packageId,
        string nodeType,
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
                $"{packageId} Designer metadata for '{nodeType}' option '{option.Name}' {boundName} '{bound}' must be representable as {boundProperty.OptionType.Name}.{boundProperty.Property.Name} type '{boundProperty.Property.PropertyType.Name}'.");

        BoundOptionAcceptsValue(
                boundProperty.OptionType,
                boundProperty.Property,
                convertedBound)
            .ShouldBeTrue(
                $"{packageId} Designer metadata for '{nodeType}' option '{option.Name}' {boundName} '{bound}' must be accepted by bound option {boundProperty.OptionType.Name}.{boundProperty.Property.Name}.");
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
                key.StartsWith("requiredWhen", StringComparison.OrdinalIgnoreCase)) ||
            resource.Attributes.ContainsKey("option");

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

    private static Dictionary<string, HashSet<string>> ReadProviderOptionKinds(string providerContent)
    {
        var optionKinds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (Match match in OptionMetadataBlockRegex().Matches(providerContent))
        {
            var body = match.Groups["body"].Value;
            var nameMatch = OptionNameAssignmentRegex().Match(body);
            var kindMatch = OptionKindAssignmentRegex().Match(body);

            if (!nameMatch.Success || !kindMatch.Success)
                continue;

            var name = nameMatch.Groups["name"].Value;
            if (!optionKinds.TryGetValue(name, out var kinds))
            {
                kinds = new HashSet<string>(StringComparer.Ordinal);
                optionKinds.Add(name, kinds);
            }

            kinds.Add(kindMatch.Groups["kind"].Value);
        }

        return optionKinds;
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

    private sealed record RegistryMessageA(string Value);

    private sealed record RegistryMessageB(string Value);

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

    private static readonly Type[] RegistryGenericArgumentTypes =
    [
        typeof(RegistryMessageA),
        typeof(RegistryMessageB)
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

    [GeneratedRegex(@"public\s+static\s+CompositionNodeRegistry\s+\w+(?:<[^>]+>)?\s*\((?<parameters>.*?)\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline)]
    private static partial Regex PublicRegistryMethodBlockRegex();

    [GeneratedRegex(@"string\s+nodeType\s*=\s*(?<constant>\w+CompositionNodeTypes\.\w+)")]
    private static partial Regex DefaultNodeTypeParameterRegex();

    [GeneratedRegex(@"(?:registry\s*)?\.Register\(\s*(?<nodeType>[\w.]+)\s*,\s*(?<factory>\w+)")]
    private static partial Regex RegistryRegistrationReferenceRegex();

    [GeneratedRegex(@"private\s+static\s+(?:async\s+)?ValueTask<ComposedNode>\s+(?<name>\w+)(?:<[^>]+>)?\s*\([^)]*\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline)]
    private static partial Regex PrivateFactoryMethodBlockRegex();

    [GeneratedRegex(@"private\s+static\s+(?:async\s+)?ValueTask<ComposedNode>\s+(?<name>\w+)(?:<[^>]+>)?\s*\([^)]*\)\s*=>\s*(?<body>.*?);", RegexOptions.Singleline)]
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

    [GeneratedRegex(@"(?:new\s+OptionDesignMetadata\s*(?:\([^)]*\))?|=>\s*new\s*\(\s*\))\s*\{(?<body>.*?)\}", RegexOptions.Singleline)]
    private static partial Regex OptionMetadataBlockRegex();

    [GeneratedRegex(@"Name\s*=\s*""(?<name>[^""]+)""")]
    private static partial Regex OptionNameAssignmentRegex();

    [GeneratedRegex(@"Kind\s*=\s*OptionValueKind\.(?<kind>\w+)")]
    private static partial Regex OptionKindAssignmentRegex();

    [GeneratedRegex(@"Nullable<(?<type>[^>]+)>")]
    private static partial Regex NullableTypeRegex();

    [GeneratedRegex(@"\b(?<property>Options|Resources|Ports)\s*=\s*\[")]
    private static partial Regex InlineMetadataCollectionAssignmentRegex();

    [GeneratedRegex(@"\bnew\s+ComponentDesignMetadata\s*(?:\(|\{)")]
    private static partial Regex DirectComponentMetadataConstructionRegex();
}
