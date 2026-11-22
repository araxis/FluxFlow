using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components.Workspace;
using FluxFlow.Studio.UI.Features.Designer.Pages;
using FluxFlow.Studio.UI.Features.Environments.Topology;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Fact]
    public async Task Canvas_label_choices_survive_page_recreation_without_touching_project_history()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 91 });
        var edited = graph.ToProjectJson();
        var state = scope.ServiceProvider.GetRequiredService<CanvasViewState>();
        await using (var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>()))
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
                var topology = await renderer.RenderComponentAsync<EnvironmentTopologyPage>();
                await components.Items.OfType<CanvasLabelToggle>().Last().VisibleChanged.InvokeAsync(false);
                topology.ToHtmlString().ShouldContain("hide-connection-labels");
                state.ShowTopologyConnectionLabels.ShouldBeFalse();
                state.ShowWorkflowConnectionLabels.ShouldBeTrue();
            });
        }
        await using (var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>()))
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
                var topology = await renderer.RenderComponentAsync<EnvironmentTopologyPage>();
                topology.ToHtmlString().ShouldContain("hide-connection-labels");
            });
        }
        await using (var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>()))
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var workflow = await renderer.RenderComponentAsync<DesignerPage>();
                workflow.ToHtmlString().ShouldNotContain("hide-connection-labels");
                await components.Items.OfType<CanvasLabelToggle>().Last().VisibleChanged.InvokeAsync(false);
                workflow.ToHtmlString().ShouldContain("hide-connection-labels");
                state.ShowWorkflowConnectionLabels.ShouldBeFalse();
            });
        }
        graph.ToProjectJson().ShouldBe(edited);
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo(); graph.ToProjectJson().ShouldBe(edited);
        await using var otherScope = provider.CreateAsyncScope();
        var otherState = otherScope.ServiceProvider.GetRequiredService<CanvasViewState>();
        otherState.ShowTopologyConnectionLabels.ShouldBeTrue();
        otherState.ShowWorkflowConnectionLabels.ShouldBeTrue();
    }
}
