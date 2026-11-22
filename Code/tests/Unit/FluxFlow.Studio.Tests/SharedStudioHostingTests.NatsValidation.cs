using FluxFlow.Authoring.Definitions.JetStream;
using FluxFlow.Authoring.Definitions.Nats;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
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
    [InlineData("nats.subscriber", "Subject", "events.>", "events.>.value", "events.*.value", false)]
    [InlineData("nats.subscriber", "QueueGroup", "workers", "workers.*", "workers.eu", true)]
    [InlineData("nats.jetstream.publisher", "Stream", "events", "events.eu", "events-eu", false)]
    [InlineData("nats.jetstream.consumer", "Stream", "events", "events/ eu", "events-eu", false)]
    [InlineData("nats.jetstream.consumer", "Consumer", "workers", "workers.*", "workers-eu", false)]
    [InlineData("nats.jetstream.publisher", "StreamSubject", "events.>", "events..value", "events.*", true)]
    [InlineData("nats.jetstream.consumer", "FilterSubject", "events.>", "events.>.value", "events.*", true)]
    [InlineData("nats.subscriber", "QueueGroup", "workers", "workers.*", "", true)]
    [InlineData("nats.jetstream.publisher", "StreamSubject", "events.>", "events..value", "", true)]
    [InlineData("nats.jetstream.consumer", "FilterSubject", "events.>", "events.>.value", "", true)]
    public async Task Nats_text_fields_reject_invalid_drafts_preserve_no_ops_and_save_valid_edits(
        string type, string property, string saved, string invalid, string valid, bool canReset)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var key = property.ToLowerInvariant();
        graph.LoadJson("""{"Workflows":{"main":{"transport":{"Type":"TYPE","PROPERTY":"SAVED","Extra":{"Keep":42}}}}}"""
            .Replace("TYPE", type).Replace("PROPERTY", key).Replace("SAVED", saved));
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
        var before = graph.ToProjectJson();
        var options = type.StartsWith("nats.jetstream.", StringComparison.Ordinal) ? JetStreamDescription.Options(type) : NatsDescription.Options(type);
        var option = options.Single(option => option.Name == property);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Configuration)] = graph.SelectedNode!.Configuration,
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var input = components.Items.OfType<MudTextField<string>>().Single();
            foreach (var draft in new[] { invalid, " ", "bad\nvalue" })
            {
                await input.ValueChanged.InvokeAsync(draft);
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            }
            await input.ValueChanged.InvokeAsync(saved);
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            if (!canReset)
            {
                await input.ValueChanged.InvokeAsync(""); graph.ToProjectJson().ShouldBe(before);
            }
            await input.ValueChanged.InvokeAsync(valid);
            if (valid.Length == 0) graph.SelectedNode!.Configuration.ContainsKey(key).ShouldBeFalse();
            else graph.SelectedNode!.Configuration[key].GetString().ShouldBe(valid);
            graph.SelectedNode.Configuration.ContainsKey(property).ShouldBeFalse();
            graph.SelectedNode.Configuration["Extra"].GetProperty("Keep").GetInt32().ShouldBe(42);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Theory]
    [InlineData("nats.jetstream.publisher", "Stream")]
    [InlineData("nats.jetstream.consumer", "Consumer")]
    public void JetStream_name_validation_matches_native_length_limit(string type, string property)
    {
        var validate = JetStreamDescription.Options(type).Single(option => option.Name == property).ValidateText!;
        validate(new string('a', 255)).ShouldBeNull();
        validate(new string('a', 256)).ShouldNotBeNull();
        FluxFlow.Components.Nats.JetStream.JetStreamOptions.Name(new string('a', 255));
        Should.Throw<ArgumentException>(() => FluxFlow.Components.Nats.JetStream.JetStreamOptions.Name(new string('a', 256)));
    }
}
