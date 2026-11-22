extern alias serial;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using FluxFlow.Core.Mapping;
using FluxFlow.Execution;
using FluxFlow.Execution.Plugins;
using FluxFlow.Plugins;
using FluxFlow.Plugins.Management;
using FluxFlow.Studio.UI;
using FluxFlow.Studio.UI.Features.Designer;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class InstalledPluginTests
{
    private static void CopyDesignPayload(string folder)
    {
        foreach (var tfm in new[] { "net8.0", "net10.0" })
        {
            var binaries = Directory.CreateDirectory(Path.Combine(folder, "lib", tfm));
            foreach (var dependency in new[] { typeof(FluxFlow.Components.Serial.SerialFrameOptions).Assembly, typeof(FluxFlow.Environments.ISerialPortDiscovery).Assembly })
                File.Copy(dependency.Location, Path.Combine(binaries.FullName, dependency.GetName().Name + ".dll"));

        }
    }

    [Fact]
    public void Base_Studio_and_companion_have_no_transitive_plugin_driver_dependency()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var remaining = new Queue<System.Reflection.Assembly>([typeof(DesignerGraphState).Assembly, typeof(InstalledPlugins).Assembly]);
        while (remaining.TryDequeue(out var assembly))
        {
            if (!seen.Add(assembly.FullName!)) continue;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name!;
                name.ShouldNotBe("System.IO.Ports");
                name.ShouldNotBe("MQTTnet");
                name.ShouldNotBe("Bogus");
                name.ShouldNotBe("Cronos");
                name.ShouldNotStartWith("FluxFlow.Plugins.Sources");
                name.ShouldNotBe("FluxFlow.Components.Source");
                name.ShouldNotBe("FluxFlow.Components.Scheduler");
                name.ShouldNotBe("FluxFlow.Components.DataGeneration");
                name.ShouldNotStartWith("NATS.");
                if (!name.StartsWith("FluxFlow.", StringComparison.Ordinal)) continue;
                name.ShouldNotContain(".Serial");
                name.ShouldNotContain(".Mqtt");
                name.ShouldNotContain("RabbitMQ");
                name.ShouldNotContain(".Nats");
                remaining.Enqueue(System.Reflection.Assembly.Load(reference));
            }
        }
    }

    [Fact]
    public void Opening_and_saving_with_plugin_removed_preserves_workflow_for_reinstall()
    {
        var plugin = new serial::FluxFlow.Plugins.Serial.SerialPlugin();
        var wire = JsonSerializer.Serialize(new[] { plugin.Description }, PluginJsonContext.Default.PluginDescriptionArray);
        var descriptions = JsonSerializer.Deserialize(wire, PluginJsonContext.Default.PluginDescriptionArray)!;
        var sample = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "serial.fluxflow")))!;
        sample["Links"]!["device"] = JsonNode.Parse("""
            [{"Source":"device.read.Output","Target":"device.format.Input","IsError":false,"SourceSide":"Left","TargetSide":"Right"}]
            """);
        var saved = sample.ToJsonString();
        var expected = sample["Configuration"];
        string? canonical = null;
        foreach (var installed in new[] { true, false, true })
        {
            using var provider = new ServiceCollection().AddStudioUI(installed ? descriptions : []).BuildServiceProvider();
            var graph = new DesignerGraphState(provider.GetServices<IComponentDescription>());
            graph.LoadProject(saved);
            graph.ResetHistory(); // Same baseline Studio establishes after Open.
            if (installed) graph.FindDescription("serial.reader")!.Documentation.ShouldContain("## Ports");
            else graph.FindDescription("serial.reader").ShouldBeNull();
            graph.CanUndo.ShouldBeFalse();
            saved = graph.ToProjectJson();
            canonical ??= saved;
            JsonNode.DeepEquals(JsonNode.Parse(saved), JsonNode.Parse(canonical)).ShouldBeTrue();
            graph.SetPortColorOverrides(new(Output: "#123456"));
            var edited = graph.ToProjectJson();
            graph.Undo();
            JsonNode.DeepEquals(JsonNode.Parse(graph.ToProjectJson()), JsonNode.Parse(saved)).ShouldBeTrue();
            graph.Redo();
            JsonNode.DeepEquals(JsonNode.Parse(graph.ToProjectJson()), JsonNode.Parse(edited)).ShouldBeTrue();
            JsonNode.DeepEquals(JsonNode.Parse(saved)!["Configuration"], expected).ShouldBeTrue();
        }
    }

    [Fact]
    public void Plugin_functions_are_scoped_to_the_installed_workflow_engine()
    {
        using var installed = new InstalledPlugins(new MathPlugin());
        using var services = WorkflowExecution.RegisterComponents(new ServiceCollection(), installed).BuildServiceProvider();
        var expression = services.GetRequiredKeyedService<IFlowExpressionEngine>("jsonata");
        expression.Compile<int>("$trialDouble(21)")(new FlowMapContext()).ShouldBe(42);
        using var empty = new InstalledPlugins();
        using var other = WorkflowExecution.RegisterComponents(new ServiceCollection(), empty).BuildServiceProvider();
        Should.Throw<Exception>(() => other.GetRequiredKeyedService<IFlowExpressionEngine>("jsonata")
            .Compile<int>("$trialDouble(21)")(new FlowMapContext()));
    }

    [Fact]
    public void Active_hosts_share_a_store_but_installation_needs_exclusive_access()
    {
        var root = Directory.CreateTempSubdirectory("fluxflow-plugin-store-");
        var lockFile = Path.Combine(root.FullName, ".store.lock");
        try
        {
            using (var first = InstalledPlugins.Load(root.FullName))
            using (var second = InstalledPlugins.Load(root.FullName))
            {
                first.Modules.ShouldBeEmpty();
                second.Modules.ShouldBeEmpty();
                Should.Throw<IOException>(() => File.Open(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose());
            }
            using (File.Open(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.WriteAllText(Path.Combine(root.FullName, "Test.Module.installed"), "../other");
            Should.Throw<InvalidDataException>(() => InstalledPlugins.Load(root.FullName));
            using (File.Open(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void Closing_plugin_host_releases_payload_for_removal_and_reinstall()
    {
        const string id = "FluxFlow.Plugins.Serial";
        var root = Directory.CreateTempSubdirectory("fluxflow-plugin-close-");
        try
        {
            for (var i = 0; i < 2; ++i)
            {
                var folder = Path.Combine(root.FullName, "active", id);
                var framework = System.Environment.Version.Major >= 10 ? "net10.0" : "net8.0";
                var binaries = Directory.CreateDirectory(Path.Combine(folder, "lib", framework));
                File.Copy(typeof(serial::FluxFlow.Plugins.Serial.SerialPlugin).Assembly.Location, Path.Combine(binaries.FullName, id + ".dll"));
                CopyDesignPayload(folder);
                File.WriteAllText(Path.Combine(folder, "fluxflow-plugin.json"),
                    """{"contract":1,"id":"FluxFlow.Plugins.Serial","entryType":"FluxFlow.Plugins.Serial.SerialPlugin"}""");
                using (var host = InstalledPlugins.Load(root.FullName))
                {
                    var descriptor = host.Descriptions.Single();
                    descriptor.Id.ShouldBe(id);
                    descriptor.Design.ShouldNotBeNull();
                    descriptor.StudioEntry.ShouldBeNull();
                    host.StudioAssembly(id).ShouldBeNull();
                    descriptor.ClientAssemblies.Count().ShouldBe(2);
                    foreach (var name in descriptor.ClientAssemblies)
                    {
                        var desktopAssembly = host.StudioAssembly(id, dependency: name);
                        File.Exists(desktopAssembly).ShouldBeTrue(desktopAssembly);
                        Path.GetFileName(Path.GetDirectoryName(desktopAssembly)).ShouldBe(framework);
                        File.Exists(host.StudioAssembly(id, browser: true, dependency: name)).ShouldBeTrue();
                    }
                    host.StudioAssembly(id, dependency: "../outside").ShouldBeNull();
                    host.StudioAssembly(id, dependency: "FluxFlow.Components.Serial").ShouldBeNull();
                }
                Directory.Delete(folder, recursive: true);
                using var absent = InstalledPlugins.Load(root.FullName);
                absent.Modules.ShouldBeEmpty();
            }
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public async Task Managed_changes_wait_for_all_hosts_and_can_be_cancelled()
    {
        const string id = "FluxFlow.Plugins.Serial";
        var trial = Directory.CreateTempSubdirectory("fluxflow-plugin-changes-");
        var store = Path.Combine(trial.FullName, "store");
        var source = Path.Combine(trial.FullName, "source");
        try
        {
            foreach (var tfm in new[] { "net8.0", "net10.0" })
            {
                var binaries = Directory.CreateDirectory(Path.Combine(source, "lib", tfm));
                File.Copy(typeof(serial::FluxFlow.Plugins.Serial.SerialPlugin).Assembly.Location, Path.Combine(binaries.FullName, id + ".dll"));
            }
            CopyDesignPayload(source);
            File.WriteAllText(Path.Combine(source, "fluxflow-plugin.json"),
                """{"contract":1,"id":"FluxFlow.Plugins.Serial","version":"1.0.0","entryType":"FluxFlow.Plugins.Serial.SerialPlugin"}""");
            using (var host = InstalledPlugins.Load(store))
            {
                var manager = host.Manager!;
                (await manager.ListAsync()).ShouldBeEmpty();
                await manager.InstallAsync(source);
                using (var concurrent = InstalledPlugins.Load(store)) concurrent.Modules.ShouldBeEmpty();
                (await manager.ListAsync()).Single().ShouldBe(new PluginEntry(id, "1.0.0", PluginState.PendingInstall) { Source = "Folder" });
                await manager.CancelAsync(id);
                (await manager.ListAsync()).ShouldBeEmpty();
                await manager.InstallAsync(source);
            }
            // A second companion can still be finishing a list or stage operation during restart.
            Task<InstalledPlugins> restart;
            using (File.Open(Path.Combine(store, ".changes.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                restart = Task.Run(() => InstalledPlugins.Load(store));
                await Task.WhenAny(restart, Task.Delay(TimeSpan.FromSeconds(2)));
            }
            using (var restarted = await restart)
            {
                var manager = restarted.Manager!;
                restarted.Descriptions.Single().Id.ShouldBe(id);
                (await manager.ListAsync()).Single().State.ShouldBe(PluginState.Active);
                await manager.RemoveAsync(id);
                (await manager.ListAsync()).Single().State.ShouldBe(PluginState.PendingRemoval);
                await manager.CancelAsync(id);
                (await manager.ListAsync()).Single().State.ShouldBe(PluginState.Active);
                await manager.RemoveAsync(id);
                using var concurrent = InstalledPlugins.Load(store);
                concurrent.Descriptions.Single().Id.ShouldBe(id);
            }
            using var removed = InstalledPlugins.Load(store);
            removed.Modules.ShouldBeEmpty();
            (await removed.Manager!.ListAsync()).ShouldBeEmpty();
        }
        finally { trial.Delete(recursive: true); }
    }

    [Fact]
    public async Task Starting_companion_waits_for_a_finishing_exclusive_store_write()
    {
        var store = Directory.CreateTempSubdirectory("fluxflow-plugin-start-");
        try
        {
            Task<InstalledPlugins> starting;
            using (File.Open(Path.Combine(store.FullName, ".store.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                starting = Task.Run(() => InstalledPlugins.Load(store.FullName));
                await Task.WhenAny(starting, Task.Delay(200));
            }
            using var host = await starting;
            host.Modules.ShouldBeEmpty();
        }
        finally { store.Delete(recursive: true); }
    }

    private sealed class MathPlugin : IFluxFlowPlugin
    {
        public PluginDescription Description => new("Test.Math", [], []);
        public void ConfigureWorkflow(IServiceCollection services) { }
        public IReadOnlyList<PluginFunction> Functions => [new("trialDouble", (Func<int,int>)(value => value * 2))];
    }
}
