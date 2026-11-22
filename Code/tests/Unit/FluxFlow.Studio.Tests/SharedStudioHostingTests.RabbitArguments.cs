using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Plugins.RabbitMQ;
using FluxFlow.Studio.UI.Features.Plugins.ObjectGroups;
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
    [InlineData("amqp.publisher", "save")]
    [InlineData("amqp.consumer", "save")]
    [InlineData("amqp.publisher", "cancel")]
    [InlineData("amqp.consumer", "cancel")]
    [InlineData("amqp.publisher", "unchanged")]
    [InlineData("amqp.consumer", "unchanged")]
    [InlineData("amqp.publisher", "stale")]
    [InlineData("amqp.consumer", "stale")]
    public async Task Rabbit_argument_dialog_preserves_transaction_and_original_values(string type, string action)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"send":{"Type":"amqp.publisher","Future":42,"Connection":"missing","tOpOlOgY":{"Queues":[{"Name":"orders","aRgUmEnTs":{"ttl":{"tYpE":"Int32","vAlUe":1000},"bytes":{"Type":"Bytes","Value":"AP8="}}}]}},"other":{"Type":"logger"}}}}""".Replace("amqp.publisher", type));
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "send");
        graph.Diagram.SelectModel(node, true);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<MudDialogProvider>();
            await renderer.RenderComponentAsync<ObjectGroupsGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ObjectGroupsGrid.Schema)] = RabbitDeclarationFields.Groups, [nameof(ObjectGroupsGrid.Configuration)] = node.Configuration,
                [nameof(ObjectGroupsGrid.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            IDialogReference? opened = null;
            dialogs.DialogInstanceAddedAsync += reference => { opened = reference; reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var edit = components.Items.OfType<MudIconButton>().Single(button => button.UserAttributes?["aria-label"]?.ToString() == "Edit argument: ttl").OnClick.InvokeAsync(new MouseEventArgs());
            opened.ShouldNotBeNull();
            var draft = components.Items.OfType<PropertyValueFields>().Last().Draft;
            var save = components.Items.OfType<MudButton>().Last(button => button.Color == Color.Primary);
            draft.Text = "invalid";
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            opened.Result.IsCompleted.ShouldBeFalse();
            graph.ToProjectJson().ShouldBe(before);
            draft.Text = action == "unchanged" ? "1000" : "2000";
            if (action == "stale") graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "other"), true);
            if (action == "cancel") opened.Close(DialogResult.Cancel());
            else await save.OnClick.InvokeAsync(new MouseEventArgs());
            await edit;
            if (action != "save")
            {
                graph.ToProjectJson().ShouldBe(before);
                graph.CanUndo.ShouldBeFalse();
                return;
            }
            node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "send");
            var arguments = node.Configuration["tOpOlOgY"].GetProperty("Queues")[0].GetProperty("aRgUmEnTs");
            arguments.GetProperty("ttl").GetProperty("vAlUe").GetInt32().ShouldBe(2000);
            arguments.GetProperty("ttl").GetProperty("tYpE").GetString().ShouldBe("Int32");
            arguments.GetProperty("bytes").GetProperty("Value").GetString().ShouldBe("AP8=");
            node.Configuration["Future"].GetInt32().ShouldBe(42);
            node.Configuration["Connection"].GetString().ShouldBe("missing");
            var saved = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
            graph.LoadProject(saved); graph.ToProjectJson().ShouldBe(saved);
        });
    }

    [Fact]
    public async Task Rabbit_arguments_add_duplicate_validation_and_remove_use_one_edit_each()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"send":{"Type":"amqp.publisher","Topology":{"Exchanges":[{"Name":"events","Arguments":{"existing":{"Type":"String","Value":"keep"}}}]}}}}}""");
        graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<MudDialogProvider>();
            await renderer.RenderComponentAsync<ObjectGroupsGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ObjectGroupsGrid.Schema)] = RabbitDeclarationFields.Groups, [nameof(ObjectGroupsGrid.Configuration)] = graph.SelectedNode!.Configuration,
                [nameof(ObjectGroupsGrid.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            IDialogReference? opened = null;
            dialogs.DialogInstanceAddedAsync += reference => { opened = reference; reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var edit = components.Items.OfType<MudIconButton>().Single(button => button.UserAttributes?["aria-label"]?.ToString() == "Add argument: events").OnClick.InvokeAsync(new MouseEventArgs());
            var draft = components.Items.OfType<PropertyValueFields>().Last().Draft;
            var save = components.Items.OfType<MudButton>().Last(button => button.Color == Color.Primary);
            draft.Name = "EXISTING";
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            opened!.Result.IsCompleted.ShouldBeFalse(); graph.CanUndo.ShouldBeFalse();
            draft.Name = "metadata"; draft.ChangeType("Table");
            draft.AddItem();
            var nested = draft.Items.Single(); nested.Name = "nested"; nested.ChangeType("Array"); nested.AddItem();
            nested.Items[0].ChangeType("Bytes"); nested.Items[0].Text = "AP8=";
            await save.OnClick.InvokeAsync(new MouseEventArgs()); await edit;
            var added = graph.ToProjectJson();
            var grid = components.Items.OfType<ObjectGroupsGrid>().Single();
            await grid.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(grid.Configuration)] = graph.SelectedNode!.Configuration }));
            await components.Items.OfType<MudIconButton>().Last(button => button.UserAttributes?["aria-label"]?.ToString() == "Remove argument: metadata").OnClick.InvokeAsync(new MouseEventArgs());
            graph.ToProjectJson().ShouldBe(before);
            graph.Undo(); graph.ToProjectJson().ShouldBe(added);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.Redo(); graph.ToProjectJson().ShouldBe(before);
        });
    }
}
