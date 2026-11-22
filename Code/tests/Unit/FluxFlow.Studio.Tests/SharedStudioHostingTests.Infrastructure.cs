using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using FluxFlow.Studio.UI.Features.Environments.Infrastructure;
using FluxFlow.Studio.UI.Features.Environments.Topology;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Extensions;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Theory]
    [InlineData("unchanged")]
    [InlineData("repeat-reset")]
    [InlineData("switch-back")]
    [InlineData("remove-undo")]
    [InlineData("newer-color")]
    [InlineData("replace-project")]
    public async Task Topology_color_callbacks_preserve_current_selection_and_history(string action)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "host", Name = "Host", Kind = EnvironmentNodeKind.Host },
            new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application, Host = "host",
                Color = action == "repeat-reset" ? null : "#aB1234" }
        ] });
        graph.AddEnvironment("Other");
        var other = graph.ActiveEnvironmentId;
        graph.SelectEnvironment("local");
        graph.ResetHistory();
        graph.RenameEnvironment("For redo"); graph.Undo();

        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<EnvironmentTopologyPage>();
            var diagram = components.Items.OfType<CascadingValue<BlazorDiagram>>().Single().Value!;
            void SelectApp() => diagram.SelectModel(diagram.Nodes.OfType<TopologyNodeModel>().Single(n => n.Definition.Id == "app"), true);
            SelectApp();
            var callback = components.Items.OfType<TopologyInspector>().Last().SetColor;
            switch (action)
            {
                case "switch-back":
                    graph.SelectEnvironment(other); graph.SelectEnvironment("local"); SelectApp();
                    break;
                case "remove-undo":
                    var topology = graph.GetEnvironmentDefinition().Topology;
                    topology.Nodes.RemoveAll(n => n.Id == "app");
                    graph.SetTopology(topology); graph.Undo(); SelectApp();
                    break;
                case "newer-color":
                    await callback.InvokeAsync("#445566");
                    break;
                case "replace-project":
                    graph.LoadProject(graph.ToProjectJson()); SelectApp();
                    break;
            }
            var before = graph.ToProjectJson();
            var revision = graph.Revision;
            var canUndo = graph.CanUndo;
            var canRedo = graph.CanRedo;
            await callback.InvokeAsync(action == "unchanged" ? "#Ab1234" : null);
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBe(canUndo);
            graph.CanRedo.ShouldBe(canRedo);

            // An old callback must not prevent subsequent edits through the current Inspector.
            await components.Items.OfType<TopologyInspector>().Last().SetColor.InvokeAsync("#987654");
            var after = graph.ToProjectJson();
            graph.GetEnvironmentDefinition().Topology.Nodes.Single(n => n.Id == "app").Color.ShouldBe("#987654");
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Fact]
    public async Task Startup_timeout_edits_only_the_current_environment_and_preserves_redo_after_rejected_edits()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://manual"}},"Workflows":{"main":{}}}""");
        var first = graph.ActiveEnvironmentId;
        graph.AddEnvironment("Test");
        var second = graph.ActiveEnvironmentId;
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = new() { Files = new() { ["custom.conf"] = "# keep\r\n" } } } });
        graph.SelectEnvironment(first);
        graph.ResetHistory();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<EnvironmentTopologyPage>();
            EventCallback<int> Field() => components.Items.OfType<MudBlazor.MudNumericField<int>>().Last(field =>
                field.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == "Startup timeout (seconds)").ValueChanged;
            MudBlazor.MudInput<string> Input() => components.Items.OfType<MudBlazor.MudInput<string>>().Last(input =>
                input.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == "Startup timeout (seconds)");

            var oldFirst = Field();
            graph.SelectEnvironment(second);
            var initial = graph.ToProjectJson();
            await oldFirst.InvokeAsync(30);
            graph.ToProjectJson().ShouldBe(initial);
            graph.SelectEnvironment(first);
            await oldFirst.InvokeAsync(30); // Same environment ID, but the original Inspector has closed.
            graph.GetEnvironment().StartupTimeoutSeconds.ShouldBe(60);
            Input().Value.ShouldBe("60");
            graph.SelectEnvironment(second);
            var revision = graph.Revision;
            foreach (var text in new[] { "", "abc", "1.5", "2147483648", "0", "601", "60" })
                await Input().SetText(text);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();
            graph.ToProjectJson().ShouldBe(initial);

            await Input().SetText("1");
            graph.GetEnvironment().StartupTimeoutSeconds.ShouldBe(1);
            var lowerBound = graph.ToProjectJson();
            var edit = Field();
            await Input().SetText("600");
            graph.GetEnvironment().StartupTimeoutSeconds.ShouldBe(600);
            graph.Environments.Single(env => env.Id == first).Setup.StartupTimeoutSeconds.ShouldBe(60);
            var saved = graph.ToProjectJson();
            var reopened = StudioTestServices.CreateGraph();
            reopened.LoadProject(saved);
            reopened.ToProjectJson().ShouldBe(saved);
            reopened.GetEnvironment().Resources["Resources:broker"].Files["custom.conf"].ShouldBe("# keep\r\n");

            graph.Undo();
            await edit.InvokeAsync(250); // The edit predates Undo.
            await Field().InvokeAsync(1);
            await Field().InvokeAsync(-1);
            graph.ToProjectJson().ShouldBe(lowerBound);
            Input().Value.ShouldBe("1");
            graph.CanRedo.ShouldBeTrue();
            graph.Undo();
            graph.ToProjectJson().ShouldBe(initial);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(lowerBound);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(saved);
            Input().Value.ShouldBe("600");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_on_moves_the_app_between_host_copies_without_moving_its_binding(bool legacyFrame)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://manual"}},"Workflows":{"main":{}}}""");
        var configuration = graph.ExportConfiguration();
        var source = new InfrastructureHost { Name = "Lab", Services = [new() { Name = "Broker", Endpoint = "mqtt://host:1883" }] };
        var template = source.CreateTopology();
        var root = template.Nodes.Single(n => n.Kind == EnvironmentNodeKind.Host);
        if (legacyFrame) { root.Width = null; root.Height = null; }
        template.Nodes.Add(new() { Name = "Legacy network", Kind = EnvironmentNodeKind.Network, Host = root.Id });
        source = source with { Topology = template };
        var topology = new EnvironmentTopology();
        var first = topology.PlaceHost(source, 0, 0);
        var second = topology.PlaceHost(source, 650, 0);
        topology.Nodes.Single(n => n.Id == first).PlacementScale = .75;
        topology.Nodes.Single(n => n.Id == second).PlacementScale = 1.5;
        var broker = topology.Nodes.Single(n => n.Host == first && n.ExistingService is not null).Id;
        var network = topology.Nodes.Single(n => n.Host == first && n.Kind == EnvironmentNodeKind.Network).Id;
        topology.Nodes.Add(new() { Id = "shared", Name = "Shared", Kind = EnvironmentNodeKind.Network });
        topology.Nodes.Add(new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application, Host = first,
            X = 40, Y = 80, Networks = [network, "shared"], Bindings = new() { ["Resources:broker"] = broker } });
        graph.SetTopology(topology);
        graph.ResetHistory();
        var original = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<EnvironmentTopologyPage>();
            var diagram = components.Items.OfType<CascadingValue<BlazorDiagram>>().Single().Value!;
            TopologyNodeModel App() => diagram.Nodes.OfType<TopologyNodeModel>().Single(n => n.Definition.Id == "app");
            diagram.SelectModel(App(), true);
            var selector = components.Items.OfType<MudBlazor.MudSelect<string>>().Last(field =>
                field.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == "Run on");
            selector.ToStringFunc!(first).ShouldNotBe(selector.ToStringFunc!(second));
            var choose = selector.ValueChanged;
            await choose.InvokeAsync(first);
            await choose.InvokeAsync("removed");
            await choose.InvokeAsync(broker);
            await choose.InvokeAsync("");
            graph.CanUndo.ShouldBeFalse();
            graph.ToProjectJson().ShouldBe(original);
            await choose.InvokeAsync(second);
            App().Container!.Definition.Id.ShouldBe(second);
            App().Position.X.ShouldBeGreaterThan(650);
            App().ConnectionPorts.Single().InitialTargetId.ShouldBe(broker);
            var saved = graph.ToProjectJson();
            var reopened = StudioTestServices.CreateGraph();
            reopened.LoadProject(saved);
            reopened.ToProjectJson().ShouldBe(saved);
            reopened.ExportConfiguration().ShouldBe(configuration);
            var restored = reopened.GetEnvironmentDefinition().Topology;
            var app = restored.Nodes.Single(n => n.Id == "app");
            app.Host.ShouldBe(second);
            app.Bindings["Resources:broker"].ShouldBe(broker);
            app.Networks.ShouldBe(new[] { "shared" });
            app.X.ShouldBeGreaterThan(650);
            System.Text.Json.JsonSerializer.Serialize(restored.Nodes.Where(n => n.Id != "app"))
                .ShouldBe(System.Text.Json.JsonSerializer.Serialize(topology.Nodes.Where(n => n.Id != "app")));
            graph.Undo();
            await choose.InvokeAsync(second); // This choice predates Undo, even though both copies still exist.
            graph.ToProjectJson().ShouldBe(original);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(saved);
        });
    }

    [Fact]
    public async Task This_computer_transfers_between_placed_copies_as_one_edit_and_ignores_stale_clicks()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://manual"}},"Workflows":{"main":{}}}""");
        var source = new InfrastructureHost { Services = [new() { Name = "Broker", Endpoint = "mqtt://host:1883" }] };
        var topology = new EnvironmentTopology();
        var first = topology.PlaceHost(source, 0, 0);
        var second = topology.PlaceHost(source, 650, 0);
        var broker = topology.Nodes.Single(node => node.Host == second).Id;
        topology.Nodes.Add(new() { Id = "app", Name = "App", Kind = EnvironmentNodeKind.Application,
            Host = first, Bindings = new() { ["Resources:broker"] = broker } });
        graph.SetTopology(topology);
        graph.ResetHistory();
        var original = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();

        string CheckAndReopen(string? expectedHost)
        {
            var saved = graph.ToProjectJson();
            var reopened = StudioTestServices.CreateGraph();
            reopened.LoadProject(saved);
            reopened.ToProjectJson().ShouldBe(saved);
            reopened.ExportConfiguration().ShouldBe(configuration);
            var expected = topology with { Nodes = topology.Nodes.Select(n => n with { LocalHost = n.Id == expectedHost }).ToList() };
            System.Text.Json.JsonSerializer.Serialize(reopened.GetEnvironmentDefinition().Topology)
                .ShouldBe(System.Text.Json.JsonSerializer.Serialize(expected));
            return saved;
        }

        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<EnvironmentTopologyPage>();
            var page = components.Items.OfType<EnvironmentTopologyPage>().Single();
            var diagram = components.Items.OfType<CascadingValue<BlazorDiagram>>().Single().Value!;
            EventCallback<bool> Checkbox(string hostId)
            {
                diagram.SelectModel(diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.Id == hostId), true);
                return components.Items.OfType<MudBlazor.MudCheckBox<bool>>().Last(box =>
                    box.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == "Use this host as the companion computer").ValueChanged;
            }
            await Checkbox(first).InvokeAsync(true);
            var onFirst = CheckAndReopen(first);
            var transfer = Checkbox(second);
            await transfer.InvokeAsync(false); // Already clear; first must remain local.
            var revision = graph.Revision;
            await transfer.InvokeAsync(true);
            var onSecond = CheckAndReopen(second);
            graph.Revision.ShouldBe(revision + 1);
            await Checkbox(second).InvokeAsync(true);
            graph.Revision.ShouldBe(revision + 1);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(onFirst);
            await transfer.InvokeAsync(true); // The same host is still selected after Undo.
            graph.ToProjectJson().ShouldBe(onFirst);
            var delayedClick = Checkbox(second);
            await page.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(EnvironmentTopologyPage.Hidden)] = true }));
            await delayedClick.InvokeAsync(true); // The connection Inspector now covers the topology.
            graph.ToProjectJson().ShouldBe(onFirst);
            graph.CanRedo.ShouldBeTrue();
            await page.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(EnvironmentTopologyPage.Hidden)] = false }));
            graph.Redo();
            graph.ToProjectJson().ShouldBe(onSecond);
            await Checkbox(second).InvokeAsync(false);
            CheckAndReopen(null).ShouldBe(original);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(onSecond);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(original);
            Should.Throw<ArgumentException>(() => graph.SetLocalExecutionHost("removed", true));
            Should.Throw<ArgumentException>(() => graph.SetLocalExecutionHost(broker, true));
            graph.ToProjectJson().ShouldBe(original);
        });
    }

    [Fact]
    public async Task Topology_binding_editors_agree_on_the_second_snapshot_unbound_and_fallback()
    {
        const string path = "Resources:broker";
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://manual"}},"Workflows":{"main":{}}}""");
        var configuration = graph.ExportConfiguration();
        var topology = new EnvironmentTopology { Nodes = [
            new() { Kind = EnvironmentNodeKind.Application, Id = "app", Name = "App" },
            new() { Kind = EnvironmentNodeKind.Service, Id = "fallback", Name = "Configured broker", Resource = path }
        ] };
        var source = new InfrastructureHost { Services = [new() { Name = "Broker", Endpoint = "mqtt://host:1883" }] };
        var first = topology.PlaceHost(source, 0, 0);
        var firstTarget = topology.Nodes.Single(n => n.Host == first).Id;
        var second = topology.PlaceHost(source, 650, 0);
        var target = topology.Nodes.Single(n => n.Host == second).Id;
        graph.SetTopology(topology);
        graph.ResetHistory();
        var fallback = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<EnvironmentTopologyPage>();
            var diagram = components.Items.OfType<CascadingValue<BlazorDiagram>>().Single().Value!;
            TopologyNodeModel Node(string id) => diagram.Nodes.OfType<TopologyNodeModel>().Single(n => n.Definition.Id == id);
            void Capture() => graph.SetTopology(EnvironmentTopologyPage.CaptureTopology(diagram, graph.GetEnvironmentDefinition().Topology, graph.Connections));
            MudBlazor.MudSelect<TopologyBinding> Selector()
            {
                diagram.SelectModel(Node("app"), true);
                return components.Items.OfType<MudBlazor.MudSelect<TopologyBinding>>().Last();
            }
            Task Choose(TopologyBinding binding) => Selector().ValueChanged.InvokeAsync(binding);
            string RoundTrip()
            {
                var json = graph.ToProjectJson();
                var reopened = StudioTestServices.CreateGraph();
                reopened.LoadProject(json);
                reopened.ToProjectJson().ShouldBe(json);
                reopened.ExportConfiguration().ShouldBe(configuration);
                return json;
            }

            Capture();
            graph.CanUndo.ShouldBeFalse();
            graph.ToProjectJson().ShouldBe(fallback);
            diagram.Links.Clear();
            diagram.Links.Add(new LinkModel(new SinglePortAnchor(Node("app").ConnectionPorts.Single()),
                new SinglePortAnchor(Node(target).ConnectionPorts.Single())));
            Capture();
            var bound = RoundTrip();
            graph.GetEnvironmentDefinition().Topology.Nodes.Single(n => n.Id == "app").Bindings[path].ShouldBe(target);
            var rev = graph.Revision;
            await Choose(TopologyBinding.Explicit("removed"));
            await Choose(TopologyBinding.Explicit(second)); // A host is not a service.
            graph.Revision.ShouldBe(rev);
            var staleChoice = Selector().ValueChanged;
            var stalePort = Node(target).ConnectionPorts.Single();
            graph.Undo(); graph.ToProjectJson().ShouldBe(fallback);
            await staleChoice.InvokeAsync(TopologyBinding.Explicit(firstTarget));
            graph.ToProjectJson().ShouldBe(fallback);
            var label = Selector().ToStringFunc!;
            label(TopologyBinding.Explicit(target)).ShouldNotBe(label(TopologyBinding.Explicit(firstTarget)));
            var withoutSecond = graph.GetEnvironmentDefinition().Topology;
            withoutSecond.Nodes.RemoveAll(n => n.InfrastructureGroup == second);
            graph.SetTopology(withoutSecond);
            var afterRemoval = graph.ToProjectJson();
            var afterRemovalRevision = graph.Revision;
            await staleChoice.InvokeAsync(TopologyBinding.Explicit(target));
            await Choose(TopologyBinding.Explicit(target));
            diagram.Links.Clear();
            diagram.Links.Add(new LinkModel(new SinglePortAnchor(Node("app").ConnectionPorts.Single()), new SinglePortAnchor(stalePort)));
            Should.Throw<ArgumentException>(Capture);
            graph.Revision.ShouldBe(afterRemovalRevision);
            graph.ToProjectJson().ShouldBe(afterRemoval);
            graph.Undo(); graph.ToProjectJson().ShouldBe(fallback);
            diagram.Links.Clear();
            diagram.Links.Add(new LinkModel(new SinglePortAnchor(Node("app").ConnectionPorts.Single()), new SinglePortAnchor(stalePort)));
            Should.Throw<ArgumentException>(Capture); // Undo restored the service ID, but not the old port.
            await Choose(TopologyBinding.Explicit(target));
            graph.ToProjectJson().ShouldBe(bound);

            diagram.Links.Clear();
            Capture();
            var unbound = RoundTrip();
            graph.GetEnvironmentDefinition().Topology.Nodes.Single(n => n.Id == "app").Bindings[path].ShouldBeNull();
            graph.Undo(); graph.ToProjectJson().ShouldBe(bound);
            await Choose(TopologyBinding.Explicit(null));
            graph.ToProjectJson().ShouldBe(unbound);
            await Choose(TopologyBinding.Configured);
            RoundTrip().ShouldBe(fallback);
            diagram.Links.Count.ShouldBe(1);
            graph.Undo(); graph.ToProjectJson().ShouldBe(unbound);
            graph.Redo(); graph.ToProjectJson().ShouldBe(fallback);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Topology_hides_network_editing_but_preserves_saved_memberships_and_history(bool infrastructureMode, bool lockedSnapshot)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883"}},"Workflows":{"main":{}}}""");
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "host", Name = "Host", Kind = EnvironmentNodeKind.Host, Width = 700, Height = 500 },
            new() { Id = "network", Name = "Saved LAN", Kind = EnvironmentNodeKind.Network, Host = "host", X = 30, Y = 80, Width = 600, Height = 350, Color = "#123456" },
            new() { Id = "broker", Name = "Broker", Kind = EnvironmentNodeKind.Service, Host = "host", Networks = ["network"],
                X = 60, Y = 170, ExistingService = new() { Name = "Broker", Endpoint = "mqtt://host:1883" } }
        ] });
        if (lockedSnapshot)
        {
            var topology = graph.GetEnvironmentDefinition().Topology;
            graph.SetTopology(topology with { Nodes = topology.Nodes.Select(node => node with
                { InfrastructureGroup = "host", PlacementScale = node.Id == "host" ? .5 : null }).ToList() });
        }
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        var savedNetwork = System.Text.Json.JsonSerializer.Serialize(graph.GetEnvironmentDefinition().Topology.Nodes[1]);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<EnvironmentTopologyPage>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(EnvironmentTopologyPage.InfrastructureMode)] = infrastructureMode }));
            var diagram = components.Items.OfType<CascadingValue<BlazorDiagram>>().Single().Value!;
            diagram.Nodes.Count.ShouldBe(2);
            var palette = components.Items.OfType<ComponentPalette>().Single();
            palette.Items.ShouldNotContain(item => item.ComponentType == "network");
            var broker = diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.Id == "broker");
            broker.Container!.Definition.Id.ShouldBe("host");
            broker.Ports.ShouldAllBe(port => port is TopologyConnectionPort);
            diagram.SelectModel(broker, true);
            view.ToHtmlString().ShouldNotContain("topology-port");
            view.ToHtmlString().ShouldNotContain(">Networks<");
            view.ToHtmlString().ShouldNotContain("Saved LAN");
            graph.ToProjectJson().ShouldBe(before);
            broker.SetPosition(150, 220);
            graph.SetTopology(EnvironmentTopologyPage.CaptureTopology(diagram, graph.GetEnvironmentDefinition().Topology));
            var moved = graph.ToProjectJson();
            graph.GetEnvironmentDefinition().Topology.Nodes.Single(node => node.Id == "broker").Networks.ShouldBe(new[] { "network" });
            System.Text.Json.JsonSerializer.Serialize(graph.GetEnvironmentDefinition().Topology.Nodes[1]).ShouldBe(savedNetwork);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(moved);

            diagram.SelectModel(diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.Id == "host"), true);
            await palette.OnSelect.InvokeAsync(infrastructureMode ? "existing:mqtt.client" : "application");
            var added = graph.GetEnvironmentDefinition().Topology.Nodes.Last();
            added.Host.ShouldBe("host");
            added.Networks.ShouldBeEmpty();
            System.Text.Json.JsonSerializer.Serialize(graph.GetEnvironmentDefinition().Topology.Nodes[1]).ShouldBe(savedNetwork);
            var afterAdd = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(moved);
            graph.Redo(); graph.ToProjectJson().ShouldBe(afterAdd);
            if (!infrastructureMode)
            {
                diagram.SelectModel(diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.Id == added.Id), true);
                await components.Items.OfType<TopologyInspector>().Last().Bind.InvokeAsync(("Resources:broker", TopologyBinding.Explicit("broker")));
                graph.GetEnvironmentDefinition().Topology.Nodes.Last().Bindings["Resources:broker"].ShouldBe("broker");
                graph.Undo(); graph.ToProjectJson().ShouldBe(afterAdd);
                graph.Redo();
            }
            var reopened = StudioTestServices.CreateGraph();
            reopened.LoadProject(graph.ToProjectJson());
            reopened.ToProjectJson().ShouldBe(graph.ToProjectJson());
            graph.ExportConfiguration().ShouldBe(configuration);
            if (lockedSnapshot)
            {
                var beforeRemoval = graph.ToProjectJson();
                diagram.SelectModel(diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.Id == "host"), true);
                await components.Items.OfType<StudioIconButton>().Single(button => button.Label == "Remove from topology").OnClick.InvokeAsync();
                graph.GetEnvironmentDefinition().Topology.Nodes.ShouldAllBe(node => node.Kind == EnvironmentNodeKind.Application);
                graph.GetEnvironmentDefinition().Topology.Validate();
                graph.Undo(); graph.ToProjectJson().ShouldBe(beforeRemoval);
            }
        });
    }

    [Fact]
    public async Task Catalog_entry_navigation_preserves_current_history_and_ignores_missing_entries()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        await preferences.SaveAsync(preferences.Value with { Environments = preferences.Value.Environments with
            { Infrastructure = [new() { Id = "office", Name = "Office" }, new() { Id = "lab", Name = "Lab" }] } });
        var project = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        project.ResetHistory();
        project.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
        var original = project.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<InfrastructureCatalog>();
            var catalog = components.Items.OfType<InfrastructureCatalog>().Single();
            var graph = components.Items.OfType<EnvironmentTopologyPage>().Last().WorkspaceGraph!;
            var topology = graph.GetEnvironmentDefinition().Topology;
            topology.Nodes[0].Name = "Office updated";
            graph.SetTopology(topology);
            catalog.SelectEntry("office");
            catalog.SelectEntry("removed");
            components.Items.OfType<EnvironmentTopologyPage>().Last().WorkspaceGraph.ShouldBeSameAs(graph);
            graph.CanUndo.ShouldBeTrue();
            graph.Undo();
            preferences.Value.Environments.Infrastructure[0].Name.ShouldBe("Office");
            graph.Redo();
            preferences.Value.Environments.Infrastructure[0].Name.ShouldBe("Office updated");
            catalog.SelectEntry("lab");
            components.Items.OfType<EnvironmentTopologyPage>().Last().WorkspaceGraph!
                .GetEnvironmentDefinition().Topology.Nodes.Single().Name.ShouldBe("Lab");
            project.ToProjectJson().ShouldBe(original);
            project.Undo();
            project.GetEnvironment().StartupTimeoutSeconds.ShouldBe(60);
            project.Redo();
            project.ToProjectJson().ShouldBe(original);
        });
    }

    [Theory]
    [InlineData("", "Office server")]
    [InlineData(" office SERVER ", "Office server")]
    [InlineData("Office server", "  Office server  ")]
    public async Task Infrastructure_name_dialog_rejects_blank_and_equivalent_saved_names(string name, string existing)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var defaults = preferences.Value.Environments with { Infrastructure = [new() { Name = existing }] };
        await preferences.SaveAsync(preferences.Value with { Environments = defaults });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<MudBlazor.IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference =>
            {
                reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
                return Task.CompletedTask;
            };
            var dialog = await dialogs.ShowAsync<InfrastructureNameDialog>("Add host");
            await components.Items.OfType<MudBlazor.MudTextField<string>>().Single().ValueChanged.InvokeAsync(name);
            await components.Items.OfType<MudBlazor.MudButton>().Single(button => button.Color == MudBlazor.Color.Primary)
                .OnClick.InvokeAsync(new MouseEventArgs());
            dialog.Result.IsCompleted.ShouldBeFalse();
            components.Items.OfType<MudBlazor.MudTextField<string>>().Single().GetState(field => field.Error).ShouldBeTrue();
            if (!string.IsNullOrWhiteSpace(name))
            {
                await preferences.SaveAsync(preferences.Value with { Environments = defaults.RemoveHost(defaults.Infrastructure.Single()) });
                await components.Items.OfType<MudBlazor.MudButton>().Single(button => button.Color == MudBlazor.Color.Primary)
                    .OnClick.InvokeAsync(new MouseEventArgs());
                dialog.Result.IsCompleted.ShouldBeTrue();
                (await dialog.Result)!.Data.ShouldBe(name.Trim());
            }
            else dialog.Close(MudBlazor.DialogResult.Cancel());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Infrastructure_host_list_actions_do_not_overwrite_an_in_progress_settings_save(bool remove)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var storage = (MemorySettings)scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>();
        var defaults = preferences.Value.Environments.AddHost("Office");
        await preferences.SaveAsync(preferences.Value with { Environments = defaults });
        var shown = new TaskCompletionSource<MudBlazor.IDialogReference>();
        scope.ServiceProvider.GetRequiredService<MudBlazor.IDialogService>().DialogInstanceAddedAsync += dialog =>
        {
            dialog.RenderCompleteTaskCompletionSource.TrySetResult(true);
            shown.SetResult(dialog);
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<InfrastructureCatalog>();
            var action = components.Items.OfType<StudioIconButton>().Single(b => b.Label == (remove ? "Remove host definition" : "Add host…")).OnClick;
            var adding = remove ? null : action.InvokeAsync();
            var dialog = remove ? null : await shown.Task;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            storage.WriteDelay = release.Task;
            var saving = preferences.SaveAsync(preferences.Value with { Environments = defaults.AddRegistry() });
            try
            {
                storage.WriteDelay = Task.CompletedTask;
                dialog?.Dismiss(MudBlazor.DialogResult.Ok("Lab"));
                await (adding ?? action.InvokeAsync());
                preferences.Value.Environments.ShouldBeSameAs(defaults);
                preferences.IsSaving.ShouldBeTrue();
            }
            finally { release.SetResult(); await saving; }
            var reopened = new StudioPreferences(storage);
            await reopened.LoadAsync();
            reopened.Value.Environments.Registries.Count.ShouldBe(defaults.Registries.Count + 1);
            reopened.Value.Environments.Infrastructure.Single().Name.ShouldBe("Office");
        });
    }

    [Fact]
    public async Task Host_removal_rejects_stale_clicks_and_preserves_the_placed_snapshot_on_reopen()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var office = new InfrastructureHost { Name = "Office", Services = [new() { Name = "Broker", Endpoint = "mqtt://office:1883" }] };
        var defaults = preferences.Value.Environments with { Infrastructure = [office] };
        defaults = defaults.AddHost(" Lab ");
        await preferences.SaveAsync(preferences.Value with { Environments = defaults });
        var project = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        project.ResetHistory();
        var before = project.ToProjectJson();
        var topology = project.GetEnvironmentDefinition().Topology;
        topology.PlaceHost(office, 80, 100);
        project.SetTopology(topology);
        var placed = project.ToProjectJson();
        placed.ShouldNotBe(before);
        var configuration = project.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<InfrastructureCatalog>();
            var catalog = components.Items.OfType<InfrastructureCatalog>().Single();
            EventCallback Remove() => components.Items.OfType<StudioIconButton>().Last(b => b.Label == "Remove host definition").OnClick;
            var oldRemove = Remove();
            catalog.SelectEntry(defaults.Infrastructure[1].Id);
            await oldRemove.InvokeAsync();
            catalog.SelectEntry(office.Id);
            await oldRemove.InvokeAsync();
            preferences.Value.Environments.ShouldBeSameAs(defaults);
            var removeOffice = Remove();
            await removeOffice.InvokeAsync();
            var saved = preferences.Value;
            await removeOffice.InvokeAsync();
            preferences.Value.ShouldBeSameAs(saved);
            var reopened = new StudioPreferences(scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>());
            await reopened.LoadAsync();
            reopened.Value.Environments.Infrastructure.Single().Name.ShouldBe("Lab");
            project.ToProjectJson().ShouldBe(placed);
            var reopenedProject = StudioTestServices.CreateGraph();
            reopenedProject.LoadProject(placed);
            reopenedProject.ToProjectJson().ShouldBe(placed);
            project.ExportConfiguration().ShouldBe(configuration);
            project.Undo(); project.ToProjectJson().ShouldBe(before);
            project.Redo(); project.ToProjectJson().ShouldBe(placed);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Infrastructure_creation_rechecks_current_names_and_ignores_results_after_disposal(bool dispose)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var project = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var original = project.ToProjectJson();
        var shown = new TaskCompletionSource<MudBlazor.IDialogReference>();
        var dialogs = scope.ServiceProvider.GetRequiredService<MudBlazor.IDialogService>();
        dialogs.DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            shown.SetResult(reference);
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<InfrastructureCatalog>();
            var creation = components.Items.OfType<StudioIconButton>().Single(button => button.Label == "Add host…").OnClick.InvokeAsync();
            var dialog = await shown.Task;
            if (dispose) components.Items.OfType<InfrastructureCatalog>().Single().Dispose();
            else await preferences.SaveAsync(preferences.Value with { Environments = preferences.Value.Environments with
                { Infrastructure = [new() { Name = "  Office server  " }] } });
            dialog.Dismiss(MudBlazor.DialogResult.Ok("office SERVER"));
            await creation;
            if (dispose) preferences.Value.Environments.Infrastructure.ShouldBeEmpty();
            else preferences.Value.Environments.Infrastructure.Single().Name.ShouldBe("  Office server  ");
            project.ToProjectJson().ShouldBe(original);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Named_infrastructure_creation_preserves_project_history_and_cancel_leaves_catalog_empty(bool cancel)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var project = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        project.ResetHistory();
        project.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
        var original = project.ToProjectJson();
        var dialogs = scope.ServiceProvider.GetRequiredService<MudBlazor.IDialogService>();
        dialogs.DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(cancel ? MudBlazor.DialogResult.Cancel() : MudBlazor.DialogResult.Ok("  Office server  "));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<InfrastructureCatalog>();
            await components.Items.OfType<StudioIconButton>().Single(button => button.Label == "Add host…").OnClick.InvokeAsync();
            if (cancel)
            {
                preferences.Value.Environments.Infrastructure.ShouldBeEmpty();
                view.ToHtmlString().ShouldContain("Define your first host");
            }
            else
            {
                var host = preferences.Value.Environments.Infrastructure.Single();
                host.Name.ShouldBe("Office server");
                components.Items.OfType<EnvironmentTopologyPage>().Single().WorkspaceGraph!
                    .GetEnvironmentDefinition().Topology.Nodes.Single().Name.ShouldBe("Office server");
                var reopened = new StudioPreferences(scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>());
                await reopened.LoadAsync();
                reopened.Value.Environments.Infrastructure.Single().Name.ShouldBe("Office server");
            }
            project.ToProjectJson().ShouldBe(original);
            project.Undo();
            project.GetEnvironment().StartupTimeoutSeconds.ShouldBe(60);
            project.Redo();
            project.ToProjectJson().ShouldBe(original);
        });
    }

    [Fact]
    public async Task Infrastructure_rapid_topology_edits_survive_slow_save_reopen_and_undo()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var storage = (MemorySettings)scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>();
        var defaults = preferences.Value.Environments.AddHost("Office");
        await preferences.SaveAsync(preferences.Value with { Environments = defaults });
        var project = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var snapshot = project.GetEnvironmentDefinition().Topology;
        snapshot.PlaceHost(defaults.Infrastructure.Single(), 40, 80);
        project.SetTopology(snapshot);
        var projectJson = project.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<InfrastructureCatalog>();
            var graph = components.Items.OfType<EnvironmentTopologyPage>().Single().WorkspaceGraph!;
            preferences.Value.Environments.Infrastructure.Single().Topology.ShouldBeNull();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            preferences.Changed += () =>
            {
                if (!preferences.IsSaving && preferences.Value.Environments.Infrastructure.Single().Topology?.Nodes[0].X == 200)
                    drained.TrySetResult();
            };
            storage.WriteDelay = release.Task;
            var topology = graph.GetEnvironmentDefinition().Topology;
            var originalX = topology.Nodes[0].X;
            topology.Nodes[0].Name = "Lab";
            graph.SetTopology(topology);
            try
            {
                preferences.IsSaving.ShouldBeTrue();
                await Should.ThrowAsync<InvalidOperationException>(() => preferences.SaveAsync(preferences.Value with
                    { Environments = defaults.RemoveHost(defaults.Infrastructure.Single()) }));
                topology.Nodes[0].X = 200;
                graph.SetTopology(topology);
                preferences.Value.Environments.ShouldBeSameAs(defaults);
            }
            finally { release.SetResult(); }
            await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var reopened = new StudioPreferences(storage);
            await reopened.LoadAsync();
            var host = reopened.Value.Environments.Infrastructure.Single();
            host.Name.ShouldBe("Lab");
            host.Topology!.Nodes[0].X.ShouldBe(200);
            graph.Undo();
            preferences.Value.Environments.Infrastructure.Single().Topology!.Nodes[0].X.ShouldBe(originalX);
            graph.Redo();
            preferences.Value.Environments.Infrastructure.Single().Topology!.Nodes[0].X.ShouldBe(200);
            project.ToProjectJson().ShouldBe(projectJson);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Infrastructure_retry_discards_an_edit_if_the_open_host_was_replaced_or_removed(bool remove)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var storage = (MemorySettings)scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>();
        var defaults = preferences.Value.Environments.AddHost("Office").AddHost("Lab");
        await preferences.SaveAsync(preferences.Value with { Environments = defaults });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<InfrastructureCatalog>();
            var graph = components.Items.OfType<EnvironmentTopologyPage>().Single().WorkspaceGraph!;
            var topology = graph.GetEnvironmentDefinition().Topology;
            topology.Nodes[0].Name = "Stale";
            storage.WriteDelay = Task.FromException(new IOException("Storage unavailable"));
            graph.SetTopology(topology);
            var retry = components.Items.OfType<MudBlazor.MudButton>().Last(button =>
                button.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == "Retry host save").OnClick;
            storage.WriteDelay = Task.CompletedTask;
            var original = defaults.Infrastructure[0];
            var otherEdit = original.CreateTopology();
            otherEdit.Nodes[0].Name = "Current";
            var changed = remove ? defaults.RemoveHost(original) : defaults.UpdateHost(original, otherEdit);
            await preferences.SaveAsync(preferences.Value with { Environments = changed });
            await retry.InvokeAsync();
            preferences.Value.Environments.ShouldBeSameAs(changed);
            view.ToHtmlString().ShouldNotContain("Retry host save");
            var expectedName = remove ? "Lab" : "Current";
            components.Items.OfType<EnvironmentTopologyPage>().Last().WorkspaceGraph!
                .GetEnvironmentDefinition().Topology.Nodes[0].Name.ShouldBe(expectedName);
            var reopened = new StudioPreferences(storage);
            await reopened.LoadAsync();
            reopened.Value.Environments.Infrastructure[0].Name.ShouldBe(expectedName);
            reopened.Value.Environments.Infrastructure.Count.ShouldBe(remove ? 1 : 2);
        });
    }

    [Fact]
    public async Task Infrastructure_retry_keeps_the_latest_draft_if_reload_is_requested_during_a_failed_save()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var storage = (MemorySettings)scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>();
        var baseline = preferences.Value.Environments.AddHost("Office");
        await preferences.SaveAsync(preferences.Value with { Environments = baseline });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<InfrastructureCatalog>();
            var graph = components.Items.OfType<EnvironmentTopologyPage>().Single().WorkspaceGraph!;
            var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            preferences.Changed += () => { if (!preferences.IsSaving) failed.TrySetResult(); };
            storage.WriteDelay = write.Task;
            var draft = graph.GetEnvironmentDefinition().Topology;
            draft.Nodes[0].Name = "First edit";
            graph.SetTopology(draft);
            draft.Nodes[0].Name = "Latest edit";
            graph.SetTopology(draft);
            Exception? reloadError;
            try { reloadError = await Record.ExceptionAsync(() => preferences.LoadAsync()); }
            finally { write.SetException(new IOException("Write failed")); }
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            storage.WriteDelay = Task.CompletedTask;
            await components.Items.OfType<MudBlazor.MudButton>().Last(button =>
                button.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == "Retry host save").OnClick.InvokeAsync();
            await preferences.LoadAsync();
            preferences.Value.Environments.Infrastructure.Single().Name.ShouldBe("Latest edit");
            reloadError.ShouldBeOfType<InvalidOperationException>();
        });
    }

    [Fact]
    public async Task Infrastructure_failed_autosave_keeps_latest_edit_for_retry_and_blocks_host_switching()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var storage = (MemorySettings)scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>();
        await preferences.SaveAsync(preferences.Value with { Environments = preferences.Value.Environments with { Infrastructure =
            [new() { Id = "p1", Name = "P1" }, new() { Id = "p2", Name = "P2" }] } });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<InfrastructureCatalog>();
            var graph = components.Items.OfType<EnvironmentTopologyPage>().Single().WorkspaceGraph!;
            var original = graph.ToProjectJson();
            storage.WriteDelay = Task.FromException(new IOException("Storage unavailable"));
            var topology = graph.GetEnvironmentDefinition().Topology;
            topology.Nodes[0] = topology.Nodes[0] with { Name = "Edited host" };
            graph.SetTopology(topology);
            var edited = graph.ToProjectJson();
            preferences.Value.Environments.Infrastructure[0].Name.ShouldBe("P1");
            view.ToHtmlString().ShouldContain("Retry save");
            var select = components.Items.OfType<FluxFlow.Studio.UI.Components.Toolbar.StudioToolbarSelect<string>>().Single();
            select.Disabled.ShouldBeTrue();
            await select.ValueChanged.InvokeAsync("p2");
            components.Items.OfType<EnvironmentTopologyPage>().Count().ShouldBe(1);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(original);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(edited);
            storage.WriteDelay = Task.CompletedTask;
            await components.Items.OfType<MudBlazor.MudButton>().Last(button =>
                button.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == "Retry host save").OnClick.InvokeAsync();
            preferences.Value.Environments.Infrastructure[0].Name.ShouldBe("Edited host");
            view.ToHtmlString().ShouldNotContain("Storage unavailable");
            select = components.Items.OfType<FluxFlow.Studio.UI.Components.Toolbar.StudioToolbarSelect<string>>().Last();
            await select.ValueChanged.InvokeAsync("p2");
            components.Items.OfType<EnvironmentTopologyPage>().Last().WorkspaceGraph!.GetEnvironmentDefinition().Topology.Nodes.Single().Name.ShouldBe("P2");
            var reopened = new StudioPreferences(storage);
            await reopened.LoadAsync();
            reopened.Value.Environments.Infrastructure[0].CreateTopology().Nodes.Single().Name.ShouldBe("Edited host");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Infrastructure_host_changes_drain_pending_edits_without_resurrecting_removed_hosts(bool remove)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var storage = (MemorySettings)scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>();
        var dialogs = scope.ServiceProvider.GetRequiredService<MudBlazor.IDialogService>();
        dialogs.DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(MudBlazor.DialogResult.Ok("P3"));
            return Task.CompletedTask;
        };
        await preferences.SaveAsync(preferences.Value with { Environments = preferences.Value.Environments with { Infrastructure =
            [new() { Id = "p1", Name = "P1" }, new() { Id = "p2", Name = "P2" }] } });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<InfrastructureCatalog>();
            var graph = components.Items.OfType<EnvironmentTopologyPage>().Single().WorkspaceGraph!;
            var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            storage.WriteDelay = delay.Task;
            var mutation = components.Items.OfType<StudioIconButton>().Single(button => button.Label ==
                (remove ? "Remove host definition" : "Add host…")).OnClick.InvokeAsync();
            var topology = graph.GetEnvironmentDefinition().Topology;
            topology.Nodes[0] = topology.Nodes[0] with { Name = "Edited during save" };
            graph.SetTopology(topology);
            topology.Nodes[0] = topology.Nodes[0] with { Name = "Latest edit" };
            graph.SetTopology(topology);
            delay.SetResult();
            await mutation;
            preferences.Value.Environments.Infrastructure.Count.ShouldBe(remove ? 1 : 3);
            if (remove) preferences.Value.Environments.Infrastructure.ShouldNotContain(host => host.Id == "p1");
            else preferences.Value.Environments.Infrastructure.Single(host => host.Id == "p1").Name.ShouldBe("Latest edit");
            components.Items.OfType<EnvironmentTopologyPage>().Last().WorkspaceGraph.ShouldNotBeSameAs(graph);
            var reopened = new StudioPreferences(storage);
            await reopened.LoadAsync();
            reopened.Value.Environments.Infrastructure.Count.ShouldBe(remove ? 1 : 3);
            if (!remove) reopened.Value.Environments.Infrastructure.Single(host => host.Id == "p1").CreateTopology().Nodes.Single().Name.ShouldBe("Latest edit");
        });
    }

    [Fact]
    public async Task Infrastructure_designer_autosaves_visual_edits_and_undo_without_changing_the_project()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var project = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        project.ResetHistory();
        var saved = project.ToProjectJson();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        await preferences.SaveAsync(preferences.Value with { Environments = preferences.Value.Environments with { Infrastructure =
            [new() { Id = "p1", Name = "P1", Services = [new() { Name = "Broker", Endpoint = "mqtt://p1:1883" }] }, new() { Id = "p2", Name = "P2" }] } });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<InfrastructureCatalog>();
            var canvas = components.Items.OfType<EnvironmentTopologyPage>().Single();
            var graph = canvas.WorkspaceGraph!;
            var palette = components.Items.OfType<ComponentPalette>().Single();
            foreach (var type in new[] { "mqtt.client", "amqp.connection", "nats.connection" })
                palette.Items.ShouldContain(item => item.ComponentType == "existing:" + type);
            var diagram = components.Items.OfType<CascadingValue<BlazorDiagram>>().Single().Value!;
            diagram.SelectModel(diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.ExistingService is not null), true);
            var inspector = components.Items.OfType<TopologyInspector>().Last();
            await inspector.SetServiceEndpoint.InvokeAsync("mqtt://p1-new:1883");
            preferences.Value.Environments.Infrastructure[0].CreateTopology().Nodes.Single(node => node.ExistingService is not null).ExistingService!.Endpoint.ShouldBe("mqtt://p1-new:1883");
            graph.Undo();
            preferences.Value.Environments.Infrastructure[0].CreateTopology().Nodes.Single(node => node.ExistingService is not null).ExistingService!.Endpoint.ShouldBe("mqtt://p1:1883");
            graph.Redo();
            var select = components.Items.OfType<FluxFlow.Studio.UI.Components.Toolbar.StudioToolbarSelect<string>>().Single();
            await select.ValueChanged.InvokeAsync("p2");
            await inspector.SetServiceEndpoint.InvokeAsync("mqtt://stale:1883");
            preferences.Value.Environments.Infrastructure[0].CreateTopology().Nodes.Single(node => node.ExistingService is not null).ExistingService!.Endpoint.ShouldBe("mqtt://p1-new:1883");
            preferences.Value.Environments.Infrastructure[1].CreateTopology().Nodes.Count.ShouldBe(1);
            view.ToHtmlString().ShouldNotContain("Check environment");
            project.ToProjectJson().ShouldBe(saved);
            project.CanUndo.ShouldBeFalse();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_applications_use_selected_saved_host_or_visible_local_host_with_one_undo(bool savedHost)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        string? hostId = null;
        if (savedHost)
        {
            var topology = graph.GetEnvironmentDefinition().Topology;
            hostId = topology.PlaceHost(new() { Services = [new()] }, 100, 100);
            topology.Nodes.Single(node => node.Id == hostId).PlacementScale = .5;
            graph.SetTopology(topology);
        }
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<EnvironmentTopologyPage>();
            var diagram = components.Items.OfType<CascadingValue<BlazorDiagram>>().Single().Value!;
            if (savedHost) diagram.SelectModel(diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.Id == hostId), true);
            await components.Items.OfType<ComponentPalette>().Single().OnSelect.InvokeAsync("application");
            var topology = graph.GetEnvironmentDefinition().Topology;
            var host = topology.Nodes.Single(node => node.Kind == EnvironmentNodeKind.Host);
            var app = topology.Nodes.Single(node => node.Kind == EnvironmentNodeKind.Application);
            app.Host.ShouldBe(host.Id);
            if (savedHost) host.Id.ShouldBe(hostId);
            else host.LocalHost.ShouldBeTrue();
            var model = diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.Id == app.Id);
            model.Container!.Definition.Id.ShouldBe(host.Id);
            model.DisplayScale.ShouldBe(savedHost ? .5 : 1);
            if (savedHost)
            {
                var service = diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.ExistingService is not null);
                (await diagram.Options.Constraints.ShouldDeleteNode(service)).ShouldBeFalse();
            }
            (await diagram.Options.Constraints.ShouldDeleteNode(model)).ShouldBeTrue();
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.ExportConfiguration().ShouldBe(configuration);
        });
    }

    [Fact]
    public async Task Locked_infrastructure_allows_connection_binding_and_whole_placement_removal()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883"}},"Workflows":{"main":{}}}""");
        var topology = graph.GetEnvironmentDefinition().Topology;
        topology.PlaceHost(new() { Services = [new() { Name = "Broker" }] }, 0, 0);
        topology.Nodes.Add(new() { Kind = EnvironmentNodeKind.Application, Name = "App" });
        graph.SetTopology(topology);
        graph.ResetHistory();
        var saved = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<EnvironmentTopologyPage>();
            var diagram = components.Items.OfType<CascadingValue<BlazorDiagram>>().Single().Value!;
            var broker = diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.ExistingService is not null);
            broker.Locked.ShouldBeFalse();
            diagram.SelectModel(broker, true);
            view.ToHtmlString().ShouldContain("Saved host settings are read-only");
            var inspector = components.Items.OfType<TopologyInspector>().Last();
            await inspector.SetColor.InvokeAsync("#123456");
            graph.ToProjectJson().ShouldBe(saved);
            var app = diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.Kind == EnvironmentNodeKind.Application);
            app.ConnectionPorts.Single().CanAttachTo(broker.ConnectionPorts.Single()).ShouldBeTrue();
            diagram.SelectModel(diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.Kind == EnvironmentNodeKind.Host), true);
            await components.Items.OfType<StudioIconButton>().Single(button => button.Label == "Remove from topology").OnClick.InvokeAsync();
            graph.GetEnvironmentDefinition().Topology.Nodes.Single().Kind.ShouldBe(EnvironmentNodeKind.Application);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(saved);
            graph.Redo();
            graph.GetEnvironmentDefinition().Topology.Nodes.Count.ShouldBe(1);
        });
    }
}
