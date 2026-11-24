using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Projects;
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
    [InlineData("install", 10)]
    [InlineData("install", 11)]
    [InlineData("start", 11)]
    [InlineData("stop", 11)]
    [InlineData("cancel", 11)]
    public async Task Windows_service_dialog_submits_only_the_selected_action(string action, int major)
    {
        const string configuration = """{"Workflows":{"main":{"unsaved":{"Type":"logger"}}}}""";
        var files = new ServiceActionFiles();
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IStudioFileAccess>(files);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference =>
            {
                reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
                return Task.CompletedTask;
            };
            var dialog = await dialogs.ShowAsync<WindowsServiceDialog>("Windows service", new DialogParameters<WindowsServiceDialog>
            {
                { d => d.SuggestedName, "Orders" }, { d => d.Export, () => Task.FromResult(configuration) }
            });
            var selector = components.Items.OfType<MudSelect<string>>().Single();
            selector.Value.ShouldBe("install");
            if (action == "cancel")
            {
                await components.Items.OfType<MudButton>().Single(b => b.Variant == Variant.Text).OnClick.InvokeAsync(new MouseEventArgs());
                (await dialog.Result)!.Canceled.ShouldBeTrue();
                files.Request.ShouldBeNull();
                return;
            }
            if (action == "install")
            {
                await components.Items.OfType<MudSelect<int>>().Single().ValueChanged.InvokeAsync(major);
                await components.Items.OfType<MudCheckBox<bool>>().Single().ValueChanged.InvokeAsync(true);
            }
            else await selector.ValueChanged.InvokeAsync(action);
            var submit = components.Items.OfType<MudButton>().Single(b => b.Variant == Variant.Filled);
            submit.Disabled.ShouldBeFalse();
            await submit.OnClick.InvokeAsync(new MouseEventArgs());
            view.ToHtmlString().ShouldContain("Access denied");
            dialog.Result!.IsCompleted.ShouldBeFalse();
            files.Allow = true;
            await submit.OnClick.InvokeAsync(new MouseEventArgs());
            (await dialog.Result)!.Canceled.ShouldBeFalse();
            files.Request.ShouldBe(new StudioServiceAction(action, "Orders", major, action == "install"));
            files.Configuration.ShouldBe(action == "install" ? configuration : "");
        });
    }

    private sealed class ServiceActionFiles : IStudioFileAccess
    {
        public StudioServiceAction? Request { get; private set; }
        public string? Configuration { get; private set; }
        public bool Allow { get; set; }

        public Task ManageWindowsServiceAsync(StudioServiceAction request, string configuration, IReadOnlyDictionary<string, Func<Stream>> sources)
        {
            Request = request;
            Configuration = configuration;
            sources.ShouldBeEmpty();
            if (!Allow) throw new InvalidOperationException("Access denied");
            return Task.CompletedTask;
        }

        public Task<StudioFile?> OpenAsync(string extension) => throw new NotSupportedException();
        public Task<StudioImageFile?> OpenImageAsync() => throw new NotSupportedException();
        public Task<StudioFile?> SaveAsync(StudioFile file, bool saveAs = false) => throw new NotSupportedException();
    }
}
