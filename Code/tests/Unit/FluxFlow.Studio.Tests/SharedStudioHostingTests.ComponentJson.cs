using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Components.Expressions;
using FluxFlow.Studio.UI.Features.Designer.Pages;
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
    public static IEnumerable<object[]> ComponentJsonEdits =>
        from type in new[] { "source", "batch", "scheduler", "logger", "mqtt.connection", "mqtt.publisher", "mqtt.subscriber", "serial.text", "ros.publisher", "ros.subscriber", "http.trigger", "ble.client", "ble.notifications" }
        from action in new[] { "save", "cancel", "no-op", "stale", "switch", "dispose" }
        select new object[] { type, action };

    [Theory]
    [MemberData(nameof(ComponentJsonEdits))]
    public async Task Component_JSON_validates_drafts_and_preserves_configuration_and_history(string type, string action)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var original = new JsonObject
        {
            ["tYpE"] = type, ["Connection"] = "Resources.missing", ["OutputCapacity"] = "64",
            ["Future"] = new JsonObject { ["bytes"] = "AP8=", ["value"] = 9007199254740993L },
            ["ExplicitNull"] = null
        };
        if (type == "mqtt.subscriber")
        {
            original["Topics"] = JsonNode.Parse("""[{"topic":"work/#","qos":"1"},{"Topic":"work/#","QoS":1}]""");
            original["AcknowledgementMode"] = "Manual"; original["MaxPendingAcknowledgements"] = 7;
        }
        if (type == "scheduler")
        {
            original["cRoN"] = "*/5 * * * * *"; original["iNcLuDeSeCoNdS"] = "true";
            original["TimeZone"] = "Europe/Stockholm";
        }
        if (type == "batch")
        {
            original["bAtChSiZe"] = "0003"; original["mAxWaIt"] = "00:00:05.0000000";
            original["Output"] = "other.Input";
        }
        if (type == "source")
        {
            original["vAlUeS"] = " INLINE: [9007199254740993,1e400,\"\\u0061\",true,null,{\"keep\":42},[]] ";
            original["Output"] = "other.Input";
        }
        if (type.StartsWith("ros.", StringComparison.Ordinal))
        {
            original["Node"] = "missing"; original["Topic"] = "/readings";
            original["qOs"] = JsonNode.Parse("""{"dEpTh":"0004","History":1}""");
        }
        if (type == "http.trigger")
        {
            original["Route"] = "/items/{id:int}"; original["mEtHoDs"] = JsonNode.Parse("""["CUSTOM","PATCH"]""");
            original["rEsPoNsEmOdE"] = 1; original["ShutdownMode"] = "drain";
            original["Response"] = "other.Output"; original["ReplyTimeout"] = "00:00:02.0001000";
        }
        if (type.StartsWith("ble.", StringComparison.Ordinal))
        {
            original["ServiceId"] = "{3F63B38E-98DF-4A52-A9F8-76B803A934B2}";
            original["CharacteristicId"] = "F50D7529-4E5D-4088-B88F-E236CD1E7F29";
            original["TimeoutSeconds"] = "0020";
            if (type == "ble.client")
            {
                original["Operation"] = 1;
                original["Value"] = JsonNode.Parse("""{"encoding":1,"data":"AP8="}""");
            }
            else original["SubscriptionMode"] = 1;
        }
        var project = new JsonObject
        {
            ["Resources"] = new JsonObject { ["broker"] = new JsonObject { ["Type"] = "mqtt.client", ["Username"] = "trial-user", ["Password"] = "trial-password" } },
            ["Workflows"] = new JsonObject { ["main"] = new JsonObject { ["target"] = original.DeepClone(), ["other"] = new JsonObject { ["Type"] = "logger" } } }
        };
        graph.LoadJson(project.ToJsonString()); graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "target"), true);
        var before = graph.ToProjectJson();
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>(); IDialogReference? opened = null;
        dialogs.DialogInstanceAddedAsync += reference => { opened = reference; reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Task? edit = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            edit = components.Items.OfType<MudButton>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Advanced component JSON"))
                .OnClick.InvokeAsync(new MouseEventArgs());
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var editor = components.Items.OfType<ExpressionCodeEditor>().Last();
            if (type == "scheduler")
                components.Items.OfType<FluxFlow.Studio.UI.Shared.Configuration.StructuredSettingsDialog>().Single().JsonOnly.ShouldBeTrue();
            var save = components.Items.OfType<MudButton>().Single(item => item.Variant == Variant.Filled);
            var invalidCapacity = original.DeepClone().AsObject(); invalidCapacity["OutputCapacity"] = 0;
            var invalidType = original.DeepClone().AsObject(); invalidType["tYpE"] = type == "source" ? "logger" : "source";
            var duplicateKey = original.ToJsonString().Replace("\"OutputCapacity\":\"64\"", "\"OutputCapacity\":\"64\",\"outputcapacity\":128");
            var invalidDrafts = new List<string> { "{", "", "[]", "null", "{}", invalidCapacity.ToJsonString(), invalidType.ToJsonString(), duplicateKey };
            if (type == "source")
            {
                foreach (var value in new[] { "", "[invalid", "inline:{}", "inline:null", "inline:123", "inline:" })
                {
                    var invalid = original.DeepClone().AsObject(); invalid["vAlUeS"] = value;
                    invalidDrafts.Add(invalid.ToJsonString());
                }
            }
            if (type == "batch")
            {
                foreach (var (key, value) in new[] { ("bAtChSiZe", "0"), ("bAtChSiZe", "1.5"), ("bAtChSiZe", "2147483648"),
                    ("mAxWaIt", "00:00:00"), ("mAxWaIt", "-00:00:00.001"), ("mAxWaIt", "50.00:00:00"), ("mAxWaIt", "invalid") })
                {
                    var invalid = original.DeepClone().AsObject(); invalid[key] = value;
                    invalidDrafts.Add(invalid.ToJsonString());
                }
            }
            if (type == "scheduler")
            {
                var conflicting = original.DeepClone().AsObject(); conflicting["Interval"] = "00:00:01";
                invalidDrafts.Add(conflicting.ToJsonString());
                var invalidCron = original.DeepClone().AsObject(); invalidCron["cRoN"] = "* * * * *";
                invalidDrafts.Add(invalidCron.ToJsonString());
                var missingDate = new JsonObject { ["Type"] = "scheduler", ["At"] = "" };
                invalidDrafts.Add(missingDate.ToJsonString());
            }
            if (type == "serial.text")
            {
                var invalidEnding = original.DeepClone().AsObject(); invalidEnding["NewLine"] = "\r";
                invalidDrafts.Add(invalidEnding.ToJsonString());
            }
            if (type.StartsWith("ros.", StringComparison.Ordinal))
            {
                var invalidQos = original.DeepClone().AsObject(); invalidQos["qOs"]!["Deadline"] = "bad duration";
                invalidDrafts.Add(invalidQos.ToJsonString());
            }
            if (type == "ros.subscriber")
            {
                var invalidQueue = original.DeepClone().AsObject(); invalidQueue["FullMode"] = "Wait";
                invalidDrafts.Add(invalidQueue.ToJsonString());
            }
            if (type == "http.trigger")
            {
                foreach (var (key, value) in new[] { ("ShutdownMode", "9"), ("ReplyTimeout", "00:00:00"),
                    ("ReplyTimeout", "50.00:00:00"), ("MaxPendingRequests", "0"), ("AcknowledgementStatusCode", "500"),
                    ("rEsPoNsEmOdE", "Acknowledge") }) // Response is still wired.
                {
                    var invalid = original.DeepClone().AsObject(); invalid[key] = value;
                    invalidDrafts.Add(invalid.ToJsonString());
                }
                var badVerb = original.DeepClone().AsObject(); badVerb["mEtHoDs"]![1] = "bad method";
                invalidDrafts.Add(badVerb.ToJsonString());
            }
            if (type.StartsWith("ble.", StringComparison.Ordinal))
            {
                var invalidUuid = original.DeepClone().AsObject(); invalidUuid["ServiceId"] = "not a UUID";
                invalidDrafts.Add(invalidUuid.ToJsonString());
                var invalidOperation = original.DeepClone().AsObject();
                if (type == "ble.client") invalidOperation["Value"]!["data"] = "invalid!";
                else invalidOperation["Input"] = "other.Output";
                invalidDrafts.Add(invalidOperation.ToJsonString());
            }
            foreach (var invalid in invalidDrafts)
            {
                await editor.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = invalid }));
                await save.OnClick.InvokeAsync(new MouseEventArgs());
                opened!.Result.IsCompleted.ShouldBeFalse(invalid); graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            }
            if (action == "cancel") { opened!.Close(DialogResult.Cancel()); return; }
            if (action == "dispose") { components.Items.OfType<IDisposable>().Single(editor => editor is ComponentJsonEditor or FluxFlow.Studio.UI.Features.Plugins.PortablePropertyConfigurator).Dispose(); return; }
            if (action == "switch")
            {
                graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "other"), true);
                opened!.Result.IsCompleted.ShouldBeTrue(); return;
            }
            if (action == "stale") { graph.AddEnvironment("Changed while editing"); graph.Undo(); }
            var revised = original.DeepClone().AsObject();
            if (action != "no-op") revised["OutputCapacity"] = 96;
            await editor.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["Value"] = revised.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) }));
            await save.OnClick.InvokeAsync(new MouseEventArgs());
        });
        await edit!;
        if (action != "save")
        { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); return; }
        var expected = original.DeepClone().AsObject(); expected["OutputCapacity"] = 96;
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(graph.SelectedNode!.Configuration), expected).ShouldBeTrue();
        var after = graph.ToProjectJson(); graph.CanRedo.ShouldBeFalse();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        if (type is "batch" or "source") graph.Diagram.Links.Count.ShouldBe(1);
        var saved = JsonNode.Parse(graph.ToJson())!.AsObject();
        JsonNode.DeepEquals(saved["Resources"], project["Resources"]).ShouldBeTrue();
    }

    [Fact]
    public async Task Each_component_Inspector_has_one_whole_component_JSON_action()
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            foreach (var description in scope.ServiceProvider.GetServices<IComponentDescription>())
            {
                graph.LoadJson("{\"Workflows\":{\"main\":{\"target\":{\"Type\":\"" + description.Palette.ComponentType + "\"}}}}");
                graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
                var html = view.ToHtmlString();
                System.Text.RegularExpressions.Regex.Matches(html, ">Advanced JSON</span>").Count.ShouldBe(1, description.Palette.ComponentType);
            }
        });
    }
}
