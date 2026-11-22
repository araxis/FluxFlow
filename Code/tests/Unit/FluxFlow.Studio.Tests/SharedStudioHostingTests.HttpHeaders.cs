using FluxFlow.Plugins;
using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Shared.Configuration;
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
    [Fact]
    public async Task HTTP_header_dialog_revalidates_after_a_different_invalid_draft()
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var native = StudioTestServices.HttpConnection().PropertyGroups!(new JsonObject())
                .SelectMany(group => group.StringMaps).Single().Validate!;
            var dialog = await dialogs.ShowAsync<StringMapEntryDialog>("Add header", new DialogParameters<StringMapEntryDialog>
            {
                { item => item.Entry, new StringMapEntry("Accept", "application/json") },
                { item => item.Validate, (Func<string, string, string?>)((name, value) => name == "Accept" ? "Duplicate name." : native(name, value)) }
            });
            var save = components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary);
            var name = components.Items.OfType<MudTextField<string>>().Single(field => field.Label == "Name");
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            name.GetState(field => field.ErrorText).ShouldBe("Duplicate name.");
            await name.ValueChanged.InvokeAsync("Bad name");
            await Task.Yield();
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            name.GetState(field => field.Error).ShouldBeTrue();
            name.GetState(field => field.ErrorText).ShouldBe("Enter a valid HTTP request header name and value.");
            dialog.Result!.IsCompleted.ShouldBeFalse();
            await name.ValueChanged.InvokeAsync("X-Valid");
            await Task.Yield();
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            (await dialog.Result)!.Data.ShouldBe(new StringMapEntry("X-Valid", "application/json"));
        });
    }

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
    public async Task HTTP_header_collection_preserves_credentials_casing_and_history(string action)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client","BaseAddress":"https://saved/","hEaDeRs":{"X-Saved":"original","X-Other":"other"},"Authentication":{"Scheme":"Basic","Password":"saved-secret"},"Future":42}},"Workflows":{"main":{}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var before = graph.ToProjectJson();
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            if (action == "stale") { graph.AddEnvironment("Changed"); graph.Undo(); }
            if (action == "dispose") components.Items.OfType<StringMapPropertyGrid>().Last().Dispose();
            var result = action switch
            {
                "unchanged" => new StringMapEntry("X-Saved", "original"),
                "duplicate" => new StringMapEntry("x-other", "replacement"),
                "invalid" => new StringMapEntry("Bad name", "value"),
                "add" => new StringMapEntry("X-New", ""),
                _ => new StringMapEntry("X-Renamed", "  preserved value  ")
            };
            reference.Dismiss(action == "cancel" ? DialogResult.Cancel() : DialogResult.Ok(result));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:api" }));
            view.ToHtmlString().ShouldNotContain("original");
            graph.ToProjectJson().ShouldBe(before);
            var label = action == "add" ? "Add header" : action == "remove" ? "Remove header X-Saved" : "Edit header X-Saved";
            await components.Items.OfType<StudioIconButton>().Last(button => button.Label == label).OnClick.InvokeAsync(new MouseEventArgs());
        });
        if (action is not ("add" or "edit" or "remove"))
        { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); return; }
        var settings = graph.Connections.Single().Settings;
        settings.ContainsKey("Headers").ShouldBeFalse();
        var headers = settings["hEaDeRs"]!.AsObject();
        headers["X-Other"]!.GetValue<string>().ShouldBe("other");
        if (action == "add") headers["X-New"]!.GetValue<string>().ShouldBeEmpty();
        else headers.ContainsKey("X-Saved").ShouldBeFalse();
        if (action == "edit") headers["X-Renamed"]!.GetValue<string>().ShouldBe("  preserved value  ");
        settings["Authentication"]!["Password"]!.GetValue<string>().ShouldBe("saved-secret");
        settings["Future"]!.GetValue<int>().ShouldBe(42);
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"X-Unknown\":42}")]
    public async Task HTTP_headers_keep_unsupported_saved_shapes_in_Advanced_JSON(string headers)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client","BaseAddress":"https://saved/","Headers":HEADERS}},"Workflows":{"main":{}}}""".Replace("HEADERS", headers));
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:api" }));
            components.Items.OfType<StudioIconButton>().Single(button => button.Label == "Add header").Disabled.ShouldBeTrue();
            view.ToHtmlString().ShouldContain("Use Advanced JSON to edit this saved value.");
            components.Items.OfType<OptionEditorField>().ShouldContain(field => field.Option.Name == "Headers");
        });
        graph.ToProjectJson().ShouldBe(before);
    }

    [Fact]
    public void HTTP_header_validation_uses_native_rules_without_exposing_values()
    {
        var map = StudioTestServices.HttpConnection().PropertyGroups!(new JsonObject())
            .SelectMany(group => group.StringMaps).Single();
        map.Validate!("Accept", "application/json").ShouldBeNull();
        map.Validate!("Content-Type", "application/json").ShouldNotBeNull();
        map.Validate!("X-Test", "private\r\nInjected: value").ShouldNotBeNull();
        var error = map.Validate!("Bad name", "private");
        error.ShouldNotBeNull();
        error!.ShouldNotContain("private");
        map.SecretValues.ShouldBeTrue();
    }
}
