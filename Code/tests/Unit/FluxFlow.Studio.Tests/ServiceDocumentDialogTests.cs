using FluxFlow.Environments;
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
    private static ServiceNameList EnabledPluginsEditor => FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpSetup.Provider
        .StartupFiles.Single(f => f.Format == ServiceDocumentKind.NameList).Names!;

    [Theory]
    [InlineData(ServiceDocumentKind.NameList, " [ rabbitmq_mqtt, rabbitmq_mqtt ] .\r\n")]
    [InlineData(ServiceDocumentKind.KeyValueLines, "key = first\nkey = second\n")]
    [InlineData(ServiceDocumentKind.Files, "{\"relative-file\":\"contents\"}")]
    public async Task Locked_service_documents_allow_source_inspection_without_validating_or_changing_saved_values(ServiceDocumentKind kind, string original)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var reference = await dialogs.ShowAsync<ServiceDocumentDialog>("Settings", new DialogParameters<ServiceDocumentDialog>
                { { d => d.Kind, kind }, { d => d.Templates, FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpSetup.Provider.StartupFiles.Last().Templates! }, { d => d.Names, EnabledPluginsEditor }, { d => d.Value, original }, { d => d.ReadOnly, true } });
            var dialog = components.Items.OfType<ServiceDocumentDialog>().Single();
            await SwitchServiceDocumentMode(dialog, true);
            var source = components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Expressions.ExpressionCodeEditor>().Last();
            source.ReadOnly.ShouldBeTrue();
            source.Value.ShouldBe(original);
            await SwitchServiceDocumentMode(dialog, false);
            await SwitchServiceDocumentMode(dialog, true);
            components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Expressions.ExpressionCodeEditor>().Last().Value.ShouldBe(original);
            components.Items.OfType<MudButton>().ShouldNotContain(button => button.Variant == Variant.Filled);
            reference.Close(DialogResult.Cancel());
            (await reference.Result)!.Canceled.ShouldBeTrue();
        });
    }

    [Theory]
    [InlineData(ServiceDocumentKind.NameList, "% keep this comment\r\n['custom-plugin', rabbitmq_mqtt].\r\n")]
    [InlineData(ServiceDocumentKind.NameList, "\uFEFF% saved on another host\r\n['custom-plugin',\n rabbitmq_mqtt].")]
    [InlineData(ServiceDocumentKind.NameList, " [ rabbitmq_mqtt,\r\n rabbitmq_mqtt ] .")]
    [InlineData(ServiceDocumentKind.KeyValueLines, "\uFEFF# keep\r\nlisteners.tcp.default=5672\nunsupported line")]
    [InlineData(ServiceDocumentKind.DirectiveLines, "\uFEFF# keep\r\nlistener\t1883\n")]
    [InlineData(ServiceDocumentKind.KeyValueLines, "custom.key =   # no value")]
    [InlineData(ServiceDocumentKind.KeyValueLines, "custom.key = 'first\r\nlooks.like.a.key = last' # keep")]
    [InlineData(ServiceDocumentKind.KeyValueLines, "custom.key=first\r\ncustom.key = second")]
    [InlineData(ServiceDocumentKind.ObjectCollections, "\uFEFF{\r\n  \"queues\": {\"future\":true}, \"unknown\": 7\n} ")]
    public async Task Service_document_source_keeps_text_outside_the_visual_subset(ServiceDocumentKind kind, string original)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var dialog = await dialogs.ShowAsync<ServiceDocumentDialog>("Startup file", new DialogParameters<ServiceDocumentDialog>
                { { d => d.Kind, kind }, { d => d.Templates, FluxFlow.Plugins.Mqtt.RabbitMqMqttSetup.Provider.StartupFiles.Last().Templates! }, { d => d.Names, EnabledPluginsEditor }, { d => d.Value, original } });
            await SwitchServiceDocumentMode(components.Items.OfType<ServiceDocumentDialog>().Single(), false);
            components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Expressions.ExpressionCodeEditor>().Last().Value.ShouldBe(original);
            await components.Items.OfType<MudButton>().Single(item => item.Variant == Variant.Filled).OnClick.InvokeAsync(new MouseEventArgs());
            (await dialog.Result)!.Data.ShouldBe(original);
        });
    }

    [Theory]
    [InlineData(ServiceDocumentKind.Variables, "{\"NAME\":\"original\"}", "Value")]
    [InlineData(ServiceDocumentKind.Files, "{\"/config.txt\":\"original\\r\\nline two\\n\"}", "Contents")]
    [InlineData(ServiceDocumentKind.ObjectCollections, "{\"vhosts\":[{\"name\":\"original\"}]}", "Name")]
    public async Task Service_document_invalid_json_stays_in_source_until_corrected_and_cancel_discards_the_draft(ServiceDocumentKind kind, string original, string label)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var reference = await dialogs.ShowAsync<ServiceDocumentDialog>("Settings", new DialogParameters<ServiceDocumentDialog>
                { { d => d.Kind, kind }, { d => d.Templates, FluxFlow.Plugins.Mqtt.RabbitMqMqttSetup.Provider.StartupFiles.Last().Templates! }, { d => d.Value, original } });
            var dialog = components.Items.OfType<ServiceDocumentDialog>().Single();
            await components.Items.OfType<MudTextField<string>>().Single(field => field.Label == label).ValueChanged.InvokeAsync("visual edit");
            await SwitchServiceDocumentMode(dialog, true);
            var source = components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Expressions.ExpressionCodeEditor>().Last();
            source.Value.ShouldContain("visual edit");
            // Static rendering uses the editor's initial text; the browser exercises Monaco input.
            await source.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = "{" }));
            await SwitchServiceDocumentMode(dialog, false);
            source.Value.ShouldBe("{");
            view.ToHtmlString().ShouldContain("document-source");
            await components.Items.OfType<MudButton>().Single(button => button.Variant == Variant.Filled).OnClick.InvokeAsync(new MouseEventArgs());
            reference.Result!.IsCompleted.ShouldBeFalse();
            if (kind is ServiceDocumentKind.Variables or ServiceDocumentKind.Files)
            {
                var invalidKey = kind == ServiceDocumentKind.Files ? """{"relative":"data"}""" : """{"A=B":"data"}""";
                await source.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = invalidKey }));
                await SwitchServiceDocumentMode(dialog, false);
                view.ToHtmlString().ShouldContain("document-source");
                await components.Items.OfType<MudButton>().Single(button => button.Variant == Variant.Filled).OnClick.InvokeAsync(new MouseEventArgs());
                reference.Result!.IsCompleted.ShouldBeFalse();
            }
            await source.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = original.Replace("original", "source edit") }));
            await SwitchServiceDocumentMode(dialog, false);
            components.Items.OfType<MudTextField<string>>().Last(field => field.Label == label).Value.ShouldBe(kind == ServiceDocumentKind.Files ? "source edit\nline two\n" : "source edit");
            await components.Items.OfType<MudButton>().Last(button => button.Variant != Variant.Filled && button.StartIcon is null).OnClick.InvokeAsync(new MouseEventArgs());
            (await reference.Result)!.Canceled.ShouldBeTrue();
            dialog.Value.ShouldBe(original);
        });
    }

    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\r\n")]
    [InlineData(false, "\n", true)]
    [InlineData(true, "\r\n", true)]
    public async Task Startup_file_advanced_edit_keeps_unrelated_line_endings(bool returnToVisual, string modelEnding, bool json = false)
    {
        var original = json ? "\uFEFF{\r\n  \"vhosts\": [],\n  \"custom\": 1884\r}" : "\uFEFF# local\r\nlistener 1883\nlistener 1884\rtopic sensors/# out 1";
        var expected = original.Replace("1884", "1885");
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var file = json
                ? FluxFlow.Plugins.Mqtt.RabbitMqMqttSetup.Provider.StartupFiles.Single(f => f.Format == ServiceDocumentKind.ObjectCollections)
                : FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider.StartupFiles.First();
            var reference = await dialogs.ShowAsync<ServiceDocumentDialog>(file.Label, new DialogParameters<ServiceDocumentDialog>
                { { d => d.Kind, file.Format!.Value }, { d => d.Templates, file.Templates }, { d => d.Value, original } });
            var dialog = components.Items.OfType<ServiceDocumentDialog>().Single();
            await SwitchServiceDocumentMode(dialog, true);
            var source = components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Expressions.ExpressionCodeEditor>().Last();
            // Monaco returns one line-ending style for the entire edited model.
            await source.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = expected.ReplaceLineEndings(modelEnding) }));
            if (returnToVisual) await SwitchServiceDocumentMode(dialog, false);
            await components.Items.OfType<MudButton>().Single(item => item.Variant == Variant.Filled).OnClick.InvokeAsync(new MouseEventArgs());
            (await reference.Result)!.Data.ShouldBe(expected);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Service_document_acl_choices_and_advanced_text_share_a_local_draft(bool cancel, bool removeRule)
    {
        const string original = "# saved\r\nuser alice\ntopic read sensors/#\rpattern write devices/%u/+\nuser bob\r\ntopic read private/#";
        var file = FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider.StartupFiles.Single(f => f.Directives is not null);
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var reference = await dialogs.ShowAsync<ServiceDocumentDialog>(file.Label, new DialogParameters<ServiceDocumentDialog>
                { { d => d.Value, original }, { d => d.Kind, file.Format!.Value }, { d => d.Directives, file.Directives! } });
            var dialog = components.Items.OfType<ServiceDocumentDialog>().Single();
            await components.Items.OfType<MudSelect<string>>().First(c => c.Label == "Access").ValueChanged.InvokeAsync("deny");
            await SwitchServiceDocumentMode(dialog, true);
            var source = components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Expressions.ExpressionCodeEditor>().Last();
            var edited = original.Replace("topic read sensors/#", "topic deny sensors/#");
            source.Value.ShouldBe(edited);
            // Raw rules outside the visual subset remain editable and savable.
            var advanced = removeRule ? edited.Replace("topic deny sensors/#\r", "") : edited.Replace("pattern write devices/%u/+", "custom_rule opaque");
            await source.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = advanced.ReplaceLineEndings("\n") }));
            await SwitchServiceDocumentMode(dialog, false);
            if (removeRule) view.ToHtmlString().ShouldNotContain("document-source");
            else view.ToHtmlString().ShouldContain("document-source");
            var button = components.Items.OfType<MudButton>().Last(b => cancel ? b.StartIcon is null && b.Variant != Variant.Filled : b.Variant == Variant.Filled);
            await button.OnClick.InvokeAsync(new MouseEventArgs());
            var result = (await reference.Result)!;
            result.Canceled.ShouldBe(cancel);
            if (!cancel) result.Data.ShouldBe(advanced);
            dialog.Value.ShouldBe(original);
        });
    }

    private static async Task SwitchServiceDocumentMode(ServiceDocumentDialog dialog, bool advanced)
    {
        var method = typeof(ServiceDocumentDialog).GetMethod("ChangeMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)method.Invoke(dialog, [advanced])!;
        await dialog.SetParametersAsync(ParameterView.Empty);
    }

    [Theory]
    [InlineData(ServiceDocumentKind.Variables, "key", "Value", false)]
    [InlineData(ServiceDocumentKind.Files, "/key", "Contents", false)]
    [InlineData(ServiceDocumentKind.Files, "/key", "Contents", true)]
    public async Task Service_document_multiline_fields_preserve_delimiters_after_browser_input(ServiceDocumentKind kind, string key, string label, bool revert)
    {
        const string originalValue = "one\r\ntwo\nthree\rfour\r\n";
        var original = " { \"" + key + "\" : \"one\\r\\ntwo\\nthree\\rfour\\r\\n\" }\r\n";
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var reference = await dialogs.ShowAsync<ServiceDocumentDialog>("Settings", new DialogParameters<ServiceDocumentDialog>
                { { d => d.Kind, kind }, { d => d.Value, original } });
            view.ToHtmlString().ShouldContain("<textarea");
            var field = components.Items.OfType<MudTextField<string>>().Single(f => f.Label == label);
            await field.ValueChanged.InvokeAsync("one\ntwo\nadded\nthree\nfour\n");
            await field.ValueChanged.InvokeAsync("one\ntwo\nadded\nTHREE\nfour\n");
            if (revert) await field.ValueChanged.InvokeAsync(originalValue.ReplaceLineEndings("\n"));
            await components.Items.OfType<MudButton>().Single(b => b.Variant == Variant.Filled).OnClick.InvokeAsync(new MouseEventArgs());
            var saved = (await reference.Result)!.Data.ShouldBeOfType<string>();
            if (revert) saved.ShouldBe(original);
            else System.Text.Json.Nodes.JsonNode.Parse(saved)![key]!.GetValue<string>().ShouldBe("one\r\ntwo\nadded\nTHREE\rfour\r\n");
        });
    }

    [Theory]
    [InlineData(ServiceDocumentKind.Variables)]
    [InlineData(ServiceDocumentKind.Files)]
    public async Task Service_document_map_inspector_records_only_real_edits_and_reopens_exact_values(ServiceDocumentKind kind)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Designer.DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client","BaseAddress":"http://example.test"}},"Workflows":{"main":{}}}""");
        const string keep = "line one\r\nline two\nline three\r";
        var binding = new ResourceBinding { Provider = "wiremock",
            Variables = new() { ["Key"] = "before", ["key"] = keep }, Files = new() { ["/Key"] = "before", ["/key"] = keep } };
        graph.SetEnvironment(new() { Resources = new() { ["Resources:api"] = binding } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        bool files = kind == ServiceDocumentKind.Files;
        var key = files ? "/Key" : "Key";
        var map = new Dictionary<string, string> { [key.ToLowerInvariant()] = keep, [key] = "before" };
        var answer = DialogResult.Cancel();
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
        dialogs.DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(answer);
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ResourcePath"] = "Resources:api" }));
            var edit = components.Items.OfType<MudButton>().Single(b => b.StartIcon == (files ? Icons.Material.Outlined.Description : Icons.Material.Outlined.DataObject));
            await edit.OnClick.InvokeAsync(new MouseEventArgs());
            answer = DialogResult.Ok(System.Text.Json.JsonSerializer.Serialize(map));
            await edit.OnClick.InvokeAsync(new MouseEventArgs());
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();

            map[key] = "changed\r\nwith LF\nand CR\r";
            answer = DialogResult.Ok(System.Text.Json.JsonSerializer.Serialize(map));
            await edit.OnClick.InvokeAsync(new MouseEventArgs());
            var saved = graph.ToProjectJson();
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(saved);
            var reopened = StudioTestServices.CreateGraph();
            reopened.LoadProject(saved);
            var result = reopened.GetEnvironment().Resources["Resources:api"];
            (files ? result.Files : result.Variables).ShouldBe(map);
            (files ? result.Variables : result.Files).ShouldBe(files ? binding.Variables : binding.Files);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bulk_startup_files_cannot_add_or_replace_a_generated_file_but_can_clear_a_conflict(bool hasConflict)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Designer.DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client"}},"Workflows":{"main":{}}}""");
        const string path = "/mosquitto/config/passwords";
        const string custom = "\uFEFFcustom\r\nline\n";
        var binding = new ResourceBinding { Provider = "mosquitto", BrokerAccess = new() { ["mosquitto"] = new() { Enabled = true } } };
        if (hasConflict) binding.Files[path] = "old";
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = binding } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var files = new Dictionary<string, string> { [path] = "replacement", ["/custom"] = custom };
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(DialogResult.Ok(System.Text.Json.JsonSerializer.Serialize(files)));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ResourcePath"] = "Resources:broker" }));
            var edit = components.Items.OfType<MudButton>().Single(b => b.StartIcon == Icons.Material.Outlined.Description);
            await edit.OnClick.InvokeAsync(new MouseEventArgs());
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();

            files.Remove(path);
            await edit.OnClick.InvokeAsync(new MouseEventArgs());
            graph.GetEnvironment().Resources["Resources:broker"].Files.ShouldBe(files);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Unchanged_startup_file_save_preserves_defaults_and_existing_overrides(bool hasOverride, bool edited)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        const string path = "/etc/rabbitmq/enabled_plugins";
        const string content = "[rabbitmq_management].\n";
        FluxFlow.Environments.ResourceBinding? changed = null;
        var binding = new FluxFlow.Environments.ResourceBinding { Provider = "rabbitmq-mqtt", Files = new() { ["/keep"] = "original" } };
        if (hasOverride) binding.Files[path] = content;
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
        dialogs.DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(DialogResult.Ok(edited ? "[]." : content));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.StartupFileEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Value"] = binding,
                ["Schema"] = FluxFlow.Plugins.Mqtt.RabbitMqMqttSetup.Provider.StartupFiles.Single(f => f.Path == path) with { DefaultContent = _ => content },
                ["ValueChanged"] = EventCallback.Factory.Create<FluxFlow.Environments.ResourceBinding>(this, value => changed = value)
            }));
            await components.Items.OfType<MudButton>().Single().OnClick.InvokeAsync(new MouseEventArgs());
            if (edited)
            {
                changed.ShouldNotBeNull();
                changed.Files[path].ShouldBe("[].");
                changed.Files["/keep"].ShouldBe("original");
            }
            else changed.ShouldBeNull();
            binding.Files.ContainsKey(path).ShouldBe(hasOverride);
        });
    }

    [Theory]
    [InlineData(ServiceDocumentKind.Variables, "{\"NAME\":\"original\"}", "Value", false, false)]
    [InlineData(ServiceDocumentKind.Variables, "{\"NAME\":\"original\"}", "Value", true, false)]
    [InlineData(ServiceDocumentKind.Variables, "{\"NAME\":\"original\"}", "Value", false, true)]
    [InlineData(ServiceDocumentKind.Files, "{\"/file\":\"original\"}", "Contents", false, false)]
    [InlineData(ServiceDocumentKind.Files, "{\"/file\":\"original\"}", "Contents", true, false)]
    [InlineData(ServiceDocumentKind.KeyValueLines, "custom.setting = original\n", "Value", false, false)]
    [InlineData(ServiceDocumentKind.KeyValueLines, "custom.setting = original\n", "Value", true, false)]
    [InlineData(ServiceDocumentKind.DirectiveLines, "plugin_opt_label original\r\n", "Value", false, false)]
    [InlineData(ServiceDocumentKind.DirectiveLines, "plugin_opt_label original\r\n", "Value", true, false)]
    [InlineData(ServiceDocumentKind.NameList, "[original].", "Plugin", false, false)]
    [InlineData(ServiceDocumentKind.NameList, "[original].", "Plugin", true, false)]
    [InlineData(ServiceDocumentKind.ObjectCollections, "{\"vhosts\":[{\"name\":\"original\"}],\"custom\":42}", "Name", false, false)]
    [InlineData(ServiceDocumentKind.ObjectCollections, "{\"vhosts\":[{\"name\":\"original\"}],\"custom\":42}", "Name", true, false)]
    [InlineData(ServiceDocumentKind.ObjectCollections, "{\"vhosts\":[{\"name\":\"original\"}],\"custom\":42}", "Name", false, true)]
    public async Task Service_document_forms_keep_changes_local_until_save(ServiceDocumentKind kind, string original, string label, bool cancel, bool readOnly)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Designer.DesignerGraphState>();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var dialog = await dialogs.ShowAsync<ServiceDocumentDialog>("Settings", new DialogParameters<ServiceDocumentDialog>
            { { d => d.Kind, kind }, { d => d.Templates, FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpSetup.Provider.StartupFiles.Last().Templates! }, { d => d.Names, EnabledPluginsEditor }, { d => d.Value, original }, { d => d.ReadOnly, readOnly } });
            view.ToHtmlString().ShouldContain("Visual");
            var field = components.Items.OfType<MudTextField<string>>().Single(item => item.Label == label);
            field.ReadOnly.ShouldBe(readOnly);
            if (readOnly)
            {
                components.Items.OfType<MudButton>().ShouldNotContain(item => item.Variant == Variant.Filled);
                dialog.Close(DialogResult.Cancel());
            }
            else
            {
                await field.ValueChanged.InvokeAsync("changed");
                graph.ToProjectJson().ShouldBe(before);
                graph.CanUndo.ShouldBeFalse();
                if (cancel) dialog.Close(DialogResult.Cancel());
                else await components.Items.OfType<MudButton>().Single(item => item.Variant == Variant.Filled).OnClick.InvokeAsync(new MouseEventArgs());
            }
            var result = (await dialog.Result)!;
            result.Canceled.ShouldBe(cancel || readOnly);
            if (!result.Canceled)
            {
                var text = result.Data.ShouldBeOfType<string>();
                text.ShouldContain("changed");
                if (kind == ServiceDocumentKind.ObjectCollections) text.ShouldContain("\"custom\": 42");
            }
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
        });
    }
}
