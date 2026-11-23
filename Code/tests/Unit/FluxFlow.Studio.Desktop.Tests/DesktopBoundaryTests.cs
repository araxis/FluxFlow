using FluxFlow.Application.Addressing;
using FluxFlow.Authoring;
using FluxFlow.Environments;
using FluxFlow.Studio.Desktop.Services;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Projects;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Desktop.Tests;

public sealed class DesktopBoundaryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fluxflow-desktop-tests", Guid.NewGuid().ToString("N"));
    public DesktopBoundaryTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Save_uses_native_identity_and_Save_As_does_not_overwrite_the_original()
    {
        var original = Path.Combine(_directory, "original.fluxflow");
        var copy = Path.Combine(_directory, "copy.fluxflow");
        const string json = """{"Configuration":{"KeepCASE":"value"},"Presentation":{"Color":"#123456"}}""";
        await File.WriteAllTextAsync(original, json);
        var dialogs = new Dialogs { OpenPath = original, SavePath = copy };
        var files = new NativeStudioFileAccess(dialogs);
        var opened = (await files.OpenAsync(".fluxflow"))!;
        opened.Location.ShouldBe(original);
        opened.Content.ShouldBe(json);
        var edited = opened with { Content = json.Replace("value", "changed") };
        (await files.SaveAsync(edited)).ShouldBe(edited);
        dialogs.SaveCalls.ShouldBe(0);
        (await File.ReadAllTextAsync(original)).ShouldBe(edited.Content);
        var savedCopy = (await files.SaveAsync(opened, saveAs: true))!;
        dialogs.SaveCalls.ShouldBe(1);
        savedCopy.Location.ShouldBe(copy);
        savedCopy.Name.ShouldBe("copy.fluxflow");
        (await File.ReadAllTextAsync(copy)).ShouldBe(json);
        (await File.ReadAllTextAsync(original)).ShouldBe(edited.Content);
    }

    [Fact]
    public async Task Password_file_import_accepts_an_extensionless_name_and_reads_verbatim_utf8()
    {
        const string source = "\uFEFFalice:$7$101$opaque==\r\nbob:$6$more=\n# keep";
        var path = Path.Combine(_directory, "passwords");
        await File.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(source));
        var dialogs = new Dialogs { OpenPath = path };
        var result = (await new NativeStudioFileAccess(dialogs).OpenVerbatimTextAsync()).ShouldNotBeNull();
        dialogs.OpenExtensions.ShouldBe("");
        result.Name.ShouldBe("passwords");
        result.Content.ShouldBe(source);
    }

    [Fact]
    public async Task Cancelled_pickers_and_failed_save_preserve_existing_files()
    {
        var path = Path.Combine(_directory, "keep.fluxflow");
        await File.WriteAllTextAsync(path, "original");
        var files = new NativeStudioFileAccess(new Dialogs());
        (await files.OpenAsync(".fluxflow")).ShouldBeNull();
        (await files.OpenImageAsync()).ShouldBeNull();
        (await files.SaveAsync(new("keep.fluxflow", "changed", path), saveAs: true)).ShouldBeNull();
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Should.ThrowAsync<UnauthorizedAccessException>(() => files.SaveAsync(new("keep.fluxflow", "changed", path)));
        (await File.ReadAllTextAsync(path)).ShouldBe("original");
        Directory.GetFiles(_directory, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelled_and_failed_publishes_preserve_destination_and_project_history()
    {
        using var plugins = new FluxFlow.Execution.Plugins.InstalledPlugins();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDesktopStudio(plugins);
        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"input":{"Type":"missing.plugin"}}}}""");
        graph.ResetHistory();
        var original = graph.ToProjectJson();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
        var edited = graph.ToProjectJson();
        graph.Undo();

        var path = Path.Combine(_directory, "application.zip");
        await File.WriteAllTextAsync(path, "previous bundle");
        var cancelled = new NativeStudioFileAccess(new Dialogs(), plugins);
        await cancelled.PublishApplicationAsync("application.zip", graph.ExportConfiguration(), 60);
        var files = new NativeStudioFileAccess(new Dialogs { SavePath = path }, plugins);
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            files.PublishApplicationAsync("application.zip", graph.ExportConfiguration(), 60));

        error.Message.ShouldContain("main.input");
        error.Message.ShouldContain("missing.plugin");
        (await File.ReadAllTextAsync(path)).ShouldBe("previous bundle");
        Directory.GetFiles(_directory, "*.tmp").ShouldBeEmpty();
        graph.ToProjectJson().ShouldBe(original);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(edited);
    }

    [Fact]
    public async Task Generate_App_cs_uses_unsaved_edits_and_preserves_history_and_previous_output()
    {
        using var plugins = new FluxFlow.Execution.Plugins.InstalledPlugins(
            new FluxFlow.Plugins.Logging.LoggingPlugin(), new FluxFlow.Plugins.Sources.SourcesPlugin());
        var graph = new DesignerGraphState(plugins.Modules.SelectMany(module => module.Description.Components));
        graph.LoadJson("""{"Workflows":{"main":{"record":{"Type":"logger"}}}}""");
        graph.ResetHistory();
        var before = graph.ExportConfiguration();
        graph.ApplyEdits(graph.PrepareEdits([new AddComponentEdit(ComponentAddress.Create("main", "unsaved"), "logger")]));
        var edited = graph.ExportConfiguration();
        var path = Path.Combine(_directory, "App.cs");
        var files = new NativeStudioFileAccess(new Dialogs { SavePath = path }, plugins);
        await files.GenerateApplicationSourceAsync(edited);
        var generated = await File.ReadAllTextAsync(path);
        generated.ShouldContain(""".Logger("unsaved", null, null)""");

        graph.Undo();
        graph.ExportConfiguration().ShouldBe(before);
        await new NativeStudioFileAccess(new Dialogs(), plugins).GenerateApplicationSourceAsync(before);
        var error = await Should.ThrowAsync<InvalidOperationException>(() => files.GenerateApplicationSourceAsync(
            """{"Workflows":{"main":{"retry":{"Type":"retry"}}}}"""));
        error.Message.ShouldContain("Install the plugin for 'retry' used by 'main.retry'");
        (await File.ReadAllTextAsync(path)).ShouldBe(generated);
        var executable = Path.Combine(_directory, "App.exe");
        var previousExecutable = await File.ReadAllBytesAsync(Environment.ProcessPath!);
        await File.WriteAllBytesAsync(executable, previousExecutable);
        await new NativeStudioFileAccess(new Dialogs(), plugins).PublishCompiledApplicationAsync(before);
        var compiled = new NativeStudioFileAccess(new Dialogs { SavePath = executable }, plugins);
        var publishError = await Should.ThrowAsync<InvalidOperationException>(() => compiled.PublishCompiledApplicationAsync(
            """{"Workflows":{"main":{"retry":{"Type":"retry"}}}}"""));
        publishError.Message.ShouldContain("Install the plugin for 'retry' used by 'main.retry'");
        var invalidCapacity = await Should.ThrowAsync<InvalidOperationException>(() => compiled.PublishCompiledApplicationAsync(
            """{"Workflows":{"main":{"tick":{"Type":"scheduler","Interval":"00:00:01","OutputCapacity":0}}}}"""));
        invalidCapacity.Message.ShouldContain("'tick.OutputCapacity' must be an integer between 1 and 2147483647");
        var invalidSourceCapacity = await Should.ThrowAsync<InvalidOperationException>(() => compiled.PublishCompiledApplicationAsync(
            """{"Workflows":{"main":{"orders":{"Type":"source","Values":"[]","OutputCapacity":0}}}}"""));
        invalidSourceCapacity.Message.ShouldContain("'orders.OutputCapacity' must be an integer between 1 and 2147483647");
        var invalidLoggerCapacity = await Should.ThrowAsync<InvalidOperationException>(() => compiled.PublishCompiledApplicationAsync(
            """{"Workflows":{"main":{"record":{"Type":"logger","Category":"Orders","OutputCapacity":0}}}}"""));
        invalidLoggerCapacity.Message.ShouldContain("'record.OutputCapacity' must be an integer between 1 and 2147483647");
        var invalidTarget = await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            compiled.PublishCompiledApplicationAsync(edited, targetMajor: 12));
        invalidTarget.ParamName.ShouldBe("targetMajor");
        (await File.ReadAllBytesAsync(executable)).ShouldBe(previousExecutable);
        Directory.GetFiles(_directory, "*.tmp").ShouldBeEmpty();
        graph.Redo();
        graph.ExportConfiguration().ShouldBe(edited);
    }

#if NET11_0_OR_GREATER
    // Both compiled targets use the .NET 11 SDK.
    [Theory]
    [InlineData(10, "win-x64", "Orders.exe")]
    [InlineData(11, "win-x64", "Orders.exe")]
    [InlineData(10, "linux-x64", "Orders")]
    [InlineData(11, "linux-x64", "Orders")]
    public async Task Compiled_publish_suggests_the_project_name_and_writes_only_the_chosen_path(int major, string runtime, string suggestion)
    {
        using var plugins = new FluxFlow.Execution.Plugins.InstalledPlugins(new FluxFlow.Plugins.Logging.LoggingPlugin());
        var graph = new DesignerGraphState(plugins.Modules.SelectMany(module => module.Description.Components));
        graph.LoadJson("""{"Workflows":{"main":{}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        graph.ApplyEdits(graph.PrepareEdits([new AddComponentEdit(ComponentAddress.Create("main", "unsaved"), "logger")]));
        var edited = graph.ToProjectJson();

        var dialogs = new Dialogs();
        await using var files = new NativeStudioFileAccess(dialogs, plugins);
        var target = new CompiledPublishTarget(major, runtime);
        await files.PublishCompiledApplicationAsync(graph.ExportConfiguration(), target, suggestion);
        dialogs.SuggestedName.ShouldBe(suggestion);
        dialogs.UseExtension.ShouldBe(!target.IsLinux);
        Directory.GetFiles(_directory).ShouldBeEmpty();

        dialogs.SavePath = Path.Combine(_directory, target.FileName("Nightly.Orders"));
        await files.PublishCompiledApplicationAsync(graph.ExportConfiguration(), target, suggestion);
        Directory.GetFiles(_directory).ShouldBe(new[] { dialogs.SavePath });
        var signature = File.ReadAllBytes(dialogs.SavePath);
        if (target.IsLinux) signature[..4].ShouldBe(new byte[] { 0x7f, 0x45, 0x4c, 0x46 });
        else signature[..2].ShouldBe(new byte[] { 0x4d, 0x5a });
        graph.ToProjectJson().ShouldBe(edited);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(edited);
    }
#endif

    [Fact]
    public async Task Closing_the_desktop_scope_cancels_the_publish_before_replacing_the_executable()
    {
        var executable = Path.Combine(_directory, "App.exe");
        var previous = await File.ReadAllBytesAsync(typeof(DesktopBoundaryTests).Assembly.Location);
        await File.WriteAllBytesAsync(executable, previous);
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(new FluxFlow.Execution.Plugins.InstalledPlugins(new FluxFlow.Plugins.Logging.LoggingPlugin()));
        builder.Services.AddScoped<IStudioFileAccess, NativeStudioFileAccess>();
        builder.Services.AddSingleton<IDesktopFileDialogs>(new Dialogs { SavePath = executable });
        using var host = builder.Build();
        var scope = host.Services.CreateAsyncScope();
        var files = scope.ServiceProvider.GetRequiredService<IStudioFileAccess>();
        var publish = files.PublishCompiledApplicationAsync("""{"Workflows":{"main":{"record":{"Type":"logger"}}}}""");
        await scope.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publish);
        (await File.ReadAllBytesAsync(executable)).ShouldBe(previous);
        Directory.GetFiles(_directory, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task Native_file_boundaries_preserve_project_configuration_and_history()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDesktopStudio(new FluxFlow.Execution.Plugins.InstalledPlugins());
        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"KeepCASE":{"Value":5},"Workflows":{"main":{}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        graph.SetConnection("service", "main", System.Text.Json.Nodes.JsonNode.Parse(
            """{"Type":"missing.connection","Address":"saved-device","Custom":{"Keep":true}}""")!.AsObject(), add: true);
        var after = graph.ToProjectJson();
        var dialogs = new Dialogs { SavePath = Path.Combine(_directory, "project.fluxflow") };
        var files = new NativeStudioFileAccess(dialogs);
        var saved = (await files.SaveAsync(new("project.fluxflow", after)))!;
        dialogs.OpenPath = saved.Location;
        (await files.OpenAsync(".fluxflow"))!.Content.ShouldBe(after);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(after);
    }

    [Fact]
    public async Task Settings_round_trip_and_failed_writes_keep_saved_defaults()
    {
        var path = Path.Combine(_directory, "settings", "settings.json");
        var storage = new NativeStudioSettingsStorage(path);
        (await storage.ReadAsync()).ShouldBeNull();
        var preferences = new StudioPreferences(storage);
        await preferences.SaveAsync(preferences.Value);
        var before = await File.ReadAllTextAsync(path);
        var reloaded = new StudioPreferences(new NativeStudioSettingsStorage(path));
        await reloaded.LoadAsync();
        await reloaded.SaveAsync(reloaded.Value);
        (await File.ReadAllTextAsync(path)).ShouldBe(before);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Should.ThrowAsync<UnauthorizedAccessException>(() => storage.WriteAsync("replacement"));
        (await File.ReadAllTextAsync(path)).ShouldBe(before);
    }

    [Theory]
    [InlineData(".png", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    public async Task Images_are_captured_before_the_source_changes(string extension, string mediaType)
    {
        var path = Path.Combine(_directory, "image" + extension);
        byte[] content = [1, 2, 3];
        await File.WriteAllBytesAsync(path, content);
        var files = new NativeStudioFileAccess(new Dialogs { OpenPath = path });
        var image = (await files.OpenImageAsync())!;
        await File.WriteAllBytesAsync(path, [4, 5, 6]);
        image.MediaType.ShouldBe(mediaType);
        Convert.FromBase64String(image.Base64).ShouldBe(content);
    }

    [Fact]
    public async Task Oversized_images_are_rejected()
    {
        var path = Path.Combine(_directory, "image.png");
        await File.WriteAllBytesAsync(path, new byte[2 * 1024 * 1024 + 1]);
        await Should.ThrowAsync<ArgumentException>(() => new NativeStudioFileAccess(new Dialogs { OpenPath = path }).OpenImageAsync());
    }

    [Fact]
    public async Task Desktop_can_run_without_Docker_and_host_shutdown_stops_the_execution()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDesktopStudio(new FluxFlow.Execution.Plugins.InstalledPlugins());
        var host = builder.Build();
        await host.StartAsync();
        var runner = host.Services.GetRequiredService<IEnvironmentRunner>();
        var id = Guid.NewGuid();
        var rerun = Guid.NewGuid();
        var request = new EnvironmentRunRequest("""{"Workflows":{"main":{}}}""", new());
        try
        {
            (await runner.StartAsync(id, request, CancellationToken.None)).State.ShouldBe("Running");
            await runner.StopAsync(id, CancellationToken.None);
            (await runner.GetAsync(id, CancellationToken.None)).State.ShouldBe("Stopped");
            await Should.ThrowAsync<ArgumentException>(() => runner.StartAsync(id, request, CancellationToken.None));
            (await runner.StartAsync(rerun, request, CancellationToken.None)).State.ShouldBe("Running");
            (await runner.GetAsync(id, CancellationToken.None)).State.ShouldBe("Stopped");
        }
        finally { await DesktopHostLifetime.StopAsync(host); }
        (await runner.GetAsync(id, CancellationToken.None)).State.ShouldBe("Stopped");
        (await runner.GetAsync(rerun, CancellationToken.None)).State.ShouldBe("Stopped");
        await Should.ThrowAsync<InvalidOperationException>(() => runner.StartAsync(Guid.NewGuid(), request, CancellationToken.None));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_cleanup_always_disposes_and_preserves_failures(bool stopFails)
    {
        var host = new FailingHost(stopFails);
        var error = await Should.ThrowAsync<AggregateException>(() => DesktopHostLifetime.StopAsync(host));
        host.Calls.ShouldBe(new[] { "Stop", "DisposeAsync" });
        error.InnerExceptions.ShouldBe(stopFails
            ? new Exception[] { host.StopError, host.DisposeError }
            : new Exception[] { host.DisposeError });
    }

    private sealed class FailingHost(bool stopFails) : IHost, IAsyncDisposable
    {
        public List<string> Calls { get; } = [];
        public Exception StopError { get; } = new InvalidOperationException("Stop failed");
        public Exception DisposeError { get; } = new IOException("Dispose failed");
        public IServiceProvider Services => throw new NotSupportedException();
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("Stop");
            return stopFails ? Task.FromException(StopError) : Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            Calls.Add("DisposeAsync");
            return ValueTask.FromException(DisposeError);
        }
        public void Dispose() => throw new InvalidOperationException("Async disposal should be used.");
    }

    private sealed class Dialogs : IDesktopFileDialogs
    {
        public string? OpenPath { get; set; }
        public string? SavePath { get; set; }
        public string? SuggestedName { get; private set; }
        public int SaveCalls { get; private set; }
        public string? OpenExtensions { get; private set; }
        public Task<string?> OpenAsync(string extensions, string? title = null) { OpenExtensions = extensions; return Task.FromResult(OpenPath); }
        public bool UseExtension { get; private set; }
        public Task<string?> SaveAsync(string name, bool useExtension = true) { SaveCalls++; SuggestedName = name; UseExtension = useExtension; return Task.FromResult(SavePath); }
    }
}
