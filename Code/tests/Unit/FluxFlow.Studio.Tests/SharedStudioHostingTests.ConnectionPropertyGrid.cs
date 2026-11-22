using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using FluxFlow.Plugins;
using FluxFlow.Studio.UI;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Extensions;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Theory]
    [InlineData(false, "COM7", "COM8", true)]
    [InlineData(true, "https://saved/", "https://updated/", true)]
    [InlineData(true, "https://saved/", "not-an-address", false)]
    public async Task Data_only_plugin_connections_honor_endpoint_format_and_preserve_saved_fields(
        bool endpointIsUri, string original, string edited, bool accepted)
    {
        var schema = new PluginConnection("test.connection", "Test connection", "Address", false,
            JsonSerializer.SerializeToElement(new { Type = "test.connection", Address = original }),
            [OptionEditorModel.Create("Address", "Address")]) { EndpointIsUri = endpointIsUri };
        var catalog = JsonSerializer.Serialize(new[] { new PluginDescription("test", [], [schema]) },
            PluginJsonContext.Default.PluginDescriptionArray);
        var services = CreateServices();
        services.AddStudioUI(JsonSerializer.Deserialize(catalog, PluginJsonContext.Default.PluginDescriptionArray));
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"device":{"Type":"test.connection","Address":"saved","Password":"kept-secret","Custom":{"Keep":true}}},"Workflows":{"main":{}}}""".Replace("\"saved\"", JsonSerializer.Serialize(original)));
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:device" }));
            var field = components.Items.OfType<MudTextField<string>>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Address"));
            await field.ValueChanged.InvokeAsync(edited);
            await Task.Yield();
            var settings = graph.Connections.Single().Settings;
            settings["Address"]!.GetValue<string>().ShouldBe(accepted ? edited : original);
            settings["Password"]!.GetValue<string>().ShouldBe("kept-secret");
            settings["Custom"]!["Keep"]!.GetValue<bool>().ShouldBeTrue();
            if (!accepted)
            {
                graph.ToProjectJson().ShouldBe(before);
                graph.CanUndo.ShouldBeFalse();
                return;
            }
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after);
            graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Theory]
    [InlineData("Edit CA file 2: second.pem", """[" first.pem ","replacement.pem"]""")]
    [InlineData("Remove CA file 2: second.pem", """[" first.pem "]""")]
    public async Task Mqtt_connection_CA_edit_commits_only_the_selected_entry(string action, string expected)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtts://saved","ClientId":"test","TrustedCertificates":[" first.pem ","second.pem"]}},"Workflows":{"main":{}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(DialogResult.Ok("replacement.pem"));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:broker" }));
            await components.Items.OfType<MudIconButton>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), action))
                .OnClick.InvokeAsync(new MouseEventArgs());
            JsonNode.DeepEquals(graph.Connections.Single().Settings["TrustedCertificates"], JsonNode.Parse(expected)).ShouldBeTrue();
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Connection_nested_json_save_and_cancel_preserve_values_and_history(bool cancel)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client","BaseAddress":"https://saved/","Headers":{"X-Saved":"original"},"Authentication":{"Scheme":"Basic","Password":"saved-secret"},"Future":42}},"Workflows":{"main":{}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(cancel ? DialogResult.Cancel() : DialogResult.Ok("""{"X-Saved":"updated","X-New":"value"}"""));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:api" }));
            await components.Items.OfType<MudButton>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Edit Headers"))
                .OnClick.InvokeAsync(new MouseEventArgs());
            if (cancel) { graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse(); return; }
            var settings = graph.Connections.Single().Settings;
            settings["Headers"]!["X-Saved"]!.GetValue<string>().ShouldBe("updated");
            settings["Headers"]!["X-New"]!.GetValue<string>().ShouldBe("value");
            settings["Authentication"]!["Password"]!.GetValue<string>().ShouldBe("saved-secret");
            settings["Future"]!.GetValue<int>().ShouldBe(42);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Fact]
    public async Task Connection_grid_edits_preserve_nested_values_service_snapshots_and_history()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Host":"saved-host","Port":8883,"Tls":true,"uSeRnAmE":"saved-user","Password":"manual-secret","clientCertificate":{"Path":"saved.pem","Password":"key-secret","Future":42},"Future":{"Keep":true}}},"Workflows":{"main":{}}}""");
        graph.SetTopology(new() { Nodes = [new() { Id = "service", Name = "Saved service", Kind = FluxFlow.Environments.EnvironmentNodeKind.Service,
            ExistingService = new() { Endpoint = "mqtts://service:8883", Mqtt = new() { Username = "service-user", Password = "service-secret" } } }] });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var topology = System.Text.Json.JsonSerializer.Serialize(graph.GetEnvironmentDefinition().Topology);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:broker" }));
            MudTextField<string> Field(string label) => components.Items.OfType<MudTextField<string>>().Last(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), label));
            Field("Password").InputType.ShouldBe(InputType.Password);
            Field("Key password").InputType.ShouldBe(InputType.Password);
            view.ToHtmlString().ShouldContain("Host");
            graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            var oldUsername = Field("Username");
            await oldUsername.ValueChanged.InvokeAsync("edited-user");
            await Task.Yield();
            Field("Username").ShouldBeSameAs(oldUsername);
            var first = graph.ToProjectJson();
            var settings = graph.Connections.Single().Settings;
            settings["uSeRnAmE"]!.GetValue<string>().ShouldBe("edited-user");
            settings.ContainsKey("Username").ShouldBeFalse();
            settings.ContainsKey("Address").ShouldBeFalse();
            settings["Password"]!.GetValue<string>().ShouldBe("manual-secret");
            settings["clientCertificate"]!["Password"]!.GetValue<string>().ShouldBe("key-secret");
            await Field("File").ValueChanged.InvokeAsync("edited.pem");
            await Task.Yield();
            var second = graph.ToProjectJson();
            settings = graph.Connections.Single().Settings;
            settings["clientCertificate"]!["Path"]!.GetValue<string>().ShouldBe("edited.pem");
            settings["clientCertificate"]!["Future"]!.GetValue<int>().ShouldBe(42);
            settings["Future"]!["Keep"]!.GetValue<bool>().ShouldBeTrue();
            graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(first);
            graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(before);
            await oldUsername.ValueChanged.InvokeAsync("stale-value");
            graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.Redo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(second);
            System.Text.Json.JsonSerializer.Serialize(graph.GetEnvironmentDefinition().Topology).ShouldBe(topology);
            graph.LoadProject(second);
            graph.Connections.Single().Settings["clientCertificate"]!["Path"]!.GetValue<string>().ShouldBe("edited.pem");
            System.Text.Json.JsonSerializer.Serialize(graph.GetEnvironmentDefinition().Topology).ShouldBe(topology);
        });
    }

    [Theory]
    [InlineData("http.client", "BaseAddress", "https://saved/", "Authentication", "Username", "Username")]
    [InlineData("mqtt.client", "Address", "mqtt://saved:1883", "ClientCertificate", "Path", "File")]
    [InlineData("mqtt.client", "Address", "mqtts://saved:8883", "ClientCertificate", "Path", "File")]
    public async Task Connection_grid_preserves_optional_groups_and_can_reset_them(string type, string endpoint, string address, string group, string property, string label)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(new JsonObject { ["Resources"] = new JsonObject { ["target"] = new JsonObject {
            ["Type"] = type, [endpoint] = address, [group] = new JsonObject { [property] = "saved", ["Password"] = "secret", ["Future"] = 42 } } },
            ["Workflows"] = new JsonObject { ["main"] = new JsonObject() } }.ToJsonString());
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:target" }));
            graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            if (type == "http.client")
            {
                // An omitted Retry block disables retries; opening the editor must not enable them.
                graph.Connections.Single().Settings.ContainsKey("Retry").ShouldBeFalse();
                await components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.StudioIconButton>().Last(item => item.Label == "Add Retry").OnClick.InvokeAsync(new MouseEventArgs());
                await Task.Yield();
                graph.Connections.Single().Settings["Retry"].ShouldBeOfType<JsonObject>();
                graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(before);
            }
            await components.Items.OfType<MudTextField<string>>().Last(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), label)).ValueChanged.InvokeAsync("edited");
            await Task.Yield();
            var edited = graph.ToProjectJson();
            graph.Connections.Single().Settings[group]!["Password"]!.GetValue<string>().ShouldBe("secret");
            graph.Connections.Single().Settings[group]!["Future"]!.GetValue<int>().ShouldBe(42);
            var reset = components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.StudioIconButton>().Last(item => item.Label == (type == "mqtt.client" ? "Reset Client certificate" : "Reset Authentication"));
            await reset.OnClick.InvokeAsync(new MouseEventArgs());
            await Task.Yield();
            graph.Connections.Single().Settings.ContainsKey(group).ShouldBeFalse();
            if (address.StartsWith("mqtt://", StringComparison.Ordinal))
                view.ToHtmlString().ShouldNotContain("aria-label=\"File\"");
            else if (type == "mqtt.client")
                components.Items.OfType<MudTextField<string>>().Last(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), label)).GetState(item => item.Value).ShouldBeNullOrEmpty();
            graph.Undo(); graph.ToProjectJson().ShouldBe(edited);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Connection_advanced_json_cancel_and_save_preserve_history(bool cancel)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://saved:1883","ClientId":"trial","Password":"secret","Future":42}},"Workflows":{"main":{}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var updated = graph.Connections.Single().Settings.DeepClone().AsObject(); updated["Username"] = "edited";
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(cancel ? DialogResult.Cancel() : DialogResult.Ok(updated.ToJsonString()));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:broker" }));
            await components.Items.OfType<MudButton>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Advanced connection JSON")).OnClick.InvokeAsync(new MouseEventArgs());
            if (cancel) { graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse(); return; }
            JsonNode.DeepEquals(graph.Connections.Single().Settings, updated).ShouldBeTrue();
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        });
    }
}
