using System.Text.Json.Nodes;
using FluxFlow.Environments;
using FluxFlow.Plugins.Http.WireMock;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
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
    private const string CustomEndpoint = "{\r\n \"Request\": {\"Path\":\"/old\"}, \"Response\": {}, \"Custom\": [3, 1]\n}\r\n";

    [Fact]
    public void WireMock_advanced_only_save_preserves_unchanged_text_and_can_repair_invalid_json()
    {
        var schema = WireMockMappings.Provider.Document!;
        var compact = JsonNode.Parse(CustomEndpoint)!.ToJsonString();
        new ServiceItemsDraft(schema, CustomEndpoint).SaveAdvanced(compact).ShouldBe(CustomEndpoint);
        new ServiceItemsDraft(schema, "{").SaveAdvanced(compact).ShouldBe(compact);
    }

    [Fact]
    public void WireMock_reformatting_a_temporary_array_preserves_the_original_object_shape()
    {
        var draft = new ServiceItemsDraft(WireMockMappings.Provider.Document!, CustomEndpoint);
        draft.LoadVisual(CustomEndpoint);
        draft.Add();
        var compact = JsonNode.Parse(draft.Serialize())!.ToJsonString();
        draft.LoadVisual(compact);
        draft.Serialize().ShouldBe(compact);
        draft.RemoveAt(1);
        draft.Serialize().ShouldBe(CustomEndpoint);
        // Explicitly wrapping one endpoint in an array is a real shape change.
        var wrapped = "[" + CustomEndpoint + "]";
        draft.LoadVisual(wrapped);
        draft.Serialize().ShouldBe(wrapped);
    }

    [Fact]
    public void WireMock_document_write_respects_the_project_snapshot_lock()
    {
        var schema = WireMockMappings.Provider.Document!;
        var binding = new ResourceBinding { Provider = "wiremock", Mappings = CustomEndpoint };
        var snapshot = binding with { Profile = new("snapshot", "Locked HTTP", true, binding.CopySetup()) };
        var result = schema.Write(snapshot, "[]");
        result.ShouldBeSameAs(snapshot);
        result.ValidateSnapshot();
        schema.Read(result).ShouldBe(CustomEndpoint);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WireMock_portable_draft_preserves_root_shape_order_and_unchanged_source(bool array)
    {
        var original = array ? "[ " + CustomEndpoint + " ]" : CustomEndpoint;
        var draft = new ServiceItemsDraft(WireMockMappings.Provider.Document!, original);
        draft.LoadVisual(original);
        draft.Add();
        var items = JsonNode.Parse(draft.Serialize())!.AsArray();
        items.Count.ShouldBe(2);
        JsonNode.DeepEquals(items[0], JsonNode.Parse(CustomEndpoint)).ShouldBeTrue();
        draft.LoadVisual(draft.Serialize()).ShouldBeFalse();
        draft.RemoveAt(1);
        draft.Serialize().ShouldBe(original);
        var item = draft.Open(0);
        item.Sections[0].Fields.OfType<DocumentText>().Single().Write("/changed");
        draft.Apply(0, item);
        var root = JsonNode.Parse(draft.Serialize())!;
        var changed = array ? root.AsArray().Single()! : root.AsObject();
        changed["Request"]!["Path"]!.GetValue<string>().ShouldBe("/changed");
        changed["Custom"]!.ToJsonString().ShouldBe("[3,1]");
        draft.SaveAdvanced(JsonNode.Parse(original)!.ToJsonString()).ShouldBe(original);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task WireMock_endpoint_add_is_one_project_edit_only_on_save(bool locked, bool cancel)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        const string path = "Resources:api";
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client"}},"Workflows":{"main":{}}}""");
        var binding = new ResourceBinding { Provider = "wiremock", Mappings = CustomEndpoint };
        if (locked) binding = binding with { Profile = new("snapshot", "Locked HTTP", true, binding.CopySetup()) };
        graph.SetEnvironment(new() { Resources = new() { [path] = binding } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += dialog =>
            {
                dialog.RenderCompleteTaskCompletionSource.TrySetResult(true);
                opened.TrySetResult();
                return Task.CompletedTask;
            };
            await renderer.RenderComponentAsync<ServiceSettingsEditor>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ServiceSettingsEditor.ResourcePath)] = path }));
            var pending = components.Items.OfType<MudButton>().Single(b => b.StartIcon == Icons.Material.Outlined.Code).OnClick.InvokeAsync(new MouseEventArgs());
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var add = components.Items.OfType<StudioIconButton>().SingleOrDefault(b => b.Label == "Add endpoint");
            if (locked) add.ShouldBeNull();
            else await add.ShouldNotBeNull().OnClick.InvokeAsync();
            graph.ToProjectJson().ShouldBe(before);
            var button = components.Items.OfType<MudButton>().Last(b => b.Variant == (cancel ? Variant.Text : Variant.Filled));
            await button.OnClick.InvokeAsync(new MouseEventArgs());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            if (cancel) { graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse(); return; }
            var saved = graph.ToProjectJson();
            var endpoints = JsonNode.Parse(graph.GetEnvironment().Resources[path].Mappings)!.AsArray();
            endpoints.Count.ShouldBe(2);
            JsonNode.DeepEquals(endpoints[0], JsonNode.Parse(CustomEndpoint)).ShouldBeTrue();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
            var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(saved);
            reopened.ToProjectJson().ShouldBe(saved);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invalid_WireMock_source_stays_open_for_repair_or_locked_inspection(bool locked)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            const string invalid = """{"Request":{"Path":"/keep"}}""";
            var dialog = await dialogs.ShowAsync<ServiceItemsDialog>("Endpoints", new DialogParameters<ServiceItemsDialog>
                { { d => d.Schema, FluxFlow.Plugins.Http.WireMock.WireMockMappings.Provider.Document! }, { d => d.Value, invalid }, { d => d.ReadOnly, locked } });
            var editor = components.Items.OfType<ExpressionCodeEditor>().Single();
            editor.Value.ShouldBe(invalid);
            editor.ReadOnly.ShouldBe(locked);
            var save = components.Items.OfType<MudButton>().SingleOrDefault(button => button.Variant == Variant.Filled);
            if (locked) { save.ShouldBeNull(); dialog.Close(DialogResult.Cancel()); return; }
            await save.ShouldNotBeNull().OnClick.InvokeAsync(new MouseEventArgs());
            dialog.Result!.IsCompleted.ShouldBeFalse();
            view.ToHtmlString().ShouldContain("requires Request and Response objects");
            const string corrected = """{"Request":{"Path":"/keep"},"Response":{},"Priority":3}""";
            await editor.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = corrected }));
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            (await dialog.Result)!.Data.ShouldBe(corrected);
        });
    }
}
