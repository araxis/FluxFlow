using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
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
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"saved-secret\"")]
    public async Task HTTP_password_supports_explicit_empty_and_reset_without_losing_history(string initial)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var auth = new JsonObject { ["Scheme"] = "Basic", ["Username"] = "saved-user", ["Future"] = 42 };
        if (initial != "missing") auth["pAsSwOrD"] = JsonNode.Parse(initial);
        var connection = new JsonObject { ["Type"] = "http.client", ["BaseAddress"] = "https://saved/", ["Authentication"] = auth };
        graph.LoadJson(new JsonObject { ["Resources"] = new JsonObject { ["api"] = connection }, ["Workflows"] = new JsonObject { ["main"] = new JsonObject() } }.ToJsonString());
        graph.ResetHistory(); var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:api" }));
            MudTextField<string> Password() => components.Items.OfType<MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Password"));
            StudioIconButton Action(string label) => components.Items.OfType<StudioIconButton>().Last(button => button.Label == label);
            JsonObject Saved() => graph.Connections.Single().Settings["Authentication"]!.AsObject();
            Password().InputType.ShouldBe(InputType.Password);
            await Password().ValueChanged.InvokeAsync(Password().GetState(field => field.Value));
            graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            if (initial != "missing")
            {
                await Action("Reset Password").OnClick.InvokeAsync(); await Task.Yield();
                Saved().Any(pair => pair.Key.Equals("Password", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
                graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(before);
                graph.Redo(); await Task.Yield();
            }
            var absent = graph.ToProjectJson();
            await Action("Set empty Password").OnClick.InvokeAsync(); await Task.Yield();
            Saved().Single(pair => pair.Key.Equals("Password", StringComparison.OrdinalIgnoreCase)).Value!.GetValue<string>().ShouldBeEmpty();
            Password().Placeholder.ShouldBe("Empty");
            Saved()["Username"]!.GetValue<string>().ShouldBe("saved-user");
            Saved()["Future"]!.GetValue<int>().ShouldBe(42);
            var empty = graph.ToProjectJson();
            graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(absent);
            await Password().ValueChanged.InvokeAsync("");
            graph.ToProjectJson().ShouldBe(absent); graph.CanRedo.ShouldBeTrue();
            graph.Redo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(empty);
            await Password().ValueChanged.InvokeAsync("replacement"); await Task.Yield();
            await Password().ValueChanged.InvokeAsync(""); await Task.Yield();
            graph.ToProjectJson().ShouldBe(empty);
            var staleReset = Action("Reset Password");
            graph.Undo(); await Task.Yield();
            var replacement = graph.ToProjectJson();
            await staleReset.OnClick.InvokeAsync(); graph.ToProjectJson().ShouldBe(replacement);
            graph.Redo(); await Task.Yield();
            graph.LoadProject(empty); graph.ToProjectJson().ShouldBe(empty);
        });
    }
}
