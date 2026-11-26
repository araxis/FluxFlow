using System.Text.Json;
using FluxFlow.Application.Addressing;
using FluxFlow.Authoring;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Projects;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
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
    [InlineData("cancel", 10)]
    [InlineData("cancel", 11)]
    public async Task Windows_service_dialog_submits_only_the_selected_action(string action, int major)
    {
        var files = new ServiceActionFiles();
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IStudioFileAccess>(files);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"source":{"Type":"source","Values":"[1]"}}}}""");
        graph.ResetHistory();
        var saved = graph.ExportConfiguration();
        graph.ApplyEdits(graph.PrepareEdits([new ConfigureComponentEdit(ComponentAddress.Parse("main.source"),
            new Dictionary<string, JsonElement?> { ["Values"] = JsonSerializer.SerializeToElement("file:data/orders.json") })]));
        var edited = graph.ExportConfiguration();
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
                { d => d.SuggestedName, "Orders" }, { d => d.Export, () => graph.ExportCompiledConfigurationAsync(graph.ActiveEnvironmentId) }
            });
            var selector = components.Items.OfType<MudSelect<string>>().Single();
            selector.Value.ShouldBe("install");
            var submit = components.Items.OfType<MudButton>().Single(b => b.Variant == Variant.Filled);
            var picker = components.Items.OfType<InputFile>().Single();
            submit.Disabled.ShouldBeTrue();
            await components.Items.OfType<MudSelect<int>>().Single().ValueChanged.InvokeAsync(major);
            var chosen = new InputFileChangeEventArgs([new OrdersFile()]);
            await picker.OnChange.InvokeAsync(chosen);
            submit.Disabled.ShouldBeFalse();
            await picker.OnChange.InvokeAsync(new InputFileChangeEventArgs([]));
            submit.Disabled.ShouldBeTrue();
            await picker.OnChange.InvokeAsync(chosen);
            if (action == "cancel")
            {
                await components.Items.OfType<MudButton>().Single(b => b.Variant == Variant.Text).OnClick.InvokeAsync(new MouseEventArgs());
                (await dialog.Result)!.Canceled.ShouldBeTrue();
                files.Request.ShouldBeNull();
            }
            else
            {
                if (action == "install")
                    await components.Items.OfType<MudCheckBox<bool>>().Single().ValueChanged.InvokeAsync(true);
                else await selector.ValueChanged.InvokeAsync(action);
                submit.Disabled.ShouldBeFalse();
                await submit.OnClick.InvokeAsync(new MouseEventArgs());
                view.ToHtmlString().ShouldContain("Access denied");
                dialog.Result!.IsCompleted.ShouldBeFalse();
                files.Allow = true;
                await submit.OnClick.InvokeAsync(new MouseEventArgs());
                (await dialog.Result)!.Canceled.ShouldBeFalse();
                files.Request.ShouldBe(new StudioServiceAction(action, "Orders", major, action == "install"));
                files.Configuration.ShouldBe(action == "install" ? edited : "");
            }
            graph.ExportConfiguration().ShouldBe(edited);
            graph.Undo();
            graph.ExportConfiguration().ShouldBe(saved);
            graph.Redo();
            graph.ExportConfiguration().ShouldBe(edited);
        });
    }

    private sealed class OrdersFile : IBrowserFile
    {
        internal static readonly byte[] Contents = [0xef, 0xbb, 0xbf, 91, 13, 10, 93];
        public string Name => "orders.json";
        public DateTimeOffset LastModified => default;
        public long Size => Contents.Length;
        public string ContentType => "application/json";
        public Stream OpenReadStream(long maxAllowedSize, CancellationToken cancellationToken) => new MemoryStream(Contents);
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
            if (request.Action == "install")
            {
                using var input = sources.Single().Value();
                sources.Keys.Single().ShouldBe("data/orders.json");
                using var copy = new MemoryStream();
                input.CopyTo(copy);
                copy.ToArray().ShouldBe(OrdersFile.Contents);
            }
            else sources.ShouldBeEmpty();
            if (!Allow) throw new InvalidOperationException("Access denied");
            return Task.CompletedTask;
        }

        public Task<StudioFile?> OpenAsync(string extension) => throw new NotSupportedException();
        public Task<StudioImageFile?> OpenImageAsync() => throw new NotSupportedException();
        public Task<StudioFile?> SaveAsync(StudioFile file, bool saveAs = false) => throw new NotSupportedException();
    }
}
