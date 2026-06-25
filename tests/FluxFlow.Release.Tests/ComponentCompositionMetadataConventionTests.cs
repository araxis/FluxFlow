using System.Text.RegularExpressions;
using System.Xml.Linq;
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

                var optionNames = OptionPropertyRegex()
                    .Matches(File.ReadAllText(optionTypeFile))
                    .Select(match => ToConfigurationKey(match.Groups["name"].Value))
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

    [GeneratedRegex(@"public\s+const\s+string\s+(?<name>\w+)\s*=")]
    private static partial Regex PublicStringConstantRegex();

    [GeneratedRegex(@"BindConfiguration<(?<type>[^>]+)>")]
    private static partial Regex BindConfigurationRegex();

    [GeneratedRegex(@"public\s+(?:required\s+)?[^\r\n{]+\s+(?<name>\w+)\s*\{\s*get;\s*init;\s*\}")]
    private static partial Regex OptionPropertyRegex();
}
