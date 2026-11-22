using Blazor.Diagrams;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Environments.Topology;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Fact]
    public void Access_fields_only_write_the_named_provider_on_an_editable_binding()
    {
        var fields = FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider.Access!;
        const string server = "mosquitto";
        var binding = new ResourceBinding();
        var empty = fields.Read(binding, server);
        fields.Write(binding, server, empty).ShouldBeSameAs(binding);
        binding.BrokerAccess.ShouldBeEmpty();
        var credentials = fields.Edits!.SetCredentials(empty, "client", " preserve spaces ");
        var edited = fields.Write(binding, server, credentials);
        binding.BrokerAccess.ShouldBeEmpty();
        edited.BrokerAccess[server].CustomPassword.ShouldBe(" preserve spaces ");
        var locked = edited with { Profile = new("snapshot", "Saved broker", true, edited.CopySetup()) };
        fields.Write(locked, server, empty).ShouldBeSameAs(locked);
        locked.ValidateSnapshot();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Broker_access_drafts_cannot_mutate_a_locked_binding(bool readFromBinding)
    {
        const string server = "mosquitto";
        var fields = FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider.Access!;
        var user = new BrokerUser { Name = "reader", Password = "original" };
        var draft = new BrokerAccess { Users = [user] };
        var saved = fields.Write(new ResourceBinding(), server, draft);
        var locked = saved with { Profile = new("snapshot", "Locked broker", true, saved.CopySetup()) };
        var retained = readFromBinding ? fields.Read(locked, server) : draft;
        retained.Users.Clear();
        locked.BrokerAccess[server].Users.ShouldBe(new[] { user });
        locked.ValidateSnapshot();
    }

    [Theory]
    [InlineData("mosquitto")]
    [InlineData("rabbitmq-mqtt")]
    public async Task Renaming_broker_users_preserves_credentials_client_selection_and_history(string server)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        const string path = "Resources:broker";
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Username":"manual","Password":"manual-pass"}},"Workflows":{"main":{}}}""");
        var client = new BrokerUser { Name = "reader", Password = "original password", Read = "read", Write = "write" };
        var other = new BrokerUser { Name = "other", Password = "separate" };
        var original = new BrokerAccess { Enabled = true, ClientUsername = client.Name, Users = [client, other],
            CustomUsername = "fallback", CustomPassword = "fallback password" };
        var otherProvider = server == "mosquitto" ? "rabbitmq-mqtt" : "mosquitto";
        var startupPath = server == "mosquitto" ? "/mosquitto/config/mosquitto.conf" : "/etc/rabbitmq/rabbitmq.conf";
        const string startup = "# local override\r\n# keep line endings\n";
        var binding = new ResourceBinding { Provider = server, Files = new() { [startupPath] = startup },
            BrokerAccess = new() { [server] = original, [otherProvider] = new() { CustomPassword = " independent " } } };
        graph.SetEnvironment(new() { Resources = new() { [path] = binding } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var config = graph.ExportConfiguration();
        BrokerAccess Access() => graph.GetEnvironment().Resources[path].BrokerAccess[server];
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ResourcePath"] = path }));
            MudTextField<string> Name() => components.Items.OfType<MudTextField<string>>().Last(item =>
                Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Username"));
            await Name().ValueChanged.InvokeAsync(client.Name);
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            await Name().ValueChanged.InvokeAsync("renamed");
            Access().ClientUsername.ShouldBe("renamed");
            Access().Users.ShouldBe(new[] { client with { Name = "renamed" }, other });
            var renamed = graph.ToProjectJson();
            var delayedRemove = components.Items.OfType<StudioIconButton>()
                .Last(item => item.Label == "Remove broker user").OnClick;
            await components.Items.OfType<MudSelect<int>>().Single(item =>
                Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "User")).ValueChanged.InvokeAsync(1);
            // The remove button belonged to the previously selected row.
            await delayedRemove.InvokeAsync();
            graph.ToProjectJson().ShouldBe(renamed);
            // A second user cannot take the name that identifies the connection user.
            await Name().ValueChanged.InvokeAsync("renamed");
            graph.ToProjectJson().ShouldBe(renamed);
            Name().Value.ShouldBe(other.Name);
            await Name().ValueChanged.InvokeAsync("another");
            Access().ClientUsername.ShouldBe("renamed");
            Access().Users[1].ShouldBe(other with { Name = "another" });
            Access().CustomUsername.ShouldBe(original.CustomUsername);
            Access().CustomPassword.ShouldBe(original.CustomPassword);
            var both = graph.ToProjectJson();
            var password = components.Items.OfType<MudTextField<string>>().Last(item =>
                Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Password"));
            await password.ValueChanged.InvokeAsync(null);
            Access().Users[1].Password.ShouldBe("");
            graph.Undo(); graph.ToProjectJson().ShouldBe(both);
            graph.Undo(); graph.ToProjectJson().ShouldBe(renamed);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.Redo(); graph.ToProjectJson().ShouldBe(both);
            graph.ExportConfiguration().ShouldBe(config);
            var reopened = StudioTestServices.CreateGraph();
            reopened.LoadProject(both);
            reopened.ToProjectJson().ShouldBe(both);
            var saved = reopened.GetEnvironment().Resources[path];
            saved.BrokerAccess[server].Users.ShouldBe(new[] { client with { Name = "renamed" }, other with { Name = "another" } });
            saved.BrokerAccess[otherProvider].CustomPassword.ShouldBe(" independent ");
            saved.Files[startupPath].ShouldBe(startup);
            var latePassword = password.ValueChanged;
            var environment = graph.GetEnvironment();
            environment.Resources[path] = environment.Resources[path] with { Provider = otherProvider };
            graph.SetEnvironment(environment);
            var switched = graph.ToProjectJson();
            await latePassword.InvokeAsync("late edit");
            graph.ToProjectJson().ShouldBe(switched);
            graph.Undo(); graph.ToProjectJson().ShouldBe(both);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mqtt_service_editor_preserves_credentials_history_and_locked_snapshots(bool locked)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var source = new InfrastructureHost { Services = [new() { Endpoint = "ws://broker:8080/mqtt?tenant=trial",
            Mqtt = new() { Username = "original-user", Password = "original-password" } }] };
        var topology = source.CreateTopology();
        if (locked)
        {
            topology = new();
            topology.PlaceHost(source, 0, 0);
        }
        graph.SetTopology(topology);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        InfrastructureService Current() => graph.GetEnvironmentDefinition().Topology.Nodes.Single(node => node.ExistingService is not null).ExistingService!;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<EnvironmentTopologyPage>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(EnvironmentTopologyPage.InfrastructureMode)] = !locked }));
            var diagram = components.Items.OfType<CascadingValue<BlazorDiagram>>().Single().Value!;
            diagram.SelectModel(diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.ExistingService is not null), true);
            var tls = components.Items.OfType<MudCheckBox<bool>>().Single(field => field.UserAttributes?["aria-label"]?.ToString() == "MQTT TLS");
            var settings = components.Items.OfType<MudSelect<string>>().Single(field => field.UserAttributes?["aria-label"]?.ToString() == "MQTT settings source");
            tls.Disabled.ShouldBe(locked);
            settings.Disabled.ShouldBe(locked);
            var previousSettings = settings.ValueChanged;
            await tls.ValueChanged.InvokeAsync(true);
            if (locked)
            {
                await settings.ValueChanged.InvokeAsync("Use connection");
                graph.ToProjectJson().ShouldBe(before);
                graph.CanUndo.ShouldBeFalse();
                return;
            }
            Current().Endpoint.ShouldBe("wss://broker:8080/mqtt?tenant=trial");
            Current().Mqtt!.Password.ShouldBe("original-password");
            var secured = graph.ToProjectJson();
            await previousSettings.InvokeAsync("Use connection");
            graph.ToProjectJson().ShouldBe(secured);
            await settings.ValueChanged.InvokeAsync("Use connection");
            Current().Mqtt.ShouldBeNull();
            var inherited = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(secured);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.Redo(); graph.ToProjectJson().ShouldBe(inherited);
            graph.ExportConfiguration().ShouldBe(configuration);
        });
    }
}
