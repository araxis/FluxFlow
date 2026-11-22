using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
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
    [Theory]
    [InlineData("{}", "Scheme")]
    [InlineData("{\"Scheme\":null}", "Scheme")]
    [InlineData("{\"Scheme\":\"\"}", "Scheme")]
    [InlineData("{\"Scheme\":\"Basic\"}", "Scheme,Username,Password")]
    [InlineData("{\"sChEmE\":\"bearer\"}", "Scheme,Token")]
    [InlineData("{\"Scheme\":\"OAuthClientCredentials\"}", "Scheme,TokenEndpoint,ClientId,ClientSecret,Scope,ClientCredentialStyle")]
    [InlineData("{\"Scheme\":\"Bearer\",\"pAsSwOrD\":\"\",\"Scope\":\"saved\"}", "Scheme,Password,Token,Scope")]
    [InlineData("{\"Scheme\":\"Basic\",\"Token\":null}", "Scheme,Username,Password")]
    [InlineData("{\"Token\":42}", "Scheme,Token")]
    [InlineData("{\"Scheme\":\"Future\"}", "Scheme,Username,Password,Token,TokenEndpoint,ClientId,ClientSecret,Scope,ClientCredentialStyle")]
    [InlineData("{\"Scheme\":42}", "Scheme,Username,Password,Token,TokenEndpoint,ClientId,ClientSecret,Scope,ClientCredentialStyle")]
    public void HTTP_authentication_visibility_preserves_saved_and_unknown_values(string json, string visible)
    {
        var group = StudioTestServices.HttpConnection().PropertyGroups!(new JsonObject()).Single(item => item.Path == "Authentication");
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        var before = JsonSerializer.Serialize(values);
        string.Join(',', group.Options.Where(option => group.IsVisible!(values, option.Name)).Select(option => option.Name)).ShouldBe(visible);
        JsonSerializer.Serialize(values).ShouldBe(before);
    }

    [Fact]
    public async Task HTTP_authentication_switching_keeps_credentials_repairable_and_undoable()
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client","BaseAddress":"https://saved/","aUtHeNtIcAtIoN":{"sChEmE":"Basic","Username":"saved-user","Password":"saved-secret","Future":42}}},"Workflows":{"main":{}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:api" }));
            MudSelect<string> Scheme() => components.Items.OfType<MudSelect<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Scheme"));
            MudTextField<string> Field(string label) => components.Items.OfType<MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), label));
            view.ToHtmlString().ShouldNotContain("aria-label=\"Token\"");
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await Scheme().ValueChanged.InvokeAsync("Bearer"); await Task.Yield();
            view.ToHtmlString().ShouldContain("aria-label=\"Token\"");
            view.ToHtmlString().ShouldContain("aria-label=\"Username\"");
            Field("Password").InputType.ShouldBe(InputType.Password);
            var changed = graph.ToProjectJson();
            var auth = graph.Connections.Single().Settings["aUtHeNtIcAtIoN"]!.AsObject();
            auth["sChEmE"]!.GetValue<string>().ShouldBe("Bearer");
            auth["Password"]!.GetValue<string>().ShouldBe("saved-secret");
            auth["Future"]!.GetValue<int>().ShouldBe(42);
            auth.ContainsKey("Scheme").ShouldBeFalse();
            graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(before);
            view.ToHtmlString().ShouldNotContain("aria-label=\"Token\"");
            graph.Redo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(changed);
            await Field("Username").ValueChanged.InvokeAsync(""); await Task.Yield();
            await components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Reset Password").OnClick.InvokeAsync(); await Task.Yield();
            view.ToHtmlString().ShouldNotContain("aria-label=\"Username\"");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Password\"");
            await Field("Token").ValueChanged.InvokeAsync("saved-token"); await Task.Yield();
            var repaired = graph.ToProjectJson();
            await Scheme().ValueChanged.InvokeAsync("OAuthClientCredentials"); await Task.Yield();
            view.ToHtmlString().ShouldContain("aria-label=\"Client ID\"");
            view.ToHtmlString().ShouldContain("aria-label=\"Token\"");
            graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(repaired);
            var reset = components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Reset Authentication");
            await reset.OnClick.InvokeAsync(new MouseEventArgs()); await Task.Yield();
            graph.Connections.Single().Settings.ContainsKey("aUtHeNtIcAtIoN").ShouldBeFalse();
            view.ToHtmlString().ShouldNotContain("aria-label=\"Token\"");
            graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(repaired);
            graph.LoadProject(repaired); graph.ToProjectJson().ShouldBe(repaired);
        });
    }
}
