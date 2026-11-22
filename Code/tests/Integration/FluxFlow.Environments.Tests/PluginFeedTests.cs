using System.IO.Compression;
using System.Text;
using FluxFlow.Execution.Plugins;
using FluxFlow.Plugins.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class PluginFeedTests
{
    [Fact]
    public async Task Download_checks_identity_before_staging_and_records_the_source()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapGet("/index.json", (HttpRequest request) => new
        {
            resources = new[] { new Dictionary<string, string> {
                ["@type"] = "PackageBaseAddress/3.0.0", ["@id"] = $"http://{request.Host}/packages/" } }
        });
        var archive = Package();
        app.MapGet("/packages/{id}/{version}/{file}", (string version) => version == "1.0.0"
            ? Results.Bytes(archive) : Results.NotFound());
        await app.StartAsync();
        var store = Directory.CreateTempSubdirectory("fluxflow-feed-");
        try
        {
            var manager = new LocalPluginManager(store.FullName);
            await manager.SaveSourcesAsync(new([], [new("Workshop", app.Urls.Single() + "/index.json")]));
            var reopened = new LocalPluginManager(store.FullName);
            var source = (await reopened.GetSourcesAsync()).Single(s => s.Name == "Workshop");
            var package = new PluginPackage(source.Url, "Sample.Plugin", "1.0.0");
            await Should.ThrowAsync<InvalidDataException>(() => manager.InstallFeedAsync(package with { Version = "9.0.0" }));
            await Should.ThrowAsync<InvalidOperationException>(() => manager.InstallFeedAsync(package with { Id = "Wrong.Plugin" }));
            (await manager.ListAsync()).ShouldBeEmpty();
            await reopened.InstallFeedAsync(package);
            (await manager.ListAsync()).Single().ShouldBe(new PluginEntry("Sample.Plugin", "1.0.0", PluginState.PendingInstall)
                { Source = "NuGet feed" });
        }
        finally { store.Delete(recursive: true); }
    }

    [Fact]
    public async Task Invalid_or_stale_edits_preserve_saved_sources()
    {
        var store = Directory.CreateTempSubdirectory("fluxflow-sources-");
        try
        {
            var manager = new LocalPluginManager(store.FullName);
            var a = new PluginSource("Lab", "https://feeds.example/lab/index.json");
            await manager.SaveSourcesAsync(new([], [a]));
            var saved = await manager.GetSourcesAsync();
            foreach (var invalid in new[] {
                a with { Name = " NUGET.ORG " }, new("Lab 2", a.Url), a with { Name = "lab", Url = "https://feeds.example/b" },
                new("Other", "http://example.org/index.json"), new("Other", "https://user:password@example.org/index.json"),
                new("Other", PluginSource.Default.Url) })
            {
                await Should.ThrowAsync<ArgumentException>(() => manager.SaveSourcesAsync(new(saved, [a, invalid])));
            }
            var renamed = a with { Name = "Workshop" };
            await manager.SaveSourcesAsync(new(saved, [renamed]));
            await Should.ThrowAsync<InvalidOperationException>(() => manager.SaveSourcesAsync(new(saved, [])));
            (await manager.GetSourcesAsync()).ShouldBe(new[] { renamed });
            await manager.SaveSourcesAsync(new([renamed], []));
            (await manager.GetSourcesAsync()).ShouldBeEmpty();
        }
        finally { store.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("""[{"Name":"Lab","Url":"https://feeds.example/a"},{"Name":" lab ","Url":"https://feeds.example/b"}]""")]
    public async Task Invalid_saved_entries_cannot_be_overwritten_as_empty_settings(string json)
    {
        var store = Directory.CreateTempSubdirectory("fluxflow-sources-");
        try
        {
            var path = Path.Combine(store.FullName, "nuget-sources.json");
            await File.WriteAllTextAsync(path, json);
            var manager = new LocalPluginManager(store.FullName);
            await Should.ThrowAsync<InvalidDataException>(manager.GetSourcesAsync);
            await Should.ThrowAsync<InvalidDataException>(() => manager.SaveSourcesAsync(new([], [])));
            (await File.ReadAllTextAsync(path)).ShouldBe(json);
        }
        finally { store.Delete(recursive: true); }
    }

    [Fact]
    public async Task Folder_install_after_reopen_preserves_legacy_feeds_and_rejects_stale_sources()
    {
        var store = Directory.CreateTempSubdirectory("fluxflow-settings-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(store.FullName, "nuget-sources.json"),
                """[{"Name":"Lab","Url":"https://feeds.example/lab/index.json"}]""");
            var manager = new LocalPluginManager(store.FullName);
            var legacy = (await manager.GetSourcesAsync()).Single();
            legacy.Kind.ShouldBe(PluginSourceKind.NuGet);
            var folder = store.CreateSubdirectory("packages");
            var source = new PluginSource("Local packages", folder.FullName) { Kind = PluginSourceKind.Folder };
            var package = Path.Combine(folder.FullName, "Sample.Plugin.nupkg");
            await File.WriteAllBytesAsync(package, Package());
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "notes.txt"), "ignored");
            await manager.SaveSourcesAsync(new([legacy], [legacy, source]));
            var reopened = new LocalPluginManager(store.FullName);
            var sources = await reopened.GetSourcesAsync();
            sources.ShouldBe(new[] { legacy, source });
            var listed = await reopened.ListFolderAsync(sources[1]);
            listed.ShouldBe(new[] { package });
            await reopened.InstallAsync(listed.Single());
            (await reopened.ListAsync()).Single().State.ShouldBe(PluginState.PendingInstall);
            await reopened.SaveSourcesAsync(new(sources, [legacy]));
            await Should.ThrowAsync<ArgumentException>(() => manager.ListFolderAsync(source));
        }
        finally { store.Delete(recursive: true); }
    }

    [Fact]
    public async Task Unavailable_folder_does_not_hide_or_block_other_sources_after_reopen()
    {
        var store = Directory.CreateTempSubdirectory("fluxflow-offline-");
        try
        {
            var manager = new LocalPluginManager(store.FullName);
            var folder = new PluginSource("Workshop", Path.Combine(store.FullName, "packages")) { Kind = PluginSourceKind.Folder };
            var feed = new PluginSource("Releases", "https://feeds.example/releases/index.json");
            await manager.SaveSourcesAsync(new([], [folder, feed]));
            await Should.ThrowAsync<DirectoryNotFoundException>(() => manager.ListFolderAsync(folder));
            await File.WriteAllTextAsync(folder.Url, "no longer a directory");

            var reopened = new LocalPluginManager(store.FullName);
            var saved = await reopened.GetSourcesAsync();
            saved.ShouldBe(new[] { folder, feed });
            var renamed = feed with { Name = "Release feed" };
            await reopened.SaveSourcesAsync(new(saved, [folder, renamed]));
            await Should.ThrowAsync<DirectoryNotFoundException>(() => reopened.ListFolderAsync(folder));
            File.Delete(folder.Url);
            Directory.CreateDirectory(folder.Url);
            (await reopened.ListFolderAsync(folder)).ShouldBeEmpty();
            (await reopened.GetSourcesAsync()).ShouldBe(new[] { folder, renamed });
        }
        finally { store.Delete(recursive: true); }
    }

    private static byte[] Package()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            void Add(string path, string content)
            {
                using var entry = zip.CreateEntry(path).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
            Add("fluxflow-plugin.json", """{"contract":1,"id":"Sample.Plugin","version":"1.0.0","entryType":"Sample.Entry"}""");
            Add("Sample.Plugin.nuspec", "<package><metadata><id>Sample.Plugin</id><version>1.0.0</version></metadata></package>");
            foreach (var target in new[] { "net8.0", "net10.0" }) Add($"lib/{target}/Sample.Plugin.dll", "");
        }
        return buffer.ToArray();
    }
}
