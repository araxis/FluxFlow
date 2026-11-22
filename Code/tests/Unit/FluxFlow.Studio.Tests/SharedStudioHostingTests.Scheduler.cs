using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.Scheduler;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Pages;
using FluxFlow.Studio.UI.Features.Observations.Validation;
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
    [InlineData(false, "*/5 * * * *", true)]
    [InlineData(true, "*/5 * * * * *", true)]
    [InlineData(false, "*/5 * * * * *", false)]
    [InlineData(true, "*/5 * * * *", false)]
    [InlineData(false, "61 * * * *", false)]
    [InlineData(false, "not cron", false)]
    [InlineData(false, " ", false)]
    [InlineData(false, "", true)]
    [InlineData(true, null, true)]
    public void Scheduler_cron_field_uses_native_syntax_and_retains_runner_zone(bool seconds, string? cron, bool valid)
    {
        var configuration = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>($$"""{"iNcLuDeSeCoNdS":"{{seconds}}","TimeZone":"Runner/Custom","Extra":42} """)!;
        var before = JsonSerializer.Serialize(configuration);
        var options = new SchedulerDescription().GetInspector(configuration).Sections.SelectMany(section => section.Options).ToArray();
        var error = options.Single(option => option.Name == "Cron").ValidateText!(cron);
        (error is null).ShouldBe(valid);
        if (!valid) { error!.ShouldContain(seconds ? "six-field" : "five-field"); error.ShouldNotContain('\0'); }
        options.Single(option => option.Name == "TimeZone").ValidateText.ShouldBeNull();
        new SchedulerDescription().Inspector.Sections.SelectMany(section => section.Options).Single(option => option.Name == "Interval").MinDuration.ShouldBe(TimeSpan.FromTicks(1));
        JsonSerializer.Serialize(configuration).ShouldBe(before);
    }

    [Fact]
    public async Task Scheduler_cron_edits_revalidate_seconds_mode_and_preserve_history()
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"timer":{"Type":"scheduler","cRoN":"*/5 * * * *","iNcLuDeSeCoNdS":"false","TimeZone":"Runner/Custom","Extra":42}}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        var before = graph.ToProjectJson();
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            MudTextField<string> Cron() => components.Items.OfType<MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Cron"));
            await Cron().ValueChanged.InvokeAsync("not cron");
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            diagnostics.Messages.Values.ShouldContain(message => message.Path == "Cron");
            await Cron().ValueChanged.InvokeAsync("*/5 * * * *");
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await components.Items.OfType<MudCheckBox<bool>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Include seconds"))
                .ValueChanged.InvokeAsync(true);
            diagnostics.Messages.Values.ShouldContain(message => message.Path == "Cron");
            graph.SelectedNode!.Configuration["cRoN"].GetString().ShouldBe("*/5 * * * *");
            var modeOnly = graph.ToProjectJson();
            await Cron().ValueChanged.InvokeAsync("*/5 * * * * *");
            diagnostics.Messages.Values.ShouldNotContain(message => message.Path == "Cron");
            graph.SelectedNode!.Configuration["cRoN"].GetString().ShouldBe("*/5 * * * * *");
            graph.SelectedNode.Configuration["TimeZone"].GetString().ShouldBe("Runner/Custom");
            graph.SelectedNode.Configuration["Extra"].GetInt32().ShouldBe(42);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(modeOnly);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Fact]
    public async Task Dependent_validation_rechecks_an_unsaved_draft_without_committing_it()
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var seconds = false; var commits = 0;
        var description = new SchedulerDescription();
        string? Validate(string? value)
        {
            var settings = new Dictionary<string, JsonElement> { ["IncludeSeconds"] = JsonSerializer.SerializeToElement(seconds) };
            return description.GetInspector(settings).Sections.SelectMany(section => section.Options).Single(option => option.Name == "Cron").ValidateText!(value);
        }
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<ValidatedTextField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Value"] = "* * * * *", ["Label"] = "Cron", ["Validate"] = (Func<string?, string?>)Validate,
                ["Address"] = FluxFlow.Application.Addressing.ComponentAddress.Create("main", "timer"),
                ["ValueChanged"] = EventCallback.Factory.Create<string?>(this, _ => commits++)
            }));
            var input = components.Items.OfType<MudTextField<string>>().Single();
            await input.ValueChanged.InvokeAsync("* * * * * *");
            commits.ShouldBe(0); diagnostics.Messages.ShouldNotBeEmpty();
            seconds = true;
            await components.Items.OfType<ValidatedTextField>().Single().SetParametersAsync(ParameterView.Empty);
            commits.ShouldBe(0); diagnostics.Messages.ShouldBeEmpty(); input.Value.ShouldBe("* * * * * *");
            seconds = false;
            await components.Items.OfType<ValidatedTextField>().Single().SetParametersAsync(ParameterView.Empty);
            commits.ShouldBe(0); diagnostics.Messages.ShouldNotBeEmpty(); input.Value.ShouldBe("* * * * * *");
        });
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"Interval\":\"00:00:01\"}", false)]
    [InlineData("{\"At\":\"2030-01-01T09:00:00+01:00\"}", false)]
    [InlineData("{\"Cron\":null,\"TimeZone\":null,\"IncludeSeconds\":null}", false)]
    [InlineData("{\"cRoN\":\"* * * * *\"}", true)]
    [InlineData("{\"Cron\":\"\"}", true)]
    [InlineData("{\"Interval\":\"00:00:01\",\"tImEzOnE\":\"Custom/RunnerZone\"}", true)]
    [InlineData("{\"iNcLuDeSeCoNdS\":false}", true)]
    [InlineData("{\"IncludeSeconds\":\"invalid\"}", true)]
    public void Scheduler_keeps_conflicting_saved_settings_available_for_repair(string json, bool cronSettings)
    {
        var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        var before = JsonSerializer.Serialize(settings);
        var description = new SchedulerDescription();
        var options = description.GetInspector(settings).Sections.SelectMany(section => section.Options).ToArray();
        options.Any(option => option.Name == "TimeZone").ShouldBe(cronSettings);
        options.Any(option => option.Name == "IncludeSeconds").ShouldBe(cronSettings);
        var modes = new[] { "Interval", "Cron", "At" }.Where(name => settings.Any(pair =>
            pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase) && pair.Value.ValueKind != JsonValueKind.Null)).ToArray();
        options.Where(option => new[] { "Interval", "Cron", "At" }.Contains(option.Name)).Select(option => option.Name)
            .ShouldBe(modes.Length == 1 ? modes : modes.Length > 1 ? new[] { "Interval", "At", "Cron" } : cronSettings ? new[] { "Cron" } : Array.Empty<string>());
        JsonSerializer.Serialize(settings).ShouldBe(before);
    }

    [Fact]
    public async Task Scheduler_schedule_selector_switches_atomically_and_round_trips_history()
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"timer":{"Type":"scheduler","cRoN":"*/5 * * * * *","iNcLuDeSeCoNdS":"true","TimeZone":"Europe/Stockholm","Extra":{"keep":42}}}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            MudSelect<string> Selector() => components.Items.OfType<MudSelect<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Schedule"));
            Selector().Value.ShouldBe("Cron");
            await Selector().ValueChanged.InvokeAsync("Cron");
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            var stale = Selector();
            await Selector().ValueChanged.InvokeAsync("Interval");
            graph.SelectedNode!.Configuration.ContainsKey("cRoN").ShouldBeFalse();
            graph.SelectedNode.Configuration.ContainsKey("TimeZone").ShouldBeFalse();
            graph.SelectedNode.Configuration.ContainsKey("iNcLuDeSeCoNdS").ShouldBeFalse();
            graph.SelectedNode.Configuration["Interval"].GetString().ShouldBe("00:00:01");
            var interval = graph.ToProjectJson();
            await stale.ValueChanged.InvokeAsync("At");
            graph.ToProjectJson().ShouldBe(interval);
            await Selector().ValueChanged.InvokeAsync("At");
            graph.SelectedNode!.Configuration["At"].GetString().ShouldBe("");
            graph.SelectedNode.Configuration.ContainsKey("Interval").ShouldBeFalse();
            var once = graph.ToProjectJson();
            await components.Items.OfType<MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "At"))
                .ValueChanged.InvokeAsync("2030-03-31T01:30:00+01:00");
            var dated = graph.ToProjectJson();
            await Selector().ValueChanged.InvokeAsync("Cron");
            graph.SelectedNode!.Configuration["Cron"].GetString().ShouldBe("* * * * *");
            graph.SelectedNode.Configuration.ContainsKey("At").ShouldBeFalse();
            graph.SelectedNode.Configuration.ContainsKey("Schedule").ShouldBeFalse();
            graph.SelectedNode.Configuration["Extra"].GetProperty("keep").GetInt32().ShouldBe(42);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(dated);
            graph.Undo(); graph.ToProjectJson().ShouldBe(once);
            graph.Undo(); graph.ToProjectJson().ShouldBe(interval);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            for (var i = 0; i < 4; i++) graph.Redo();
            graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Fact]
    public void Scheduler_selector_preserves_conflicts_until_an_explicit_choice()
    {
        var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"iNtErVaL":"00:00:07","aT":"2030-01-01T09:00:00+02:00","Cron":"* * * * *","TimeZone":"Custom/RunnerZone","Extra":42}""")!;
        var original = JsonSerializer.Serialize(settings);
        var group = SchedulerDescription.Groups(System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject()).Single();
        var field = group.Fields!(settings).Single();
        field.Value.ShouldBe("");
        group.Options.Select(option => option.Name).ShouldContain("Interval");
        group.Options.Select(option => option.Name).ShouldContain("At");
        group.Options.Select(option => option.Name).ShouldContain("Cron");
        JsonSerializer.Serialize(settings).ShouldBe(original);
        field.Write!(settings, "unknown"); JsonSerializer.Serialize(settings).ShouldBe(original);
        field.Write!(settings, "At");
        settings["aT"].GetString().ShouldBe("2030-01-01T09:00:00+02:00");
        settings.Keys.ShouldBe(new[] { "aT", "Extra" });
    }

    [Theory]
    [InlineData(null, "2030-03-30T01:02:03.1234567+05:30")]
    [InlineData("null", "2030-01-01T00:00:00Z")]
    [InlineData("\"invalid saved date\"", "2030-01-01T09:00:00-07:00")]
    [InlineData("\"2030-01-01T09:00:00+01:00\"", "")]
    [InlineData("\"2030-01-01T09:00:00+01:00\"", "2030-01-01T09:00:00+01:00")]
    public async Task Scheduler_date_editor_preserves_offset_spelling_invalid_drafts_and_history(string? saved, string valid)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"timer":{"Type":"scheduler","Extra":42}}}}""");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        if (saved is not null) node.Configuration["aT"] = JsonSerializer.Deserialize<JsonElement>(saved);
        graph.ResetHistory();
        if (saved != "null") { graph.AddEnvironment("Undo me"); graph.Undo(); }
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        var canRedo = graph.CanRedo;
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        var option = new SchedulerDescription().Inspector.Sections.SelectMany(section => section.Options).Single(option => option.Name == "At");
        await using (var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>()))
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(OptionEditorField.Configuration)] = node.Configuration,
                    [nameof(OptionEditorField.Option)] = option,
                    [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
                }));
                graph.ToProjectJson().ShouldBe(before);
                diagnostics.Messages.Count.ShouldBe(saved?.Contains("invalid") == true ? 1 : 0);
                var input = components.Items.OfType<MudTextField<string>>().Single();
                await input.ValueChanged.InvokeAsync("not a date");
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBe(canRedo);
                diagnostics.Messages.Values.Single().Path.ShouldBe("At");
                await components.Items.OfType<MudIconButton>().Single(button => button.UserAttributes?.GetValueOrDefault("aria-label") as string == "Show validation for At")
                    .OnClick.InvokeAsync(new MouseEventArgs());
                diagnostics.Selected.ShouldNotBeNull();
                await input.ValueChanged.InvokeAsync(valid);
                diagnostics.Messages.ShouldBeEmpty();
            });
        }
        diagnostics.Messages.ShouldBeEmpty();
        var after = graph.ToProjectJson();
        if (saved is not null && JsonSerializer.Deserialize<JsonElement>(saved).ToString() == valid)
        { after.ShouldBe(before); graph.CanRedo.ShouldBeTrue(); return; }
        var key = saved is null ? "At" : "aT";
        if (valid.Length == 0) node.Configuration.ContainsKey(key).ShouldBeFalse();
        else node.Configuration[key].GetString().ShouldBe(valid);
        node.Configuration["Extra"].GetInt32().ShouldBe(42);
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }
}
