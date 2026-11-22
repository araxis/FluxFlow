using FluxFlow.Plugins.Management;
using FluxFlow.Studio.UI;
using FluxFlow.Studio.UI.Components.Navigation;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Pages;
using FluxFlow.Studio.UI.Features.Plugins;
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
    [InlineData("Manage plugins", false)]
    [InlineData("Manage plugins", true)]
    [InlineData("Open registries", false)]
    [InlineData("Open registries", true)]
    [InlineData("Open resource defaults", false)]
    [InlineData("Open resource defaults", true)]
    public async Task Plugin_source_menu_navigation_respects_discard_and_cancel(string destination, bool discard)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var manager = new SettingsPluginManager();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IPluginManager>(manager);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var confirmations = 0;
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            confirmations++;
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(discard ? DialogResult.Ok(true) : DialogResult.Cancel());
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            async Task OpenMenu(string label)
            {
                var menu = components.Items.OfType<MudMenu>().Last(item => item.Label?.StartsWith(label, StringComparison.Ordinal) == true);
                await renderer.RenderComponentAsync<MudElement>(ParameterView.FromDictionary(new Dictionary<string, object?>
                    { [nameof(MudElement.ChildContent)] = menu.ChildContent }));
            }
            MudMenuItem Menu(string label) => components.Items.OfType<MudMenuItem>().Last(item =>
                Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), label));
            await OpenMenu("Plugins");
            await Menu("Plugin settings").OnClick.InvokeAsync();
            graph.ResetHistory();
            var before = graph.ToProjectJson();
            var name = components.Items.OfType<MudTextField<string>>().Single(field => field.Label == "Name");
            await name.ValueChanged.InvokeAsync("Unsaved source");
            await OpenMenu(destination == "Manage plugins" ? "Plugins" : "Environment");
            await Menu(destination).OnClick.InvokeAsync();
            confirmations.ShouldBe(1);
            if (discard)
            {
                view.ToHtmlString().ShouldNotContain("class=\"plugin-settings\"");
                await OpenMenu("Plugins");
                await Menu("Plugin settings").OnClick.InvokeAsync();
                view.ToHtmlString().ShouldNotContain("Unsaved source");
            }
            else
            {
                view.ToHtmlString().ShouldContain("class=\"plugin-settings\"");
                name.Value.ShouldBe("Unsaved source");
            }
            manager.SaveCount.ShouldBe(0);
            manager.Saved.Single().Name.ShouldBe("Workshop");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.CanRedo.ShouldBeFalse();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Plugin_sources_discard_and_navigation_do_not_interrupt_a_pending_save(bool failSave)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var manager = new SettingsPluginManager { FailSave = failSave, SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IPluginManager>(manager);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<PluginSettingsPage>();
            var name = components.Items.OfType<MudTextField<string>>().Single(field => field.Label == "Name");
            var page = components.Items.OfType<PluginSettingsPage>().Single();
            var save = components.Items.OfType<StudioNavigationAction>().Single(action => action.Label == "Save sources");
            var discard = components.Items.OfType<StudioNavigationAction>().Single(action => action.Label == "Discard changes");
            await name.ValueChanged.InvokeAsync("Draft source");
            var pending = save.OnClick.InvokeAsync();
            save.Disabled.ShouldBeTrue();
            discard.Disabled.ShouldBeTrue();
            (await page.ConfirmLeaveAsync()).ShouldBeFalse();
            await discard.OnClick.InvokeAsync();
            name.Value.ShouldBe("Draft source");
            manager.SaveGate.SetResult();
            await pending;
            if (failSave)
            {
                save.Disabled.ShouldBeFalse();
                name.Value.ShouldBe("Draft source");
                manager.Saved.Single().Name.ShouldBe("Workshop");
                await discard.OnClick.InvokeAsync();
                save.Disabled.ShouldBeTrue();
                components.Items.OfType<MudTextField<string>>().Last(field => field.Label == "Name").Value.ShouldBe("Workshop");
            }
            else manager.Saved.Single().Name.ShouldBe("Draft source");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Plugin_sources_save_and_reload_failure_preserve_drafts_and_project_history(bool failReload)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var manager = new SettingsPluginManager { FailReload = failReload };
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IPluginManager>(manager);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<PluginSettingsPage>();
            view.ToHtmlString().ShouldContain("nuget.org");
            var name = components.Items.OfType<MudTextField<string>>().Single(field => field.Label == "Name");
            await name.ValueChanged.InvokeAsync("Renamed source");
            var save = components.Items.OfType<StudioNavigationAction>().Single(action => action.Label == "Save sources");
            save.Disabled.ShouldBeFalse();
            await save.OnClick.InvokeAsync();
            manager.Saved.Single().Name.ShouldBe("Renamed source");
            save.Disabled.ShouldBeTrue();
            if (failReload)
            {
                view.ToHtmlString().ShouldContain("Sources unavailable");
                name.Disabled.ShouldBeTrue();
                await save.OnClick.InvokeAsync();
                manager.SaveCount.ShouldBe(1);
                manager.FailReload = false;
                var retry = components.Items.OfType<MudButton>().Last(button => button.OnClick.HasDelegate);
                await retry.OnClick.InvokeAsync();
                view.ToHtmlString().ShouldNotContain("Sources unavailable");
            }
            var discard = components.Items.OfType<StudioNavigationAction>().Single(action => action.Label == "Discard changes");
            discard.Disabled.ShouldBeTrue();
            graph.ToProjectJson().ShouldBe(project);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();
        });
    }

    private sealed class SettingsPluginManager : IPluginManager
    {
        public PluginSource[] Saved { get; private set; } = [new("Workshop", "https://example.test/index.json")];
        public int SaveCount { get; private set; }
        public bool FailReload { get; set; }
        public bool FailSave { get; set; }
        public TaskCompletionSource? SaveGate { get; set; }
        public Task<PluginSource[]> GetSourcesAsync() => FailReload && SaveCount > 0
            ? Task.FromException<PluginSource[]>(new IOException("Temporary read failure")) : Task.FromResult(Saved);
        public async Task SaveSourcesAsync(PluginSourceChange change)
        {
            if (SaveGate is not null) await SaveGate.Task;
            if (FailSave) throw new IOException("Temporary write failure");
            change.Original.ShouldBe(Saved);
            Saved = change.Updated;
            SaveCount++;
        }
        public Task<PluginEntry[]> ListAsync() => Task.FromResult<PluginEntry[]>([]);
        public Task<string[]> ListFolderAsync(PluginSource source) => throw new NotSupportedException();
        public Task InstallAsync(string localPath) => throw new NotSupportedException();
        public Task InstallArchiveAsync(string name, Stream archive) => throw new NotSupportedException();
        public Task InstallFolderArchiveAsync(Stream archive) => throw new NotSupportedException();
        public Task InstallFeedAsync(PluginPackage package) => throw new NotSupportedException();
        public Task RemoveAsync(string id) => throw new NotSupportedException();
        public Task CancelAsync(string id) => throw new NotSupportedException();
    }
}
