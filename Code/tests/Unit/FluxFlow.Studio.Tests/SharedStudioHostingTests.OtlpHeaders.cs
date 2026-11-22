using FluxFlow.Plugins;
using System.Text.Json;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Plugins.Logging;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Shared.Configuration;
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
    [InlineData("add")]
    [InlineData("edit")]
    [InlineData("remove")]
    [InlineData("cancel")]
    [InlineData("unchanged")]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    [InlineData("stale")]
    [InlineData("dispose")]
    public async Task OTLP_header_collection_preserves_native_string_and_history(string action)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"collector":{"Type":"logging.otlp","Endpoint":"https://collector.test/v1/logs","hEaDeRs":" X-Saved = original== ,X-Other=a%2cb","Future":42}},"Workflows":{"main":{}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var before = graph.ToProjectJson();
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            if (action == "stale") { graph.AddEnvironment("Changed"); graph.Undo(); }
            if (action == "dispose") components.Items.OfType<StringMapPropertyGrid>().Last().Dispose();
            var entry = action switch
            {
                "unchanged" => new StringMapEntry("X-Saved", "original=="),
                "duplicate" => new StringMapEntry("x-other", "replacement"),
                "invalid" => new StringMapEntry("Bad name", "private"),
                "add" => new StringMapEntry("X-New", ""),
                _ => new StringMapEntry("X-Renamed", "a,b=c%20")
            };
            reference.Dismiss(action == "cancel" ? DialogResult.Cancel() : DialogResult.Ok(entry));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:collector" }));
            view.ToHtmlString().ShouldNotContain("original==");
            graph.ToProjectJson().ShouldBe(before);
            var label = action == "add" ? "Add header" : action == "remove" ? "Remove header X-Saved" : "Edit header X-Saved";
            await components.Items.OfType<StudioIconButton>().Last(button => button.Label == label).OnClick.InvokeAsync(new MouseEventArgs());
        });
        if (action is not ("add" or "edit" or "remove"))
        { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); return; }
        var settings = graph.Connections.Single().Settings;
        settings.ContainsKey("Headers").ShouldBeFalse();
        var text = settings["hEaDeRs"]!.GetValue<string>();
        text.ShouldContain("X-Other=a%2cb");
        var parsed = OtlpHeaders.Read(JsonSerializer.SerializeToElement(text))!;
        parsed.Single(entry => entry.Name == "X-Other").Value.ShouldBe("a,b");
        if (action == "add") { text.ShouldStartWith(" X-Saved = original== ,"); parsed.Single(entry => entry.Name == "X-New").Value.ShouldBeEmpty(); }
        else parsed.ShouldNotContain(entry => entry.Name == "X-Saved");
        if (action == "edit") parsed.Single(entry => entry.Name == "X-Renamed").Value.ShouldBe("a,b=c%20");
        settings["Future"]!.GetValue<int>().ShouldBe(42);
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"X-Test=first,x-test=second\"")]
    [InlineData("\"Bad name=private\"")]
    [InlineData("\"X-Test=private%0d%0aInjected\"")]
    public void OTLP_headers_retain_unrepresentable_values_for_JSON_repair(string json)
    {
        using var document = JsonDocument.Parse(json);
        OtlpHeaders.Read(document.RootElement).ShouldBeNull();
        document.RootElement.GetRawText().ShouldBe(json);
    }

    [Theory]
    [InlineData("https://collector.test/v1/logs", true)]
    [InlineData("http://collector.test:4317/", true)]
    [InlineData("ftp://collector.test/", false)]
    [InlineData("https://user:secret@collector.test/", false)]
    [InlineData("https://collector.test/#fragment", false)]
    [InlineData("relative", false)]
    [InlineData("", false)]
    public async Task OTLP_endpoint_validation_preserves_invalid_drafts_and_saved_values(string candidate, bool valid)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"collector":{"Type":"logging.otlp","endpoint":"https://saved.test/","Headers":"X-Saved=private","Future":42}},"Workflows":{"main":{}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:collector" }));
            await components.Items.OfType<MudTextField<string>>().Single(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Endpoint")).ValueChanged.InvokeAsync(candidate);
        });
        if (!valid) { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); return; }
        var settings = graph.Connections.Single().Settings;
        settings["endpoint"]!.GetValue<string>().ShouldBe(candidate); settings.ContainsKey("Endpoint").ShouldBeFalse();
        settings["Headers"]!.GetValue<string>().ShouldBe("X-Saved=private"); settings["Future"]!.GetValue<int>().ShouldBe(42);
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.Redo(); graph.ToProjectJson().ShouldBe(after);
    }
}
