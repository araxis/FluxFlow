using FluxFlow.Environments;
using FluxFlow.Plugins.Nats;
using FluxFlow.Studio.UI.Features.Designer.Components.Expressions;
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
    private static ServiceStartupFile NatsConfigFile => NatsSetup.Provider.StartupFiles.Single();

    [Theory]
    [InlineData(null, "None")]
    [InlineData("NATS_TOKEN_ENVIRONMENT_VARIABLE", "Token ($NATS_TOKEN)")]
    [InlineData("NATS_USERNAME_ENVIRONMENT_VARIABLE", "Username/password ($NATS_USERNAME, $NATS_PASSWORD)")]
    public void Nats_preview_reflects_generated_settings_without_resolving_credentials(string? variable, string expected)
    {
        var binding = new ResourceBinding();
        if (variable is not null) binding.Variables.Add(variable, "HOST_REFERENCE");
        if (variable == "NATS_USERNAME_ENVIRONMENT_VARIABLE") binding.Variables.Add("NATS_PASSWORD_ENVIRONMENT_VARIABLE", "HOST_PASSWORD_REFERENCE");
        var file = NatsConfigFile;
        file.Preview!.Read(file.DefaultContent(binding)).ShouldBe(new[]
            { "Listener port: 4222", "Authentication: " + expected, "JetStream: Disabled" });
    }

    [Theory]
    [InlineData("enabled", "Enabled")]
    [InlineData("false", "Disabled")]
    public void Nats_preview_reads_explicit_jetstream_and_named_references(string setting, string expected)
    {
        var file = NatsConfigFile;
        var source = $"# local\r\njetstream: {setting}\n// comment\r\nport: 4333\rauthorization {{ token: $BROKER_TOKEN }}";
        var draft = new ServiceDocumentDraft(file.Format!.Value, source, preview: file.Preview);
        draft.PreviewItems.ShouldBe(new[] { "Listener port: 4333", "Authentication: Token ($BROKER_TOKEN)", "JetStream: " + expected });
        draft.Serialize().ShouldBe(source);
    }

    [Theory]
    [InlineData("authorization { token: \"sensitive-literal\" }")]
    [InlineData("authorization { users: [{ user: visible, password: sensitive-literal }] }")]
    [InlineData("authorization { token: \"$QUOTED_LITERAL\" }")]
    [InlineData("authorization{ token: $BROKER_TOKEN }")]
    [InlineData("authorization{ users: [{ user: $USR, password: $PWD }] }")]
    [InlineData("jetstream{ store_dir: /tmp/jetstream }")]
    [InlineData("# ignored\0text\nauthorization { token: $TOKEN }")]
    [InlineData("\uFEFF# imported file\r\nport: 4333", "")]
    [InlineData("include secret-file.conf")]
    [InlineData("port: 4333")]
    [InlineData("jetstream: enabled\njetstream: disabled")]
    public void Nats_preview_does_not_echo_or_partially_interpret_custom_source(string custom, string prefix = "port: 4222\r\n")
    {
        var file = NatsConfigFile;
        var text = prefix + custom;
        var draft = new ServiceDocumentDraft(file.Format!.Value, text, preview: file.Preview);
        draft.PreviewItems.Single().ShouldBe("Custom configuration. See Advanced text for the full settings.");
        draft.Serialize().ShouldBe(text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Nats_preview_advanced_edit_and_cancel_preserve_exact_source_and_project_history(bool edit, bool cancel)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var file = NatsConfigFile;
        const string original = "# local override\r\nport: 4222\njetstream { store_dir: /tmp/jetstream }\rauthorization { token: $NATS_TOKEN }";
        var binding = new ResourceBinding { Provider = "nats" };
        if (edit) binding.Files[file.Path] = original;
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"nats.connection"}},"Workflows":{"main":{}}}""");
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = binding } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();

        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialogs.DialogInstanceAddedAsync += reference =>
            {
                reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
                opened.TrySetResult();
                return Task.CompletedTask;
            };
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
            var pending = components.Items.OfType<MudButton>().Single().OnClick.InvokeAsync(new MouseEventArgs());
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var dialog = components.Items.OfType<ServiceDocumentDialog>().Single();
            view.ToHtmlString().ShouldContain(edit ? "JetStream: Enabled" : "JetStream: Disabled");
            await SwitchServiceDocumentMode(dialog, true);
            var source = components.Items.OfType<ExpressionCodeEditor>().Last();
            source.Value.ShouldBe(edit ? original : file.DefaultContent(binding));
            var changed = original.Replace("$NATS_TOKEN", "\"literal-not-for-preview\"");
            if (edit) await source.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = changed.ReplaceLineEndings("\n") }));
            await SwitchServiceDocumentMode(dialog, false);
            if (edit)
            {
                var html = view.ToHtmlString();
                html.ShouldContain("Custom configuration.");
                html.ShouldNotContain("literal-not-for-preview");
            }
            graph.ToProjectJson().ShouldBe(before);
            var action = components.Items.OfType<MudButton>().Last(b => b.Variant == (cancel ? Variant.Text : Variant.Filled));
            await action.OnClick.InvokeAsync(new MouseEventArgs());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            if (!edit || cancel) { graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse(); return; }
            var saved = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
            var reopened = StudioTestServices.CreateGraph();
            reopened.LoadProject(saved);
            reopened.GetEnvironment().Resources["Resources:broker"].Files[file.Path].ShouldBe(changed);
        });
    }
}
