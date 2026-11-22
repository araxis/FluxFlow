using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Projects;
using FluxFlow.Studio.UI.Features.Environments.Services;
using FluxFlow.Studio.UI.Features.Environments.Services.Documents;
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
    private static ServiceStartupFile PasswordFile => FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider.StartupFiles.Single(f => f.Format == ServiceDocumentKind.TextPreview);
    private const string PasswordText = "\uFEFFzoë:$7$101$opaque==\r\n# keep:comment\n<alice>:$6$second\runrecognized-line\r\nzoë:$future$third";

    [Fact]
    public void Mosquitto_password_preview_keeps_distinct_usernames_in_file_order()
    {
        const string source = "\uFEFF\u00a0alice:$7$salt$hash1\r\n\talice :$7$salt$hash2\n" +
            "val   smith:$7$salt$hash3\r\nval smith:$7$salt$hash4\n\u00a0alice:$7$salt$hash5";
        var file = PasswordFile;
        var draft = new ServiceDocumentDraft(file.Format!.Value, source, preview: file.Preview);
        draft.PreviewItems.ShouldBe(new[] { "\u00a0alice", "alice", "val   smith", "val smith", "\u00a0alice" });
        draft.Serialize().ShouldBe(source);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public async Task Mosquitto_password_preview_shows_only_usernames_and_never_reconstructs_source(bool readOnly, bool cancel, bool edit)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var file = PasswordFile;
        file.Preview!.Read(PasswordText).ShouldBe(new[] { "zoë", "<alice>", "zoë" });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var reference = await dialogs.ShowAsync<ServiceDocumentDialog>(file.Label, new DialogParameters<ServiceDocumentDialog>
                { { d => d.Value, PasswordText }, { d => d.Kind, file.Format!.Value }, { d => d.Preview, file.Preview }, { d => d.ReadOnly, readOnly } });
            var dialog = components.Items.OfType<ServiceDocumentDialog>().Single();
            var html = view.ToHtmlString();
            html.ShouldContain("&lt;alice&gt;");
            html.ShouldNotContain("opaque");
            html.ShouldNotContain("$future");
            html.ShouldNotContain("unrecognized-line");
            components.Items.OfType<MudTextField<string>>().ShouldBeEmpty();
            await SwitchServiceDocumentMode(dialog, true);
            var source = components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Expressions.ExpressionCodeEditor>().Last();
            source.Value.ShouldBe(PasswordText);
            source.ReadOnly.ShouldBe(readOnly);
            var edited = PasswordText.Replace("<alice>", "bob");
            if (edit)
                await source.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = edited.ReplaceLineEndings("\n") }));
            await SwitchServiceDocumentMode(dialog, false);
            if (edit) view.ToHtmlString().ShouldContain(">bob</li>");
            var button = components.Items.OfType<MudButton>().Single(b => b.Variant == (readOnly || cancel ? Variant.Text : Variant.Filled));
            await button.OnClick.InvokeAsync(new MouseEventArgs());
            var result = (await reference.Result)!;
            result.Canceled.ShouldBe(readOnly || cancel);
            if (!result.Canceled) result.Data.ShouldBe(edit ? edited : PasswordText);
            dialog.Value.ShouldBe(PasswordText);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false, true)]
    public async Task Mosquitto_password_import_is_one_undoable_replacement_unless_cancelled_or_generated(bool cancel, bool generatedDuringImport, bool importDefault = false)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var files = new PasswordImport();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IStudioFileAccess>(files);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var file = PasswordFile;
        file.GeneratedWithUsers.ShouldBeTrue();
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client"}},"Workflows":{"main":{}}}""");
        var binding = new ResourceBinding { Provider = "mosquitto" };
        if (!importDefault) binding.Files[file.Path] = "before:$6$original";
        var imported = importDefault ? file.Read(binding) : PasswordText;
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = binding } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<StartupFileEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Schema"] = file, ["Value"] = binding,
                ["ValueChanged"] = EventCallback.Factory.Create<ResourceBinding>(this, value =>
                {
                    var setup = graph.GetEnvironment();
                    setup.Resources["Resources:broker"] = value;
                    graph.SetEnvironment(setup);
                })
            }));
            var import = components.Items.OfType<MudIconButton>().Single(b => b.Icon == Icons.Material.Outlined.UploadFile);
            var pending = import.OnClick.InvokeAsync(new MouseEventArgs());
            if (generatedDuringImport)
                await components.Items.OfType<StartupFileEditor>().Single().SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                    { ["Value"] = binding with { BrokerAccess = new() { [binding.Provider] = new() { Enabled = true } } } }));
            files.Completion.SetResult(cancel ? null : new StudioFile("passwords", imported));
            await pending;
            if (cancel || generatedDuringImport) { graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse(); return; }
            var saved = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
            var reopened = StudioTestServices.CreateGraph();
            reopened.LoadProject(saved);
            reopened.GetEnvironment().Resources["Resources:broker"].Files[file.Path].ShouldBe(imported);
        });
    }

    private sealed class PasswordImport : IStudioFileAccess
    {
        public TaskCompletionSource<StudioFile?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<StudioFile?> OpenVerbatimTextAsync() => Completion.Task;
        public Task<StudioFile?> OpenAsync(string extension) => throw new NotSupportedException();
        public Task<StudioImageFile?> OpenImageAsync() => throw new NotSupportedException();
        public Task<StudioFile?> SaveAsync(StudioFile file, bool saveAs = false) => throw new NotSupportedException();
    }
}
