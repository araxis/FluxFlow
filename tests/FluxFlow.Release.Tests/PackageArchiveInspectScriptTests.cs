using System.IO.Compression;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackageArchiveInspectScriptTests
{
    private static readonly string PackageMetadataNamespace =
        "h" + "ttp://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

    [Fact]
    public async Task Archive_inspect_script_accepts_expected_archives()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var version = ReadProjectVersion(root, package);
        var packageSource = Path.Combine(Path.GetTempPath(), $"fluxflow-package-source-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(packageSource);
            CreatePackageArchive(packageSource, package.PackageId, version);
            CreateSymbolArchive(packageSource, package.PackageId, version);

            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "package-archive-inspect.ps1",
                "-PackageId",
                package.PackageId,
                "-Version",
                version,
                "-PackageSource",
                packageSource);

            result.ExitCode.ShouldBe(0, result.ToString());
            result.StandardOutput.ShouldContain($"ARCHIVE_OK={package.PackageId}");
            result.StandardOutput.ShouldContain($"{package.PackageId}.{version}.nupkg");
            result.StandardOutput.ShouldContain($"{package.PackageId}.{version}.snupkg");
        }
        finally
        {
            if (Directory.Exists(packageSource))
                Directory.Delete(packageSource, recursive: true);
        }
    }

    [Fact]
    public async Task Archive_inspect_script_rejects_missing_symbol_entry()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetConfigurationPackage(root);
        var version = ReadProjectVersion(root, package);
        var packageSource = Path.Combine(Path.GetTempPath(), $"fluxflow-package-source-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(packageSource);
            CreatePackageArchive(packageSource, package.PackageId, version);
            CreateSymbolArchive(packageSource, package.PackageId, version, includeNet10Symbol: false);

            var result = await ReleaseScriptRunner.RunAsync(
                root,
                "package-archive-inspect.ps1",
                "-PackageId",
                package.PackageId,
                "-Version",
                version,
                "-PackageSource",
                packageSource);

            result.ExitCode.ShouldNotBe(0);
            result.ToString().ShouldContain($"lib/net10.0/{package.PackageId}.pdb");
            result.ToString().ShouldContain("missing entry");
        }
        finally
        {
            if (Directory.Exists(packageSource))
                Directory.Delete(packageSource, recursive: true);
        }
    }

    private static PackageManifestEntry GetConfigurationPackage(string root)
        => PackageManifest
            .Read(root)
            .Single(entry => entry.Alias == "components-configuration");

    private static string ReadProjectVersion(string root, PackageManifestEntry package)
    {
        var projectPath = Path.Combine(root, NormalizePath(package.Project));
        var project = XDocument.Load(projectPath);
        return project
            .Descendants()
            .Where(element => element.Name.LocalName == "Version")
            .Select(element => element.Value.Trim())
            .First(value => value.Length > 0);
    }

    private static void CreatePackageArchive(string packageSource, string packageId, string version)
    {
        var archivePath = Path.Combine(packageSource, $"{packageId}.{version}.nupkg");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);

        AddEntry(archive, $"{packageId}.nuspec", CreatePackageNuspec(packageId, version));
        AddEntry(archive, "README.md", "# Package");
        AddEntry(archive, $"lib/net8.0/{packageId}.dll", "net8");
        AddEntry(archive, $"lib/net10.0/{packageId}.dll", "net10");
    }

    private static void CreateSymbolArchive(
        string packageSource,
        string packageId,
        string version,
        bool includeNet10Symbol = true)
    {
        var archivePath = Path.Combine(packageSource, $"{packageId}.{version}.snupkg");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);

        AddEntry(archive, $"{packageId}.nuspec", CreateSymbolNuspec(packageId, version));
        AddEntry(archive, $"lib/net8.0/{packageId}.pdb", "net8");

        if (includeNet10Symbol)
            AddEntry(archive, $"lib/net10.0/{packageId}.pdb", "net10");
    }

    private static string CreatePackageNuspec(string packageId, string version)
        => $$"""
           <?xml version="1.0" encoding="utf-8"?>
           <package xmlns="{{PackageMetadataNamespace}}">
             <metadata>
               <id>{{packageId}}</id>
               <version>{{version}}</version>
               <readme>README.md</readme>
             </metadata>
           </package>
           """;

    private static string CreateSymbolNuspec(string packageId, string version)
        => $$"""
           <?xml version="1.0" encoding="utf-8"?>
           <package xmlns="{{PackageMetadataNamespace}}">
             <metadata>
               <id>{{packageId}}</id>
               <version>{{version}}</version>
               <packageTypes>
                 <packageType name="SymbolsPackage" />
               </packageTypes>
             </metadata>
           </package>
           """;

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
}
