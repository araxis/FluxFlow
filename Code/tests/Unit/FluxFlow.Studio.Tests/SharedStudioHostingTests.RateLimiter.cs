using System.Text.Json.Nodes;
using System.Text.Json;
using FluxFlow.Plugins;
using FluxFlow.Plugins.FlowControl;
using FluxFlow.Studio.UI;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
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
    public void Rate_limiter_scope_metadata_survives_transport_and_preserves_older_plugin_defaults()
    {
        var wire = JsonSerializer.Serialize(new[] { new FlowControlPlugin().Description }, PluginJsonContext.Default.PluginDescriptionArray);
        var catalog = JsonSerializer.Deserialize(wire, PluginJsonContext.Default.PluginDescriptionArray)!;
        catalog.Single().Connections.Single().WorkflowResourcesAreLocal.ShouldBeTrue();
        using var provider = new ServiceCollection().AddStudioUI([catalog.Single() with { Design = null }]).BuildServiceProvider();
        provider.GetServices<ConnectionDescription>().Single(item => item.Type == "rateLimiter.tokenBucket")
            .WorkflowResourcesAreLocal.ShouldBeTrue();

        var legacy = JsonNode.Parse(wire)!;
        legacy[0]!["Connections"]![0]!.AsObject().Remove("WorkflowResourcesAreLocal").ShouldBeTrue();
        JsonSerializer.Deserialize(legacy.ToJsonString(), PluginJsonContext.Default.PluginDescriptionArray)!
            .Single().Connections.Single().WorkflowResourcesAreLocal.ShouldBeFalse();
    }

    [Fact]
    public async Task Rate_limiter_shared_grid_validates_edits_and_preserves_unknown_fields_and_history()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"budget":{"Type":"rateLimiter.tokenBucket","TokenLimit":3,"TokensPerPeriod":2,"ReplenishmentPeriod":"00:00:02.0000000","QueueLimit":7,"Future":{"Keep":true}}},"Workflows":{"main":{"limit":{"Type":"rateLimit","Limiter":"budget","Request":"limit.Reply"}}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:budget" }));
            var html = view.ToHtmlString();
            foreach (var label in new[] { "Burst size", "Refill count", "Refill interval", "Queue limit" }) html.ShouldContain(label);
            graph.ToProjectJson().ShouldBe(before);
            var burst = components.Items.OfType<MudTextField<string>>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Burst size"));
            await burst.ValueChanged.InvokeAsync("0");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            await burst.ValueChanged.InvokeAsync("3");
            graph.CanUndo.ShouldBeFalse();
            await burst.ValueChanged.InvokeAsync("5");
        });
        graph.Connections.Single().Settings["TokenLimit"]!.GetValue<int>().ShouldBe(5);
        graph.Connections.Single().Settings["ReplenishmentPeriod"]!.GetValue<string>().ShouldBe("00:00:02.0000000");
        graph.Connections.Single().Settings["Future"]!["Keep"]!.GetValue<bool>().ShouldBeTrue();
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    [Fact]
    public void Missing_flow_control_plugin_preserves_resources_wiring_and_history_through_reinstall()
    {
        var plugin = new FlowControlPlugin();
        var original = StudioTestServices.CreateGraph();
        original.LoadJson("""{"Resources":{"budget":{"Type":"rateLimiter.tokenBucket","QueueLimit":0,"Future":42}},"Workflows":{"main":{"limit":{"Type":"rateLimit","Limiter":"budget","Request":"limit.Reply","ResponseTimeout":"00:00:01"}}}}""");
        var saved = original.ToProjectJson();
        string? canonical = null;
        foreach (var installed in new[] { true, false, true })
        {
            using var services = new ServiceCollection().AddStudioUI(installed ? [plugin.Description] : []).BuildServiceProvider();
            var graph = new DesignerGraphState(services.GetServices<FluxFlow.Authoring.IComponentDescription>());
            graph.LoadProject(saved);
            graph.ResetHistory();
            saved = graph.ToProjectJson();
            canonical ??= saved;
            JsonNode.DeepEquals(JsonNode.Parse(saved), JsonNode.Parse(canonical)).ShouldBeTrue();
            graph.SetPortColorOverrides(new(Output: "#123456"));
            var edited = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(saved);
            graph.Redo(); graph.ToProjectJson().ShouldBe(edited);
        }
    }
}
