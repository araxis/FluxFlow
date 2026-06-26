using System.Reflection;
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
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        entries.ShouldNotBeEmpty("component composition packages should be listed in the release manifest.");

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var providerFiles = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*ComponentDesignMetadataProvider.cs",
                    SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .ToArray();

            providerFiles.Length.ShouldBe(
                1,
                $"{entry.PackageId} must ship exactly one package-owned Designer metadata provider.");

            var providerContent = File.ReadAllText(providerFiles[0]);
            providerContent.Contains(
                    "IComponentDesignMetadataProvider",
                    StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} provider must implement IComponentDesignMetadataProvider.");

            var project = XDocument.Load(projectPath);
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
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var project = XDocument.Load(projectPath);
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
    public void Component_composition_designer_metadata_matches_default_registry_metadata()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var project = XDocument.Load(projectPath);
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
    public void Component_composition_designer_metadata_is_documented()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var publicApiOverview = File.ReadAllText(Path.Combine(root, "docs", "14-public-api-overview.md"));
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var project = XDocument.Load(projectPath);
            var version = ReadRequiredProperty(project, "Version", entry.PackageId);
            var readmePath = Path.Combine(projectDirectory, "README.md");
            var providerFile = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*ComponentDesignMetadataProvider.cs",
                    SearchOption.TopDirectoryOnly)
                .ShouldHaveSingleItem();
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
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var nodeTypesFile = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*CompositionNodeTypes.cs",
                    SearchOption.TopDirectoryOnly)
                .ShouldHaveSingleItem($"{entry.PackageId} must keep node-type constants in one file.");
            var providerFile = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*ComponentDesignMetadataProvider.cs",
                    SearchOption.TopDirectoryOnly)
                .ShouldHaveSingleItem();
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
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var nodeTypesFile = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*CompositionNodeTypes.cs",
                    SearchOption.TopDirectoryOnly)
                .ShouldHaveSingleItem($"{entry.PackageId} must keep node-type constants in one file.");
            var registryFile = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*CompositionNodeRegistryExtensions.cs",
                    SearchOption.TopDirectoryOnly)
                .ShouldHaveSingleItem($"{entry.PackageId} must keep registry extensions in one file.");
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
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var resourceNamesFiles = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*CompositionResourceNames.cs",
                    SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .ToArray();

            resourceNamesFiles.Length.ShouldBeLessThanOrEqualTo(
                1,
                $"{entry.PackageId} must keep resource-name constants in one file.");

            if (resourceNamesFiles.Length == 0)
                continue;

            var providerFile = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*ComponentDesignMetadataProvider.cs",
                    SearchOption.TopDirectoryOnly)
                .ShouldHaveSingleItem();
            var providerContent = File.ReadAllText(providerFile);
            var resourceTypeName = Path.GetFileNameWithoutExtension(resourceNamesFiles[0]);
            var resourceConstants = PublicStringConstantRegex()
                .Matches(File.ReadAllText(resourceNamesFiles[0]))
                .Select(match => match.Groups["name"].Value)
                .ToArray();

            resourceConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} resource-name file should expose at least one resource constant.");
            providerContent.Contains(
                    "ResourceDesignMetadata",
                    StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} provider must expose Designer resource metadata.");

            foreach (var resourceConstant in resourceConstants)
            {
                var resourceReference = $"{resourceTypeName}.{resourceConstant}";
                providerContent.Contains(resourceReference, StringComparison.Ordinal)
                    .ShouldBeTrue($"{entry.PackageId} provider must expose resource '{resourceReference}'.");
            }
        }
    }

    [Fact]
    public void Component_composition_resource_names_are_used_by_registry_extensions()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var resourceNamesFiles = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*CompositionResourceNames.cs",
                    SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .ToArray();

            resourceNamesFiles.Length.ShouldBeLessThanOrEqualTo(
                1,
                $"{entry.PackageId} must keep resource-name constants in one file.");

            if (resourceNamesFiles.Length == 0)
                continue;

            var registryFile = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*CompositionNodeRegistryExtensions.cs",
                    SearchOption.TopDirectoryOnly)
                .ShouldHaveSingleItem($"{entry.PackageId} must keep registry extensions in one file.");
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
    public void Component_composition_port_names_are_exposed_by_designer_metadata()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var portNamesFiles = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*CompositionPortNames.cs",
                    SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .ToArray();

            portNamesFiles.Length.ShouldBeLessThanOrEqualTo(
                1,
                $"{entry.PackageId} must keep port-name constants in one file.");

            if (portNamesFiles.Length == 0)
                continue;

            var providerFile = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*ComponentDesignMetadataProvider.cs",
                    SearchOption.TopDirectoryOnly)
                .ShouldHaveSingleItem();
            var providerContent = File.ReadAllText(providerFile);
            var portTypeName = Path.GetFileNameWithoutExtension(portNamesFiles[0]);
            var portConstants = PublicStringConstantRegex()
                .Matches(File.ReadAllText(portNamesFiles[0]))
                .Select(match => match.Groups["name"].Value)
                .ToArray();

            portConstants.ShouldNotBeEmpty(
                $"{entry.PackageId} port-name file should expose at least one port constant.");
            providerContent.Contains(
                    "PortDesignMetadata",
                    StringComparison.Ordinal)
                .ShouldBeTrue($"{entry.PackageId} provider must expose Designer port metadata.");

            foreach (var portConstant in portConstants)
            {
                var portReference = $"{portTypeName}.{portConstant}";
                providerContent.Contains(portReference, StringComparison.Ordinal)
                    .ShouldBeTrue($"{entry.PackageId} provider must expose port '{portReference}'.");
            }
        }
    }

    [Fact]
    public void Component_composition_port_names_are_used_by_registry_extensions()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var portNamesFiles = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*CompositionPortNames.cs",
                    SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .ToArray();

            portNamesFiles.Length.ShouldBeLessThanOrEqualTo(
                1,
                $"{entry.PackageId} must keep port-name constants in one file.");

            if (portNamesFiles.Length == 0)
                continue;

            var registryFile = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*CompositionNodeRegistryExtensions.cs",
                    SearchOption.TopDirectoryOnly)
                .ShouldHaveSingleItem($"{entry.PackageId} must keep registry extensions in one file.");
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
        var sourceRoot = Path.Combine(root, "src");
        var sourceFiles = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .ToArray();
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var providerFile = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*ComponentDesignMetadataProvider.cs",
                    SearchOption.TopDirectoryOnly)
                .ShouldHaveSingleItem();
            var providerContent = File.ReadAllText(providerFile);
            var boundOptionTypes = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*CompositionNodeRegistryExtensions.cs",
                    SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText)
                .SelectMany(content => BindConfigurationRegex()
                    .Matches(content)
                    .Select(match => match.Groups["type"].Value.Trim()))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

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
                var optionNames = OptionPropertyRegex()
                    .Matches(optionContent)
                    .Concat(ValidatedOptionPropertyRegex().Matches(optionContent))
                    .Select(match => ToConfigurationKey(match.Groups["name"].Value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                optionNames.ShouldNotBeEmpty(
                    $"{entry.PackageId} option type '{optionType}' should expose configuration properties.");

                foreach (var optionName in optionNames)
                {
                    ProviderMentionsOption(providerContent, optionName)
                        .ShouldBeTrue(
                            $"{entry.PackageId} must describe bound option '{optionName}' from '{optionType}' or declare it in omittedOptions.");
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
        var entries = PackageManifest
            .Read(root)
            .Where(IsComponentCompositionPackage)
            .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in entries)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, NormalizePath(entry.Project)));
            var projectDirectory = Path.GetDirectoryName(projectPath).ShouldNotBeNull();
            var providerFile = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*ComponentDesignMetadataProvider.cs",
                    SearchOption.TopDirectoryOnly)
                .ShouldHaveSingleItem();
            var providerContent = File.ReadAllText(providerFile);
            var providerOptionKinds = ReadProviderOptionKinds(providerContent);
            var boundOptionTypes = Directory
                .EnumerateFiles(
                    projectDirectory,
                    "*CompositionNodeRegistryExtensions.cs",
                    SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText)
                .SelectMany(content => BindConfigurationRegex()
                    .Matches(content)
                    .Select(match => match.Groups["type"].Value.Trim()))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

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

    private static bool IsComponentCompositionPackage(PackageManifestEntry entry)
        => entry.PackageId.StartsWith("FluxFlow.Components.", StringComparison.Ordinal)
            && entry.PackageId.EndsWith(".Composition", StringComparison.Ordinal);

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

        foreach (var method in registryMethods)
        {
            InvokeRegistryMethod(method, registry, packageId);
        }

        return registry;
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

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    private static bool ProviderMentionsOption(
        string providerContent,
        string optionName)
        => providerContent.Contains(
            $"\"{optionName}\"",
            StringComparison.Ordinal);

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

    private static readonly Type[] RegistryGenericArgumentTypes =
    [
        typeof(RegistryMessageA),
        typeof(RegistryMessageB)
    ];

    [GeneratedRegex(@"public\s+const\s+string\s+(?<name>\w+)\s*=")]
    private static partial Regex PublicStringConstantRegex();

    [GeneratedRegex(@"public\s+static\s+string\s+(?<name>\w+)\s*\(")]
    private static partial Regex PublicStaticStringMethodRegex();

    [GeneratedRegex(@"BindConfiguration<(?<type>[^>]+)>")]
    private static partial Regex BindConfigurationRegex();

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
}
