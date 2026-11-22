using FluxFlow.Authoring;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Pages;
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
    public async Task Connection_property_history_keeps_the_inspector_open_when_the_connection_still_exists()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://localhost","Password":"retained-secret"}},"Workflows":{"main":{"log":{"Type":"logger"}}}}""");
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        graph.ResetHistory();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            var page = components.Items.OfType<DesignerPage>().Single();
            await EventCallback.Factory.Create<string>(page, page.OpenConnection).InvokeAsync("Resources:broker");
            var before = graph.ToProjectJson();
            await components.Items.OfType<MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Session expiry"))
                .ValueChanged.InvokeAsync("4294967295");
            var after = graph.ToProjectJson();
            after.ShouldNotBe(before);
            foreach (var action in new[] { "Undo", "Redo" })
            {
                await components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.StudioIconButton>()
                    .Last(button => button.Label == action).OnClick.InvokeAsync();
                await Task.Yield();
                graph.ToProjectJson().ShouldBe(action == "Undo" ? before : after);
                view.ToHtmlString().ShouldContain("aria-label=\"Connection properties\"");
                graph.HasSelection.ShouldBeFalse();
                components.Items.OfType<ConnectionInspector>().Last().Selection.ShouldBe("Resources:broker");
                graph.Connections.Single().Settings["Password"]!.GetValue<string>().ShouldBe("retained-secret");
            }
            graph.RemoveConnection("Resources:broker");
            await Task.Yield();
            view.ToHtmlString().ShouldNotContain("aria-label=\"Connection properties\"");
        });
    }

    [Theory]
    [InlineData(false, "apply")]
    [InlineData(true, "apply")]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    [InlineData(false, "stale")]
    [InlineData(true, "stale")]
    public async Task Connection_lifecycle_edits_preserve_credentials_history_and_reject_stale_results(bool remove, string outcome)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://localhost","Password":"retained-test-value"}},"Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","Connection":"broker"}}}}""");
        graph.ResetHistory();
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            if (outcome == "stale") graph.AddEnvironment("Changed");
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(outcome == "cancel" ? DialogResult.Cancel() : DialogResult.Ok(true));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            var page = components.Items.OfType<DesignerPage>().Single();
            await EventCallback.Factory.Create<string>(page, page.OpenConnection).InvokeAsync("Resources:broker");
            var before = graph.ToProjectJson();
            if (remove)
            {
                var menu = components.Items.OfType<MudMenu>().Last(menu => menu.AriaLabel == "Inspector panel menu");
                await renderer.RenderComponentAsync<MudElement>(ParameterView.FromDictionary(new Dictionary<string, object?>
                    { [nameof(MudElement.ChildContent)] = menu.ChildContent }));
                await components.Items.OfType<MudMenuItem>().Last(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Remove connection")).OnClick.InvokeAsync();
            }
            else
            {
                await components.Items.OfType<MudButton>().Last(button => Equals(button.UserAttributes?.GetValueOrDefault("aria-label"), "Rename connection")).OnClick.InvokeAsync();
                var field = components.Items.OfType<MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Connection name"));
                await field.ValueChanged.InvokeAsync("renamed");
                if (outcome == "stale") graph.AddEnvironment("Changed");
                await field.OnKeyDown.InvokeAsync(new() { Key = outcome == "cancel" ? "Escape" : "Enter" });
                await field.OnBlur.InvokeAsync(new()); // Blur following Enter or Escape must not commit again.
            }
            if (outcome != "apply")
            {
                graph.Connections.Single().Name.ShouldBe("broker");
                graph.Connections.Single().Settings["Password"]!.GetValue<string>().ShouldBe("retained-test-value");
                if (outcome == "cancel") { graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse(); }
                return;
            }
            var after = graph.ToProjectJson();
            if (remove) graph.Connections.ShouldBeEmpty();
            else
            {
                components.Items.OfType<ConnectionInspector>().Last().Selection.ShouldBe("Resources:renamed");
                graph.Connections.Single().Settings["Password"]!.GetValue<string>().ShouldBe("retained-test-value");
            }
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Fact]
    public async Task Connections_use_docked_inspector_and_restore_component_selection_without_editing()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://saved:1883","Password":"saved-secret"}},"Workflows":{"main":{"log":{"Type":"logger"}}}}""");
        graph.ResetHistory();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            var page = components.Items.OfType<DesignerPage>().Single();
            graph.Diagram.SetZoom(.8); graph.Diagram.SetPan(20, 30);
            var before = graph.ToProjectJson();
            await EventCallback.Factory.Create<string>(page, page.OpenConnection).InvokeAsync("Resources:broker");
            view.ToHtmlString().ShouldContain("aria-label=\"Connection properties\"");
            view.ToHtmlString().ShouldNotContain("studio-designer-page\" hidden");
            view.ToHtmlString().ShouldNotContain("studio-settings-page");
            components.Items.OfType<ConnectionInspector>().Last().Selection.ShouldBe("Resources:broker");
            graph.Diagram.Zoom.ShouldBe(.8);
            graph.Diagram.Pan.ShouldBe(new Blazor.Diagrams.Core.Geometry.Point(20, 30));
            graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            page.BeginInspectorEdit();
            await components.Items.OfType<MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Address"))
                .ValueChanged.InvokeAsync("mqtt://edited:1883");
            graph.Connections.Single().Settings["Address"]!.GetValue<string>().ShouldBe("mqtt://edited:1883");
            graph.Connections.Single().Settings["Password"]!.GetValue<string>().ShouldBe("saved-secret");
            graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(before);
            graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
            await Task.Yield();
            view.ToHtmlString().ShouldNotContain("aria-label=\"Connection properties\"");
            view.ToHtmlString().ShouldContain("Component name");
            graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            await EventCallback.Factory.Create<string>(page, page.OpenConnection).InvokeAsync("Resources:broker");
            graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://another:1883"}},"Workflows":{"main":{}}}""");
            await Task.Yield();
            view.ToHtmlString().ShouldNotContain("aria-label=\"Connection properties\"");
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("add")]
    [InlineData("stale")]
    public async Task Add_connection_opens_inspector_only_after_success_and_is_undoable(string outcome)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"log":{"Type":"logger"}}}}""");
        graph.ResetHistory();
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
        dialogs.DialogInstanceAddedAsync += reference =>
        {
            if (outcome == "stale") graph.AddEnvironment("Changed while adding");
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(outcome == "cancel" ? DialogResult.Cancel() : DialogResult.Ok(new ConfigurationResource("broker", null,
                new() { ["Type"] = "mqtt.client", ["Address"] = "mqtt://localhost:1883" })));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            var page = components.Items.OfType<DesignerPage>().Single();
            var before = graph.ToProjectJson();
            page.BeginInspectorEdit();
            await EventCallback.Factory.Create(page, page.AddConnection).InvokeAsync();
            if (outcome != "add")
            {
                graph.Connections.ShouldBeEmpty();
                view.ToHtmlString().ShouldNotContain("aria-label=\"Connection properties\"");
                if (outcome == "cancel") { graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse(); }
                return;
            }
            var after = graph.ToProjectJson();
            view.ToHtmlString().ShouldContain("aria-label=\"Connection properties\"");
            graph.Connections.Single().Name.ShouldBe("broker");
            await components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.StudioIconButton>()
                .Last(button => button.Label == "Undo").OnClick.InvokeAsync();
            await Task.Yield(); graph.ToProjectJson().ShouldBe(before);
            view.ToHtmlString().ShouldNotContain("aria-label=\"Connection properties\"");
            await components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.StudioIconButton>()
                .Last(button => button.Label == "Redo").OnClick.InvokeAsync();
            graph.ToProjectJson().ShouldBe(after);
        });
    }
}
