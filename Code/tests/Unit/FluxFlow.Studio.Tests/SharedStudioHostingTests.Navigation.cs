using FluxFlow.Studio.UI.Components.Navigation;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Pages;
using FluxFlow.Studio.UI.Features.Designer.Projects;
using FluxFlow.Studio.UI.Features.Designer.Settings;
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
    public async Task Stale_environment_name_edits_do_not_change_the_project_or_run_selection()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.AddEnvironment("Lab");
        graph.ResetHistory();
        var lab = graph.ActiveEnvironmentId;
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            graph.SelectEnvironment("local");
            graph.SelectEnvironment(lab);
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(DialogResult.Ok("Late addition"));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            var page = components.Items.OfType<DesignerPage>().Single();
            await components.Items.OfType<StudioRunControls>().Last().EnvironmentChanged.InvokeAsync("local");
            page.SelectEnvironment(lab);
            await page.SetParametersAsync(ParameterView.Empty);
            var rename = components.Items.OfType<TopologyInspector>().Last().RenameEnvironment;
            var before = graph.ToProjectJson();
            components.Items.OfType<StudioRunControls>().Last().EnvironmentId.ShouldBe("local");
            await page.AddEnvironment();
            await rename.InvokeAsync("Late rename");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.CanRedo.ShouldBeFalse();
            components.Items.OfType<StudioRunControls>().Last().EnvironmentId.ShouldBe("local");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Opening_project_uses_its_saved_run_environment_without_changing_configuration_or_history(bool useDefaultId)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var files = new ProjectSelectionFiles();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IStudioFileAccess>(files);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        if (useDefaultId) graph.RenameEnvironment("Bound services");
        else graph.AddEnvironment("Bound services");
        var selected = graph.ActiveEnvironmentId;
        var saved = graph.ToProjectJson();
        files.File = new("trial.fluxflow", saved);
        graph.LoadJson("""{"Workflows":{"main":{}}}""");
        graph.ResetHistory();
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(DialogResult.Ok(true));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            var page = components.Items.OfType<DesignerPage>().Single();
            await EventCallback.Factory.Create(page, () => page.Load(true)).InvokeAsync();
            graph.ActiveEnvironmentId.ShouldBe(selected);
            components.Items.OfType<StudioRunControls>().Last().EnvironmentId.ShouldBe(selected);
            graph.ToProjectJson().ShouldBe(saved);
            graph.CanUndo.ShouldBeFalse(); graph.CanRedo.ShouldBeFalse();

            graph.RenameEnvironment("Renamed services");
            await page.SetParametersAsync(ParameterView.Empty);
            components.Items.OfType<StudioRunControls>().Last().EnvironmentId.ShouldBe(selected);
            graph.Undo();
            await page.SetParametersAsync(ParameterView.Empty);
            graph.EnvironmentName.ShouldBe("Bound services");
            components.Items.OfType<StudioRunControls>().Last().EnvironmentId.ShouldBe(selected);
            graph.ToProjectJson().ShouldBe(saved);
            graph.Redo();
            await page.SetParametersAsync(ParameterView.Empty);
            graph.EnvironmentName.ShouldBe("Renamed services");
            components.Items.OfType<StudioRunControls>().Last().EnvironmentId.ShouldBe(selected);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelling_project_open_preserves_run_selection_configuration_and_history(bool selectFile)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var files = new ProjectSelectionFiles();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IStudioFileAccess>(files);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        if (selectFile) files.File = new("other.fluxflow", graph.ToProjectJson());
        graph.AddEnvironment("Selected environment");
        var selected = graph.ActiveEnvironmentId;
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        graph.RenameEnvironment("Renamed environment");
        var edited = graph.ToProjectJson();
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(DialogResult.Cancel());
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            var page = components.Items.OfType<DesignerPage>().Single();
            await EventCallback.Factory.Create(page, () => page.Load(true)).InvokeAsync();
            components.Items.OfType<StudioRunControls>().Last().EnvironmentId.ShouldBe(selected);
            graph.ToProjectJson().ShouldBe(edited);
            graph.CanUndo.ShouldBeTrue(); graph.CanRedo.ShouldBeFalse();
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(edited);
        });
    }

    private sealed class ProjectSelectionFiles : IStudioFileAccess
    {
        public StudioFile? File { get; set; }
        public Task<StudioFile?> OpenAsync(string extension) { extension.ShouldBe(".fluxflow"); return Task.FromResult(File); }
        public Task<StudioImageFile?> OpenImageAsync() => throw new InvalidOperationException("No image is requested.");
        public Task<StudioFile?> SaveAsync(StudioFile file, bool saveAs = false) => throw new InvalidOperationException("Opening must not save files.");
    }

    [Fact]
    public async Task Navigation_overflow_keeps_action_state_and_history_across_width_changes()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
        var original = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            RenderFragment actions = builder =>
            {
                builder.OpenComponent<StudioNavigationAction>(0);
                builder.AddAttribute(1, "Label", "Undo environment change");
                builder.AddAttribute(2, "Icon", Icons.Material.Outlined.Undo);
                builder.AddAttribute(3, "Disabled", !graph.CanUndo);
                builder.AddAttribute(4, "OnClick", EventCallback.Factory.Create(this, graph.Undo));
                builder.CloseComponent();
            };
            RenderFragment content = builder =>
            {
                builder.OpenComponent<StudioNavigationActions>(0);
                builder.AddAttribute(1, "ChildContent", actions);
                builder.CloseComponent();
            };
            await renderer.RenderComponentAsync<CascadingValue<Breakpoint>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Value"] = Breakpoint.Lg, ["ChildContent"] = content
            }));
            var cascade = components.Items.OfType<CascadingValue<Breakpoint>>().Single();
            async Task Resize(Breakpoint width) => await cascade.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Value"] = width, ["ChildContent"] = content
            }));
            components.Items.OfType<StudioNavigationAction>().Last().Overflow.ShouldBeFalse();
            await Resize(Breakpoint.Sm);
            var menu = components.Items.OfType<MudMenu>().Last();
            menu.AriaLabel.ShouldBe("More page actions");
            // Static rendering does not mount popovers; render the menu's actual content.
            await renderer.RenderComponentAsync<CascadingValue<string>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Value"] = "overflow", ["ChildContent"] = menu.ChildContent
            }));
            var action = components.Items.OfType<StudioNavigationAction>().Last();
            action.Overflow.ShouldBeTrue();
            action.Disabled.ShouldBeFalse();
            graph.ToProjectJson().ShouldBe(original);
            await action.OnClick.InvokeAsync();
            graph.GetEnvironment().StartupTimeoutSeconds.ShouldBe(60);
            await Resize(Breakpoint.Lg);
            action = components.Items.OfType<StudioNavigationAction>().Last();
            action.Overflow.ShouldBeFalse();
            action.Disabled.ShouldBeTrue();
            graph.CanRedo.ShouldBeTrue();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(original);
        });
    }

    [Fact]
    public async Task Shared_navigation_moves_page_actions_and_routes_catalog_history_without_editing_the_project()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.IsProjectOpen = true;
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
        var original = graph.ToProjectJson();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        await preferences.SaveAsync(preferences.Value with { Environments = preferences.Value.Environments with {
            Infrastructure = [new() { Id = "p1", Name = "P1" }] } });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            var page = components.Items.OfType<DesignerPage>().Single();
            async Task Open(string section)
            {
                page.OpenEnvironment(section);
                await page.SetParametersAsync(ParameterView.Empty);
            }
            string Header() => view.ToHtmlString().Split("</header>")[0];

            await Open("Infrastructure");
            Header().ShouldContain("Host definitions");
            Header().ShouldContain("Add host");
            Header().ShouldContain("Back to designer");
            Header().ShouldContain(">View");
            Header().ShouldNotContain("Run application locally");
            view.ToHtmlString().ShouldNotContain("settings-page-header");
            view.ToHtmlString().ShouldNotContain("catalog-toolbar");
            var catalogGraph = components.Items.OfType<EnvironmentTopologyPage>().Last().WorkspaceGraph!;
            var topology = catalogGraph.GetEnvironmentDefinition().Topology;
            topology.Nodes[0].Name = "P1 renamed";
            catalogGraph.SetTopology(topology);
            var menus = components.Items.OfType<StudioDesignerMenus>().Last();
            menus.Commands!.CanUndo.ShouldBeTrue();
            await menus.Commands.Undo.InvokeAsync();
            preferences.Value.Environments.Infrastructure[0].Name.ShouldBe("P1");
            await menus.Commands.Redo.InvokeAsync();
            preferences.Value.Environments.Infrastructure[0].Name.ShouldBe("P1 renamed");
            graph.ToProjectJson().ShouldBe(original);
            graph.CanUndo.ShouldBeTrue();

            await Open("Registries");
            Header().ShouldContain("Registries");
            Header().ShouldContain("Add registry");
            Header().ShouldNotContain("Add host");
            Header().ShouldContain(">View");
            Header().ShouldContain(">Edit");
            menus = components.Items.OfType<StudioDesignerMenus>().Last();
            menus.Commands.ShouldBeNull();
            await Open("Defaults");
            Header().ShouldContain("Resource defaults");
            Header().ShouldContain("Add profile");
            Header().ShouldNotContain("Add registry");
            await components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.ResourceDefaultsSettings>().Last().OpenRegistries.InvokeAsync();
            Header().ShouldContain("Registries");
            await Open("Defaults");
            Header().ShouldContain("Resource defaults");
            await components.Items.OfType<StudioPageChrome>().Last(chrome => chrome.Title == "Resource defaults").Back.InvokeAsync();
            Header().ShouldNotContain("FluxFlow Studio");
            Header().ShouldNotContain("Resource defaults");
            Header().ShouldContain(">File");
            Header().ShouldContain(">Edit");
            Header().ShouldContain(">View");
            Header().ShouldContain("Run application locally");
            Header().ShouldNotContain("Add profile");
            graph.ToProjectJson().ShouldBe(original);
            graph.Undo();
            graph.GetEnvironment().StartupTimeoutSeconds.ShouldBe(60);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(original);
        });
    }
}
