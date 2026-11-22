using FluxFlow.Studio.UI;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Observations;
using FluxFlow.Studio.UI.Features.Observations.Logs;
using FluxFlow.Studio.UI.Features.Observations.Traces;
using FluxFlow.Studio.UI.Features.Observations.Output;
using FluxFlow.Studio.UI.Features.Observations.Details;
using System.Text.Json;
using FluxFlow.Conversations;
using FluxFlow.Studio.UI.Features.Designer.Pages;
using FluxFlow.Studio.UI.Features.Designer.Projects;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Extensions;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Theory]
    [InlineData("cancel", true)]
    [InlineData("edit", true)]
    [InlineData("hide", true)]
    [InlineData("dispose", true)]
    [InlineData("cancel", false)]
    [InlineData("edit", false)]
    [InlineData("hide", false)]
    [InlineData("dispose", false)]
    public async Task Environment_check_toasts_repeat_without_editing_and_ignore_obsolete_results(string interruption, bool success)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var run = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        var snackbar = scope.ServiceProvider.GetRequiredService<MudBlazor.ISnackbar>();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
        var saved = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>();
            var page = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>().Single();
            var check = components.Items.OfType<StudioIconButton>().Single(button => button.Label == "Check environment");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var pending = check.OnClick.InvokeAsync(new MouseEventArgs());
                check.Disabled.ShouldBeTrue();
                runner.CheckRelease.SetResult(new(success, "Environment check result"));
                await pending;
                snackbar.ShownSnackbars.Single().Severity.ShouldBe(success ? MudBlazor.Severity.Success : MudBlazor.Severity.Error);
                view.ToHtmlString().ShouldNotContain("Environment check result");
                snackbar.Clear();
                await page.SetParametersAsync(ParameterView.Empty);
                snackbar.ShownSnackbars.ShouldBeEmpty();
                graph.ToProjectJson().ShouldBe(saved);
                graph.Revision.ShouldBe(revision);
            }
            var late = check.OnClick.InvokeAsync(new MouseEventArgs());
            if (interruption == "cancel") run.CancelCheck();
            else if (interruption == "edit") graph.SetEnvironment(new() { StartupTimeoutSeconds = 120 });
            else if (interruption == "hide")
                await page.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Hidden"] = true }));
            else page.Dispose();
            runner.CheckRelease.SetResult(new(success, "Late result"));
            await late;
            snackbar.ShownSnackbars.ShouldBeEmpty();
            view.ToHtmlString().ShouldNotContain("Late result");
            if (interruption == "edit") graph.Undo();
        });
        graph.ToProjectJson().ShouldBe(saved);
        graph.Undo();
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldNotBe(90);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(saved);
    }

    [Fact]
    public async Task Topology_application_traces_keep_whole_hierarchies_and_stop_retention_but_reject_stale_requests()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var run = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "first", Name = "Application", Kind = FluxFlow.Environments.EnvironmentNodeKind.Application },
            new() { Id = "second", Name = "Application", Kind = FluxFlow.Environments.EnvironmentNodeKind.Application },
            new() { Id = "idle", Name = "Application", Kind = FluxFlow.Environments.EnvironmentNodeKind.Application }
        ] });
        var environment = graph.ActiveEnvironmentId;
        graph.AddEnvironment("Other");
        var other = graph.ActiveEnvironmentId;
        graph.SelectEnvironment(environment);
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 123 });
        var project = graph.ToProjectJson();
        var root = TraceSpan("shared", "root", null, 0, 20);
        var child = TraceSpan("shared", "child", "root", 2, 5) with
        { Attributes = new Dictionary<string, JsonElement> { ["flow.application.instance.id"] = JsonSerializer.SerializeToElement("first") } };
        var sibling = TraceSpan("shared", "sibling", "root", 10, 5) with
        { Status = "Error", Attributes = new Dictionary<string, JsonElement> { ["flow.application.instance.id"] = JsonSerializer.SerializeToElement("second") } };
        var unrelated = TraceSpan("unrelated", "other", null, 30, 5) with { Attributes = sibling.Attributes };
        var spans = new[] { root, child, sibling, unrelated };
        var original = JsonSerializer.Serialize(spans);
        var start = run.StartAsync(graph);
        runner.Snapshot = new(runner.Id, "Running", [], Traces: new(spans, 0));
        runner.Release.SetResult(runner.Snapshot);
        await start;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            var designer = components.Items.OfType<DesignerPage>().Single();
            async Task<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel> OpenTopology(string instance = "first")
            {
                designer.SelectEnvironment(environment);
                await designer.SetParametersAsync(ParameterView.Empty);
                return components.Items.OfType<CascadingValue<Blazor.Diagrams.BlazorDiagram>>().Last().Value!.Nodes
                    .OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single(node => node.Definition.Id == instance);
            }
            var node = await OpenTopology();
            var target = node.TelemetryTarget!;
            graph.SelectEnvironment(other);
            await node.ShowTraces!(target);
            view.ToHtmlString().ShouldNotContain("Application traces filter");
            graph.SelectEnvironment(environment);
            graph.SetEnvironment(new() { StartupTimeoutSeconds = 124 });
            await node.ShowTraces!(target);
            view.ToHtmlString().ShouldNotContain("Application traces filter");
            graph.Undo();
            await node.ShowTraces!(target);
            var traces = components.Items.OfType<ExecutionTraces>().Last();
            traces.Application!.InstanceId.ShouldBe("first");
            view.ToHtmlString().ShouldNotContain("studio-designer-page\" hidden");
            AssertHierarchy();
            await traces.SearchTextChanged.InvokeAsync(sibling.Name);
            await traces.ErrorsOnlyChanged.InvokeAsync(true);
            AssertHierarchy();
            await traces.SelectSpan("child");
            await run.StopAsync();
            AssertHierarchy();
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"child\"");
            await traces.OnClearApplicationFilter.InvokeAsync();
            traces.Application.ShouldBeNull();
            traces.SearchText.ShouldBe(sibling.Name);
            traces.ErrorsOnly.ShouldBeTrue();
            await traces.OnClearFilters.InvokeAsync();
            view.ToHtmlString().ShouldContain("2 / 2 traces");
            node = await OpenTopology("second");
            await node.ShowTraces!(node.TelemetryTarget!);
            traces = components.Items.OfType<ExecutionTraces>().Last();
            traces.Application!.InstanceId.ShouldBe("second");
            view.ToHtmlString().ShouldContain("2 / 2 traces");
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"child\"");
            node = await OpenTopology("idle");
            await node.ShowTraces!(node.TelemetryTarget!);
            traces.Application!.InstanceId.ShouldBe("idle");
            view.ToHtmlString().ShouldContain("0 / 2 traces");
            view.ToHtmlString().ShouldContain("No retained traces match the current filters.");
            view.ToHtmlString().ShouldNotContain("data-span-id=");
            await traces.OnClearApplicationFilter.InvokeAsync();
            view.ToHtmlString().ShouldContain("2 / 2 traces");
            node = await OpenTopology();
            await node.ShowTraces!(node.TelemetryTarget!);
            traces = components.Items.OfType<ExecutionTraces>().Last();
            AssertHierarchy();
            await traces.OnClearFilters.InvokeAsync();
            traces.Application.ShouldBeNull();
            node = await OpenTopology();
            await node.ShowTraces!(node.TelemetryTarget!);
            start = run.StartAsync(graph);
            runner.Snapshot = runner.Snapshot with { Id = runner.Id };
            runner.Release.SetResult(runner.Snapshot);
            await start;
            traces = components.Items.OfType<ExecutionTraces>().Last();
            traces.Application.ShouldBeNull();
            await node.ShowTraces!(target);
            view.ToHtmlString().ShouldNotContain("Application traces filter");
            await run.StopAsync();

            void AssertHierarchy()
            {
                var html = view.ToHtmlString();
                html.ShouldContain("1 / 2 traces");
                html.ShouldContain("data-span-id=\"root\" data-depth=\"0\"");
                html.ShouldContain("data-span-id=\"child\" data-depth=\"1\"");
                html.ShouldContain("data-span-id=\"sibling\" data-depth=\"1\"");
                html.ShouldNotContain("data-span-id=\"other\"");
            }
        });
        JsonSerializer.Serialize(run.Traces.Records).ShouldBe(original);
        graph.ToProjectJson().ShouldBe(project);
        graph.Undo();
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldNotBe(123);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(project);
    }

    [Fact]
    public async Task Topology_application_logs_use_exact_instance_ids_and_retain_after_stop_without_editing()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var run = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "first", Name = "Application", Kind = FluxFlow.Environments.EnvironmentNodeKind.Application },
            new() { Id = "second", Name = "Application", Kind = FluxFlow.Environments.EnvironmentNodeKind.Application }
        ] });
        var environment = graph.ActiveEnvironmentId;
        graph.AddEnvironment("Other");
        var other = graph.ActiveEnvironmentId;
        graph.SelectEnvironment(environment);
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 123 });
        var project = graph.ToProjectJson();
        FluxFlow.Environments.ExecutionLog Log(long sequence, string message, string? instance) =>
            new(sequence, DateTimeOffset.UtcNow, "Information", "main.record", 7, "Event", message, "Full exception",
                new(DateTimeOffset.UtcNow, 9, "Information", message, null, null, 0,
                    instance is null ? [] : [new("flow.application.instance.id", JsonSerializer.SerializeToElement(instance))], []));
        var records = new[] { Log(1, "First instance", "first"), Log(2, "Second instance", "second"), Log(3, "Unscoped", null) };
        var diagnostic = Log(1, "Instance startup", "first");
        var originalRecords = JsonSerializer.Serialize(records);
        var start = run.StartAsync(graph);
        runner.Snapshot = new(runner.Id, "Running", [], Logs: new(records, 0), Diagnostics: new([diagnostic], 0));
        runner.Release.SetResult(runner.Snapshot);
        await start;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            var designer = components.Items.OfType<DesignerPage>().Single();
            designer.SelectEnvironment(environment);
            await designer.SetParametersAsync(ParameterView.Empty);
            var diagram = components.Items.OfType<CascadingValue<Blazor.Diagrams.BlazorDiagram>>().Last().Value!;
            var node = diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single(node => node.Definition.Id == "first");
            var target = node.TelemetryTarget!;
            target.InstanceId.ShouldBe("first");
            target.RunId.ShouldBe(runner.Id);

            graph.SelectEnvironment(other);
            await node.ShowLogs!(target);
            view.ToHtmlString().ShouldNotContain("Application logs filter");
            graph.SelectEnvironment(environment);
            graph.SetEnvironment(new() { StartupTimeoutSeconds = 124 });
            await node.ShowLogs!(target);
            view.ToHtmlString().ShouldNotContain("Application logs filter");
            graph.Undo();
            await node.ShowLogs!(target);
            view.ToHtmlString().ShouldContain("Application logs filter");
            view.ToHtmlString().ShouldNotContain("studio-designer-page\" hidden");
            var logs = components.Items.OfType<ExecutionLogs>().Last();
            logs.Application!.InstanceId.ShouldBe("first");
            view.ToHtmlString().ShouldContain("2 / 4 retained");
            view.ToHtmlString().ShouldNotContain(">Second instance</button>");
            view.ToHtmlString().ShouldNotContain(">Unscoped</button>");
            await logs.SearchTextChanged.InvokeAsync("startup");
            view.ToHtmlString().ShouldContain("1 / 4 retained");
            await run.StopAsync();
            logs.Application!.InstanceId.ShouldBe("first");
            view.ToHtmlString().ShouldContain("1 / 4 retained");
            await logs.OnClearApplicationFilter.InvokeAsync();
            logs.Application.ShouldBeNull();
            logs.SearchText.ShouldBe("startup");
            await logs.ClearFilters();
            view.ToHtmlString().ShouldContain("4 / 4 retained");

            designer.SelectEnvironment(environment);
            await designer.SetParametersAsync(ParameterView.Empty);
            diagram = components.Items.OfType<CascadingValue<Blazor.Diagrams.BlazorDiagram>>().Last().Value!;
            node = diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single(node => node.Definition.Id == "first");
            await node.ShowLogs!(node.TelemetryTarget!);
            logs = components.Items.OfType<ExecutionLogs>().Last();
            logs.Application!.InstanceId.ShouldBe("first");
            await logs.ClearFilters();
            logs.Application.ShouldBeNull();
            // Requesting the same instance again must work after its filter was removed.
            designer.SelectEnvironment(environment);
            await designer.SetParametersAsync(ParameterView.Empty);
            diagram = components.Items.OfType<CascadingValue<Blazor.Diagrams.BlazorDiagram>>().Last().Value!;
            node = diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single(node => node.Definition.Id == "first");
            await node.ShowLogs!(node.TelemetryTarget!);
            logs.Application!.InstanceId.ShouldBe("first");
            start = run.StartAsync(graph);
            runner.Snapshot = runner.Snapshot with { Id = runner.Id };
            runner.Release.SetResult(runner.Snapshot);
            await start;
            logs.Application.ShouldBeNull();
            await node.ShowLogs!(target);
            view.ToHtmlString().ShouldNotContain("Application logs filter");
            await run.StopAsync();
        });
        JsonSerializer.Serialize(run.Logs.Records).ShouldBe(originalRecords);
        graph.ToProjectJson().ShouldBe(project);
        graph.Undo();
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldNotBe(123);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(project);
    }

    [Fact]
    public async Task Run_environment_selection_cancels_stale_checks_and_stop_keeps_the_running_snapshot()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var run = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        graph.LoadJson("""{"KeepCase":42,"Workflows":{"main":{}}}""");
        graph.IsProjectOpen = true;
        var firstId = graph.ActiveEnvironmentId;
        var firstName = graph.EnvironmentName;
        graph.AddEnvironment("Integration test");
        var secondId = graph.ActiveEnvironmentId;
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 120 });
        graph.SelectEnvironment(firstId);
        graph.ResetHistory();
        var configuration = graph.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            var picker = components.Items.OfType<MudBlazor.MudSelect<string>>().Single(item => item.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Run environment"));
            var play = components.Items.OfType<StudioIconButton>().Single(item => item.Label == "Run application locally");
            var designer = components.Items.OfType<DesignerPage>().Single();
            designer.SelectEnvironment(secondId);
            await designer.SetParametersAsync(ParameterView.Empty);
            components.Items.OfType<FluxFlow.Studio.UI.Components.Navigation.StudioRunControls>().Last().EnvironmentId.ShouldBe(firstId);
            designer.SelectWorkflow(graph.WorkflowName);
            await designer.SetParametersAsync(ParameterView.Empty);
            var pending = play.OnClick.InvokeAsync();
            runner.Request!.Environment.StartupTimeoutSeconds.ShouldBe(60);
            var lateCheck = runner.CheckRelease;
            await picker.ValueChanged.InvokeAsync(secondId);
            runner.Token.IsCancellationRequested.ShouldBeTrue();
            lateCheck.SetResult(new(true, "Old environment"));
            await pending;
            runner.Id.ShouldBe(Guid.Empty);
            view.ToHtmlString().ShouldNotContain("studio-designer-page\" hidden");
            run.CheckResult.ShouldBeNull();

            var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            run.Changed += () => { if (run.Status?.State == "Preparing") preparing.TrySetResult(); };
            pending = play.OnClick.InvokeAsync();
            runner.CheckRelease.SetResult(new(true, "Ready"));
            await preparing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var runningId = runner.Id;
            runner.Request!.Environment.StartupTimeoutSeconds.ShouldBe(120);
            run.RunEnvironmentName.ShouldBe("Integration test");
            runner.Release.SetResult(new(runningId, "Running", []));
            await pending;
            picker = components.Items.OfType<MudBlazor.MudSelect<string>>().Last(item => item.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Run environment"));
            await picker.ValueChanged.InvokeAsync(firstId);
            graph.EnvironmentName.ShouldBe(firstName);
            run.RunEnvironmentName.ShouldBe("Integration test");
            System.Net.WebUtility.HtmlDecode(view.ToHtmlString()).ShouldContain("Integration test · Running");
            var saved = graph.ToProjectJson();
            await components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Stop and clean up").OnClick.InvokeAsync();
            runner.Stopped.ShouldBe(new[] { runningId });
            run.Status!.State.ShouldBe("Stopped");
            run.RunEnvironmentName.ShouldBe("Integration test");
            graph.ToProjectJson().ShouldBe(saved);
            graph.ExportConfiguration().ShouldBe(configuration);
            graph.CanUndo.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Environment_inspector_timeout_is_undoable_and_rejects_edits_after_switching()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"KeepCase":42,"Resources":{"api":{"Type":"http.client"}},"Workflows":{"main":{}}}""");
        graph.SetTopology(new() { Nodes = [new() { Id = "api", Kind = FluxFlow.Environments.EnvironmentNodeKind.Service, Name = "api", Resource = "Resources:api" }] });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>();
            var page = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>().Single();
            var inspector = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyInspector>().Last();
            var change = inspector.SetTimeout;
            await change.InvokeAsync(0);
            await change.InvokeAsync(601);
            graph.ToProjectJson().ShouldBe(before);
            await change.InvokeAsync(120);
            var saved = graph.ToProjectJson();
            graph.GetEnvironment().StartupTimeoutSeconds.ShouldBe(120);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
            var reopened = StudioTestServices.CreateGraph();
            reopened.LoadProject(saved);
            reopened.GetEnvironment().StartupTimeoutSeconds.ShouldBe(120);
            page.ShowServiceSetup("Resources:api").ShouldBeTrue();
            components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyInspector>().Last().Selected!.Id.ShouldBe("api");
            components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>().Last().ResourcePath.ShouldBe("Resources:api");
            page.ShowServiceSetup("Resources:removed").ShouldBeFalse();
            page.ShowEnvironmentProperties();
            components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyInspector>().Last().Selected.ShouldBeNull();
            graph.ToProjectJson().ShouldBe(saved);
            graph.AddEnvironment("Other");
            var switched = graph.ToProjectJson();
            await change.InvokeAsync(300);
            graph.ToProjectJson().ShouldBe(switched);
            graph.GetEnvironment().StartupTimeoutSeconds.ShouldBe(60);
            graph.ExportConfiguration().ShouldBe(configuration);
        });
    }

    [Theory]
    [InlineData("Application", false)]
    [InlineData("Host", true)]
    [InlineData("Service", false)]
    public async Task Check_navigation_preserves_view_and_history_and_rejects_obsolete_targets(string kind, bool switchEnvironment)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var run = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://localhost:1883"}},"Workflows":{"main":{}}}""");
        graph.IsProjectOpen = true;
        graph.SetTopology(new() { Nodes = [new()
        {
            Id = "target", Name = "Target", Kind = Enum.Parse<FluxFlow.Environments.EnvironmentNodeKind>(kind),
            Resource = kind == "Service" ? "Resources:broker" : null, X = 1800, Y = 1200
        }] });
        graph.ResetHistory();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>();
            var page = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>().Single();
            var diagram = components.Items.OfType<CascadingValue<Blazor.Diagrams.BlazorDiagram>>().Last().Value!;
            diagram.SetContainer(new(new Blazor.Diagrams.Core.Geometry.Point(0, 0), new Blazor.Diagrams.Core.Geometry.Size(800, 500)));
            diagram.SetZoom(.8);
            diagram.SetPan(25, 40);
            var original = graph.ToProjectJson();
            var revision = graph.Revision;
            var check = run.CheckAsync(graph);
            var result = new FluxFlow.Environments.EnvironmentCheckResult(false, "Check this item")
            {
                NodeId = kind == "Service" ? null : "target",
                Resource = kind == "Service" ? "resources:BROKER" : null
            };
            runner.CheckRelease.SetResult(result);
            await check;
            view.ToHtmlString().ShouldNotContain("Check this item");
            scope.ServiceProvider.GetRequiredService<MudBlazor.ISnackbar>().ShownSnackbars.Single().Message.ShouldBe("Check this item");
            page.Workspace.Hide("Inspector");
            page.ShowCheckResult(result);
            page.RevealCheckTarget();
            diagram.GetSelectedModels().OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single().Definition.Id.ShouldBe("target");
            diagram.Zoom.ShouldBe(.8);
            diagram.Pan.ShouldNotBe(new Blazor.Diagrams.Core.Geometry.Point(25, 40));
            page.Workspace.Layout.Contains("Inspector").ShouldBeTrue();
            var pan = diagram.Pan;
            page.ShowCheckResult(result);
            page.RevealCheckTarget();
            diagram.Pan.ShouldBe(pan);
            graph.Revision.ShouldBe(revision);
            graph.ToProjectJson().ShouldBe(original);
            graph.CanUndo.ShouldBeFalse();

            page.Workspace.Layout.Group("Inspector", "Canvas");
            page.ShowCheckResult(result);
            page.RevealCheckTarget();
            var group = page.Workspace.Layout.Visit().Single(item => item.Node.Panels.Contains("Canvas")).Node;
            group.Panels.ShouldContain("Inspector");
            group.ActivePanel.ShouldBe("Canvas");
            diagram.Zoom.ShouldBe(.8);
            diagram.Pan.ShouldBe(pan);
            graph.ToProjectJson().ShouldBe(original);
            graph.CanUndo.ShouldBeFalse();

            check = run.CheckAsync(graph);
            page.ShowCheckResult(result);
            runner.CheckRelease.SetResult(new(false, "General failure"));
            await check;
            view.ToHtmlString().ShouldNotContain("Show in topology");
            run.ResolveCheckNode(graph, result).ShouldBeNull();
            check = run.CheckAsync(graph);
            var missing = result with { NodeId = "removed", Resource = null };
            runner.CheckRelease.SetResult(missing);
            await check;
            view.ToHtmlString().ShouldNotContain("Show in topology");
            page.ShowCheckResult(missing);
            diagram.Pan.ShouldBe(pan);

            check = run.CheckAsync(graph);
            runner.CheckRelease.SetResult(result);
            await check;
            page.ShowCheckResult(result);
            if (switchEnvironment) graph.AddEnvironment("Other");
            else graph.SetTopology(new());
            var changed = graph.ToProjectJson();
            page.RevealCheckTarget();
            page.ShowCheckResult(result);
            diagram.GetSelectedModels().ShouldBeEmpty();
            diagram.Pan.ShouldBe(pan);
            graph.ToProjectJson().ShouldBe(changed);
            view.ToHtmlString().ShouldNotContain("Show in topology");
            run.CheckResult.ShouldBeNull();
            graph.Undo();
            graph.ToProjectJson().ShouldBe(original);
            graph.Redo();
            if (switchEnvironment) graph.Environments.Count.ShouldBe(2);
            else graph.ToProjectJson().ShouldBe(changed);

            graph.Undo();
            check = run.CheckAsync(graph);
            runner.CheckRelease.SetResult(result);
            await check;
            diagram.SetPan(25, 40);
            page.ShowCheckResult(result);
            page.Dispose();
            page.RevealCheckTarget();
            diagram.Pan.ShouldBe(new Blazor.Diagrams.Core.Geometry.Point(25, 40));
            diagram.Zoom.ShouldBe(.8);
            graph.ToProjectJson().ShouldBe(original);
        });
    }

    [Theory]
    [InlineData("mqtt.client", "Address", "mqtt://localhost:1883", "mqtt://broker.internal:1883", false)]
    [InlineData("http.client", "BaseAddress", "http://localhost:8080/v1/", "http://api.internal:8080/v1/", true)]
    public async Task Editing_a_topology_connection_returns_to_the_same_canvas_and_preserves_history(
        string type, string endpointProperty, string originalEndpoint, string updatedEndpoint, bool workflowScoped)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var settings = new System.Text.Json.Nodes.JsonObject { ["Type"] = type, [endpointProperty] = originalEndpoint, ["KeepCase"] = 42 };
        var workflow = new System.Text.Json.Nodes.JsonObject();
        var configuration = new System.Text.Json.Nodes.JsonObject { ["Workflows"] = new System.Text.Json.Nodes.JsonObject { ["main"] = workflow } };
        (workflowScoped ? workflow : configuration)["Resources"] = new System.Text.Json.Nodes.JsonObject
        {
            ["service"] = settings,
            ["other"] = settings.DeepClone()
        };
        graph.LoadJson(configuration.ToJsonString());
        graph.IsProjectOpen = true;
        var path = workflowScoped ? "Workflows:main:Resources:service" : "Resources:service";
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "remote", Name = "Remote", Kind = FluxFlow.Environments.EnvironmentNodeKind.Host },
            new() { Id = "service", Name = "Service", Kind = FluxFlow.Environments.EnvironmentNodeKind.Service, Resource = path, Host = "remote" }
        ] });
        graph.ResetHistory();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<DesignerPage>();
            var page = components.Items.OfType<DesignerPage>().Single();
            await EventCallback.Factory.Create<string>(page, page.SelectEnvironment).InvokeAsync(graph.ActiveEnvironmentId);
            var topology = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>().Single();
            rendered.ToHtmlString().ShouldNotMatch("<main[^>]*class=\"environment-topology\"[^>]* hidden");
            var diagram = components.Items.OfType<CascadingValue<Blazor.Diagrams.BlazorDiagram>>().Last().Value!;
            diagram.SelectModel(diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single(node => node.Definition.Id == "service"), true);
            diagram.SetZoom(.8);
            diagram.SetPan(25, 40);
            var before = graph.ToProjectJson();
            await components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Edit connection").OnClick.InvokeAsync();
            topology.Hidden.ShouldBeTrue();
            rendered.ToHtmlString().ShouldMatch("<main[^>]*class=\"environment-topology\"[^>]* hidden");
            var connections = components.Items.OfType<FluxFlow.Studio.UI.Features.Connections.ConnectionInspector>().Last();
            connections.Selection.ShouldBe(path);
            connections.BackLabel.ShouldBe("Back to topology");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            await components.Items.OfType<MudBlazor.MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Address")).ValueChanged.InvokeAsync(updatedEndpoint);
            var edited = graph.ToProjectJson();
            await components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Back to topology").OnClick.InvokeAsync();
            topology.Hidden.ShouldBeFalse();
            rendered.ToHtmlString().ShouldNotMatch("<main[^>]*class=\"environment-topology\"[^>]* hidden");
            components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>().Count().ShouldBe(1);
            diagram.Zoom.ShouldBe(.8);
            diagram.Pan.ShouldBe(new Blazor.Diagrams.Core.Geometry.Point(25, 40));
            var selected = diagram.GetSelectedModels().OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single();
            selected.Definition.Id.ShouldBe("service");
            selected.Endpoint!.Value.ShouldBe(updatedEndpoint);
            selected.Endpoint.Warning.ShouldBeNull();
            graph.Connections.Single(item => item.Path == path).Settings["KeepCase"]!.GetValue<int>().ShouldBe(42);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            diagram.GetSelectedModels().OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single().Endpoint!.Warning.ShouldNotBeNull();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(edited);

            await components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Edit connection").OnClick.InvokeAsync();
            connections = components.Items.OfType<FluxFlow.Studio.UI.Features.Connections.ConnectionInspector>().Last();
            var otherPath = graph.Connections.Single(item => item.Name == "other").Path;
            await EventCallback.Factory.Create<string>(page, page.OpenConnection).InvokeAsync(otherPath);
            await components.Items.OfType<MudBlazor.MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Address")).ValueChanged.InvokeAsync(updatedEndpoint);
            connections.Selection.ShouldBe(otherPath);
            graph.Undo();
            connections.Selection.ShouldBe(otherPath);
            graph.Connections.Single(item => item.Path == otherPath).Settings[endpointProperty]!.GetValue<string>().ShouldBe(originalEndpoint);
            graph.Redo();
            connections.Selection.ShouldBe(otherPath);
            graph.Connections.Single(item => item.Path == otherPath).Settings[endpointProperty]!.GetValue<string>().ShouldBe(updatedEndpoint);
            await components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Back to topology").OnClick.InvokeAsync();
            topology.Hidden.ShouldBeFalse();
            diagram.GetSelectedModels().OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single().Definition.Id.ShouldBe("service");
            diagram.Zoom.ShouldBe(.8);
            diagram.Pan.ShouldBe(new Blazor.Diagrams.Core.Geometry.Point(25, 40));

            var beforeNavigation = graph.ToProjectJson();
            await components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Edit connection").OnClick.InvokeAsync();
            await EventCallback.Factory.Create<string>(page, page.SelectWorkflow).InvokeAsync("main");
            rendered.ToHtmlString().ShouldNotContain("aria-label=\"Connections\"");
            rendered.ToHtmlString().ShouldNotContain("aria-label=\"Environment topology\"");
            graph.ToProjectJson().ShouldBe(beforeNavigation);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Topology_live_endpoints_clear_during_stop_and_ignore_late_startup(bool cancelDuringStartup)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner { StopRelease = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var run = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client","BaseAddress":"http://configured/v1/"}},"Workflows":{"main":{}}}""");
        graph.SetTopology(new() { Nodes = [new() { Id = "api", Name = "API", Kind = FluxFlow.Environments.EnvironmentNodeKind.Service, Resource = "Resources:api" }] });
        var setup = graph.GetEnvironment();
        setup.Resources["Resources:api"] = new() { Provider = "wiremock" };
        graph.SetEnvironment(setup);
        graph.ResetHistory();
        var original = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var rendered = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>();
            var diagram = components.Items.OfType<CascadingValue<Blazor.Diagrams.BlazorDiagram>>().Single().Value!;
            diagram.SelectModel(diagram.Nodes.Single(), true);
            rendered.ToHtmlString().ShouldContain("Assigned at startup");
            var start = run.StartAsync(graph);
            var first = new FluxFlow.Environments.EnvironmentRunStatus(runner.Id, "Running",
                [new("Resources:api", "wiremock", "http://127.0.0.1:45678/", "first")]);
            diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single().RunState!.Label.ShouldBe("Starting");
            if (!cancelDuringStartup)
            {
                runner.Release.SetResult(first);
                await start;
                rendered.ToHtmlString().ShouldContain("http://127.0.0.1:45678/v1/");
                diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single().RunState!.Label.ShouldBe("Running");
            }
            var stop = run.StopAsync();
            diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single().RunState!.Label.ShouldBe("Stopping");
            rendered.ToHtmlString().ShouldContain("Assigned at startup");
            rendered.ToHtmlString().ShouldNotContain("45678");
            if (cancelDuringStartup)
            {
                runner.Release.SetResult(first);
                await start;
                rendered.ToHtmlString().ShouldNotContain("45678");
            }
            runner.Snapshot = first;
            runner.StopRelease.SetResult();
            await stop;
            diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single().RunState!.Label.ShouldBe("Stopped");
            rendered.ToHtmlString().ShouldNotContain("45678");
            graph.ToProjectJson().ShouldBe(original);
            graph.CanUndo.ShouldBeFalse();
            start = run.StartAsync(graph);
            rendered.ToHtmlString().ShouldNotContain("45678");
            runner.Release.SetResult(new(runner.Id, "Running", [new("Resources:api", "wiremock", "http://127.0.0.1:45679/", "second")]));
            await start;
            rendered.ToHtmlString().ShouldContain("http://127.0.0.1:45679/v1/");
            graph.AddEnvironment("Other");
            rendered.ToHtmlString().ShouldNotContain("45679");
            graph.Undo();
            rendered.ToHtmlString().ShouldContain("45679");
            graph.ToProjectJson().ShouldBe(original);
            await run.StopAsync();
        });
    }

    [Fact]
    public async Task Topology_service_endpoint_and_warning_follow_placement_undo_without_changing_connections()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Host":"localhost","Port":1883}},"Workflows":{"main":{}}}""");
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "remote", Name = "Remote", Kind = FluxFlow.Environments.EnvironmentNodeKind.Host },
            new() { Id = "broker", Name = "Broker", Kind = FluxFlow.Environments.EnvironmentNodeKind.Service, Resource = "Resources:broker", Host = "remote" }
        ] });
        graph.ResetHistory();
        var saved = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var rendered = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>();
            var diagram = components.Items.OfType<CascadingValue<Blazor.Diagrams.BlazorDiagram>>().Single().Value;
            var service = diagram!.Nodes.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single(node => node.Definition.Id == "broker");
            service.Endpoint!.Value.ShouldBe("mqtt://localhost:1883");
            diagram.SelectModel(service, true);
            rendered.ToHtmlString().ShouldContain("Effective endpoint");
            rendered.ToHtmlString().ShouldContain("It points to the execution computer");
            graph.ToProjectJson().ShouldBe(saved);
            graph.CanUndo.ShouldBeFalse();
            var inspector = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyInspector>().Last();
            await inspector.SetHost.InvokeAsync("");
            rendered.ToHtmlString().ShouldNotContain("It points to the execution computer");
            graph.Undo();
            rendered.ToHtmlString().ShouldContain("It points to the execution computer");
            graph.ToProjectJson().ShouldBe(saved);
            graph.Redo();
            rendered.ToHtmlString().ShouldNotContain("It points to the execution computer");
            graph.ExportConfiguration().ShouldBe(configuration);
            graph.AddEnvironment("Other");
            rendered.ToHtmlString().ShouldNotContain("mqtt://localhost:1883");
        });
    }

    [Fact]
    public async Task Topology_does_not_draw_saved_bindings_to_an_application_as_valid_connections()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://localhost:1883","ClientId":"test"}},"Workflows":{"main":{}}}""");
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "app", Name = "Application", Kind = FluxFlow.Environments.EnvironmentNodeKind.Application,
                Bindings = new() { ["Resources:broker"] = "other-app" } },
            new() { Id = "other-app", Name = "Other application", Kind = FluxFlow.Environments.EnvironmentNodeKind.Application }
        ] });
        graph.ResetHistory();
        var saved = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>();
            var diagram = components.Items.OfType<CascadingValue<Blazor.Diagrams.BlazorDiagram>>().Single().Value;
            diagram.Links.ShouldBeEmpty();
            graph.EnvironmentLimitation()!.ShouldContain("unbound");
            graph.ToProjectJson().ShouldBe(saved);
            graph.CanUndo.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Topology_color_reset_preserves_layout_and_host_assignment_fits_the_saved_block()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "host", Name = "Host", Kind = FluxFlow.Environments.EnvironmentNodeKind.Host, Width = 500, Height = 400 },
            new() { Id = "other", Name = "Other host", Kind = FluxFlow.Environments.EnvironmentNodeKind.Host, X = 600, Width = 500, Height = 400 },
            new() { Id = "app", Name = "Application", Kind = FluxFlow.Environments.EnvironmentNodeKind.Application, Host = "host", X = 306, Y = 200, Color = "#123456" }
        ] });
        graph.ResetHistory();
        var original = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>();
            var diagram = components.Items.OfType<CascadingValue<Blazor.Diagrams.BlazorDiagram>>().Single().Value;
            diagram.SelectModel(diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyNodeModel>().Single(node => node.Definition.Id == "app"), true);
            var inspector = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyInspector>().Last();
            var previousColorEdit = inspector.SetColor;
            await previousColorEdit.InvokeAsync(null);
            var reset = graph.GetEnvironmentDefinition().Topology.Nodes.Single(node => node.Id == "app");
            reset.Color.ShouldBeNull();
            reset.X.ShouldBe(306);
            reset.Y.ShouldBe(200);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(original);
            var revision = graph.Revision;
            await previousColorEdit.InvokeAsync(null);
            await inspector.SetColor.InvokeAsync("#123456");
            graph.Revision.ShouldBe(revision);
            graph.CanRedo.ShouldBeTrue();
            graph.ToProjectJson().ShouldBe(original);
            await inspector.SetHost.InvokeAsync("other");
            await inspector.SetHost.InvokeAsync("host");
            var contained = graph.ToProjectJson();
            var saved = graph.GetEnvironmentDefinition().Topology.Nodes.Single(node => node.Id == "app");
            saved.X.ShouldBe(32);
            saved.Y.ShouldBe(80);
            saved.Color.ShouldBe("#123456");
            saved.KeepInsideContainer.ShouldBeFalse();
            graph.Undo();
            graph.Undo();
            graph.ToProjectJson().ShouldBe(original);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(contained);
            graph.ExportConfiguration().ShouldBe(configuration);
        });
    }

    [Fact]
    public async Task Topology_palette_recognizes_saved_connection_casing_and_rejects_callbacks_after_disposal()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://localhost:1883"}},"Workflows":{"main":{}}}""");
        graph.SetTopology(new() { Nodes = [new() { Kind = FluxFlow.Environments.EnvironmentNodeKind.Service, Name = "Broker", Resource = "resources:BROKER" }] });
        graph.ResetHistory();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>();
            var palette = components.Items.OfType<ComponentPalette>().Single();
            var type = palette.Items.Single(item => item.DisplayName == "broker").ComponentType;
            palette.IsDisabled!(type).ShouldBeTrue();
            var before = graph.ToProjectJson();
            await palette.OnSelect.InvokeAsync(type);
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>().Single().Dispose();
            graph.AddEnvironment("Other");
            var other = graph.ToProjectJson();
            await palette.OnSelect.InvokeAsync("host");
            graph.ToProjectJson().ShouldBe(other);
            graph.GetEnvironmentDefinition().Topology.Nodes.ShouldBeEmpty();
        });
    }

    [Fact]
    public async Task Topology_docking_is_view_only_and_property_edits_preserve_history_across_environment_switches()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var originalEnvironment = graph.ActiveEnvironmentId;
        graph.AddEnvironment("Other");
        var otherEnvironment = graph.ActiveEnvironmentId;
        graph.SelectEnvironment(originalEnvironment);
        graph.ResetHistory();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>();
            var workspace = components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Workspace.DockWorkspace>().Single();
            var before = graph.ToProjectJson();
            var diagram = components.Items.OfType<CascadingValue<Blazor.Diagrams.BlazorDiagram>>().Single().Value;
            diagram.SetZoom(.65);
            view.ToHtmlString().ShouldContain($"{.65:P0}");
            workspace.Layout.Root.Percent = 25;
            workspace.Group("Inspector", "Canvas");
            workspace.TogglePin("Components");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            var palette = components.Items.OfType<ComponentPalette>().Single();
            await palette.OnSelect.InvokeAsync("host");
            var added = graph.ToProjectJson();
            var inspector = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyInspector>().Last();
            inspector.Selected!.Kind.ShouldBe(FluxFlow.Environments.EnvironmentNodeKind.Host);
            view.ToHtmlString().ShouldContain("studio-property-grid");
            await inspector.RenameNode.InvokeAsync("Office host");
            var renamed = graph.ToProjectJson();
            graph.GetEnvironmentDefinition().Topology.Nodes.Single().Name.ShouldBe("Office host");
            graph.Undo();
            graph.ToProjectJson().ShouldBe(added);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(renamed);
            graph.SelectEnvironment(otherEnvironment);
            components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyInspector>().Last().Selected.ShouldBeNull();
            graph.GetEnvironmentDefinition().Topology.Nodes.ShouldBeEmpty();
            graph.SelectEnvironment(originalEnvironment);
            graph.ToProjectJson().ShouldBe(renamed);
            workspace.Layout.IsAutoHidden("Components").ShouldBeTrue();
            workspace.Layout.Visit().Single(item => item.Node.Panels.Contains("Canvas")).Node.Panels.ShouldContain("Inspector");
            components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Workspace.DockWorkspace>().Count().ShouldBe(1);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Topology_ignores_property_callbacks_from_previous_selection_or_environment(bool switchEnvironment)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>();
            var environmentInspector = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyInspector>().Last();
            var palette = components.Items.OfType<ComponentPalette>().Single();
            await palette.OnSelect.InvokeAsync("host");
            await palette.OnSelect.InvokeAsync("application");
            var previousInspector = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyInspector>().Last();
            if (switchEnvironment) graph.AddEnvironment("Other");
            await palette.OnSelect.InvokeAsync("host");
            var before = graph.ToProjectJson();
            var revision = graph.Revision;
            await previousInspector.RenameNode.InvokeAsync("Late name");
            await previousInspector.SetHost.InvokeAsync("");
            await previousInspector.SetColor.InvokeAsync("#123456");
            await previousInspector.Bind.InvokeAsync(("Resources:broker", FluxFlow.Environments.TopologyBinding.Explicit("stale-service")));
            await environmentInspector.RenameEnvironment.InvokeAsync("Late environment name");
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Topology_cancels_pending_environment_check_when_setup_changes(bool switchEnvironment)
    {
        var services = CreateServices();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var run = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Topology.EnvironmentTopologyPage>();
            var check = run.CheckAsync(graph);
            run.Checking.ShouldBeTrue();
            if (switchEnvironment) graph.AddEnvironment("Other");
            else graph.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
            run.CheckRevision.ShouldBeNull();
            await check.WaitAsync(TimeSpan.FromSeconds(2));
            run.Checking.ShouldBeFalse();
            runner.CheckRelease.SetResult(new(true, "Late result"));
            run.CheckResult.ShouldBeNull();
        });
    }

    [Theory]
    [InlineData(false, "true")]
    [InlineData(true, "false")]
    public async Task Shared_palette_emits_explicit_html_drag_state(bool disabled, string expected)
    {
        await using var provider = CreateServices().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<ComponentPalette>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ComponentPalette.Items)] = graph.Components.Take(1).Select(item => item.Palette).ToArray(),
                [nameof(ComponentPalette.IsDisabled)] = (Func<string, bool>)(_ => disabled)
            }));
            view.ToHtmlString().ShouldContain($"draggable=\"{expected}\"");
            view.ToHtmlString().ShouldNotContain("draggable=\"\"");
        });
    }

    [Theory]
    [InlineData("jsonata", "{'Method':'POST','Body':{'reading':Reading}}", "{\n  'Method': 'POST',\n  'Body': {\n    'reading': Reading\n  }\n}")]
    [InlineData("jsonata", "{'unfinished':", "{'unfinished':")]
    [InlineData("jsonata", "file:sample.jsonata", "file:sample.jsonata")]
    [InlineData("jsonata", "base64:eyJBIjoxfQ==", "base64:eyJBIjoxfQ==")]
    [InlineData("csharp", "new { Reading = 1 }", "new { Reading = 1 }")]
    public async Task Inspector_formats_only_the_preview_and_preserves_stored_expression(string engine, string source, string expected)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Designer.Components.Expressions.ExpressionField>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = source, ["Engine"] = engine }));
            var preview = components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Expressions.ExpressionCodeEditor>().Single();
            preview.Value.ShouldBe(expected);
            preview.ReadOnly.ShouldBeTrue();
            components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Expressions.ExpressionField>().Single().Value.ShouldBe(source);
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
        });
    }

    [Fact]
    public async Task Play_checks_setup_then_runs_and_retries_on_canvas_with_logs_without_editing_project()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var run = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        graph.LoadJson("""{"KeepCase":42,"Workflows":{"main":{"map":{"Type":"mapper","Expression":"$"}}}}""");
        graph.IsProjectOpen = true;
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            var before = graph.ToProjectJson();
            var revision = graph.Revision;
            view.ToHtmlString().ShouldNotContain("<iframe");
            view.ToHtmlString().ShouldNotContain(">Observability</button>");
            StudioIconButton Play() => components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Run application locally");
            var pending = Play().OnClick.InvokeAsync();
            Play().Loading.ShouldBeTrue();
            runner.CheckRelease.SetResult(new(false, "Choose a valid container image."));
            await pending;
            Play().Loading.ShouldBeFalse();
            runner.Id.ShouldBe(Guid.Empty);
            view.ToHtmlString().ShouldContain("Choose a valid container image.");
            view.ToHtmlString().ShouldContain("studio-designer-page\" hidden");

            var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            run.Changed += () => { if (run.Busy && run.Status?.State == "Preparing") preparing.TrySetResult(); };
            pending = Play().OnClick.InvokeAsync();
            runner.CheckRelease.SetResult(new(true, "Ready"));
            await preparing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var html = view.ToHtmlString();
            html.ShouldNotContain("studio-designer-page\" hidden");
            html.ShouldMatch("aria-selected=\"true\"[^>]*>Logs");
            Play().Loading.ShouldBeTrue();
            html.ShouldContain("aria-label=\"Loading\"");
            html.ShouldNotContain("Run status");
            html.ShouldNotContain("No execution logs");
            html.ShouldContain(">Category</span>");
            var cancelledStart = pending;
            var cancelledId = runner.Id;
            var lateResponse = runner.Release;
            await components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Stop and clean up").OnClick.InvokeAsync();
            runner.Token.IsCancellationRequested.ShouldBeTrue();
            Play().Loading.ShouldBeFalse();
            Play().Disabled.ShouldBeFalse();
            preparing = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = Play().OnClick.InvokeAsync();
            runner.CheckRelease.SetResult(new(true, "Ready"));
            await preparing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var currentId = runner.Id;
            currentId.ShouldNotBe(cancelledId);
            lateResponse.SetResult(new(cancelledId, "Running", [], Logs: new([
                new(99, DateTimeOffset.UtcNow, "Error", "Old.Run", 0, null, "Late cancelled record", null)], 0)));
            await cancelledStart;
            run.Status!.Id.ShouldBe(currentId);
            run.Logs.Records.ShouldBeEmpty();
            Play().Loading.ShouldBeTrue();
            runner.Release.SetResult(new(runner.Id, "Failed", [], "Original startup failure"));
            await pending;
            await run.StopAsync();
            Play().Loading.ShouldBeFalse();
            run.Failure.ShouldBe("Original startup failure");
            view.ToHtmlString().ShouldNotContain("Cleanup completed");

            preparing = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = Play().OnClick.InvokeAsync();
            runner.CheckRelease.SetResult(new(true, "Ready"));
            await preparing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            runner.Release.SetResult(new(runner.Id, "Running", []));
            await pending;
            Play().Loading.ShouldBeFalse();
            await run.StopAsync();
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
            graph.Undo();
            graph.GetEnvironment().StartupTimeoutSeconds.ShouldBe(60);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(before);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Play_does_not_start_after_cancel_or_edit_during_check(bool edit)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.IsProjectOpen = true;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            var before = graph.ToProjectJson();
            var pending = components.Items.OfType<StudioIconButton>().Single(item => item.Label == "Run application locally").OnClick.InvokeAsync();
            if (edit) graph.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
            else await components.Items.OfType<StudioIconButton>().Single(item => item.Label == "Stop and clean up").OnClick.InvokeAsync();
            runner.Token.IsCancellationRequested.ShouldBeTrue();
            runner.CheckRelease.SetResult(new(true, "Late result"));
            await pending;
            runner.Id.ShouldBe(Guid.Empty);
            view.ToHtmlString().ShouldNotContain("studio-designer-page\" hidden");
            if (edit) graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
        });
    }

    [Fact]
    public async Task Execution_traces_show_hierarchy_timing_and_complete_encoded_details()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var clipboard = new RecordingClipboard();
        services.AddSingleton<IJSRuntime>(clipboard);
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var root = TraceSpan("trace-a", "root", null, 0, 20) with
        {
            Status = "Error", StatusDescription = "failure <script>bad()</script>",
            Events = [new("exception", DateTimeOffset.UnixEpoch, new Dictionary<string, JsonElement> { ["exception.stacktrace"] = JsonSerializer.SerializeToElement("outer\ninner\nfull stack") })],
            Links = [new("remote-trace", "remote-span", "vendor=one", 1, true,
                new Dictionary<string, JsonElement> { ["link.detail"] = JsonSerializer.SerializeToElement("<linked>\ncontext") })],
            TraceState = "vendor=two"
        };
        var child = TraceSpan("trace-a", "child", "root", 5, 5);
        var orphan = TraceSpan("trace-a", "orphan", "remote-parent", 15, 5);
        var snapshot = new FluxFlow.Environments.ExecutionTraceSnapshot([child, orphan, root], 12);
        var original = JsonSerializer.Serialize(snapshot);
        var selections = new List<FluxFlow.Environments.ExecutionTrace>();
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionTraces>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Value"] = snapshot,
                ["OnSelectSpan"] = EventCallback.Factory.Create<FluxFlow.Environments.ExecutionTrace>(selections, selections.Add)
            }));
            var html = view.ToHtmlString();
            html.IndexOf("data-span-id=\"root\"", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("data-span-id=\"child\"", StringComparison.Ordinal));
            html.ShouldContain("data-span-id=\"child\" data-depth=\"1\"");
            html.ShouldContain("left:25%;width:25%");
            html.ShouldContain("12 older spans");
            html.ShouldContain("Parent not captured");
            html.ShouldContain("Collapse main.root");
            html.ShouldContain("aria-expanded=\"true\"");
            System.Net.WebUtility.HtmlDecode(html).ShouldContain("Start +5 ms; span duration 5 ms");
            html.ShouldNotContain("aria-label=\"Span details\"");
            await components.Items.OfType<ExecutionTraces>().Single().SelectSpan("root");
            html = view.ToHtmlString();
            html.ShouldContain("trace-selected");
            html.ShouldContain("&lt;script&gt;bad()&lt;/script&gt;");
            html.ShouldNotContain("<script>");
            System.Net.WebUtility.HtmlDecode(html).ShouldContain("outer\ninner\nfull stack");
            html.ShouldContain("remote-trace");
            html.ShouldContain("vendor=one");
            html.ShouldContain("vendor=two");
            html.ShouldContain("link.detail");
            System.Net.WebUtility.HtmlDecode(html).ShouldContain("<linked>\ncontext");
            html.ShouldContain("Ended");
            html.ShouldContain(root.StartTime.Add(root.Duration).ToString("O").Replace("+", "&#x2B;"));
            html.ShouldContain("00:00:00.0200000");
            html.ShouldContain("Source version");
            html.ShouldContain("Resource (1)");
            html.ShouldContain("20 ms");
            selections.Single().ShouldBeSameAs(root);
            await components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Copy details").OnClick.InvokeAsync();
            clipboard.Texts.Single().ShouldContain("\n");
            JsonSerializer.Serialize(JsonSerializer.Deserialize<FluxFlow.Environments.ExecutionTrace>(clipboard.Texts.Single())).ShouldBe(JsonSerializer.Serialize(root));
            await components.Items.OfType<ExecutionTraces>().Single().SelectSpan(null);
            selections.Count.ShouldBe(1);
        });
        JsonSerializer.Serialize(snapshot).ShouldBe(original);
    }

    [Fact]
    public async Task Deep_trace_rows_keep_real_depth_and_parent_navigation_with_compact_indentation()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var spans = Enumerable.Range(0, 31).Select(i => TraceSpan("deep", $"step{i:00}",
            i == 0 ? null : $"step{i - 1:00}", i, 1)).ToArray();
        var snapshot = new FluxFlow.Environments.ExecutionTraceSnapshot(spans, 0);
        var original = JsonSerializer.Serialize(snapshot);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionTraces>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Value"] = snapshot }));
            var traces = components.Items.OfType<ExecutionTraces>().Single();
            var html = view.ToHtmlString();
            html.ShouldContain("data-span-id=\"step30\" data-depth=\"30\"");
            html.ShouldContain("aria-label=\"Depth 30\"");
            await traces.SelectSpan("step30");
            await components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Go to parent span").OnClick.InvokeAsync();
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"step29\" data-depth=\"29\"");
            await traces.ToggleSpan("step04");
            view.ToHtmlString().ShouldNotContain("data-span-id=\"step30\"");
            await traces.ToggleSpan("step04");
            view.ToHtmlString().ShouldContain("data-span-id=\"step30\" data-depth=\"30\"");
        });
        JsonSerializer.Serialize(snapshot).ShouldBe(original);
    }

    [Fact]
    public async Task Execution_span_details_retain_after_stop_and_reject_stale_selection()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var clipboard = new RecordingClipboard();
        services.AddSingleton<IJSRuntime>(clipboard);
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 123 });
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        var session = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        var span = TraceSpan("trace", "span", null, 0, 0) with { Duration = TimeSpan.FromTicks(1) };
        var original = JsonSerializer.Serialize(span);
        var start = session.StartAsync(graph);
        runner.Release.SetResult(new(runner.Id, "Running", [], Traces: new([span], 0)));
        await start;
        var run = runner.Id;
        var selections = new List<FluxFlow.Environments.ExecutionTrace>();
        ParameterView Parameters(Guid id, params FluxFlow.Environments.ExecutionTrace[] spans) => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["RunId"] = id, ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot(spans, 0),
            ["OnSelectSpan"] = EventCallback.Factory.Create<FluxFlow.Environments.ExecutionTrace>(selections, selections.Add)
        });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionTraces>(Parameters(run, span));
            var traces = components.Items.OfType<ExecutionTraces>().Single();
            await traces.SelectSpan("span", run, "trace");
            view.ToHtmlString().ShouldContain("0.0001 ms (00:00:00.0000001)");
            await session.StopAsync();
            await traces.SetParametersAsync(Parameters(session.Status!.Id, session.Traces.Records.ToArray()));
            view.ToHtmlString().ShouldContain("aria-label=\"Span details\"");
            var copy = components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Copy details").OnClick;
            await copy.InvokeAsync();
            JsonSerializer.Serialize(JsonSerializer.Deserialize<FluxFlow.Environments.ExecutionTrace>(clipboard.Texts.Single())).ShouldBe(original);
            await traces.SetParametersAsync(Parameters(run));
            await copy.InvokeAsync();
            clipboard.Texts.Count.ShouldBe(1);
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await traces.SelectSpan("span", run, "trace");
            selections.Count.ShouldBe(1);
            await traces.SetParametersAsync(Parameters(run, span));
            await traces.SelectSpan("span", run, "trace");
            copy = components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Copy details").OnClick;
            var next = Guid.NewGuid();
            await traces.SetParametersAsync(Parameters(next, span));
            await copy.InvokeAsync();
            clipboard.Texts.Count.ShouldBe(1);
            await traces.SelectSpan("span", run, "trace");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await traces.SelectSpan("span", next, "trace");
            await traces.SetParametersAsync(Parameters(next, span with { TraceId = "other" }));
            await traces.SelectSpan("span", next, "trace");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            selections.Count.ShouldBe(3);
            await traces.SelectSpan("span", next, "other");
            view.ToHtmlString().ShouldContain("aria-label=\"Span details\"");
            await components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Close span details").OnClick.InvokeAsync();
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
        });
        JsonSerializer.Serialize(span).ShouldBe(original);
        graph.Revision.ShouldBe(revision);
        graph.ToProjectJson().ShouldBe(project);
        graph.Undo();
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldNotBe(123);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(project);
    }

    [Fact]
    public async Task Execution_trace_filters_match_whole_traces_without_pruning_or_mutating_spans()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var root = TraceSpan("failed", "root", null, 0, 20) with { Name = "Receive" };
        var child = TraceSpan("failed", "child", "root", 5, 5) with { Name = "HTTP POST", Status = "Error" };
        var sibling = TraceSpan("failed", "sibling", "root", 10, 3) with { Name = "Transform" };
        var healthy = TraceSpan("healthy", "healthy", null, 30, 5) with { Name = "HTTP POST", Status = "Ok" };
        var eventOnly = TraceSpan("event", "event", null, 40, 5) with
        {
            Name = "Handled failure", Events = [new("exception", DateTimeOffset.UnixEpoch, new Dictionary<string, JsonElement>())]
        };
        var snapshot = new FluxFlow.Environments.ExecutionTraceSnapshot([root, child, sibling, healthy, eventOnly], 0);
        var original = JsonSerializer.Serialize(snapshot);
        var run = Guid.NewGuid();
        ParameterView Parameters(string search = "", bool errors = false, FluxFlow.Environments.ExecutionTrace? requested = null,
            FluxFlow.Environments.ExecutionTraceSnapshot? value = null) =>
            ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Value"] = value ?? snapshot, ["RunId"] = run, ["SearchText"] = search, ["ErrorsOnly"] = errors, ["RequestedSpan"] = requested });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionTraces>(Parameters());
            var traces = components.Items.OfType<ExecutionTraces>().Single();
            view.ToHtmlString().ShouldContain("3 / 3 traces");
            await traces.SetParametersAsync(Parameters("  http post  "));
            view.ToHtmlString().ShouldContain("2 / 3 traces");
            await traces.SetParametersAsync(Parameters("http", true));
            var html = view.ToHtmlString();
            html.ShouldContain("1 / 3 traces");
            html.ShouldContain("data-span-id=\"root\" data-depth=\"0\"");
            html.ShouldContain("data-span-id=\"child\" data-depth=\"1\"");
            html.ShouldContain("data-span-id=\"sibling\" data-depth=\"1\"");
            html.ShouldContain("left:25%;width:25%");
            await traces.SelectSpan("sibling");
            // Search and error matches may belong to different spans in the same trace.
            await traces.SetParametersAsync(Parameters("MAIN.ROOT", true));
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"sibling\"");
            await traces.SetParametersAsync(Parameters("no match", true, child));
            view.ToHtmlString().ShouldContain("0 / 3 traces");
            view.ToHtmlString().ShouldContain("No retained traces match the current filters.");
            view.ToHtmlString().ShouldContain("Search traces");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await traces.SelectSpan("child", run, "failed");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await traces.SetParametersAsync(Parameters(errors: true));
            view.ToHtmlString().ShouldContain("1 / 3 traces");
            view.ToHtmlString().ShouldNotContain("data-span-id=\"event\"");
            await traces.SetParametersAsync(Parameters());
            view.ToHtmlString().ShouldContain("3 / 3 traces");

            // Completed child spans may arrive before their parents during a run.
            await traces.SetParametersAsync(Parameters("http", true, value: new([], 0)));
            view.ToHtmlString().ShouldContain("No completed spans yet");
            await traces.SetParametersAsync(Parameters("http", true, value: new([child], 0)));
            view.ToHtmlString().ShouldContain("1 / 1 traces");
            view.ToHtmlString().ShouldContain("Parent not captured");
            await traces.SelectSpan("child");
            await traces.SetParametersAsync(Parameters("http", true));
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"child\" data-depth=\"1\"");
            view.ToHtmlString().ShouldContain("data-span-id=\"sibling\"");
            view.ToHtmlString().ShouldNotContain("Parent not captured");

            var secondError = healthy with { Status = "Error" };
            await traces.SetParametersAsync(Parameters("http", true, value: new([root, child, sibling, secondError, eventOnly], 0)));
            view.ToHtmlString().ShouldContain("2 / 3 traces");
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"child\"");
            // Eviction removes a match, not the other retained records in that trace.
            await traces.SetParametersAsync(Parameters("http", true, value: new([root, sibling, secondError, eventOnly], 1)));
            view.ToHtmlString().ShouldContain("1 / 3 traces");
            view.ToHtmlString().ShouldContain("data-span-id=\"healthy\"");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await traces.SelectSpan("child", run, "failed");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await traces.SetParametersAsync(Parameters("MAIN.ROOT", value: new([root, sibling, secondError, eventOnly], 1)));
            view.ToHtmlString().ShouldContain("data-span-id=\"root\" data-depth=\"0\"");
            view.ToHtmlString().ShouldContain("data-span-id=\"sibling\" data-depth=\"1\"");
        });
        JsonSerializer.Serialize(snapshot).ShouldBe(original);
    }

    [Fact]
    public async Task Execution_trace_filters_survive_stop_and_clear_for_log_navigation_without_editing()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 123 });
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        var session = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        var span = TraceSpan("trace", "span", null, 0, 20) with { Status = "Error" };
        var start = session.StartAsync(graph);
        runner.Release.SetResult(new(runner.Id, "Running", [], Traces: new([span], 0)));
        await start;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ObservationPanel>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["LogsRequest"] = 1 }));
            await components.Items.OfType<ExecutionLogs>().Last().OnViewTrace.InvokeAsync(span);
            var traces = components.Items.OfType<ExecutionTraces>().Last();
            await components.Items.OfType<MudBlazor.MudTextField<string>>().Last(field => field.Label == "Search traces").ValueChanged.InvokeAsync("MAIN.SPAN");
            await components.Items.OfType<MudBlazor.MudSwitch<bool>>().Last(field => field.Label == "Errors only").ValueChanged.InvokeAsync(true);
            await session.StopAsync();
            traces.SearchText.ShouldBe("MAIN.SPAN");
            traces.ErrorsOnly.ShouldBeTrue();
            view.ToHtmlString().ShouldContain("1 / 1 traces");
            view.ToHtmlString().ShouldContain("aria-label=\"Span details\"");
            await traces.SearchTextChanged.InvokeAsync("no match");
            view.ToHtmlString().ShouldContain("No retained traces match the current filters.");
            await traces.OnClearFilters.InvokeAsync();
            traces.SearchText.ShouldBeEmpty();
            traces.ErrorsOnly.ShouldBeFalse();
            view.ToHtmlString().ShouldContain("1 / 1 traces");
            await traces.SearchTextChanged.InvokeAsync("no match");
            await traces.ErrorsOnlyChanged.InvokeAsync(true);
            await traces.OnShowRelatedLogs.InvokeAsync(span);
            await components.Items.OfType<ExecutionLogs>().Last().OnViewTrace.InvokeAsync(span);
            traces = components.Items.OfType<ExecutionTraces>().Last();
            traces.SearchText.ShouldBeEmpty();
            traces.ErrorsOnly.ShouldBeFalse();
            view.ToHtmlString().ShouldContain("aria-label=\"Span details\"");
            await traces.ErrorsOnlyChanged.InvokeAsync(true);
            var rerun = session.StartAsync(graph);
            var fresh = span with { TraceId = "new-trace", Status = "Ok" };
            runner.Release.SetResult(new(runner.Id, "Running", [], Traces: new([fresh], 0)));
            await rerun;
            view.ToHtmlString().ShouldContain("0 / 1 traces");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await traces.OnClearFilters.InvokeAsync();
            view.ToHtmlString().ShouldContain("1 / 1 traces");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await session.StopAsync();
            session.Traces.Records.Single().ShouldBeSameAs(fresh);
        });
        graph.Revision.ShouldBe(revision);
        graph.ToProjectJson().ShouldBe(project);
        graph.Undo();
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldNotBe(123);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(project);
    }

    [Fact]
    public async Task Execution_trace_branches_preserve_timing_and_collapse_during_updates()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var run = Guid.NewGuid();
        var spans = new[] { TraceSpan("trace", "root", null, 0, 20), TraceSpan("trace", "child", "root", 5, 5), TraceSpan("trace", "other", null, 15, 5) };
        ParameterView Parameters(Guid id) => ParameterView.FromDictionary(new Dictionary<string, object?>
        { ["RunId"] = id, ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot(spans, 0) });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionTraces>(Parameters(run));
            var component = components.Items.OfType<ExecutionTraces>().Single();
            var oldPicker = components.Items.OfType<MudBlazor.MudSelect<string>>().Single().ValueChanged;
            await component.SelectSpan("child");
            await component.ToggleSpan("root");
            var html = view.ToHtmlString();
            html.ShouldNotContain("data-span-id=\"child\"");
            html.ShouldContain("Expand main.root");
            html.ShouldContain("aria-expanded=\"false\"");
            html.ShouldContain("trace-selected\" data-span-id=\"root\"");
            html.ShouldContain("left:75%;width:25%");
            // A queued click on a child hidden by collapse must not reopen its details.
            await component.SelectSpan("child");
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"root\"");
            await component.SetParametersAsync(Parameters(run));
            view.ToHtmlString().ShouldNotContain("data-span-id=\"child\"");
            await component.ToggleSpan("root");
            view.ToHtmlString().ShouldContain("data-span-id=\"child\" data-depth=\"1\"");
            await component.SelectSpan(null);
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await component.ToggleSpan("root");
            await component.SetParametersAsync(Parameters(Guid.NewGuid()));
            view.ToHtmlString().ShouldContain("data-span-id=\"child\"");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await component.SelectSpan("child");
            await oldPicker.InvokeAsync("trace");
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"child\"");
            await component.ToggleSpan("root", run, "trace");
            await component.ToggleSpan("root", component.RunId, "previous-trace");
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"child\"");
            view.ToHtmlString().ShouldContain("data-span-id=\"child\"");
        });
    }

    [Fact]
    public async Task Collapsed_trace_branches_show_elapsed_range_without_changing_original_span_timing()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var root = TraceSpan("trace", "root", null, 0, 2);
        var prepare = TraceSpan("trace", "prepare", "root", 5, 3);
        var record = TraceSpan("trace", "record", "prepare", 12, 4);
        var overlap = TraceSpan("trace", "overlap", "prepare", 6, 8);
        var unrelated = TraceSpan("trace", "other", null, 40, 10);
        var spans = new[] { root, prepare, record, overlap, unrelated };
        var original = JsonSerializer.Serialize(spans);
        var run = Guid.NewGuid();
        ParameterView Parameters(long dropped = 0) => ParameterView.FromDictionary(new Dictionary<string, object?>
        { ["RunId"] = run, ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot(spans, dropped) });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionTraces>(Parameters());
            var component = components.Items.OfType<ExecutionTraces>().Single();
            await component.SelectSpan("prepare");
            await component.ToggleSpan("prepare");
            var html = view.ToHtmlString();
            html.ShouldNotContain("data-span-id=\"record\"");
            html.ShouldNotContain("data-span-id=\"overlap\"");
            html.ShouldContain("branch elapsed 11 ms across 3 retained spans");
            html.ShouldContain("span duration 3 ms");
            html.ShouldContain("trace-branch-range\" style=\"left:10%;width:22%");
            html.ShouldContain("trace-bar \" style=\"left:10%;width:6%");
            html.ShouldContain("5 spans");
            html.ShouldContain("50 ms");
            components.Items.OfType<ExecutionSpanDetails>().Last().Span.ShouldBe(prepare);
            JsonSerializer.Serialize(spans).ShouldBe(original);

            // Retained snapshots (including after Stop) keep the collapsed range.
            await component.SetParametersAsync(Parameters());
            view.ToHtmlString().ShouldContain("branch elapsed 11 ms");
            spans = [root, prepare, record with { Duration = TimeSpan.FromMilliseconds(15) }, overlap, unrelated];
            await component.SetParametersAsync(Parameters());
            view.ToHtmlString().ShouldContain("branch elapsed 22 ms across 3 retained spans");
            await component.ToggleSpan("root");
            view.ToHtmlString().ShouldContain("branch elapsed 27 ms across 4 retained spans");
            await component.ToggleSpan("root");
            await component.ToggleSpan("prepare");
            view.ToHtmlString().ShouldNotContain("class=\"trace-branch-range\"");
            view.ToHtmlString().ShouldContain("data-span-id=\"record\"");

            // Only retained descendants contribute; unrelated roots never do.
            spans = [root, prepare, overlap, unrelated];
            await component.SetParametersAsync(Parameters(1));
            await component.ToggleSpan("prepare");
            view.ToHtmlString().ShouldContain("branch elapsed 9 ms across 2 retained spans");
            run = Guid.NewGuid();
            await component.SetParametersAsync(Parameters());
            view.ToHtmlString().ShouldNotContain("class=\"trace-branch-range\"");
        });
    }

    [Fact]
    public async Task Execution_trace_link_navigation_handles_filters_missing_targets_stop_and_stale_actions()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 123 });
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        var session = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        var link = new FluxFlow.Environments.ExecutionTraceLink("target", "leaf", null, 1, true, new Dictionary<string, JsonElement>());
        var missing = link with { TraceId = "missing" };
        var back = link with { TraceId = "source", SpanId = "source" };
        var source = TraceSpan("source", "source", null, 30, 5) with { Name = "Source operation", Status = "Error", Links = [link, missing] };
        var root = TraceSpan("target", "root", null, 0, 20) with { Links = [link] };
        var child = TraceSpan("target", "child", "root", 2, 10);
        var leaf = TraceSpan("target", "leaf", "child", 4, 2) with { Links = [back] };
        var wrongTrace = TraceSpan("unrelated", "leaf", null, -20, 1);
        var records = new[] { wrongTrace, root, child, leaf, source };
        var original = JsonSerializer.Serialize(records);
        var start = session.StartAsync(graph);
        runner.Release.SetResult(new(runner.Id, "Running", [], Traces: new(records, 0)));
        await start;
        var selections = new List<FluxFlow.Environments.ExecutionTrace>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ObservationPanel>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["LogsRequest"] = 1,
                ["OnSelectSpan"] = EventCallback.Factory.Create<FluxFlow.Environments.ExecutionTrace>(selections, selections.Add)
            }));
            await components.Items.OfType<ExecutionLogs>().Last().OnViewTrace.InvokeAsync(source);
            var traces = components.Items.OfType<ExecutionTraces>().Last();
            ExecutionSpanDetails Details() => components.Items.OfType<ExecutionSpanDetails>().Last();
            Details().CanNavigateLink!(link).ShouldBeTrue();
            Details().CanNavigateLink!(missing).ShouldBeFalse();
            view.ToHtmlString().ShouldContain("Linked span not captured");
            await traces.SearchTextChanged.InvokeAsync("Source operation");
            await traces.ErrorsOnlyChanged.InvokeAsync(true);
            var navigate = Details().OnNavigateLink;
            await navigate.InvokeAsync(missing);
            traces.ErrorsOnly.ShouldBeTrue();
            Details().Span.ShouldBeSameAs(source);
            await session.StopAsync();
            await navigate.InvokeAsync(link);
            traces.SearchText.ShouldBeEmpty();
            traces.ErrorsOnly.ShouldBeFalse();
            Details().Span.ShouldBeSameAs(leaf);
            selections.Last().ShouldBeSameAs(leaf);
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"leaf\" data-depth=\"2\"");
            var count = selections.Count;
            await navigate.InvokeAsync(link); // The originating span is no longer selected.
            selections.Count.ShouldBe(count);
            await Details().OnNavigateLink.InvokeAsync(back);
            Details().Span.ShouldBeSameAs(source);
            await Details().OnNavigateLink.InvokeAsync(link); // Repeated cross-trace navigation.
            await traces.SelectSpan("root");
            await traces.ToggleSpan("child");
            await Details().OnNavigateLink.InvokeAsync(link); // Reveal a linked descendant inside a collapsed branch.
            Details().Span.ShouldBeSameAs(leaf);
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"leaf\" data-depth=\"2\"");
            await Details().OnNavigateLink.InvokeAsync(back);
            navigate = Details().OnNavigateLink;
            await traces.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot([source, root, child, wrongTrace], 1) }));
            Details().CanNavigateLink!(link).ShouldBeFalse();
            count = selections.Count;
            await navigate.InvokeAsync(link); // Evicted target must not resolve to the same Span ID in another trace.
            selections.Count.ShouldBe(count);
            var rerun = session.StartAsync(graph);
            runner.Release.SetResult(new(runner.Id, "Running", [], Traces: new(records, 0)));
            await rerun;
            await traces.SelectSpan("source");
            count = selections.Count;
            await navigate.InvokeAsync(link); // Same IDs in a new run do not revive an old action.
            selections.Count.ShouldBe(count);
            navigate = Details().OnNavigateLink;
            traces.Dispose();
            await navigate.InvokeAsync(link);
            selections.Count.ShouldBe(count);
            await session.StopAsync();
        });
        JsonSerializer.Serialize(records).ShouldBe(original);
        graph.Revision.ShouldBe(revision);
        graph.ToProjectJson().ShouldBe(project);
        graph.Undo();
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldNotBe(123);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(project);
    }

    [Fact]
    public async Task Execution_trace_link_navigation_does_not_depend_on_parent_render_timing()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var scrolling = new TraceScrolling();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IJSRuntime>(scrolling);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var link = new FluxFlow.Environments.ExecutionTraceLink("target", "child", null, 1, false, new Dictionary<string, JsonElement>());
        var source = TraceSpan("source", "source", null, 30, 5) with
        {
            Name = "Source", Links = [link], Status = "Error",
            Attributes = new Dictionary<string, JsonElement> { ["flow.application.instance.id"] = JsonSerializer.SerializeToElement("first") }
        };
        var root = TraceSpan("target", "root", null, 0, 20);
        var target = TraceSpan("target", "child", "root", 2, 5);
        var run = Guid.NewGuid();
        var selections = new List<FluxFlow.Environments.ExecutionTrace>();
        var cleared = false;
        ParameterView Parameters(Guid id, string search, bool errors) => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["RunId"] = id, ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot([root, target, source], 0),
            ["SearchText"] = search, ["ErrorsOnly"] = errors,
            ["Application"] = search.Length > 0 ? new ApplicationTelemetryTarget(id, "first", "Application") : null,
            ["OnSelectSpan"] = EventCallback.Factory.Create<FluxFlow.Environments.ExecutionTrace>(selections, selections.Add),
            ["OnClearFilters"] = EventCallback.Factory.Create(this, () => { cleared = true; })
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<RenderedExecutionTraces>(Parameters(run, "Source", true));
            var traces = components.Items.OfType<RenderedExecutionTraces>().Single();
            await traces.SelectSpan("source");
            await components.Items.OfType<ExecutionSpanDetails>().Last().OnNavigateLink.InvokeAsync(link);
            cleared.ShouldBeTrue();
            selections.Last().ShouldBeSameAs(target);
            // Apply the parent's cleared parameters after the click handler, as with a batched render.
            await traces.SetParametersAsync(Parameters(run, "", false));
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"child\" data-depth=\"1\"");
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);

            await traces.SetParametersAsync(Parameters(run, "Source", true));
            await traces.SelectSpan("source");
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await traces.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["OnClearFilters"] = EventCallback.Factory.Create(this, () => release.Task) }));
            var navigate = components.Items.OfType<ExecutionSpanDetails>().Last().OnNavigateLink.InvokeAsync(link);
            navigate.IsCompleted.ShouldBeFalse();
            await traces.SetParametersAsync(Parameters(Guid.NewGuid(), "Source", true));
            await traces.SelectSpan("source");
            var count = selections.Count;
            release.SetResult();
            await navigate;
            selections.Count.ShouldBe(count);
            view.ToHtmlString().ShouldContain("1 / 2 traces");
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"source\"");
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);
        });
    }

    [Fact]
    public async Task Execution_trace_back_navigation_reveals_retained_spans_and_resets_on_rerun()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var scrolling = new TraceScrolling();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IJSRuntime>(scrolling);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 123 });
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        var session = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        var link = new FluxFlow.Environments.ExecutionTraceLink("other", "root", null, 1, false, new Dictionary<string, JsonElement>());
        var root = TraceSpan("trace", "root", null, 0, 20) with { Links = [link] };
        var child = TraceSpan("trace", "child", "root", 2, 10);
        var leaf = TraceSpan("trace", "leaf", "child", 4, 2);
        var other = TraceSpan("other", "root", null, -10, 5) with { Name = "Other operation", Status = "Error" };
        var records = new[] { other, root, child, leaf };
        var original = JsonSerializer.Serialize(records);
        var start = session.StartAsync(graph);
        runner.Release.SetResult(new(runner.Id, "Running", [], Traces: new(records, 0)));
        await start;
        var selections = new List<FluxFlow.Environments.ExecutionTrace>();
        var cleared = false;
        ParameterView Parameters(Guid run, params FluxFlow.Environments.ExecutionTrace[] spans) => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["RunId"] = run, ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot(spans, 0),
            ["SearchText"] = "", ["ErrorsOnly"] = false,
            ["OnSelectSpan"] = EventCallback.Factory.Create<FluxFlow.Environments.ExecutionTrace>(selections, selections.Add),
            ["OnClearFilters"] = EventCallback.Factory.Create(this, () => { cleared = true; })
        });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<RenderedExecutionTraces>(Parameters(session.Status!.Id, records));
            var traces = components.Items.OfType<RenderedExecutionTraces>().Single();
            StudioIconButton Button(string label) => components.Items.OfType<StudioIconButton>().Last(button => button.Label == label);
            Button("Back to previous span").Disabled.ShouldBeTrue();
            await traces.SelectSpan("leaf");
            await Button("Go to parent span").OnClick.InvokeAsync();
            await traces.ToggleSpan("child");
            await Button("Back to previous span").OnClick.InvokeAsync();
            selections.Last().ShouldBeSameAs(leaf);
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"leaf\" data-depth=\"2\"");
            Button("Back to previous span").Disabled.ShouldBeTrue();
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);
            await Button("Go to parent span").OnClick.InvokeAsync();
            await Button("Go to parent span").OnClick.InvokeAsync();
            await components.Items.OfType<ExecutionSpanDetails>().Last().OnNavigateLink.InvokeAsync(link);
            selections.Last().ShouldBeSameAs(other);
            await session.StopAsync();
            // Keep history across Stop, but skip the evicted child and never confuse equal Span IDs across traces.
            await traces.SetParametersAsync(Parameters(session.Status!.Id, other, root, leaf));
            await traces.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["SearchText"] = "Other operation", ["ErrorsOnly"] = true }));
            await Button("Back to previous span").OnClick.InvokeAsync();
            cleared.ShouldBeTrue();
            selections.Last().ShouldBeSameAs(root);
            await traces.SetParametersAsync(Parameters(session.Status!.Id, other, root, leaf));
            var selectionCount = selections.Count;
            await Button("Reveal selected span").OnClick.InvokeAsync();
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(2);
            selections.Count.ShouldBe(selectionCount);
            Button("Back to previous span").Disabled.ShouldBeFalse();
            Button("Forward to next span").Disabled.ShouldBeFalse();
            cleared = false;
            await traces.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["SearchText"] = "flow.process" }));
            await Button("Forward to next span").OnClick.InvokeAsync();
            cleared.ShouldBeTrue();
            selections.Last().ShouldBeSameAs(other);
            await traces.SetParametersAsync(Parameters(session.Status!.Id, other, root, leaf));
            await Button("Back to previous span").OnClick.InvokeAsync();
            selections.Last().ShouldBeSameAs(root);
            await Button("Back to previous span").OnClick.InvokeAsync();
            selections.Last().ShouldBeSameAs(leaf);
            Button("Back to previous span").Disabled.ShouldBeTrue();
            await traces.SetParametersAsync(Parameters(session.Status!.Id, records));
            await traces.SelectSpan("root");
            var stale = Button("Back to previous span").OnClick;
            var rerun = session.StartAsync(graph);
            runner.Release.SetResult(new(runner.Id, "Running", [], Traces: new(records, 0)));
            await rerun;
            await traces.SetParametersAsync(Parameters(session.Status!.Id, records));
            Button("Back to previous span").Disabled.ShouldBeTrue();
            await traces.SelectSpan("leaf");
            await traces.SelectSpan("root");
            var count = selections.Count;
            await stale.InvokeAsync();
            selections.Count.ShouldBe(count);
            stale = Button("Back to previous span").OnClick;
            await traces.SetParametersAsync(Parameters(session.Status!.Id, root));
            Button("Back to previous span").Disabled.ShouldBeTrue();
            await stale.InvokeAsync();
            selections.Count.ShouldBe(count);
            traces.Dispose();
            await stale.InvokeAsync();
            selections.Count.ShouldBe(count);
            await session.StopAsync();
        });
        JsonSerializer.Serialize(records).ShouldBe(original);
        graph.Revision.ShouldBe(revision);
        graph.ToProjectJson().ShouldBe(project);
        graph.Undo();
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldNotBe(123);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(project);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Execution_trace_history_handles_empty_filters_and_pending_selection_changes(bool rerun, bool forward)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var root = TraceSpan("trace", "root", null, 0, 20);
        var child = TraceSpan("trace", "child", "root", 2, 5);
        var run = Guid.NewGuid();
        var selections = new List<FluxFlow.Environments.ExecutionTrace>();
        ParameterView Parameters(Guid id, string search = "") => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["RunId"] = id, ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot([root, child], 0),
            ["SearchText"] = search,
            ["OnSelectSpan"] = EventCallback.Factory.Create<FluxFlow.Environments.ExecutionTrace>(selections, selections.Add)
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionTraces>(Parameters(run));
            var traces = components.Items.OfType<ExecutionTraces>().Single();
            StudioIconButton Back() => components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Back to previous span");
            StudioIconButton Navigate() => components.Items.OfType<StudioIconButton>().Last(button => button.Label ==
                (forward ? "Forward to next span" : "Back to previous span"));
            await traces.SelectSpan("root");
            await traces.SelectSpan("child");
            if (forward) await Back().OnClick.InvokeAsync();
            await traces.SetParametersAsync(Parameters(run, "no match"));
            await traces.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["OnClearFilters"] = EventCallback.Factory.Create(this, () => traces.SetParametersAsync(Parameters(run))) }));
            await Navigate().OnClick.InvokeAsync();
            selections.Last().ShouldBeSameAs(forward ? child : root);
            Navigate().Disabled.ShouldBeTrue();
            view.ToHtmlString().ShouldContain($"trace-selected\" data-span-id=\"{(forward ? "child" : "root")}\"");

            if (forward)
            {
                await traces.SelectSpan("root");
                await traces.SelectSpan("child");
                await Back().OnClick.InvokeAsync();
            }
            else await traces.SelectSpan("child");
            await traces.SetParametersAsync(Parameters(run, "no match"));
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await traces.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["OnClearFilters"] = EventCallback.Factory.Create(this, () => release.Task) }));
            var back = Navigate().OnClick.InvokeAsync();
            back.IsCompleted.ShouldBeFalse();
            // A rerun or a newer manual selection must win over pending history navigation.
            await traces.SetParametersAsync(Parameters(rerun ? Guid.NewGuid() : run));
            await traces.SelectSpan("child");
            var count = selections.Count;
            release.SetResult();
            await back;
            selections.Count.ShouldBe(count);
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"child\"");
            Navigate().Disabled.ShouldBe(forward || rerun);
        });
    }

    [Fact]
    public async Task Execution_trace_forward_skips_missing_spans_and_clears_on_new_navigation_and_rerun()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var root = TraceSpan("trace", "root", null, 0, 20);
        var child = TraceSpan("trace", "child", "root", 2, 10);
        var leaf = TraceSpan("trace", "leaf", "child", 4, 2);
        var records = new[] { root, child, leaf };
        var original = JsonSerializer.Serialize(records);
        var run = Guid.NewGuid();
        var selections = new List<FluxFlow.Environments.ExecutionTrace>();
        ParameterView Parameters(Guid id, params FluxFlow.Environments.ExecutionTrace[] spans) => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["RunId"] = id, ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot(spans, 0),
            ["OnSelectSpan"] = EventCallback.Factory.Create<FluxFlow.Environments.ExecutionTrace>(selections, selections.Add)
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionTraces>(Parameters(run, records));
            var traces = components.Items.OfType<ExecutionTraces>().Single();
            StudioIconButton Back() => components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Back to previous span");
            StudioIconButton Forward() => components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Forward to next span");
            Forward().Disabled.ShouldBeTrue();
            await traces.SelectSpan("root");
            await traces.SelectSpan("child");
            await traces.SelectSpan("leaf");
            await Back().OnClick.InvokeAsync();
            await Back().OnClick.InvokeAsync();
            // Eviction skips the nearest destination without losing a later retained destination.
            await traces.SetParametersAsync(Parameters(run, root, leaf));
            await traces.ToggleSpan("root");
            await Forward().OnClick.InvokeAsync();
            selections.Last().ShouldBeSameAs(leaf);
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"leaf\"");
            Forward().Disabled.ShouldBeTrue();
            await Back().OnClick.InvokeAsync();
            await traces.SetParametersAsync(Parameters(run, records));
            await traces.ToggleSpan("root");
            var stale = Forward().OnClick;
            await traces.SelectSpan("child");
            Forward().Disabled.ShouldBeTrue();
            var count = selections.Count;
            await stale.InvokeAsync();
            selections.Count.ShouldBe(count);
            await Back().OnClick.InvokeAsync();
            Forward().Disabled.ShouldBeFalse();
            await traces.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["RequestedSpan"] = leaf }));
            Forward().Disabled.ShouldBeTrue();
            await Back().OnClick.InvokeAsync();
            stale = Forward().OnClick;
            await traces.SetParametersAsync(Parameters(Guid.NewGuid(), records));
            Forward().Disabled.ShouldBeTrue();
            Back().Disabled.ShouldBeTrue();
            await traces.SelectSpan("root");
            count = selections.Count;
            await stale.InvokeAsync();
            selections.Count.ShouldBe(count);
            await traces.SelectSpan("leaf");
            await Back().OnClick.InvokeAsync();
            stale = Forward().OnClick;
            await traces.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot([root], 2) }));
            Forward().Disabled.ShouldBeTrue();
            count = selections.Count;
            await stale.InvokeAsync();
            selections.Count.ShouldBe(count);
            traces.Dispose();
            await stale.InvokeAsync();
            selections.Count.ShouldBe(count);
        });
        JsonSerializer.Serialize(records).ShouldBe(original);
    }

    [Fact]
    public async Task Execution_trace_reveal_selected_span_ignores_missing_selection_and_stale_actions()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var scrolling = new TraceScrolling();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IJSRuntime>(scrolling);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var root = TraceSpan("trace", "root", null, 0, 20);
        var child = TraceSpan("trace", "child", "root", 2, 5);
        var run = Guid.NewGuid();
        var selections = new List<FluxFlow.Environments.ExecutionTrace>();
        ParameterView Parameters(Guid id, params FluxFlow.Environments.ExecutionTrace[] spans) => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["RunId"] = id, ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot(spans, 0),
            ["OnSelectSpan"] = EventCallback.Factory.Create<FluxFlow.Environments.ExecutionTrace>(selections, selections.Add)
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<RenderedExecutionTraces>(Parameters(run, root, child));
            var traces = components.Items.OfType<RenderedExecutionTraces>().Single();
            StudioIconButton Reveal() => components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Reveal selected span");
            Reveal().Disabled.ShouldBeTrue();
            await Reveal().OnClick.InvokeAsync();
            await traces.AfterRender();
            scrolling.Selectors.ShouldBeEmpty();
            await traces.SelectSpan("child");
            Reveal().Disabled.ShouldBeFalse();
            await Reveal().OnClick.InvokeAsync();
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);
            selections.Count.ShouldBe(1);
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);
            var stale = Reveal().OnClick;
            await traces.SelectSpan("root");
            await stale.InvokeAsync();
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);
            await traces.SelectSpan("child");
            await Reveal().OnClick.InvokeAsync();
            await traces.SetParametersAsync(Parameters(run, root));
            Reveal().Disabled.ShouldBeTrue();
            await stale.InvokeAsync();
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);
            await traces.SetParametersAsync(Parameters(run, root, child));
            await traces.SelectSpan("child");
            await Reveal().OnClick.InvokeAsync();
            await traces.SetParametersAsync(Parameters(Guid.NewGuid(), root, child));
            Reveal().Disabled.ShouldBeTrue();
            await traces.SelectSpan("child");
            await stale.InvokeAsync();
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);
            await Reveal().OnClick.InvokeAsync();
            stale = Reveal().OnClick;
            traces.Dispose();
            await stale.InvokeAsync();
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);
            selections.Count.ShouldBe(5);
        });
    }

    [Fact]
    public async Task Execution_trace_parent_navigation_handles_missing_parents_stop_and_stale_actions()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var scrolling = new TraceScrolling();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IJSRuntime>(scrolling);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 123 });
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        var session = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        var spans = new[]
        {
            TraceSpan("trace", "root", null, 0, 20), TraceSpan("trace", "child", "root", 2, 10),
            TraceSpan("trace", "leaf", "child", 4, 2), TraceSpan("trace", "side", null, 10, 10),
            TraceSpan("trace", "side-child", "side", 12, 2)
        };
        var original = JsonSerializer.Serialize(spans);
        var start = session.StartAsync(graph);
        runner.Release.SetResult(new(runner.Id, "Running", [], Traces: new(spans, 0)));
        await start;
        var run = session.Status!.Id;
        var selections = new List<FluxFlow.Environments.ExecutionTrace>();
        ParameterView Parameters(Guid id, params FluxFlow.Environments.ExecutionTrace[] records) => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["RunId"] = id, ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot(records, 0),
            ["OnSelectSpan"] = EventCallback.Factory.Create<FluxFlow.Environments.ExecutionTrace>(selections, selections.Add)
        });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var wrongParent = TraceSpan("different-trace", "child", null, -20, 1);
            var view = await renderer.RenderComponentAsync<RenderedExecutionTraces>(Parameters(run, spans[2], wrongParent));
            var traces = components.Items.OfType<RenderedExecutionTraces>().Single();
            StudioIconButton ParentButton() => components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Go to parent span");
            await traces.SelectSpan("leaf");
            ParentButton().Disabled.ShouldBeTrue();
            view.ToHtmlString().ShouldContain("Parent not captured");
            await ParentButton().OnClick.InvokeAsync();
            selections.Count.ShouldBe(1);
            await traces.SetParametersAsync(Parameters(run, spans));
            ParentButton().Disabled.ShouldBeFalse();
            await traces.ToggleSpan("side");
            await ParentButton().OnClick.InvokeAsync();
            selections.Last().ShouldBeSameAs(spans[1]);
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);
            scrolling.Selectors.Single().ShouldStartWith("#trace-view-");
            scrolling.Selectors.Single().ShouldEndWith(" .trace-selected");
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"child\"");
            view.ToHtmlString().ShouldNotContain("data-span-id=\"side-child\"");
            await session.StopAsync();
            await traces.SetParametersAsync(Parameters(session.Status!.Id, session.Traces.Records.ToArray()));
            await ParentButton().OnClick.InvokeAsync();
            selections.Last().ShouldBeSameAs(spans[0]);
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"root\"");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Go to parent span\"");
            await traces.SelectSpan("leaf");
            var stale = ParentButton().OnClick;
            await traces.SelectSpan("child");
            var count = selections.Count;
            await stale.InvokeAsync();
            selections.Count.ShouldBe(count);
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"child\"");
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);
            await traces.SetParametersAsync(Parameters(run, spans.Select(span => span with { TraceId = "other" }).ToArray()));
            await traces.SelectSpan("leaf");
            count = selections.Count;
            await stale.InvokeAsync();
            selections.Count.ShouldBe(count);
            var next = Guid.NewGuid();
            await traces.SetParametersAsync(Parameters(next, spans));
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await traces.SelectSpan("leaf");
            count = selections.Count;
            await stale.InvokeAsync();
            selections.Count.ShouldBe(count);
            stale = ParentButton().OnClick;
            await traces.SetParametersAsync(Parameters(next, spans[2]));
            ParentButton().Disabled.ShouldBeTrue();
            await stale.InvokeAsync();
            selections.Count.ShouldBe(count);
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"leaf\"");
            await traces.SetParametersAsync(Parameters(next, spans[2] with { ParentSpanId = "leaf" }));
            ParentButton().Disabled.ShouldBeTrue();
            await traces.SetParametersAsync(Parameters(next, spans));
            stale = ParentButton().OnClick;
            await stale.InvokeAsync();
            count = selections.Count;
            await traces.SetParametersAsync(Parameters(Guid.NewGuid(), spans));
            await traces.AfterRender();
            scrolling.Selectors.Count.ShouldBe(1);
            traces.Dispose();
            await stale.InvokeAsync();
            selections.Count.ShouldBe(count);
        });
        JsonSerializer.Serialize(spans).ShouldBe(original);
        graph.Revision.ShouldBe(revision);
        graph.ToProjectJson().ShouldBe(project);
        graph.Undo();
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldNotBe(123);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(project);
    }

    [Fact]
    public async Task Execution_trace_expand_and_collapse_all_preserve_selection_stop_and_run_isolation()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 123 });
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        var session = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        var spans = new[]
        {
            TraceSpan("trace", "root", null, 0, 20), TraceSpan("trace", "child", "root", 2, 10),
            TraceSpan("trace", "leaf", "child", 4, 2), TraceSpan("trace", "orphan", "missing", 10, 10),
            TraceSpan("trace", "orphan-child", "orphan", 12, 2)
        };
        var original = JsonSerializer.Serialize(spans);
        var start = session.StartAsync(graph);
        runner.Release.SetResult(new(runner.Id, "Running", [], Traces: new(spans, 0)));
        await start;
        var run = session.Status!.Id;
        var selections = new List<FluxFlow.Environments.ExecutionTrace>();
        ParameterView Parameters(Guid id, params FluxFlow.Environments.ExecutionTrace[] records) => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["RunId"] = id, ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot(records, 0),
            ["OnSelectSpan"] = EventCallback.Factory.Create<FluxFlow.Environments.ExecutionTrace>(selections, selections.Add)
        });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionTraces>(Parameters(run, spans));
            var traces = components.Items.OfType<ExecutionTraces>().Single();
            StudioIconButton Button(string label) => components.Items.OfType<StudioIconButton>().Last(button => button.Label == label);
            Button("Expand all").Disabled.ShouldBeTrue();
            Button("Collapse all").Disabled.ShouldBeFalse();
            await Button("Collapse all").OnClick.InvokeAsync();
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await Button("Expand all").OnClick.InvokeAsync();
            await traces.SelectSpan("leaf");
            await Button("Collapse all").OnClick.InvokeAsync();
            selections.Last().SpanId.ShouldBe("root");
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"root\"");
            view.ToHtmlString().ShouldNotContain("data-span-id=\"child\"");
            view.ToHtmlString().ShouldNotContain("data-span-id=\"orphan-child\"");
            view.ToHtmlString().ShouldContain("data-span-id=\"orphan\" data-depth=\"0\"");
            Button("Collapse all").Disabled.ShouldBeTrue();
            Button("Expand all").Disabled.ShouldBeFalse();
            var selectedCount = selections.Count;
            await Button("Expand all").OnClick.InvokeAsync();
            selections.Count.ShouldBe(selectedCount);
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"root\"");
            view.ToHtmlString().ShouldContain("data-span-id=\"leaf\" data-depth=\"2\"");
            await Button("Collapse all").OnClick.InvokeAsync();
            selections.Count.ShouldBe(selectedCount);
            await traces.ToggleSpan("root");
            view.ToHtmlString().ShouldContain("data-span-id=\"child\"");
            view.ToHtmlString().ShouldNotContain("data-span-id=\"leaf\"");
            await traces.SelectSpan("child");
            selectedCount = selections.Count;
            await Button("Expand all").OnClick.InvokeAsync();
            selections.Count.ShouldBe(selectedCount);
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"child\"");
            view.ToHtmlString().ShouldContain("data-span-id=\"leaf\" data-depth=\"2\"");
            view.ToHtmlString().ShouldContain("data-span-id=\"orphan-child\"");
            await traces.SelectSpan("orphan-child");
            await Button("Collapse all").OnClick.InvokeAsync();
            selections.Last().SpanId.ShouldBe("orphan");
            await traces.SetParametersAsync(Parameters(run, [.. spans, TraceSpan("trace", "late", "root", 15, 5)]));
            view.ToHtmlString().ShouldNotContain("data-span-id=\"late\"");
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"orphan\"");
            await session.StopAsync();
            await traces.SetParametersAsync(Parameters(session.Status!.Id, session.Traces.Records.ToArray()));
            view.ToHtmlString().ShouldContain("trace-selected\" data-span-id=\"orphan\"");
            view.ToHtmlString().ShouldNotContain("data-span-id=\"leaf\"");
            var oldExpand = Button("Expand all").OnClick;
            var oldCollapse = Button("Collapse all").OnClick;
            var other = spans.Select(span => span with { TraceId = "other" }).ToArray();
            await traces.SetParametersAsync(Parameters(run, other));
            await oldCollapse.InvokeAsync();
            view.ToHtmlString().ShouldContain("data-span-id=\"leaf\"");
            await Button("Collapse all").OnClick.InvokeAsync();
            await oldExpand.InvokeAsync();
            view.ToHtmlString().ShouldNotContain("data-span-id=\"leaf\"");
            oldExpand = Button("Expand all").OnClick;
            oldCollapse = Button("Collapse all").OnClick;
            await traces.SetParametersAsync(Parameters(Guid.NewGuid(), other));
            await oldCollapse.InvokeAsync();
            view.ToHtmlString().ShouldContain("data-span-id=\"leaf\"");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
            await Button("Collapse all").OnClick.InvokeAsync();
            await oldExpand.InvokeAsync();
            view.ToHtmlString().ShouldNotContain("data-span-id=\"leaf\"");
            await Button("Expand all").OnClick.InvokeAsync();
            await traces.SetParametersAsync(Parameters(traces.RunId!.Value, other[0]));
            Button("Expand all").Disabled.ShouldBeTrue();
            Button("Collapse all").Disabled.ShouldBeTrue();
        });
        JsonSerializer.Serialize(spans).ShouldBe(original);
        graph.Revision.ShouldBe(revision);
        graph.ToProjectJson().ShouldBe(project);
        graph.Undo();
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldNotBe(123);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(project);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(25)]
    public async Task Execution_trace_short_spans_fill_the_scale_and_keep_the_exact_axis_end(long ticks)
    {
        await using var provider = CreateServices().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var span = TraceSpan("trace", "short", null, 0, 0) with { Duration = TimeSpan.FromTicks(ticks) };
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionTraces>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot([span], 0) }));
            var html = view.ToHtmlString();
            html.ShouldContain("left:0%;width:100%");
            var axis = html[(html.IndexOf("class=\"trace-axis\"", StringComparison.Ordinal))..];
            axis = axis[..axis.IndexOf("</div>", StringComparison.Ordinal)];
            axis.ShouldContain($">{span.Duration.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} ms</span>");
        });
    }

    [Fact]
    public async Task Execution_traces_keep_selection_during_polling_and_reset_on_run_change_or_eviction()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var graph = StudioTestServices.CreateGraph();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 123 });
        var project = graph.ToProjectJson();
        var run = Guid.NewGuid();
        var first = TraceSpan("first", "one", null, 0, 0);
        var second = TraceSpan("second", "two", "missing", 10, 3);
        ParameterView Parameters(Guid id, params FluxFlow.Environments.ExecutionTrace[] spans) => ParameterView.FromDictionary(new Dictionary<string, object?>
        { ["RunId"] = id, ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot(spans, 0) });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionTraces>(Parameters(run, first, second));
            var component = components.Items.OfType<ExecutionTraces>().Single();
            var picker = components.Items.OfType<MudBlazor.MudSelect<string>>().Single();
            await picker.ValueChanged.InvokeAsync("first");
            await component.SetParametersAsync(Parameters(run, first, second));
            view.ToHtmlString().ShouldContain("data-span-id=\"one\"");
            view.ToHtmlString().ShouldNotContain("data-span-id=\"two\"");
            view.ToHtmlString().ShouldNotContain("NaN");
            await component.SetParametersAsync(Parameters(Guid.NewGuid(), first, second));
            view.ToHtmlString().ShouldContain("data-span-id=\"two\"");
            await component.SetParametersAsync(Parameters(run, first));
            await component.SetParametersAsync(Parameters(run, second));
            view.ToHtmlString().ShouldContain("data-span-id=\"two\"");
            // A malformed cycle is shown once per span, never recursively expanded forever.
            await component.SetParametersAsync(Parameters(run, first with { ParentSpanId = "three" }, first with { SpanId = "three", ParentSpanId = "one" }));
            view.ToHtmlString().Split("data-span-id=").Length.ShouldBe(3);
            await component.SetParametersAsync(Parameters(run));
            view.ToHtmlString().ShouldContain("No completed spans yet");
            view.ToHtmlString().ShouldNotContain("Span details");
        });
        graph.ToProjectJson().ShouldBe(project);
        graph.Undo();
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldNotBe(123);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(project);
    }

    [Fact]
    public async Task Execution_logs_offer_navigation_only_for_matching_trace_and_span_ids()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var record = CorrelatedLog("target", "child");
        var original = JsonSerializer.Serialize(record);
        var selected = new List<FluxFlow.Environments.ExecutionTrace>();
        var expected = TraceSpan("target", "child", "parent", 5, 2);
        ParameterView Parameters(params FluxFlow.Environments.ExecutionTrace[] spans) => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["Value"] = new FluxFlow.Environments.ExecutionLogSnapshot([record, record with { Sequence = 2, Telemetry = null }], 0),
            ["Diagnostics"] = new FluxFlow.Environments.ExecutionLogSnapshot([record], 0),
            ["Traces"] = new FluxFlow.Environments.ExecutionTraceSnapshot(spans, 0),
            ["OnViewTrace"] = EventCallback.Factory.Create<FluxFlow.Environments.ExecutionTrace>(this, selected.Add)
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionLogs>(Parameters(
                TraceSpan("other", "child", null, 0, 1), TraceSpan("target", "other-child", null, 0, 1)));
            var logs = components.Items.OfType<ExecutionLogs>().Single();
            view.ToHtmlString().ShouldNotContain("View trace");
            view.ToHtmlString().Split("Span unavailable").Length.ShouldBe(3);
            await logs.SetParametersAsync(Parameters(expected));
            view.ToHtmlString().ShouldNotContain("Span unavailable");
            var buttons = components.Items.OfType<MudBlazor.MudButton>().Where(button => !button.Disabled).ToArray();
            buttons.Length.ShouldBe(2);
            foreach (var button in buttons) await button.OnClick.InvokeAsync(new MouseEventArgs());
            selected.ShouldBe(new[] { expected, expected });
            await logs.SetParametersAsync(Parameters());
            view.ToHtmlString().ShouldNotContain("View trace");
            view.ToHtmlString().ShouldContain("Span unavailable");
        });
        JsonSerializer.Serialize(record).ShouldBe(original);
    }

    [Fact]
    public async Task Execution_logs_follow_controls_preserve_filters_and_records_through_stop()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var javascript = new DelayedLogScrolling();
        javascript.Ready.SetResult();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IJSRuntime>(javascript);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var record = CorrelatedLog("target", "child");
        var original = JsonSerializer.Serialize(record);
        var span = TraceSpan("target", "child", null, 0, 1);
        var runId = Guid.NewGuid();
        ParameterView Parameters(bool live) => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["Value"] = new FluxFlow.Environments.ExecutionLogSnapshot([record], 0),
            ["RunId"] = runId,
            ["IsLive"] = live,
            ["ColumnFilters"] = SeverityFilters(13),
            ["SearchText"] = "retained search",
            ["RelatedSpan"] = span,
            ["TraceOnly"] = true
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<RenderedExecutionLogs>(Parameters(true));
            var logs = components.Items.OfType<RenderedExecutionLogs>().Single();
            await logs.AfterRender(true);
            view.ToHtmlString().ShouldContain("Follow latest");
            view.ToHtmlString().ShouldContain("Jump to latest");
            var jump = components.Items.OfType<StudioIconButton>().Single(button => button.Label == "Jump to latest");
            await jump.OnClick.InvokeAsync();
            await logs.AfterRender(false);
            javascript.FollowingAtUpdate.ShouldBe(true);
            javascript.Jumps.ShouldBe(1);
            await logs.SetFollowing(false);
            await logs.PauseFollowing();
            view.ToHtmlString().ShouldContain("Jump to latest");
            await logs.SetParametersAsync(Parameters(false));
            view.ToHtmlString().ShouldContain("Jump to latest");
            await jump.OnClick.InvokeAsync();
            await logs.AfterRender(false);
            javascript.FollowingAtUpdate.ShouldBe(false);
            javascript.Jumps.ShouldBe(2);
            view.ToHtmlString().ShouldContain("Jump to latest");
            await logs.SetFollowing(true);
            await logs.AfterRender(false);
            javascript.FollowingAtUpdate.ShouldBe(true);
            javascript.Jumps.ShouldBe(2);
            logs.ColumnFilters.Single().Value.ShouldBe(3);
            logs.SearchText.ShouldBe("retained search");
            logs.RelatedSpan.ShouldBeSameAs(span);
            logs.TraceOnly.ShouldBeTrue();
            logs.Value.Records.Single().ShouldBeSameAs(record);
        });
        JsonSerializer.Serialize(record).ShouldBe(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execution_logs_initialize_scrolling_once_during_pending_import(bool disposeWhileLoading)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var javascript = new DelayedLogScrolling();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IJSRuntime>(javascript);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<RenderedExecutionLogs>();
            var logs = components.Items.OfType<RenderedExecutionLogs>().Single();
            var first = logs.AfterRender(true);
            var refresh = logs.AfterRender(false);
            if (disposeWhileLoading) await logs.DisposeAsync();
            javascript.Ready.SetResult();
            await Task.WhenAll(first, refresh);
            javascript.Imports.ShouldBe(1);
            javascript.Controllers.ShouldBe(disposeWhileLoading ? 0 : 1);
        });
    }

    [Fact]
    public async Task Execution_log_details_pause_following_during_scroll_initialization()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var javascript = new DelayedLogScrolling { ControllerReady = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        javascript.Ready.SetResult();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IJSRuntime>(javascript);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<RenderedExecutionLogs>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Value"] = new FluxFlow.Environments.ExecutionLogSnapshot([CorrelatedLog("trace", "span")], 0),
                ["IsLive"] = true
            }));
            var logs = components.Items.OfType<RenderedExecutionLogs>().Single();
            var initialize = logs.AfterRender(true);
            javascript.Controllers.ShouldBe(1);
            await logs.SelectRecord("Engine", 1, null);
            javascript.ControllerReady.SetResult();
            await initialize;
            javascript.FollowingAtUpdate.ShouldBe(false);
            view.ToHtmlString().ShouldContain("aria-label=\"Log details\"");
            view.ToHtmlString().ShouldContain("Jump to latest");
        });
    }

    private sealed class RenderedExecutionLogs : ExecutionLogs
    {
        public Task AfterRender(bool firstRender) => base.OnAfterRenderAsync(firstRender);
    }

    private sealed class RenderedExecutionTraces : ExecutionTraces
    {
        public Task AfterRender() => base.OnAfterRenderAsync(false);
    }

    private sealed class TraceScrolling : IJSRuntime
    {
        public List<string> Selectors { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "mudScrollManager.scrollIntoView") Selectors.Add((string)args![0]!);
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class DelayedLogScrolling : IJSRuntime, IJSObjectReference
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? ControllerReady { get; init; }
        public bool? FollowingAtUpdate { get; private set; }
        private bool _following;
        public int Imports { get; private set; }
        public int Controllers { get; private set; }
        public int Jumps { get; private set; }
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "import")
            {
                Imports++;
                await Ready.Task;
                return (TValue)(object)this;
            }
            if (identifier == "create")
            {
                Controllers++;
                _following = (bool)args![2]!;
                if (ControllerReady is not null) await ControllerReady.Task;
                return (TValue)(object)this;
            }
            if (identifier == "setFollowing") _following = (bool)args![0]!;
            if (identifier == "jumpToLatest") Jumps++;
            if (identifier == "update") FollowingAtUpdate = _following;
            return default!;
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static List<MudBlazor.IFilterDefinition<ExecutionLogRow>> SeverityFilters(int minimum) => minimum == 0 ? [] :
        [new MudBlazor.FilterDefinition<ExecutionLogRow> { Value = (minimum - 1) / 4,
            FilterFunction = row => row.Record.Telemetry?.SeverityNumber is >= 1 and <= 24 && row.Record.Telemetry.SeverityNumber >= minimum }];

    private static Task ApplySeverity(MudBlazor.MudDataGrid<ExecutionLogRow> grid, int level) => grid.AddFilterAsync(
        new MudBlazor.FilterDefinition<ExecutionLogRow> { Column = grid.RenderedColumns.Single(column => column.Title == "Level"),
            Operator = MudBlazor.FilterOperator.Number.GreaterThanOrEqual, Value = level });

    [Fact]
    public async Task Log_grid_filters_sort_and_clear_preserve_originals_selection_and_Stop()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var template = CorrelatedLog("trace", "span");
        var records = Enumerable.Range(1, 24).Select(level => template with
        {
            Sequence = level, Timestamp = template.Timestamp.AddSeconds(level), Message = $"original-{level}",
            Telemetry = template.Telemetry! with { SeverityNumber = level }
        }).ToArray();
        var original = JsonSerializer.Serialize(records);
        var run = Guid.NewGuid();
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionLogs>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Value"] = new FluxFlow.Environments.ExecutionLogSnapshot(records, 0),
                ["RunId"] = run, ["IsLive"] = true
            }));
            var logs = components.Items.OfType<ExecutionLogs>().Single();
            var grid = components.Items.OfType<MudBlazor.MudDataGrid<ExecutionLogRow>>().Single();
            grid.ColumnResizeMode.ShouldBe(MudBlazor.ResizeMode.Column);
            grid.ReadOnly.ShouldBeTrue();
            foreach (var level in Enumerable.Range(0, 6))
            {
                await grid.ClearFiltersAsync();
                await ApplySeverity(grid, level);
                view.ToHtmlString().ShouldContain($"{24 - level * 4} / 24 retained");
                await grid.ClearFiltersAsync();
                await grid.AddFilterAsync(new MudBlazor.FilterDefinition<ExecutionLogRow>
                {
                    Column = grid.RenderedColumns.Single(column => column.Title == "Level"),
                    Operator = MudBlazor.FilterOperator.Number.Equal, Value = level
                });
                grid.FilteredItems.Count().ShouldBe(4);
            }
            await logs.ClearFilters();
            async Task Filter(string title, string op, object value) => await grid.AddFilterAsync(new MudBlazor.FilterDefinition<ExecutionLogRow>
            { Column = grid.RenderedColumns.Single(column => column.Title == title), Operator = op, Value = value });
            await Filter("Source", MudBlazor.FilterOperator.String.Equal, "Engine");
            await Filter("Category", MudBlazor.FilterOperator.String.Equal, template.Category);
            await Filter("Time (UTC)", MudBlazor.FilterOperator.DateTime.OnOrAfter, template.Timestamp.UtcDateTime.AddSeconds(20));
            grid.FilteredItems.Count().ShouldBe(5);
            await grid.SetSortAsync(nameof(ExecutionLogRow.Time), MudBlazor.SortDirection.Descending, row => row.Time);
            // Static rendering does not run the grid's browser lifecycle after sorting.
            await logs.SetParametersAsync(ParameterView.Empty);
            grid.FilteredItems.First().Record.ShouldBeSameAs(records[^1]);
            view.ToHtmlString().ShouldContain("data-log-latest=\"true\"");
            await logs.SelectRecord("Engine", 20, run);
            await Filter("Message", MudBlazor.FilterOperator.String.Contains, "original-24");
            view.ToHtmlString().ShouldContain("This record is hidden by the current filters.");
            await logs.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["IsLive"] = false }));
            logs.ColumnFilters.Count.ShouldBe(4);
            grid.FilteredItems.Single().Record.ShouldBeSameAs(records[^1]);
            await logs.ClearFilters();
            grid.FilteredItems.Count().ShouldBe(24);
            view.ToHtmlString().ShouldContain("aria-label=\"Log details\"");
            await logs.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["RunId"] = Guid.NewGuid(), ["Value"] = new FluxFlow.Environments.ExecutionLogSnapshot([], 0) }));
            view.ToHtmlString().ShouldNotContain("aria-label=\"Log details\"");
            view.ToHtmlString().ShouldNotContain("data-log-latest=\"true\"");
        });
        JsonSerializer.Serialize(records).ShouldBe(original);
    }

    [Fact]
    public async Task Log_level_menu_renders_the_standard_level_names()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<LogSeverityFilter>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Context"] = new MudBlazor.FilterContext<ExecutionLogRow>(new MudBlazor.MudDataGrid<ExecutionLogRow>())
            }));
            var options = components.Items.OfType<MudBlazor.MudSelectItem<int?>>().ToArray();
            options.Select(option => option.Value).ShouldBe(Enumerable.Range(0, 6).Select(level => (int?)level));
            foreach (var option in options)
            {
                var label = await renderer.RenderComponentAsync<MudBlazor.MudText>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { ["ChildContent"] = option.ChildContent }));
                label.ToHtmlString().ShouldContain($">{(LogLevel)option.Value!.Value}</p>");
            }
        });
    }

    [Fact]
    public async Task Log_header_controls_stay_stable_during_updates_and_clear_filters_without_resuming_follow()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ObservationPanel>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["LogsRequest"] = 1 }));
            var html = view.ToHtmlString();
            html.IndexOf("aria-label=\"Log controls\"", StringComparison.Ordinal).ShouldBeLessThan(
                html.IndexOf("class=\"observation-body\"", StringComparison.Ordinal));
            html.ShouldNotContain("placeholder=\"Message, category or exception\"");
            var logs = components.Items.OfType<ExecutionLogs>().Single();
            var menu = components.Items.OfType<MudBlazor.MudMenu>().Single(item => item.AriaLabel == "Search logs");
            await logs.SearchTextChanged.InvokeAsync("retained");
            logs.SearchText.ShouldBe("retained");
            menu.Color.ShouldBe(MudBlazor.Color.Primary);
            await logs.SetParametersAsync(ParameterView.Empty);
            components.Items.OfType<MudBlazor.MudMenu>().Count(item => item.AriaLabel == "Search logs").ShouldBe(1);
            logs.SearchText.ShouldBe("retained");
            var follow = components.Items.OfType<StudioIconButton>().Single(button => button.Label == "Follow latest");
            var openChanges = new List<bool>();
            await menu.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Open"] = true,
                ["OpenChanged"] = EventCallback.Factory.Create<bool>(this, value => openChanges.Add(value))
            }));
            // Static rendering does not mount the popup, so render its existing content to exercise the input callback.
            await renderer.RenderComponentAsync<MudBlazor.MudPaper>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["ChildContent"] = menu.ChildContent }));
            var search = components.Items.OfType<MudBlazor.MudTextField<string>>().Last(field => field.Label == "Search logs");
            await search.OnKeyDown.InvokeAsync(new KeyboardEventArgs { Key = "Home" });
            openChanges.ShouldBeEmpty();
            await search.OnKeyDown.InvokeAsync(new KeyboardEventArgs { Key = "Escape" });
            openChanges.ShouldBe(new[] { false });
            logs.SearchText.ShouldBe("retained");
            await follow.OnClick.InvokeAsync();
            follow.Pressed.ShouldBe(false);
            await ApplySeverity(components.Items.OfType<MudBlazor.MudDataGrid<ExecutionLogRow>>().Single(), 3);
            await components.Items.OfType<StudioIconButton>().Single(button => button.Label == "Clear filters").OnClick.InvokeAsync();
            logs.SearchText.ShouldBeEmpty();
            logs.ColumnFilters.ShouldBeEmpty();
            follow.Pressed.ShouldBe(false);
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
        });
    }

    [Fact]
    public async Task Execution_logs_combine_text_severity_and_correlation_without_mutating_records()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var template = CorrelatedLog("trace", "child");
        var records = Enumerable.Range(0, 25).Select(number => template with
        {
            Sequence = number + 1, Message = $"record-{number:D2}-body", Level = "Original severity",
            Category = number == 17 ? "Case.Category" : "Original.Component",
            Exception = number == 18 ? "Outer exception\nInner failure\n   at Complete.Stack()" : null,
            Telemetry = template.Telemetry! with { SeverityNumber = number, SpanId = number == 18 ? "sibling" : "child" }
        }).Append(template with { Sequence = 26, Message = "No telemetry", Telemetry = null }).ToArray();
        var original = JsonSerializer.Serialize(records);
        ParameterView Parameters(int minimum, string search = "", bool related = false, bool traceOnly = false) => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["Value"] = new FluxFlow.Environments.ExecutionLogSnapshot(records[..18], 3),
            ["Diagnostics"] = new FluxFlow.Environments.ExecutionLogSnapshot(records[18..], 2),
            ["ColumnFilters"] = SeverityFilters(minimum), ["SearchText"] = search,
            ["RelatedSpan"] = related ? TraceSpan("trace", "child", null, 0, 1) : null, ["TraceOnly"] = traceOnly
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionLogs>(Parameters(0));
            var logs = components.Items.OfType<ExecutionLogs>().Single();
            foreach (var (minimum, count) in new[] { (0, 26), (1, 24), (5, 20), (9, 16), (13, 12), (17, 8), (21, 4) })
            {
                await logs.SetParametersAsync(Parameters(minimum));
                var html = view.ToHtmlString();
                html.ShouldContain($"{count} / 26 retained");
                html.ShouldContain("3 older engine records");
                html.ShouldContain("2 older runner records");
                html.ShouldContain("record-24-body");
                if (minimum > 0)
                {
                    html.ShouldContain($"record-{minimum:D2}-body");
                    html.ShouldNotContain($"record-{minimum - 1:D2}-body");
                    html.ShouldNotContain("No telemetry");
                }
                else html.ShouldContain("No telemetry");
            }
            await logs.SetParametersAsync(Parameters(13, "  RECORD-13-BODY  "));
            view.ToHtmlString().ShouldContain("1 / 26 retained");
            await logs.SetParametersAsync(Parameters(17, "case.category", true));
            view.ToHtmlString().ShouldContain("1 / 26 retained");
            view.ToHtmlString().ShouldContain("record-17-body");
            await logs.SetParametersAsync(Parameters(17, "complete.stack", true));
            view.ToHtmlString().ShouldContain("0 / 26 retained");
            view.ToHtmlString().ShouldContain("No retained logs match the current filters.");
            await logs.SetParametersAsync(Parameters(17, "complete.stack", true, true));
            view.ToHtmlString().ShouldContain("1 / 26 retained");
            view.ToHtmlString().ShouldContain("record-18-body");
            await logs.SelectRecord("Runner", 19, null);
            view.ToHtmlString().ShouldContain("Inner failure");
            logs.ClearSelection();
            await logs.SetParametersAsync(Parameters(0, "  "));
            view.ToHtmlString().ShouldContain("26 / 26 retained");
        });
        JsonSerializer.Serialize(records).ShouldBe(original);
    }

    [Fact]
    public async Task Related_logs_filter_both_sources_by_exact_ids_without_changing_retained_records()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var span = TraceSpan("target", "child", null, 0, 1);
        var engine = new FluxFlow.Environments.ExecutionLogSnapshot([
            CorrelatedLog("target", "child") with { Message = "Selected engine record" },
            CorrelatedLog("target", "sibling") with { Sequence = 2, Message = "Sibling record" },
            CorrelatedLog("other", "child") with { Sequence = 3, Message = "Other trace record" },
            CorrelatedLog("target", "child") with { Sequence = 4, Message = "Uncorrelated record", Telemetry = null }
        ], 0);
        var runner = new FluxFlow.Environments.ExecutionLogSnapshot([
            CorrelatedLog("target", "child") with { Message = "Selected runner record" }
        ], 0);
        var original = JsonSerializer.Serialize(new[] { engine, runner });
        bool wholeTrace = false, cleared = false;
        ParameterView Parameters(FluxFlow.Environments.ExecutionTrace? selected, bool traceOnly = false) => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["Value"] = engine, ["Diagnostics"] = runner, ["RelatedSpan"] = selected, ["TraceOnly"] = traceOnly,
            ["TraceOnlyChanged"] = EventCallback.Factory.Create<bool>(this, value => wholeTrace = value),
            ["OnClearFilter"] = EventCallback.Factory.Create(this, () => cleared = true)
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionLogs>(Parameters(span));
            var logs = components.Items.OfType<ExecutionLogs>().Single();
            var html = view.ToHtmlString();
            html.ShouldContain("Selected engine record");
            html.ShouldContain("Selected runner record");
            html.ShouldNotContain("Sibling record");
            html.ShouldNotContain("Other trace record");
            html.ShouldNotContain("Uncorrelated record");
            await components.Items.OfType<MudBlazor.MudSelect<bool>>().Single().ValueChanged.InvokeAsync(true);
            wholeTrace.ShouldBeTrue();
            await logs.SetParametersAsync(Parameters(span, wholeTrace));
            view.ToHtmlString().ShouldContain("Sibling record");
            view.ToHtmlString().ShouldNotContain("Other trace record");
            view.ToHtmlString().ShouldNotContain("Uncorrelated record");
            await logs.SetParametersAsync(Parameters(span with { SpanId = "absent" }));
            view.ToHtmlString().ShouldContain("No retained logs match the current filters.");
            await logs.SetParametersAsync(Parameters(span with { TraceId = "absent" }, true));
            view.ToHtmlString().ShouldContain("No retained logs match the current filters.");
            var chip = components.Items.OfType<MudBlazor.MudChip<string>>().Single();
            await chip.OnClose.InvokeAsync(chip);
            cleared.ShouldBeTrue();
            await logs.SetParametersAsync(Parameters(null));
            html = view.ToHtmlString();
            html.ShouldNotContain("Related logs filter");
            html.ShouldNotContain("No retained logs match");
            html.ShouldContain("Other trace record");
            html.ShouldContain("Uncorrelated record");
        });
        JsonSerializer.Serialize(new[] { engine, runner }).ShouldBe(original);
    }

    [Fact]
    public async Task Log_trace_request_reveals_the_exact_span_and_does_not_override_later_selection()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var run = Guid.NewGuid();
        var parent = TraceSpan("target", "parent", null, 0, 20);
        var child = TraceSpan("target", "child", "parent", 5, 2);
        var other = TraceSpan("newer", "child", null, 30, 5);
        ParameterView Parameters(Guid id, FluxFlow.Environments.ExecutionTrace? request) => ParameterView.FromDictionary(new Dictionary<string, object?>
        { ["RunId"] = id, ["Value"] = new FluxFlow.Environments.ExecutionTraceSnapshot([parent, child, other], 0), ["RequestedSpan"] = request });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionTraces>(Parameters(run, null));
            var viewer = components.Items.OfType<ExecutionTraces>().Single();
            var picker = components.Items.OfType<MudBlazor.MudSelect<string>>().Single();
            await picker.ValueChanged.InvokeAsync("target");
            await viewer.ToggleSpan("parent");
            view.ToHtmlString().ShouldNotContain("data-span-id=\"child\"");
            await viewer.SetParametersAsync(Parameters(run, child));
            view.ToHtmlString().ShouldContain("aria-label=\"Span details\"");
            view.ToHtmlString().ShouldContain("data-span-id=\"child\" data-depth=\"1\"");
            view.ToHtmlString().ShouldContain("aria-expanded=\"true\"");
            await viewer.SelectSpan("parent");
            await viewer.SetParametersAsync(Parameters(run, child));
            view.ToHtmlString().ShouldMatch("trace-selected[^>]*data-span-id=\"parent\"");
            await viewer.SetParametersAsync(Parameters(Guid.NewGuid(), null));
            view.ToHtmlString().ShouldNotContain("aria-label=\"Span details\"");
        });
    }

    [Fact]
    public async Task Trace_selection_navigates_to_the_recorded_component_after_Stop_without_editing_and_rejects_stale_projects()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var run = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        graph.LoadJson("""{"KeepCase":42,"Workflows":{"main":{"receiver":{"Type":"mapper","Expression":"$"}},"other":{"receiver":{"Type":"mapper","Expression":"$"}}}}""");
        graph.IsProjectOpen = true;
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
        var span = TraceSpan("trace", "receiver", null, 0, 20);
        var log = CorrelatedLog(span.TraceId, span.SpanId);
        EventCallback<FluxFlow.Environments.ExecutionTrace> oldLogNavigation = default;
        EventCallback<FluxFlow.Environments.ExecutionTrace> oldRelatedNavigation = default;
        var start = run.StartAsync(graph);
        runner.Release.SetResult(new(runner.Id, "Running", [], Logs: new([log], 0), Traces: new([span], 0)));
        await start;
        run.ResolveTraceComponent(graph, span)!.Value.ShouldBe("main.receiver");
        var revision = graph.Revision;
        var configuration = graph.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            graph.SelectWorkflow("other");
            var observations = components.Items.OfType<ObservationPanel>().Single();
            await observations.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["LogsRequest"] = 1 }));
            // Keep the designer's current request value when it rerenders after canvas navigation.
            await observations.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["LogsRequest"] = 0 }));
            var logs = components.Items.OfType<ExecutionLogs>().Last();
            var viewTraceButton = components.Items.OfType<MudBlazor.MudButton>()
                .Last(button => Equals(button.UserAttributes?.GetValueOrDefault("aria-label"), "View trace"));
            oldLogNavigation = logs.OnViewTrace;
            await logs.OnViewTrace.InvokeAsync(span with { TraceId = "missing-trace" });
            await logs.OnViewTrace.InvokeAsync(span with { SpanId = "missing-span" });
            graph.WorkflowName.ShouldBe("other");
            // A poll can replace a span with an equivalent deserialized instance before a click is handled.
            var previousSnapshotSpan = JsonSerializer.Deserialize<FluxFlow.Environments.ExecutionTrace>(JsonSerializer.Serialize(span))!;
            previousSnapshotSpan.ShouldNotBe(span);
            await logs.OnViewTrace.InvokeAsync(previousSnapshotSpan);
            graph.WorkflowName.ShouldBe("main");
            await viewTraceButton.OnClick.InvokeAsync(new MouseEventArgs());
            view.ToHtmlString().ShouldMatch("aria-selected=\"true\"[^>]*>Trace");
            view.ToHtmlString().ShouldContain("aria-label=\"Span details\"");
            run.Logs.Records.Single().ShouldBe(log);
            var traces = components.Items.OfType<ExecutionTraces>().Last();
            oldRelatedNavigation = traces.OnShowRelatedLogs;
            await components.Items.OfType<MudBlazor.MudButton>()
                .Last(button => Equals(button.UserAttributes?.GetValueOrDefault("aria-label"), "Show related logs"))
                .OnClick.InvokeAsync(new MouseEventArgs());
            view.ToHtmlString().ShouldMatch("aria-selected=\"true\"[^>]*>Logs");
            view.ToHtmlString().ShouldContain("Related logs filter");
            logs = components.Items.OfType<ExecutionLogs>().Last();
            logs.RelatedSpan.ShouldBe(span);
            logs.TraceOnly.ShouldBeFalse();
            await logs.TraceOnlyChanged.InvokeAsync(true);
            logs.TraceOnly.ShouldBeTrue();
            await ApplySeverity(components.Items.OfType<MudBlazor.MudDataGrid<ExecutionLogRow>>().Last(), 3);
            await logs.SearchTextChanged.InvokeAsync("original");
            logs.ColumnFilters.Single().Value.ShouldBe(3);
            logs.SearchText.ShouldBe("original");
            await run.StopAsync();
            logs.RelatedSpan.ShouldBe(span);
            logs.TraceOnly.ShouldBeTrue();
            logs.ColumnFilters.Single().Value.ShouldBe(3);
            logs.SearchText.ShouldBe("original");
            run.Logs.Records.Single().ShouldBe(log);
            view.ToHtmlString().ShouldContain("Related logs filter");
            await logs.OnClearFilter.InvokeAsync();
            logs.RelatedSpan.ShouldBeNull();
            logs.ColumnFilters.Single().Value.ShouldBe(3);
            logs.SearchText.ShouldBe("original");
            view.ToHtmlString().ShouldNotContain("Related logs filter");
            await oldRelatedNavigation.InvokeAsync(previousSnapshotSpan);
            logs.RelatedSpan.ShouldBe(span);
            logs.TraceOnly.ShouldBeFalse();
            await logs.OnViewTrace.InvokeAsync(span);
            await oldRelatedNavigation.InvokeAsync(span);
            logs = components.Items.OfType<ExecutionLogs>().Last();
            logs.ColumnFilters.Single().Value.ShouldBe(3);
            logs.SearchText.ShouldBe("original");
            await logs.OnClearFilters.InvokeAsync();
            logs.RelatedSpan.ShouldBeNull();
            logs.ColumnFilters.ShouldBeEmpty();
            logs.SearchText.ShouldBeEmpty();
            graph.WorkflowName.ShouldBe("main");
            graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.Selected).NodeName.ShouldBe("receiver");
            var selected = graph.SelectedNode!;
            selected.Size = new(210, 240);
            graph.Diagram.SetContainer(new(new Blazor.Diagrams.Core.Geometry.Point(20, 40), new Blazor.Diagrams.Core.Geometry.Size(800, 500)));
            graph.Diagram.SetZoom(.65);
            var page = components.Items.OfType<DesignerPage>().Single();
            page.RevealTraceComponent();
            graph.Diagram.Zoom.ShouldBe(.65);
            graph.Diagram.Pan.X.ShouldBe(400 - (selected.Position.X + 105) * .65);
            graph.Diagram.Pan.Y.ShouldBe(250 - (selected.Position.Y + 120) * .65);
            selected.TraceFocusVersion.ShouldBe(1);
            await observations.OnSelectSpan.InvokeAsync(span);
            page.RevealTraceComponent();
            selected.TraceFocusVersion.ShouldBe(2);
            graph.Diagram.Zoom.ShouldBe(.65);
            var html = view.ToHtmlString();
            html.ShouldMatch($"<strong[^>]*>receiver</strong>\\s*<small[^>]*>{graph.SelectedNode!.Presentation.DisplayName}</small>");
            graph.ExportConfiguration().ShouldBe(configuration);
            graph.Revision.ShouldBe(revision);

            graph.RenameSelectedNode("renamed");
            await observations.OnSelectSpan.InvokeAsync(span);
            var pan = graph.Diagram.Pan;
            page.RevealTraceComponent();
            graph.Diagram.Pan.ShouldBe(pan);
            selected.TraceFocusVersion.ShouldBe(2);
            graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.Selected).NodeName.ShouldBe("renamed");
            run.ResolveTraceComponent(graph, span).ShouldBeNull();
            graph.Undo();
            graph.ExportConfiguration().ShouldBe(configuration);
            run.ResolveTraceComponent(graph, span).ShouldBeNull();
            graph.Redo();
            graph.ExportConfiguration().ShouldContain("renamed");
            graph.LoadJson(configuration);
            run.ResolveTraceComponent(graph, span).ShouldBeNull();
            await oldRelatedNavigation.InvokeAsync(span);
            view.ToHtmlString().ShouldContain("Related logs filter");
            logs = components.Items.OfType<ExecutionLogs>().Last();
            await ApplySeverity(components.Items.OfType<MudBlazor.MudDataGrid<ExecutionLogRow>>().Last(), 4);
            await logs.SearchTextChanged.InvokeAsync("rerun-search");
        });
        var rerun = run.StartAsync(graph);
        var fresh = span with { TraceId = "new-trace" };
        runner.Release.SetResult(new(runner.Id, "Running", [], Traces: new([fresh], 0)));
        await rerun;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            graph.SelectWorkflow("other");
            await oldLogNavigation.InvokeAsync(fresh);
            graph.WorkflowName.ShouldBe("other");
            var logs = components.Items.OfType<ExecutionLogs>().Last();
            logs.RelatedSpan.ShouldBeNull();
            logs.ColumnFilters.Single().Value.ShouldBe(4);
            logs.SearchText.ShouldBe("rerun-search");
            await oldRelatedNavigation.InvokeAsync(fresh);
            logs.RelatedSpan.ShouldBeNull();
        });
        run.ResolveTraceComponent(graph, span).ShouldBeNull();
        run.ResolveTraceComponent(graph, fresh)!.Value.ShouldBe("main.receiver");
        await run.StopAsync();
    }

    [Fact]
    public async Task Component_activity_coalesces_traffic_without_editing_and_clears_on_Stop_stale_projects_and_rerun()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner { StopRelease = new() };
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var run = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        graph.LoadJson("""{"KeepCase":42,"Workflows":{"main":{"receiver":{"Type":"mapper","Expression":"$"}},"other":{"receiver":{"Type":"mapper","Expression":"$"}}}}""");
        graph.IsProjectOpen = true;
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
        var configuration = graph.ToProjectJson();
        var revision = graph.Revision;
        var span = TraceSpan("trace", "receiver", null, 0, 20);
        var records = Enumerable.Range(1, 1000).Select(i => span with { Sequence = i, SpanId = $"span-{i}" }).ToArray();
        records[997] = TraceSpan("trace", "missing", null, 997, 20);
        records[998] = records[998] with { Attributes = new Dictionary<string, JsonElement> { ["flow.component.address"] = JsonSerializer.SerializeToElement("other.receiver") } };
        records[999] = records[999] with { Attributes = new Dictionary<string, JsonElement> { ["flow.component.address"] = JsonSerializer.SerializeToElement("invalid") } };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<DesignerPage>();
            var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
            graph.Diagram.SelectModel(node, true);
            graph.Diagram.SetZoom(.65);
            var pan = graph.Diagram.Pan;
            var refreshes = 0;
            node.Changed += CountRefresh;
            void CountRefresh(Blazor.Diagrams.Core.Models.Base.Model _) => refreshes++;
            var start = run.StartAsync(graph);
            runner.Snapshot = new(runner.Id, "Running", [], Traces: new(records, 0));
            runner.Release.SetResult(runner.Snapshot);
            await start;
            node.ActivityVersion.ShouldBe(1000);
            refreshes.ShouldBe(1);
            view.ToHtmlString().ShouldContain("component-activity-glow");
            node.Selected.ShouldBeTrue();
            graph.Diagram.Zoom.ShouldBe(.65);
            graph.Diagram.Pan.ShouldBe(pan);
            graph.ToProjectJson().ShouldBe(configuration);
            graph.Revision.ShouldBe(revision);

            // Repeated snapshots and selection changes must not replay the retained buffer.
            graph.Diagram.UnselectModel(node);
            var previous = refreshes;
            var check = run.CheckAsync(graph);
            run.CancelCheck();
            runner.CheckRelease.SetResult(new(true, "Ready"));
            await check;
            refreshes.ShouldBe(previous);

            // Another busy poll still produces only one pulse, even after older spans are evicted.
            var next = records.Select(record => record with { Sequence = record.Sequence + 1000 }).ToArray();
            runner.Snapshot = runner.Snapshot with { Traces = new(next, 1000) };
            for (var attempt = 0; attempt < 100 && node.ActivityVersion != 2000; attempt++) await Task.Delay(50);
            node.ActivityVersion.ShouldBe(2000);
            refreshes.ShouldBe(previous + 1);

            // Mounting a view during a run must not replay retained historical spans.
            var mounted = new ComponentActivity(run);
            previous = refreshes;
            mounted.Update(graph, run);
            refreshes.ShouldBe(previous);

            var stop = run.StopAsync();
            node.ActivityVersion.ShouldBe(0);
            view.ToHtmlString().ShouldNotContain("component-activity-glow");
            runner.StopRelease.SetResult();
            await stop;
            run.Traces.Records.Count.ShouldBe(1000);
            node.ActivityVersion.ShouldBe(0);

            start = run.StartAsync(graph);
            runner.Snapshot = new(runner.Id, "Running", [], Traces: new([span], 0));
            runner.Release.SetResult(runner.Snapshot);
            await start;
            node.ActivityVersion.ShouldBe(1);
            graph.SelectWorkflow("other");
            node.ActivityVersion.ShouldBe(0);
            graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().ActivityVersion.ShouldBe(0);
            graph.SelectWorkflow("main");
            node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
            node.ActivityVersion.ShouldBe(0);
            graph.Diagram.SelectModel(node, true);
            graph.RenameSelectedNode("renamed");
            node.ActivityVersion.ShouldBe(0);
            run.RecentTraceComponents(graph, 0).ShouldBeNull();
            graph.Undo();
            run.RecentTraceComponents(graph, 0).ShouldBeNull();
            graph.Redo();
            graph.ExportConfiguration().ShouldContain("renamed");
            await run.StopAsync();
        });
    }

    private static FluxFlow.Environments.ExecutionTrace TraceSpan(string trace, string id, string? parent, int startMs, int durationMs) =>
        new(startMs + 1, "flow.process", "FluxFlow.Core", "1", trace, id, parent, null, 1, "Consumer",
            DateTimeOffset.UnixEpoch.AddMilliseconds(startMs), TimeSpan.FromMilliseconds(durationMs), "Unset", null,
            new Dictionary<string, JsonElement> { ["flow.component.address"] = JsonSerializer.SerializeToElement("main." + id) },
            [], [], new Dictionary<string, JsonElement> { ["service.name"] = JsonSerializer.SerializeToElement("FluxFlow.Execution") });

    private static FluxFlow.Environments.ExecutionLog CorrelatedLog(string trace, string span) =>
        new(1, DateTimeOffset.UnixEpoch, "Warning", "Original.Component", 0, null, "Original message", null)
        { Telemetry = new(DateTimeOffset.UnixEpoch, 13, "Warning", "Original message", trace, span, 1, [], []) };

    [Fact]
    public async Task Execution_logs_display_original_details_as_encoded_text()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var clipboard = new RecordingClipboard();
        services.AddSingleton<IJSRuntime>(clipboard);
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var timestamp = new DateTimeOffset(2026, 10, 27, 12, 34, 56, TimeSpan.FromHours(2));
        var record = new FluxFlow.Environments.ExecutionLog(1, timestamp, "Critical", "Original.Dependency", 42, "OriginalEvent",
            "Message <script>bad()</script>\nsecond line", "Outer exception\nInner exception\n   at Original.Stack()");
        record = record with { Telemetry = new(timestamp, 21, "Critical", "Message {Value}", "0123456789abcdef0123456789abcdef", "0123456789abcdef", 1,
            [new("Value", System.Text.Json.JsonSerializer.SerializeToElement("<script>attribute</script>")),
             new("connection.name", System.Text.Json.JsonSerializer.SerializeToElement("broker"))],
            [new("service.name", System.Text.Json.JsonSerializer.SerializeToElement("execution"))]) };
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionLogs>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Value"] = new FluxFlow.Environments.ExecutionLogSnapshot([record], 3),
              ["Diagnostics"] = new FluxFlow.Environments.ExecutionLogSnapshot([record with { Message = "Workflow started.", Category = "FluxFlow.Execution" }], 0) }));
            view.ToHtmlString().ShouldNotContain("Outer exception");
            await components.Items.OfType<ExecutionLogs>().Single().SelectRecord("Engine", 1, null);
            var html = view.ToHtmlString();
            html.ShouldContain("Critical");
            html.ShouldContain("Original.Dependency");
            html.ShouldContain("OriginalEvent");
            html.ShouldContain("42");
            html.ShouldContain(System.Text.Encodings.Web.HtmlEncoder.Default.Encode(timestamp.ToString("O")));
            html.ShouldContain("&lt;script&gt;bad()&lt;/script&gt;");
            html.ShouldNotContain("<script>");
            html.ShouldContain("second line");
            System.Net.WebUtility.HtmlDecode(html).ShouldContain(record.Exception!);
            html.ShouldContain("3 older engine records");
            html.ShouldContain("Engine");
            html.ShouldContain("Runner");
            html.ShouldContain("Workflow started.");
            html.ShouldContain("0123456789abcdef0123456789abcdef");
            html.ShouldContain("Severity number");
            html.ShouldContain("Message {Value}");
            html.ShouldContain("connection.name");
            html.ShouldContain("service.name");
            html.ShouldContain("&lt;script&gt;attribute&lt;/script&gt;");
            await components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Copy details").OnClick.InvokeAsync();
            clipboard.Texts.Single().ShouldContain("\n");
            JsonSerializer.Serialize(JsonSerializer.Deserialize<FluxFlow.Environments.ExecutionLog>(clipboard.Texts.Single())).ShouldBe(JsonSerializer.Serialize(record));
        });
    }

    [Fact]
    public async Task Execution_log_details_keep_source_identity_and_stop_selection_but_reject_stale_runs()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var clipboard = new RecordingClipboard();
        services.AddSingleton<IJSRuntime>(clipboard);
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var configuration = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var engine = CorrelatedLog("target", "child") with { Exception = "Engine exception" };
        var runner = engine with { Message = "Runner message", Exception = "Runner exception", Telemetry = null };
        var original = JsonSerializer.Serialize(new[] { engine, runner });
        var run = Guid.NewGuid();
        ParameterView Parameters(Guid id, bool live = true, string search = "", bool retained = true) => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["Value"] = new FluxFlow.Environments.ExecutionLogSnapshot(retained ? [engine] : [], retained ? 0 : 1),
            ["Diagnostics"] = new FluxFlow.Environments.ExecutionLogSnapshot([runner], 0),
            ["RunId"] = id, ["IsLive"] = live, ["SearchText"] = search
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionLogs>(Parameters(run));
            var logs = components.Items.OfType<ExecutionLogs>().Single();
            await logs.SelectRecord("Runner", 1, run);
            var copyRunner = components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Copy details").OnClick;
            await copyRunner.InvokeAsync();
            JsonSerializer.Serialize(JsonSerializer.Deserialize<FluxFlow.Environments.ExecutionLog>(clipboard.Texts.Single())).ShouldBe(JsonSerializer.Serialize(runner));
            view.ToHtmlString().ShouldContain("Runner exception");
            view.ToHtmlString().ShouldNotContain("Engine exception");
            view.ToHtmlString().ShouldContain("Jump to latest");
            await logs.SelectRecord("Engine", 1, run);
            await copyRunner.InvokeAsync();
            clipboard.Texts.Count.ShouldBe(1);
            view.ToHtmlString().ShouldContain("Engine exception");
            view.ToHtmlString().ShouldNotContain("Runner exception");
            await logs.SetParametersAsync(Parameters(run, false, "Runner message"));
            view.ToHtmlString().ShouldContain("Engine exception");
            view.ToHtmlString().ShouldContain("This record is hidden by the current filters.");
            logs.SearchText.ShouldBe("Runner message");
            var copyEngine = components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Copy details").OnClick;
            await copyEngine.InvokeAsync();
            JsonSerializer.Serialize(JsonSerializer.Deserialize<FluxFlow.Environments.ExecutionLog>(clipboard.Texts.Last())).ShouldBe(JsonSerializer.Serialize(engine));
            await logs.SetParametersAsync(Parameters(run, false, retained: false));
            await copyEngine.InvokeAsync();
            clipboard.Texts.Count.ShouldBe(2);
            view.ToHtmlString().ShouldNotContain("aria-label=\"Log details\"");
            await logs.SelectRecord("Engine", 1, run);
            view.ToHtmlString().ShouldNotContain("aria-label=\"Log details\"");
            await logs.SetParametersAsync(Parameters(run, false));
            await logs.SelectRecord("Engine", 1, run);
            copyEngine = components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Copy details").OnClick;
            var next = Guid.NewGuid();
            await logs.SetParametersAsync(Parameters(next));
            await copyEngine.InvokeAsync();
            clipboard.Texts.Count.ShouldBe(2);
            view.ToHtmlString().ShouldNotContain("aria-label=\"Log details\"");
            await logs.SelectRecord("Engine", 1, run);
            view.ToHtmlString().ShouldNotContain("aria-label=\"Log details\"");
            await logs.SelectRecord("Runner", 1, next);
            var close = components.Items.OfType<StudioIconButton>().Last(button => button.Label == "Close log details");
            await close.OnClick.InvokeAsync();
            view.ToHtmlString().ShouldNotContain("aria-label=\"Log details\"");
        });
        JsonSerializer.Serialize(new[] { engine, runner }).ShouldBe(original);
        graph.ToProjectJson().ShouldBe(configuration);
        graph.Revision.ShouldBe(revision);
    }

    [Fact]
    public async Task Execution_outputs_display_full_encoded_message_and_port()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var message = System.Text.Json.JsonSerializer.SerializeToElement(new { Value = "<script>bad()</script>", Headers = new { MiXeD = "retained" }, MessageId = "original-id" });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionOutputs>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Value"] = new FluxFlow.Environments.ExecutionOutputSnapshot([new(1, DateTimeOffset.UtcNow, "main.mapper.Output", message, null)], 2) }));
            var html = view.ToHtmlString();
            html.ShouldContain("main.mapper.Output");
            html.ShouldContain("original-id");
            html.ShouldContain("MiXeD");
            html.ShouldContain("retained");
            html.ShouldNotContain("<script>");
            html.ShouldContain("2 output samples");
            var grid = components.Items.OfType<FluxFlow.Studio.UI.Components.Grid.StudioDataGrid<FluxFlow.Environments.ExecutionOutput>>().Single();
            grid.ColumnResizeMode.ShouldBe(MudBlazor.ResizeMode.Column);
            grid.FilterMode.ShouldBe(MudBlazor.DataGridFilterMode.ColumnFilterMenu);
            grid.ReadOnly.ShouldBeTrue();
            grid.Filterable.ShouldBeTrue();
            var original = grid.Items!.Single();
            var filter = new MudBlazor.FilterDefinition<FluxFlow.Environments.ExecutionOutput>
            {
                Column = grid.RenderedColumns.Single(column => column.Title == "Output port"),
                Operator = MudBlazor.FilterOperator.String.Contains,
                Value = "missing"
            };
            await grid.AddFilterAsync(filter);
            grid.FilteredItems.ShouldBeEmpty();
            grid.Items!.Single().ShouldBeSameAs(original);
            await grid.ClearFiltersAsync();
            await grid.SetParametersAsync(ParameterView.Empty);
            grid.FilteredItems.Single().ShouldBeSameAs(original);
            await grid.AddFilterAsync(new MudBlazor.FilterDefinition<FluxFlow.Environments.ExecutionOutput>
            {
                Column = grid.RenderedColumns.Single(column => column.Title == "Message"),
                Operator = MudBlazor.FilterOperator.String.Contains,
                Value = "original-id"
            });
            grid.FilteredItems.Single().ShouldBeSameAs(original);
        });
    }

    [Fact]
    public async Task Environment_status_keeps_encoded_failure_after_cleanup_and_retry_uses_current_setup()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var run = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Environments.StudioRunSession>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.RunStatus>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Retry"] = EventCallback.Factory.Create(this, () => run.RetryAsync(graph)) }));
            var start = run.StartAsync(graph);
            view.ToHtmlString().ShouldContain("Preparing services and starting the workflow.");
            const string failure = "Broker failure <script>alert(1)</script>\nOriginal detail";
            runner.Release.SetResult(new(runner.Id, "Failed", [], failure));
            await start;
            await run.StopAsync();
            var html = view.ToHtmlString();
            html.ShouldContain("Cleanup completed");
            html.ShouldContain("Original detail");
            html.ShouldContain("&lt;script&gt;");
            html.ShouldNotContain("<script>alert(1)</script>");
            graph.SetEnvironment(new() { StartupTimeoutSeconds = 120 });
            var saved = graph.ToProjectJson();
            var retryButton = components.Items.OfType<MudBlazor.MudButton>().Last(button => button.StartIcon == MudBlazor.Icons.Material.Outlined.Refresh);
            var retry = retryButton.OnClick.InvokeAsync();
            runner.Request!.Environment.StartupTimeoutSeconds.ShouldBe(120);
            view.ToHtmlString().ShouldNotContain("Original detail");
            runner.Release.SetResult(new(runner.Id, "Running", [new("Resources:broker", "mosquitto", "mqtt://127.0.0.1:1883/", "trial-container")]));
            await retry;
            html = view.ToHtmlString();
            html.ShouldContain("mqtt://127.0.0.1:1883/");
            html.ShouldContain("trial-container");
            graph.ToProjectJson().ShouldBe(saved);
            await run.StopAsync();
        });
    }

    [Theory]
    [InlineData("mosquitto")]
    [InlineData("rabbitmq-mqtt")]
    public async Task Added_profile_user_stays_selected_after_autosave(string server)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var storage = new MemorySettings();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IStudioSettingsStorage>(storage);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var registry = new FluxFlow.Environments.ImageRegistry();
        var original = new FluxFlow.Environments.ContainerProfile { Provider = server, RegistryId = registry.Id, Settings = new()
        {
            Provider = server, Image = "test:latest", Files = new() { ["/keep.txt"] = "KeepCase" },
            BrokerAccess = new() { [server] = new() { Enabled = true, ClientUsername = "user-1", Users = [new() { Name = "user-1", Read = "keep/#" }] } }
        } };
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [registry], Profiles = [original] } });
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var before = graph.ToProjectJson();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.WriteDelay = completion.Task;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.EnvironmentPage>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["InitialSection"] = "Defaults" }));
            var adding = components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Add broker user").OnClick.InvokeAsync();
            preferences.IsSaving.ShouldBeTrue();
            preferences.Value.Environments.Profiles.Single().ShouldBe(original);
            // A parent can render the previous binding while storage is still saving.
            await components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Services.BrokerAccessEditor>().Single().SetParametersAsync(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = original.Settings, ["Disabled"] = true }));
            completion.SetResult();
            await adding;
            components.Items.OfType<MudBlazor.MudSelect<int>>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "User")).GetState(item => item.Value).ShouldBe(1);
            components.Items.OfType<MudBlazor.MudTextField<string>>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Username")).GetState(item => item.Value).ShouldBe("user-2");
            var saved = preferences.Value.Environments.Profiles.Single();
            saved.Settings.BrokerAccess[server].Users.Select(item => item.Name).ShouldBe(new[] { "user-1", "user-2" });
            saved.Settings.BrokerAccess[server].Users[0].ShouldBe(original.Settings.BrokerAccess[server].Users[0]);
            saved.Settings.BrokerAccess[server].ClientUsername.ShouldBe("user-1");
            saved.Settings.Files.ShouldBe(original.Settings.Files);
            var reopened = new StudioPreferences(storage);
            await reopened.LoadAsync();
            JsonSerializer.Serialize(reopened.Value.Environments.Profiles.Single()).ShouldBe(JsonSerializer.Serialize(saved));
            await components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Remove broker user").OnClick.InvokeAsync();
            JsonSerializer.Serialize(preferences.Value.Environments.Profiles.Single()).ShouldBe(JsonSerializer.Serialize(original));
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
        });
    }

    [Theory]
    [InlineData("mosquitto")]
    [InlineData("rabbitmq-mqtt")]
    public async Task Pending_user_add_does_not_select_a_same_named_user_after_binding_changes(string server)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var original = new FluxFlow.Environments.ResourceBinding
        {
            BrokerAccess = new() { [server] = new() { Users = [new() { Name = "user-1" }] } }
        };
        var replacement = new FluxFlow.Environments.ResourceBinding
        {
            BrokerAccess = new() { [server] = new() { Users = [new() { Name = "reader" }, new() { Name = "user-2" }] } }
        };
        var before = JsonSerializer.Serialize(replacement);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FluxFlow.Environments.ResourceBinding? submitted = null;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.BrokerAccessEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Service"] = server, ["Value"] = original,
                ["Fields"] = scope.ServiceProvider.GetServices<FluxFlow.Environments.EnvironmentProviderDescription>().Single(p => p.Name == server).Access,
                ["ValueChanged"] = EventCallback.Factory.Create<FluxFlow.Environments.ResourceBinding>(this, async value =>
                {
                    submitted = value;
                    await completion.Task;
                })
            }));
            var adding = components.Items.OfType<StudioIconButton>().Single(item => item.Label == "Add broker user").OnClick.InvokeAsync();
            submitted!.BrokerAccess[server].Users.Last().Name.ShouldBe("user-2");
            await components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Services.BrokerAccessEditor>().Single().SetParametersAsync(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = replacement }));
            completion.SetResult();
            await adding;
            components.Items.OfType<MudBlazor.MudSelect<int>>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "User")).GetState(item => item.Value).ShouldBe(0);
            components.Items.OfType<MudBlazor.MudTextField<string>>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Username")).GetState(item => item.Value).ShouldBe("reader");
            JsonSerializer.Serialize(replacement).ShouldBe(before);
            original.BrokerAccess[server].Users.ShouldHaveSingleItem();
        });
    }

    [Theory]
    [InlineData("mosquitto")]
    [InlineData("rabbitmq-mqtt")]
    public async Task Adding_and_removing_broker_users_configures_access_without_a_mode_switch(string server)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883"}},"Workflows":{"main":{}}}""");
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = new()
        {
            Provider = server, Files = new() { ["/keep.txt"] = "KeepCase" },
            BrokerAccess = new() { [server] = new() { CustomUsername = "original", CustomPassword = "keep" } }
        } } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var page = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ResourcePath"] = "Resources:broker" }));
            page.ToHtmlString().ShouldNotContain("Configure users visually");
            await components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Add broker user").OnClick.InvokeAsync();
            var access = graph.GetEnvironment().Resources["Resources:broker"].BrokerAccess[server];
            access.Enabled.ShouldBeTrue();
            access.ClientUsername.ShouldBe(access.Users.Single().Name);
            var added = graph.ToProjectJson();
            await components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Add broker user").OnClick.InvokeAsync();
            components.Items.OfType<MudBlazor.MudTextField<string>>().Last(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Username"))
                .GetState(item => item.Value).ShouldBe("user-2");
            graph.GetEnvironment().Resources["Resources:broker"].BrokerAccess[server].ClientUsername.ShouldBe("user-1");
            await components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Remove broker user").OnClick.InvokeAsync();
            graph.ToProjectJson().ShouldBe(added);
            graph.Undo();
            graph.Undo();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.ToProjectJson().ShouldBe(added);
            var delayedSelection = components.Items.OfType<MudBlazor.MudSelect<string>>().Last(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Workflow connection user")).ValueChanged;
            await components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Remove broker user").OnClick.InvokeAsync();
            var binding = graph.GetEnvironment().Resources["Resources:broker"];
            binding.BrokerAccess[server].Enabled.ShouldBeFalse();
            binding.BrokerAccess[server].Users.ShouldBeEmpty();
            binding.BrokerAccess[server].CustomUsername.ShouldBe("original");
            binding.BrokerAccess[server].CustomPassword.ShouldBe("keep");
            binding.Files["/keep.txt"].ShouldBe("KeepCase");
            var removed = graph.ToProjectJson();
            await delayedSelection.InvokeAsync(access.ClientUsername);
            graph.ToProjectJson().ShouldBe(removed);
            graph.Undo(); graph.ToProjectJson().ShouldBe(added);
            graph.Redo(); graph.ToProjectJson().ShouldBe(removed);
            graph.ExportConfiguration().ShouldBe(configuration);
        });
    }

    [Theory]
    [InlineData("mosquitto", "Subscribe topics", true)]
    [InlineData("rabbitmq-mqtt", "Subscribe pattern", true)]
    [InlineData("mosquitto", "Subscribe topics", false)]
    [InlineData("rabbitmq-mqtt", "Subscribe pattern", false)]
    public async Task Broker_configurators_preserve_snapshots_and_apply_user_edits_as_one_history_action(string server, string readLabel, bool enabled)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883"}},"Workflows":{"main":{}}}""");
        var registry = new FluxFlow.Environments.ImageRegistry();
        var preset = new FluxFlow.Environments.ContainerProfile { Provider = server, RegistryId = registry.Id, Settings = new()
        {
            Image = "test:1", Files = new() { ["/keep.txt"] = "KeepCase" }, BrokerAccess = new()
            {
                [server] = new() { Enabled = enabled, ClientUsername = "reader", Users =
                [new() { Name = "reader", Password = "test", Read = "original" }, new() { Name = "publisher", Password = "other", Write = "publish" }] }
            }
        } };
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = preset.CreateBinding([registry]) } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var page = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ResourcePath"] = "Resources:broker" }));
            graph.ToProjectJson().ShouldBe(before);
            if (!enabled) page.ToHtmlString().ShouldContain("Saved users are inactive");
            var field = components.Items.OfType<MudBlazor.MudTextField<string>>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), readLabel));
            field.GetState(item => item.Value).ShouldBe("original");
            var delayedEdit = field.ValueChanged;
            await components.Items.OfType<MudBlazor.MudSelect<int>>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "User")).ValueChanged.InvokeAsync(1);
            await delayedEdit.InvokeAsync("changed");
            var binding = graph.GetEnvironment().Resources["Resources:broker"];
            binding.BrokerAccess[server].Enabled.ShouldBeTrue();
            binding.BrokerAccess[server].Users[0].Read.ShouldBe("changed");
            binding.BrokerAccess[server].Users[1].ShouldBe(preset.Settings.BrokerAccess[server].Users[1]);
            binding.Profile!.Settings.BrokerAccess[server].Users[0].Read.ShouldBe("original");
            binding.Files["/keep.txt"].ShouldBe("KeepCase");
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.ExportConfiguration().ShouldBe(configuration);
            var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(after);
            reopened.GetEnvironment().Resources["Resources:broker"].BrokerAccess[server].Users[0].Read.ShouldBe("changed");
            var locked = binding with { Profile = binding.Profile with { SetupLocked = true, Settings = binding.CopySetup() } };
            graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = locked } });
            var lockedProject = graph.ToProjectJson();
            await field.ValueChanged.InvokeAsync("must not apply");
            await components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Add broker user").OnClick.InvokeAsync();
            await components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Remove broker user").OnClick.InvokeAsync();
            graph.ToProjectJson().ShouldBe(lockedProject);
        });
    }

    [Theory]
    [InlineData("mosquitto", "/mosquitto/config/acl", "Access control list", false)]
    [InlineData("rabbitmq-mqtt", "/etc/rabbitmq/definitions.json", "Definitions", false)]
    [InlineData("mosquitto", "/mosquitto/config/acl", "Access control list", true)]
    [InlineData("rabbitmq-mqtt", "/etc/rabbitmq/definitions.json", "Definitions", true)]
    public async Task Broker_file_overrides_can_be_reset_without_removing_users_unless_locked(string server, string path, string title, bool locked)
    {
        var services = CreateServices();
        var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883"}},"Workflows":{"main":{}}}""");
        var setup = new FluxFlow.Environments.ResourceBinding
        {
            Provider = server, Files = new() { [path] = "custom", ["/keep.txt"] = "KeepCase" },
            BrokerAccess = new() { [server] = new() { Enabled = true, ClientUsername = "reader", Users = [new() { Name = "reader", Password = "test" }] } }
        };
        setup = setup with { Profile = new("preset", "Broker", locked, setup.CopySetup()) };
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = setup } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ResourcePath"] = "Resources:broker" }));
            var reset = components.Items.OfType<StudioIconButton>().SingleOrDefault(item => item.Label == $"Reset {title}");
            if (locked)
            {
                reset.ShouldBeNull();
                graph.ToProjectJson().ShouldBe(before);
                graph.CanUndo.ShouldBeFalse();
                return;
            }
            reset.ShouldNotBeNull(); reset.Disabled.ShouldBeFalse();
            await reset.OnClick.InvokeAsync();
            var binding = graph.GetEnvironment().Resources["Resources:broker"];
            binding.Files.ContainsKey(path).ShouldBeFalse();
            binding.Files["/keep.txt"].ShouldBe("KeepCase");
            binding.Profile!.Settings.Files[path].ShouldBe("custom");
            binding.BrokerAccess[server].Enabled.ShouldBeTrue();
            binding.BrokerAccess[server].Users.Single().Name.ShouldBe("reader");
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.ExportConfiguration().ShouldBe(configuration);
        });
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, true, "acl")]
    [InlineData(false, true, false, "acl")]
    [InlineData(false, false, true, "passwords")]
    [InlineData(false, false, true, "mosquitto.conf")]
    [InlineData(false, false, true, "definitions.json")]
    [InlineData(false, false, false, "definitions.json")]
    public async Task Startup_file_editor_rejects_stale_or_generated_changes_and_preserves_cancel(bool stale, bool cancel, bool enableUsers, string filename = "rabbitmq.conf")
    {
        var services = CreateServices();
        var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dialogs = scope.ServiceProvider.GetRequiredService<MudBlazor.IDialogService>();
        var description = filename is "mosquitto.conf" or "acl" or "passwords" ? FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider : FluxFlow.Plugins.Mqtt.RabbitMqMqttSetup.Provider;
        var file = description.StartupFiles.Single(f => f.Path.EndsWith("/" + filename, StringComparison.Ordinal));
        var edited = file.Format == FluxFlow.Environments.ServiceDocumentKind.ObjectCollections ? """{ "vhosts":[] }""" : "mqtt.listeners.tcp.default = 1884\n";
        long revision = 1;
        FluxFlow.Environments.ResourceBinding? changed = null;
        var original = new FluxFlow.Environments.ResourceBinding { Provider = description.Name, Files = new() { ["/keep"] = "unchanged" } };
        dialogs.DialogInstanceAddedAsync += async reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            if (stale) revision++;
            if (enableUsers)
            {
                var editor = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Services.StartupFileEditor>().Single();
                await editor.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                    { ["Value"] = original with { BrokerAccess = new() { [description.Name] = new() { Enabled = true } } } }));
            }
            reference.Dismiss(cancel ? MudBlazor.DialogResult.Cancel() : MudBlazor.DialogResult.Ok(edited));
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.StartupFileEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Value"] = original, ["Schema"] = file,
                ["CurrentRevision"] = (Func<long>)(() => revision),
                ["ValueChanged"] = EventCallback.Factory.Create<FluxFlow.Environments.ResourceBinding>(this, value => changed = value)
            }));
            await components.Items.OfType<MudBlazor.MudButton>().Single().OnClick.InvokeAsync(new MouseEventArgs());
            original.Files.Count.ShouldBe(1);
            if (stale || cancel || enableUsers) changed.ShouldBeNull();
            else
            {
                changed!.Files[file.Path].ShouldBe(edited);
                changed.Files["/keep"].ShouldBe("unchanged");
            }
        });
    }

    [Theory]
    [InlineData("eclipse-mosquitto:latest", "rabbitmq:latest")]
    [InlineData("eclipse-mosquitto:2.0.22", "eclipse-mosquitto:2.0.22")]
    [InlineData("eclipse-mosquitto", "eclipse-mosquitto")]
    [InlineData("eclipse-mosquitto:Latest", "eclipse-mosquitto:Latest")]
    [InlineData("eclipse-mosquitto@sha256:abc", "eclipse-mosquitto@sha256:abc")]
    [InlineData("team/custom@sha256:abc", "team/custom@sha256:abc")]
    public async Task Mqtt_server_selection_preserves_profiles_and_applies_a_separate_undoable_snapshot(string image, string expectedImage)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var registry = new FluxFlow.Environments.ImageRegistry();
        var original = new FluxFlow.Environments.ContainerProfile { Name = "Broker", Provider = "mosquitto", RegistryId = registry.Id,
            DefaultForResourceType = "mqtt.client", SetupLocked = true, Settings = new() { Provider = "mosquitto", Image = image,
                PullIfMissing = false, Files = new() { ["/keep.txt"] = "KeepCase" }, Variables = new() { ["Exact"] = "Value" },
                BrokerAccess = new() { ["mosquitto"] = new() { CustomPassword = " original " }, ["rabbitmq-mqtt"] = new() { CustomPassword = " separate " } } } };
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [registry], Profiles = [original] } });
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"KeepCase":42,"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883"}},"Workflows":{"main":{}}}""");
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = original.CreateBinding([registry]) } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.ResourceDefaultsSettings>();
            var type = components.Items.OfType<MudBlazor.MudSelect<string>>().Single(item => item.Label == "Connection type");
            type.Value.ShouldBe("mqtt.client");
            var server = components.Items.OfType<MudBlazor.MudSelect<string>>().Single(item => item.Label == "Server implementation");
            server.Value.ShouldBe("mosquitto");
            var initialSelection = server.ValueChanged;
            var initialPreferences = preferences.Value;
            await initialSelection.InvokeAsync("mosquitto");
            preferences.Value.ShouldBeSameAs(initialPreferences);
            await server.ValueChanged.InvokeAsync("wiremock");
            preferences.Value.Environments.Profiles.Single().ShouldBe(original);
            await server.ValueChanged.InvokeAsync("rabbitmq-mqtt");
            await preferences.LoadAsync();
            var changed = preferences.Value.Environments.Profiles.Single();
            JsonSerializer.Serialize(changed).ShouldBe(JsonSerializer.Serialize(original with { Provider = "rabbitmq-mqtt",
                Settings = original.Settings with { Provider = "rabbitmq-mqtt", Image = expectedImage } }));
            type.Value.ShouldBe("mqtt.client");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["ResourcePath"] = "Resources:broker" }));
            await components.Items.OfType<MudBlazor.MudSelect<string>>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Run using")).ValueChanged.InvokeAsync("rabbitmq-mqtt");
            var applied = graph.ToProjectJson();
            var binding = graph.GetEnvironment().Resources["Resources:broker"];
            binding.Provider.ShouldBe("rabbitmq-mqtt");
            binding.Image.ShouldBe($"docker.io/{expectedImage}");
            binding.Profile!.SetupLocked.ShouldBeTrue();
            binding.Files.ShouldBe(original.Settings.Files);
            binding.Variables.ShouldBe(original.Settings.Variables);
            binding.PullIfMissing.ShouldBeFalse();
            await type.ValueChanged.InvokeAsync("http.client");
            var httpProfile = preferences.Value.Environments.Profiles.Single();
            httpProfile.Provider.ShouldBe("wiremock");
            httpProfile.DefaultForResourceType.ShouldBeNull();
            httpProfile.Settings.Files.ShouldBe(original.Settings.Files);
            httpProfile.Settings.Variables.ShouldBe(original.Settings.Variables);
            await initialSelection.InvokeAsync("rabbitmq-mqtt");
            await server.ValueChanged.InvokeAsync("rabbitmq-mqtt");
            preferences.Value.Environments.Profiles.Single().ShouldBe(httpProfile);
            graph.ToProjectJson().ShouldBe(applied);
            await preferences.SaveAsync(preferences.Value with { Environments = preferences.Value.Environments with { Profiles = [original] } });
            graph.ToProjectJson().ShouldBe(applied);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(applied);
            graph.ExportConfiguration().ShouldBe(configuration);
        });
    }

    [Fact]
    public async Task Failed_server_selection_can_be_retried_and_old_default_actions_cannot_reassign_a_profile()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var storage = (MemorySettings)scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>();
        var registry = new FluxFlow.Environments.ImageRegistry();
        var editedProfile = FluxFlow.Environments.ContainerProfileServers.Create(FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider, registry);
        var mqttDefault = editedProfile with { Id = "default", DefaultForResourceType = "mqtt.client" };
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [registry], Profiles = [editedProfile, mqttDefault] } });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.ResourceDefaultsSettings>();
            var type = components.Items.OfType<MudBlazor.MudSelect<string>>().Single(item => item.Label == "Connection type");
            var oldSetDefault = components.Items.OfType<MudBlazor.MudButton>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Set default profile")).OnClick;
            // Another settings view clears the current owner before this button is pressed.
            var initial = preferences.Value;
            await preferences.SaveAsync(initial with { Environments = initial.Environments.ClearDefault(mqttDefault.Id, FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider) });
            var externallyChanged = preferences.Value;
            await oldSetDefault.InvokeAsync(new MouseEventArgs());
            preferences.Value.ShouldBeSameAs(externallyChanged);
            // Reloading while this page stays open must refresh the button and invalidate its old callback.
            var assigned = externallyChanged.Environments.SetDefault(editedProfile.Id, FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider);
            await preferences.SaveAsync(externallyChanged with { Environments = assigned });
            await preferences.LoadAsync();
            view.ToHtmlString().ShouldContain("Clear default profile");
            var oldClear = components.Items.OfType<MudBlazor.MudButton>().Last(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Clear default profile")).OnClick;
            await preferences.SaveAsync(initial);
            view.ToHtmlString().ShouldContain("Set default profile");
            await preferences.SaveAsync(initial with { Environments = assigned });
            await oldClear.InvokeAsync(new MouseEventArgs());
            preferences.Value.Environments.ShouldBeSameAs(assigned);
            await preferences.SaveAsync(initial);
            storage.WriteDelay = Task.FromException(new IOException("Could not write preferences"));
            await type.ValueChanged.InvokeAsync("http.client");
            view.ToHtmlString().ShouldContain("Could not write preferences");
            type.Value.ShouldBe("mqtt.client");
            preferences.Value.Environments.Profiles[0].ShouldBe(editedProfile);
            storage.WriteDelay = Task.CompletedTask;
            await type.ValueChanged.InvokeAsync("http.client");
            type.Value.ShouldBe("http.client");
            await oldSetDefault.InvokeAsync(new MouseEventArgs());
            await preferences.LoadAsync();
            var saved = preferences.Value.Environments.Profiles;
            saved[0].Provider.ShouldBe("wiremock");
            saved[0].DefaultForResourceType.ShouldBeNull();
            saved[1].DefaultForResourceType.ShouldBe("mqtt.client");
        });
    }

    [Fact]
    public async Task Profiles_require_a_registry_and_the_empty_state_opens_registry_setup()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 61 });
        graph.Undo();
        var before = graph.ToProjectJson();
        var opened = false;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.ResourceDefaultsSettings>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["OpenRegistries"] = EventCallback.Factory.Create(this, () => opened = true) }));
            var add = components.Items.OfType<MudBlazor.MudMenu>().Single(item => item.Label == "Add profile");
            add.Disabled.ShouldBeTrue();
            await renderer.RenderComponentAsync<MudBlazor.MudPaper>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["ChildContent"] = add.ChildContent }));
            await components.Items.OfType<MudBlazor.MudMenuItem>().First().OnClick.InvokeAsync(new MouseEventArgs());
            preferences.Value.Environments.Profiles.ShouldBeEmpty();
            await components.Items.OfType<MudBlazor.MudButton>().Single(item => item.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Configure registries")).OnClick.InvokeAsync(new MouseEventArgs());
            opened.ShouldBeTrue();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanRedo.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task Loading_legacy_images_creates_explicit_registries_without_changing_customization()
    {
        var storage = new MemorySettings();
        var json = """
            {"Theme":"Dark","Environments":{"Registries":[],
              "Profiles":[
                {"Id":"hub","Name":"Hub mock","Provider":"wiremock","SetupLocked":true,
                 "Settings":{"Image":"team/mock:1","Mappings":"[]","Variables":{"Exact":"Value"}}},
                {"Id":"private","Name":"Private mock","Settings":{"Image":"private.example:5000/team/mock@sha256:abc"}}
              ]}}
            """;
        var settingsJson = JsonSerializer.SerializeToNode(new StudioSettings(), StudioJsonContext.Default.StudioSettings)!;
        settingsJson["Environments"] = System.Text.Json.Nodes.JsonNode.Parse(json)!["Environments"]!.DeepClone();
        json = settingsJson.ToJsonString();
        await storage.WriteAsync(json);
        var preferences = new StudioPreferences(storage);
        await preferences.LoadAsync();
        var defaults = preferences.Value.Environments;
        defaults.Registries.Select(item => item.Name).ShouldContain("Docker Hub");
        defaults.Profiles[0].ResolveImage(defaults.Registries).ShouldBe("docker.io/team/mock:1");
        defaults.Profiles[0].Settings.Variables["Exact"].ShouldBe("Value");
        defaults.Profiles[0].SetupLocked.ShouldBeTrue();
        defaults.Profiles[1].ResolveImage(defaults.Registries).ShouldBe("private.example:5000/team/mock@sha256:abc");
        (await storage.ReadAsync()).ShouldBe(json);
        await preferences.SaveAsync(preferences.Value);
        var saved = await storage.ReadAsync();
        await preferences.LoadAsync();
        (await storage.ReadAsync()).ShouldBe(saved);
        preferences.Value.Environments.Profiles.Select(item => item.RegistryId).ShouldBe(defaults.Profiles.Select(item => item.RegistryId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unavailable")]
    [InlineData("unavailable", "http.client")]
    [InlineData("unavailable", "mqtt.client")]
    public async Task Existing_profiles_keep_their_setup_until_a_supported_connection_type_is_selected(string? setup, string? savedDefault = null)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var registry = new FluxFlow.Environments.ImageRegistry();
        var original = new FluxFlow.Environments.ContainerProfile { Provider = setup, RegistryId = registry.Id, SetupLocked = true, DefaultForResourceType = savedDefault,
            Settings = new() { Image = "team/custom:7", Mappings = "[]", Files = new() { ["/keep.txt"] = "KeepCase" } } };
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [registry], Profiles = [original] } });
        var saved = await scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>().ReadAsync();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 61 });
        graph.Undo();
        var project = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.ResourceDefaultsSettings>();
            view.ToHtmlString().ShouldContain("Choose a supported connection type");
            view.ToHtmlString().ShouldNotContain("None (image only)");
            var select = components.Items.OfType<MudBlazor.MudSelect<string>>().Single(item => item.Label == "Connection type");
            await select.ValueChanged.InvokeAsync("");
            await select.ValueChanged.InvokeAsync("unavailable");
            (await scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>().ReadAsync()).ShouldBe(saved);
            await select.ValueChanged.InvokeAsync("http.client");
            JsonSerializer.Serialize(preferences.Value.Environments.Profiles.Single()).ShouldBe(
                JsonSerializer.Serialize(original with { Provider = "wiremock", DefaultForResourceType = savedDefault == "http.client" ? savedDefault : null,
                    Settings = original.Settings with { Provider = "wiremock" } }));
            graph.ToProjectJson().ShouldBe(project);
            graph.CanRedo.ShouldBeTrue();
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Visual_endpoint_dialog_preserves_no_op_and_supports_add_remove_and_read_only(bool readOnly, bool edit)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudBlazor.MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<MudBlazor.IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            const string original = """{ "Request": {"Path":"/original"}, "Response": {"BodyAsJson":{"KeepCase":42}}, "Priority":7 }""";
            var dialog = await dialogs.ShowAsync<FluxFlow.Studio.UI.Features.Environments.Services.Documents.ServiceItemsDialog>("Endpoints", new MudBlazor.DialogParameters<FluxFlow.Studio.UI.Features.Environments.Services.Documents.ServiceItemsDialog>
            { { d => d.Schema, FluxFlow.Plugins.Http.WireMock.WireMockMappings.Provider.Document! }, { d => d.Value, original }, { d => d.ReadOnly, readOnly } });
            view.ToHtmlString().ShouldContain("/original");
            if (readOnly)
            {
                components.Items.OfType<StudioIconButton>().Any(b => b.Label == "Add endpoint").ShouldBeFalse();
                components.Items.OfType<MudBlazor.MudButton>().Any(b => b.Variant == MudBlazor.Variant.Filled).ShouldBeFalse();
                components.Items.OfType<MudBlazor.MudTextField<string>>().Single(f => f.Label == "Path").ReadOnly.ShouldBeTrue();
                dialog.Close(MudBlazor.DialogResult.Cancel());
                return;
            }
            if (edit)
            {
                await components.Items.OfType<MudBlazor.MudTextField<string>>().Last(f => f.Label == "Path").ValueChanged.InvokeAsync("/edited");
                await components.Items.OfType<StudioIconButton>().Last(b => b.Label == "Add endpoint").OnClick.InvokeAsync();
                view.ToHtmlString().ShouldContain("/edited");
                await components.Items.OfType<StudioIconButton>().Last(b => b.Label == "Remove endpoint").OnClick.InvokeAsync();
            }
            await components.Items.OfType<MudBlazor.MudButton>().Single(b => b.Variant == MudBlazor.Variant.Filled).OnClick.InvokeAsync(new MouseEventArgs());
            var result = (await dialog.Result)!.Data.ShouldBeOfType<string>();
            if (!edit) result.ShouldBe(original);
            else
            {
                var mapping = System.Text.Json.Nodes.JsonNode.Parse(result)!;
                mapping.ShouldBeOfType<System.Text.Json.Nodes.JsonObject>();
                mapping["Request"]!["Path"]!.GetValue<string>().ShouldBe("/edited");
                mapping["Priority"]!.GetValue<int>().ShouldBe(7);
                mapping["Response"]!["BodyAsJson"]!["KeepCase"]!.GetValue<int>().ShouldBe(42);
            }
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Shared_WireMock_editor_preserves_profile_or_project_ownership_and_cancel(bool defaults, bool cancel)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"KeepCase":42,"Resources":{"api":{"Type":"http.client","BaseAddress":"http://original/"}},"Workflows":{"main":{}}}""");
        var registry = new FluxFlow.Environments.ImageRegistry { Name = "Docker Hub" };
        var preset = new FluxFlow.Environments.ContainerProfile { Id = "p2", Name = "P2", RegistryId = registry.Id, Provider = "wiremock", Settings = new() { Image = "mock:1" } };
        graph.SetEnvironment(new() { Resources = new() { ["Resources:api"] = preset.CreateBinding([registry]) } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [registry], Profiles = [preset] } });
        const string edited = """[{"Request":{"Path":"/P2"},"Response":{"BodyAsJson":{"KeepCase":42}}}]""";
        var dialogs = scope.ServiceProvider.GetRequiredService<MudBlazor.IDialogService>();
        dialogs.DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(cancel ? MudBlazor.DialogResult.Cancel() : MudBlazor.DialogResult.Ok(edited));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            if (defaults) await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.ResourceDefaultsSettings>();
            else await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ResourcePath"] = "Resources:api" }));
            components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Services.Documents.ServiceItemEditor>().Single().Value.ShouldBe("[]");
            await components.Items.OfType<MudBlazor.MudButton>().Single(item => item.StartIcon == MudBlazor.Icons.Material.Outlined.Code).OnClick.InvokeAsync(new MouseEventArgs());
            var binding = graph.GetEnvironment().Resources["Resources:api"];
            binding.Profile!.Settings.Mappings.ShouldBe("[]");
            if (defaults)
            {
                preferences.Value.Environments.Profiles.Single().Settings.Mappings.ShouldBe(cancel ? "[]" : edited);
                await components.Items.OfType<StudioIconButton>().Single(item => item.Label == "Lock project setup").OnClick.InvokeAsync();
                await preferences.LoadAsync();
                preferences.Value.Environments.Profiles.Single().SetupLocked.ShouldBeTrue();
                components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Services.Documents.ServiceItemEditor>().Single().ReadOnly.ShouldBeFalse();
                graph.ToProjectJson().ShouldBe(before);
                graph.CanUndo.ShouldBeFalse();
            }
            else
            {
                preferences.Value.Environments.Profiles.Single().Settings.Mappings.ShouldBe("[]");
                binding.Mappings.ShouldBe(cancel ? "[]" : edited);
                graph.CanUndo.ShouldBe(!cancel);
                var after = graph.ToProjectJson();
                if (!cancel)
                {
                    graph.Undo();
                    graph.ToProjectJson().ShouldBe(before);
                    graph.CanUndo.ShouldBeFalse();
                    graph.Redo();
                    graph.ToProjectJson().ShouldBe(after);
                }
            }
            graph.ExportConfiguration().ShouldBe(configuration);
        });
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("cancel")]
    [InlineData("invalid")]
    [InlineData("stale")]
    public async Task Mapping_import_preserves_pending_edits_and_validates_before_applying(string outcome)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var files = new MappingFiles();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IStudioFileAccess>(files);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client","BaseAddress":"http://original/"}},"Workflows":{"main":{}}}""");
        graph.SetEnvironment(new() { Resources = new() { ["Resources:api"] = new() { Provider = "wiremock", Image = "mock:1" } } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ResourcePath"] = "Resources:api" }));
            var pending = components.Items.OfType<MudBlazor.MudButton>().Single(item => item.StartIcon == MudBlazor.Icons.Material.Outlined.UploadFile).OnClick.InvokeAsync(new MouseEventArgs());
            if (outcome == "stale") graph.SetEnvironment(graph.GetEnvironment() with { StartupTimeoutSeconds = 75 });
            var current = graph.ToProjectJson();
            const string mapping = """{"Request":{"Path":"/test"},"Response":{"StatusCode":201}}""";
            files.Completion.SetResult(outcome == "cancel" ? null : new("mappings.json", outcome == "invalid" ? "{}" : mapping));
            await pending;
            if (outcome == "apply")
            {
                graph.GetEnvironment().Resources["Resources:api"].Mappings.ShouldBe(mapping);
                var applied = graph.ToProjectJson();
                graph.Undo();
                graph.ToProjectJson().ShouldBe(before);
                graph.CanUndo.ShouldBeFalse();
                graph.Redo();
                graph.ToProjectJson().ShouldBe(applied);
            }
            else graph.ToProjectJson().ShouldBe(current);
        });
    }

    private sealed class MappingFiles : IStudioFileAccess
    {
        public TaskCompletionSource<StudioFile?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<StudioImageFile?> OpenImageAsync() => throw new InvalidOperationException();
        public Task<StudioFile?> OpenAsync(string extension) { extension.ShouldBe(".json"); return Completion.Task; }
        public Task<StudioFile?> SaveAsync(StudioFile file, bool saveAs = false) => throw new InvalidOperationException();
    }

    [Fact]
    public async Task Service_setup_shows_connection_usages_and_navigation_preserves_history()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883"}},"Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","Connection":"broker"}}}}""");
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 61 });
        graph.Undo();
        var before = graph.ToProjectJson();
        string? selected = null;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ResourcePath"] = "Resources:broker",
                ["OpenConnection"] = EventCallback.Factory.Create<string>(this, value => selected = value)
            }));
            view.ToHtmlString().ShouldContain("main.receive");
            view.ToHtmlString().ShouldContain("MQTT");
            view.ToHtmlString().ShouldContain("mqtt://original:1883");
            view.ToHtmlString().ShouldNotContain("Copy from profile");
            await components.Items.OfType<StudioIconButton>().Single(item => item.Label == "Edit connection").OnClick.InvokeAsync();
            selected.ShouldBe("Resources:broker");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanRedo.ShouldBeTrue();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Service_setup_rejects_stale_advanced_actions_after_applying_a_locked_profile(bool variables)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client"}},"Workflows":{"main":{}}}""");
        var binding = new FluxFlow.Environments.ResourceBinding { Provider = "wiremock", Image = "test/http:1" };
        graph.SetEnvironment(new() { Resources = new() { ["Resources:api"] = binding } });
        graph.ResetHistory();
        var original = graph.ToProjectJson();
        var dialogsOpened = 0;
        var dialogs = scope.ServiceProvider.GetRequiredService<MudBlazor.IDialogService>();
        dialogs.DialogInstanceAddedAsync += reference =>
        {
            dialogsOpened++;
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(MudBlazor.DialogResult.Ok("""{"Changed":"value"}"""));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ResourcePath"] = "Resources:api" }));
            var staleAction = components.Items.OfType<MudBlazor.MudButton>().Single(button => button.StartIcon ==
                (variables ? MudBlazor.Icons.Material.Outlined.DataObject : MudBlazor.Icons.Material.Outlined.Description)).OnClick;
            graph.SetEnvironment(new() { Resources = new() { ["Resources:api"] = binding with
                { Profile = new("locked", "Locked service", true, binding.CopySetup()) } } });
            var lockedProject = graph.ToProjectJson();
            await staleAction.InvokeAsync(new MouseEventArgs());
            graph.GetEnvironment().Resources["Resources:api"].Profile.ShouldNotBeNull().SetupLocked.ShouldBeTrue();
            graph.ToProjectJson().ShouldBe(lockedProject);
            dialogsOpened.ShouldBe(0);
            graph.Undo(); graph.ToProjectJson().ShouldBe(original);
            graph.Redo(); graph.ToProjectJson().ShouldBe(lockedProject);
        });
    }

    [Fact]
    public async Task Shared_image_editor_preserves_profile_settings_and_resolves_registry_changes_without_project_edits()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 61 });
        graph.Undo();
        var before = graph.ToProjectJson();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var registry = new FluxFlow.Environments.ImageRegistry { Id = "private", Address = "registry.example:5000", Name = "Private" };
        var otherRegistry = new FluxFlow.Environments.ImageRegistry { Id = "mirror", Address = "mirror.example", Name = "Mirror" };
        var profile = new FluxFlow.Environments.ContainerProfile { Id = "custom", Name = "Custom", RegistryId = registry.Id, Provider = "wiremock", Settings = new()
        {
            Provider = "wiremock", Image = "team/mock:1", Mappings = "[]",
            Files = new() { ["/test.txt"] = "KeepCase" }, Variables = new() { ["ExactName"] = "ExactValue" }
        } };
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [registry, otherRegistry], Profiles = [profile] } });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.ResourceDefaultsSettings>();
            var imageField = components.Items.OfType<MudBlazor.MudTextField<string>>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Image name"));
            await imageField.ValueChanged.InvokeAsync(null);
            preferences.Value.Environments.Profiles.Single().ShouldBe(profile);
            view.ToHtmlString().ShouldContain("Enter an image name and tag or digest");
            await imageField.ValueChanged.InvokeAsync("team/mock");
            var registrySelect = components.Items.OfType<MudBlazor.MudSelect<string>>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Registry"));
            await registrySelect.ValueChanged.InvokeAsync(registry.Id);
            var saved = preferences.Value.Environments.Profiles.Single();
            saved.RegistryId.ShouldBe(registry.Id);
            saved.Settings.Image.ShouldBe("team/mock:1");
            saved.ResolveImage(preferences.Value.Environments.Registries).ShouldBe("registry.example:5000/team/mock:1");
            await registrySelect.ValueChanged.InvokeAsync(otherRegistry.Id);
            preferences.Value.Environments.Profiles.Single().Settings.Image.ShouldBe("team/mock:1");
            preferences.Value.Environments.Profiles.Single().ResolveImage(preferences.Value.Environments.Registries).ShouldBe("mirror.example/team/mock:1");
            await registrySelect.ValueChanged.InvokeAsync(registry.Id);
            await components.Items.OfType<MudBlazor.MudTextField<string>>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Tag")).ValueChanged.InvokeAsync(" 2 ");
            await registrySelect.ValueChanged.InvokeAsync("");
            saved = preferences.Value.Environments.Profiles.Single();
            saved.RegistryId.ShouldBe(registry.Id);
            saved.Settings.Image.ShouldBe("team/mock:2");
            saved.Settings.Mappings.ShouldBe(profile.Settings.Mappings);
            saved.Settings.Files.ShouldBe(profile.Settings.Files);
            saved.Settings.Variables.ShouldBe(profile.Settings.Variables);
            await preferences.LoadAsync();
            preferences.Value.Environments.Profiles.Single().Settings.Image.ShouldBe(saved.Settings.Image);
            graph.ToProjectJson().ShouldBe(before);
            graph.CanRedo.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task Connections_show_the_definition_and_usages_and_open_project_setup_without_editing()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883","ClientId":"keep"}},"Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","Connection":"broker"}}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        string? selected = null;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Connections.ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Selection"] = "Resources:broker",
                ["Setup"] = EventCallback.Factory.Create<string>(this, value => selected = value)
            }));
            view.ToHtmlString().ShouldContain("main.receive");
            view.ToHtmlString().ShouldContain("mqtt://original:1883");
            var setup = components.Items.OfType<MudBlazor.MudButton>().Single(item => item.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == "Environment setup");
            await setup.OnClick.InvokeAsync(new MouseEventArgs());
            selected.ShouldBe("Resources:broker");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            var endpoint = components.Items.OfType<MudBlazor.MudTextField<string>>().Last(item => item.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == "Address");
            await endpoint.ValueChanged.InvokeAsync("relative/path");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            endpoint = components.Items.OfType<MudBlazor.MudTextField<string>>().Last(item => item.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == "Address");
            await endpoint.ValueChanged.InvokeAsync("mqtt://original:1883");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            await endpoint.ValueChanged.InvokeAsync("mqtt://updated:1883");
            graph.Connections.Single().Settings["Address"]!.GetValue<string>().ShouldBe("mqtt://updated:1883");
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            graph.Redo();
            graph.Connections.Single().Settings["ClientId"]!.GetValue<string>().ShouldBe("keep");
            graph.Connections.Single().Settings["Address"]!.GetValue<string>().ShouldBe("mqtt://updated:1883");
        });
    }
    [Fact]
    public async Task Profile_edits_keep_invalid_drafts_local_and_preserve_the_default_on_reopen()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var mqtt = FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider;
        var defaults = new FluxFlow.Environments.EnvironmentDefaults().AddRegistry().AddProfile(mqtt);
        defaults = defaults.SetDefault(defaults.Profiles.Single().Id, mqtt);
        await preferences.SaveAsync(preferences.Value with { Environments = defaults });
        var initial = defaults.Profiles.Single();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.ResourceDefaultsSettings>();
            MudBlazor.MudTextField<string> Field(string label) => components.Items.OfType<MudBlazor.MudTextField<string>>().Last(item => item.Label == label);
            var staleDescription = Field("Description (optional)").ValueChanged;
            await Field("Name").ValueChanged.InvokeAsync("");
            await staleDescription.InvokeAsync("Old edit");
            Field("Name").Value.ShouldBe("");
            view.ToHtmlString().ShouldContain("A profile name and settings are required.");
            preferences.Value.Environments.ShouldBeSameAs(defaults);
            await Field("Name").ValueChanged.InvokeAsync(initial.Name);
            view.ToHtmlString().ShouldNotContain("A profile name and settings are required.");
            preferences.Value.Environments.ShouldBeSameAs(defaults);

            await Field("Name").ValueChanged.InvokeAsync("Lab MQTT");
            var saved = preferences.Value;
            var nameBeforeReload = Field("Name").ValueChanged;
            await nameBeforeReload.InvokeAsync("Lab MQTT");
            preferences.Value.ShouldBeSameAs(saved);
            await preferences.LoadAsync();
            await nameBeforeReload.InvokeAsync("Stale");
            var reopened = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.ResourceDefaultsSettings>();
            reopened.ToHtmlString().ShouldContain("Select profile Lab MQTT");
            JsonSerializer.Serialize(preferences.Value.Environments.Profiles.Single()).ShouldBe(JsonSerializer.Serialize(initial with { Name = "Lab MQTT" }));
        });
    }

    [Fact]
    public async Task Profile_default_transfer_and_removal_preserve_other_settings_and_project_history()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var registry = new FluxFlow.Environments.ImageRegistry { Name = "Docker Hub" };
        var original = new FluxFlow.Environments.EnvironmentDefaults { Registries = [registry], Profiles = [
            new() { Id = "mosquitto", Name = "Mosquitto", RegistryId = registry.Id, Provider = "mosquitto", DefaultForResourceType = "mqtt.client", Settings = new() { Image = "eclipse-mosquitto:2" } },
            new() { Id = "wiremock", Name = "WireMock", RegistryId = registry.Id, Provider = "wiremock", DefaultForResourceType = "http.client", Settings = new() { Image = "mock:1" } }
        ] };
        await preferences.SaveAsync(preferences.Value with { Environments = original });
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.AddNode(graph.FindDescription("scheduler")!);
        graph.Undo();
        var beforeEdit = graph.ToProjectJson();
        graph.Redo();
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.ResourceDefaultsSettings>();
            view.ToHtmlString().ShouldNotContain("Use image address directly");
            var oldRemove = components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Remove profile").OnClick;
            var oldLock = components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Lock project setup").OnClick;
            var add = components.Items.OfType<MudBlazor.MudMenu>().Single(item => item.Label == "Add profile");
            await renderer.RenderComponentAsync<MudBlazor.MudPaper>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["ChildContent"] = add.ChildContent }));
            var addMqtt = components.Items.OfType<MudBlazor.MudMenuItem>().Single(item => item.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Add MQTT profile")).OnClick;
            await addMqtt.InvokeAsync(new MouseEventArgs());
            var added = preferences.Value.Environments.Profiles.Last();
            var afterAdd = preferences.Value;
            await oldRemove.InvokeAsync();
            await addMqtt.InvokeAsync(new MouseEventArgs());
            await oldLock.InvokeAsync();
            preferences.Value.ShouldBeSameAs(afterAdd);
            afterAdd.Environments.Profiles.Count.ShouldBe(3);
            afterAdd.Environments.Profiles.Single(item => item.DefaultForResourceType == "mqtt.client").Id.ShouldBe("mosquitto");
            added.Provider.ShouldBe("mosquitto");
            added.Settings.Provider.ShouldBe("mosquitto");
            added.Settings.Image.ShouldBe("eclipse-mosquitto:latest");
            view.ToHtmlString().ShouldContain("<h2>New profile</h2>");
            view.ToHtmlString().ShouldNotContain("None (image only)");
            await components.Items.OfType<MudBlazor.MudSelect<string>>().Last(item => item.Label == "Connection type").ValueChanged.InvokeAsync("mqtt.client");
            await components.Items.OfType<MudBlazor.MudButton>().Last(item => item.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Set default profile")).OnClick.InvokeAsync(new MouseEventArgs());
            var connectionType = components.Items.OfType<MudBlazor.MudSelect<string>>().Last(item => item.Label == "Connection type");
            await connectionType.ValueChanged.InvokeAsync("");
            await connectionType.ValueChanged.InvokeAsync("unavailable");
            await connectionType.ValueChanged.InvokeAsync("mqtt.client");
            preferences.Value.Environments.Profiles.Single(item => item.DefaultForResourceType == "mqtt.client").Id.ShouldBe(added.Id);
            await components.Items.OfType<MudBlazor.MudCheckBox<bool>>().Last(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Pull image if missing")).ValueChanged.InvokeAsync(false);
            var defaultProfile = preferences.Value.Environments.Profiles.Single(item => item.Id == added.Id);
            defaultProfile.Settings.PullIfMissing.ShouldBeFalse();
            var clear = components.Items.OfType<MudBlazor.MudButton>().Last(item => item.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Clear default profile")).OnClick;
            await clear.InvokeAsync(new MouseEventArgs());
            preferences.Value.Environments.Profiles.ShouldNotContain(item => item.DefaultForResourceType == "mqtt.client");
            await preferences.LoadAsync();
            JsonSerializer.Serialize(preferences.Value.Environments.Profiles.Single(item => item.Id == added.Id))
                .ShouldBe(JsonSerializer.Serialize(defaultProfile with { DefaultForResourceType = null }));
            await components.Items.OfType<MudBlazor.MudButton>().Last(item => item.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Set default profile")).OnClick.InvokeAsync(new MouseEventArgs());
            JsonSerializer.Serialize(preferences.Value.Environments.Profiles.Single(item => item.Id == added.Id))
                .ShouldBe(JsonSerializer.Serialize(defaultProfile));
            var reassigned = preferences.Value;
            await clear.InvokeAsync(new MouseEventArgs());
            preferences.Value.ShouldBeSameAs(reassigned);
            var remove = components.Items.OfType<StudioIconButton>().Last(item => item.Label == "Remove profile").OnClick;
            var oldName = components.Items.OfType<MudBlazor.MudTextField<string>>().Last(item => item.Label == "Name").ValueChanged;
            var storage = (MemorySettings)scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>();
            var finishWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            storage.WriteDelay = finishWrite.Task;
            var removing = remove.InvokeAsync();
            var lateNameEdit = oldName.InvokeAsync("Late rename");
            var ignoredWhileSaving = lateNameEdit.IsCompletedSuccessfully;
            finishWrite.SetResult();
            await Task.WhenAll(removing, lateNameEdit);
            ignoredWhileSaving.ShouldBeTrue();
            var afterRemove = preferences.Value;
            await remove.InvokeAsync();
            await oldName.InvokeAsync("Removed profile");
            preferences.Value.ShouldBeSameAs(afterRemove);
            preferences.Value.Environments.Profiles.ShouldNotContain(item => item.Id == added.Id);
            preferences.Value.Environments.Profiles.ShouldNotContain(item => item.DefaultForResourceType == "mqtt.client");
            view.ToHtmlString().ShouldContain("<h2>Mosquitto</h2>");
            JsonSerializer.Serialize(preferences.Value.Environments.Profiles.Single(item => item.Id == "wiremock"))
                .ShouldBe(JsonSerializer.Serialize(original.Profiles.Single(item => item.Id == "wiremock")));
            await preferences.LoadAsync();
            preferences.Value.Environments.Profiles.ShouldNotContain(item => item.Id == added.Id);
            var reopened = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.ResourceDefaultsSettings>();
            reopened.ToHtmlString().ShouldContain("Select profile Mosquitto");
            reopened.ToHtmlString().ShouldNotContain("Select profile New profile");
            preferences.Value.Environments.Profiles.ShouldNotContain(item => item.DefaultForResourceType == "mqtt.client");
            graph.ToProjectJson().ShouldBe(project);
            graph.Revision.ShouldBe(revision);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(beforeEdit);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(project);
        });
    }

    [Fact]
    public async Task Registry_actions_reject_stale_clicks_and_preserve_referenced_settings_on_reopen()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var access = new PendingRegistryAccess();
        access.Completion.SetResult(new(true, "Checked deleted registry"));
        services.AddSingleton<FluxFlow.Environments.IRegistryAccess>(access);
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var original = new FluxFlow.Environments.EnvironmentDefaults().AddRegistry()
            .AddProfile(FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider);
        await preferences.SaveAsync(preferences.Value with { Environments = original });
        var saved = JsonSerializer.Serialize(preferences.Value);
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.AddNode(graph.FindDescription("scheduler")!);
        var project = graph.ToProjectJson();
        graph.Undo();
        var undone = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.RegistrySettings>();
            EventCallback Action(string label) => components.Items.OfType<StudioIconButton>().Last(item => item.Label == label).OnClick;
            var oldRemove = Action("Remove registry");
            await oldRemove.InvokeAsync();
            view.ToHtmlString().ShouldContain("Change profiles that use this registry before removing it.");
            preferences.Value.Environments.ShouldBeSameAs(original);
            var add = Action("Add registry");
            await add.InvokeAsync();
            var added = preferences.Value;
            var unusedId = added.Environments.Registries.Last().Id;
            await add.InvokeAsync();
            await oldRemove.InvokeAsync();
            preferences.Value.ShouldBeSameAs(added);

            var removeBeforeReload = Action("Remove registry");
            await preferences.LoadAsync();
            var reloaded = preferences.Value;
            reloaded.Environments.Registries.Last().Id.ShouldBe(unusedId);
            await removeBeforeReload.InvokeAsync();
            preferences.Value.ShouldBeSameAs(reloaded);
            var remove = Action("Remove registry");
            var oldCheck = components.Items.OfType<MudBlazor.MudButton>()
                .Last(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Test registry connection")).OnClick;
            await remove.InvokeAsync();
            var removed = preferences.Value;
            await remove.InvokeAsync();
            await oldCheck.InvokeAsync(new MouseEventArgs());
            access.Token.CanBeCanceled.ShouldBeFalse();
            view.ToHtmlString().ShouldNotContain("Checked deleted registry");
            preferences.Value.ShouldBeSameAs(removed);
            await preferences.LoadAsync();
            var reopened = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.RegistrySettings>();
            reopened.ToHtmlString().ShouldContain("Select registry Docker Hub");
            JsonSerializer.Serialize(preferences.Value).ShouldBe(saved);
            graph.Revision.ShouldBe(revision);
            graph.ToProjectJson().ShouldBe(undone);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(project);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(undone);
        });
    }

    [Fact]
    public async Task Registry_edits_keep_invalid_drafts_local_and_only_persist_current_changes()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var original = new FluxFlow.Environments.EnvironmentDefaults().AddRegistry().AddProfile(FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider);
        await preferences.SaveAsync(preferences.Value with { Environments = original });
        var registry = original.Registries.Single();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.AddNode(graph.FindDescription("scheduler")!);
        var project = graph.ToProjectJson();
        graph.Undo();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.RegistrySettings>();
            MudBlazor.MudTextField<string> Field(string label) => components.Items.OfType<MudBlazor.MudTextField<string>>().Last(field => field.Label == label);
            var staleName = Field("Name").ValueChanged;
            await Field("Registry host").ValueChanged.InvokeAsync("https://wrong.example");
            await staleName.InvokeAsync("Stale");
            Field("Registry host").Value.ShouldBe("https://wrong.example");
            view.ToHtmlString().ShouldContain("Use a registry host");
            preferences.Value.Environments.ShouldBeSameAs(original);
            await Field("Registry host").ValueChanged.InvokeAsync(registry.Address);
            view.ToHtmlString().ShouldNotContain("Use a registry host");
            preferences.Value.Environments.ShouldBeSameAs(original);

            var storage = (MemorySettings)scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>();
            storage.WriteDelay = Task.FromException(new IOException("Registry settings could not be written"));
            await Field("Name").ValueChanged.InvokeAsync("Mirror");
            view.ToHtmlString().ShouldContain("Registry settings could not be written");
            preferences.Value.Environments.ShouldBeSameAs(original);
            storage.WriteDelay = Task.CompletedTask;
            await Field("Name").ValueChanged.InvokeAsync("Mirror");
            view.ToHtmlString().ShouldNotContain("Registry settings could not be written");
            preferences.Value.Environments.Registries.Single().Name.ShouldBe("Mirror");
            await Field("Registry host").ValueChanged.InvokeAsync(" mirror.example:5000 ");
            await Field("Description (optional)").ValueChanged.InvokeAsync("  Preserve spaces  ");
            var saved = preferences.Value;
            var beforeReload = Field("Name").ValueChanged;
            await beforeReload.InvokeAsync("Mirror");
            preferences.Value.ShouldBeSameAs(saved);
            await preferences.LoadAsync();
            await beforeReload.InvokeAsync("Old edit");
            var reopened = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.RegistrySettings>();
            reopened.ToHtmlString().ShouldContain("Select registry Mirror");
            preferences.Value.Environments.Registries.Single().ShouldBe(registry with
                { Name = "Mirror", Address = "mirror.example:5000", Description = "  Preserve spaces  " });
            preferences.Value.Environments.Profiles.Single().RegistryId.ShouldBe(registry.Id);
            graph.Revision.ShouldBe(revision);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(project);
        });
    }

    [Theory]
    [InlineData("Docker credentials")]
    [InlineData("Anonymous")]
    public async Task Registry_sign_in_is_optional_and_preserves_saved_settings(string authentication)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var registry = new FluxFlow.Environments.ImageRegistry { Authentication = authentication };
        new FluxFlow.Environments.ImageRegistry().Authentication.ShouldBe("Docker credentials");
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [registry] } });
        var saved = JsonSerializer.Serialize(preferences.Value);
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.AddNode(graph.FindDescription("scheduler")!);
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        var canUndo = graph.CanUndo;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.RegistrySettings>();
            view.ToHtmlString().ShouldNotContain("Password or access token");
            if (authentication == "Docker credentials")
            {
                view.ToHtmlString().ShouldContain("Use existing Docker login");
                var toggle = components.Items.OfType<MudBlazor.MudButton>().Single(item => item.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Toggle registry sign-in fields"));
                await toggle.OnClick.InvokeAsync(new MouseEventArgs());
                view.ToHtmlString().ShouldContain("Password or access token");
                var password = components.Items.OfType<MudBlazor.MudTextField<string>>().Single(item => item.Label == "Password or access token");
                await password.ValueChanged.InvokeAsync("temporary-test-value");
                await toggle.OnClick.InvokeAsync(new MouseEventArgs());
                view.ToHtmlString().ShouldNotContain("Password or access token");
                await toggle.OnClick.InvokeAsync(new MouseEventArgs());
                view.ToHtmlString().ShouldNotContain("temporary-test-value");
                var currentPassword = components.Items.OfType<MudBlazor.MudTextField<string>>().Last(item => item.Label == "Password or access token");
                var previousUserEdit = components.Items.OfType<MudBlazor.MudTextField<string>>().Last(item => item.Label == "Username").ValueChanged;
                var previousTokenEdit = currentPassword.ValueChanged;
                await currentPassword.ValueChanged.InvokeAsync("previous-host-token");
                var host = components.Items.OfType<MudBlazor.MudTextField<string>>().Single(item => item.Label == "Registry host");
                await host.ValueChanged.InvokeAsync("registry.example");
                view.ToHtmlString().ShouldNotContain("Password or access token");
                await toggle.OnClick.InvokeAsync(new MouseEventArgs());
                await previousUserEdit.InvokeAsync("old-registry-user");
                await previousTokenEdit.InvokeAsync("previous-host-token");
                components.Items.OfType<MudBlazor.MudTextField<string>>().Last(item => item.Label == "Username").Value.ShouldBeNullOrEmpty();
                components.Items.OfType<MudBlazor.MudTextField<string>>().Last(item => item.Label == "Password or access token").Value.ShouldBeNullOrEmpty();
                view.ToHtmlString().ShouldNotContain("previous-host-token");
                await host.ValueChanged.InvokeAsync(registry.Address);
            }
            else
            {
                view.ToHtmlString().ShouldContain("No Studio sign-in");
                view.ToHtmlString().ShouldContain("does not force anonymous access");
            }
            JsonSerializer.Serialize(preferences.Value).ShouldBe(saved);
            graph.ToProjectJson().ShouldBe(project);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBe(canUndo);
        });
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true, true)]
    public async Task Applying_a_profile_copies_it_into_one_undoable_project_edit(bool useDefault, bool locked, bool standalone, bool startManual = false)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"KeepCase":42,"Resources":{"api":{"Type":"http.client","BaseAddress":"http://original/"}},"Workflows":{"main":{}}}""");
        if (!useDefault && !startManual) graph.SetEnvironment(new() { Resources = new() { ["Resources:api"] = new() { Provider = "wiremock", Image = "original/mock:1", Files = new() { ["/old.txt"] = "Old" } } } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var registry = new FluxFlow.Environments.ImageRegistry { Id = "private", Address = "registry.example", Name = "Private" };
        var profile = new FluxFlow.Environments.ContainerProfile { Id = "custom", Name = "Custom", Provider = "wiremock", DefaultForResourceType = useDefault ? "http.client" : null, RegistryId = registry.Id, SetupLocked = locked,
            Settings = new() { Image = "team/mock:1", Mappings = """[{"Request":{"Path":"/test"},"Response":{"StatusCode":201}}]""", Variables = new() { ["CaseSensitive"] = "Keep" } } };
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [registry], Profiles = [profile] } });
        graph.ToProjectJson().ShouldBe(before);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            if (standalone)
            {
                var editor = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
                    { ["ResourcePath"] = "Resources:api" }));
                editor.ToHtmlString().ShouldNotContain("Project resources");
                editor.ToHtmlString().ShouldNotContain("Add connection");
            }
            else
            {
                var inspector = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Topology.TopologyInspector>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Definition"] = graph.GetEnvironmentDefinition(),
                    ["Selected"] = new FluxFlow.Environments.EnvironmentNode { Id = "api", Kind = FluxFlow.Environments.EnvironmentNodeKind.Service, Name = "api", Resource = "Resources:api" }
                }));
                inspector.ToHtmlString().ShouldContain("property-section-with-actions");
                inspector.ToHtmlString().ShouldContain(">Setup</span>");
                inspector.ToHtmlString().ShouldNotContain("role=\"tab\"");
                graph.ToProjectJson().ShouldBe(before);
            }
            if (!useDefault)
            {
                var menu = components.Items.OfType<MudBlazor.MudMenu>().Single(item => item.Label == "Use profile");
                await renderer.RenderComponentAsync<MudBlazor.MudPaper>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["ChildContent"] = menu.ChildContent }));
                graph.ToProjectJson().ShouldBe(before);
                graph.CanUndo.ShouldBeFalse();
                await components.Items.OfType<MudBlazor.MudMenuItem>().Single(item => item.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Copy profile Custom")).OnClick.InvokeAsync(new MouseEventArgs());
            }
            else await components.Items.OfType<MudBlazor.MudSelect<string>>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Run using")).ValueChanged.InvokeAsync("wiremock");
            var applied = graph.ToProjectJson();
            graph.GetEnvironment().Resources["Resources:api"].Image.ShouldBe("registry.example/team/mock:1");
            graph.GetEnvironment().Resources["Resources:api"].Mappings.ShouldBe(profile.Settings.Mappings);
            graph.GetEnvironment().Resources["Resources:api"].Profile!.SetupLocked.ShouldBe(locked);
            components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.ContainerImageEditor>().Last().Disabled.ShouldBe(locked);
            components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Services.Documents.ServiceItemEditor>().Last().ReadOnly.ShouldBe(locked);
            components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Services.Documents.ServiceItemEditor>().Last().Value.ShouldBe(profile.Settings.Mappings);
            if (locked)
            {
                var imageEditor = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.ContainerImageEditor>().Last();
                await imageEditor.ValueChanged.InvokeAsync(imageEditor.Value with { Settings = imageEditor.Value.Settings with { Image = "other:2" } });
                graph.ToProjectJson().ShouldBe(applied);
                var dialogs = scope.ServiceProvider.GetRequiredService<MudBlazor.IDialogService>();
                dialogs.DialogInstanceAddedAsync += reference =>
                {
                    reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
                    reference.Dismiss(MudBlazor.DialogResult.Ok("[]"));
                    return Task.CompletedTask;
                };
                await components.Items.OfType<MudBlazor.MudButton>().Last(item => item.StartIcon == MudBlazor.Icons.Material.Outlined.Code).OnClick.InvokeAsync(new MouseEventArgs());
                graph.ToProjectJson().ShouldBe(applied);
            }
            var profileMenu = components.Items.OfType<MudBlazor.MudMenu>().Last(item => item.Label == "Use profile");
            await renderer.RenderComponentAsync<MudBlazor.MudPaper>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["ChildContent"] = profileMenu.ChildContent }));
            var copy = components.Items.OfType<MudBlazor.MudMenuItem>().Last(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Copy profile Custom")).OnClick;
            var oldSelection = components.Items.OfType<MudBlazor.MudSelect<string>>().Last(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Run using")).ValueChanged;
            var appliedRevision = graph.Revision;
            await copy.InvokeAsync(new MouseEventArgs());
            graph.Revision.ShouldBe(appliedRevision);
            await preferences.SaveAsync(preferences.Value with { Environments = preferences.Value.Environments with { Profiles = [profile with { Settings = profile.Settings with { Image = "team/mock:2" } }] } });
            await copy.InvokeAsync(new MouseEventArgs());
            await oldSelection.InvokeAsync("existing");
            graph.Revision.ShouldBe(appliedRevision);
            graph.ToProjectJson().ShouldBe(applied);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(applied);
            graph.ExportConfiguration().ShouldBe(configuration);
            var reopened = StudioTestServices.CreateGraph();
            reopened.LoadProject(applied);
            reopened.GetEnvironment().Resources["Resources:api"].Profile!.Settings.Mappings.ShouldBe(profile.Settings.Mappings);
            reopened.GetEnvironment().Resources["Resources:api"].Profile!.SetupLocked.ShouldBe(locked);
            applied.ShouldNotContain("RegistryId");
            applied.ShouldNotContain("Password");

            var previousEditor = components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>().Last();
            var previousRunUsing = components.Items.OfType<MudBlazor.MudSelect<string>>().Last(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Run using"));
            previousEditor.Dispose();
            graph.AddEnvironment("Other");
            graph.SetEnvironment(new() { Resources = new() { ["Resources:api"] = new() { Provider = "wiremock", Image = "other:1" } } });
            var other = graph.ToProjectJson();
            var revision = graph.Revision;
            await previousRunUsing.ValueChanged.InvokeAsync("existing");
            graph.ToProjectJson().ShouldBe(other);
            graph.Revision.ShouldBe(revision);
        });
    }

    [Theory]
    [InlineData("dispose", false)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    public async Task Registry_test_cancellation_ignores_late_results_and_does_not_edit_the_project(string reason, bool fail)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var access = new PendingRegistryAccess();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Environments.IRegistryAccess>(access);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [new() { Name = "Test" }] } });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.RegistrySettings>();
            var test = components.Items.OfType<MudBlazor.MudButton>().Single(item => item.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Test registry connection"));
            var pending = test.OnClick.InvokeAsync(new MouseEventArgs());
            if (reason == "dispose") components.Items.OfType<FluxFlow.Studio.UI.Features.Environments.RegistrySettings>().Single().Dispose();
            else if (reason == "remove")
            {
                var defaults = preferences.Value.Environments;
                await preferences.SaveAsync(preferences.Value with { Environments = defaults.RemoveRegistry(defaults.Registries.Single().Id) });
                view.ToHtmlString().ShouldContain("No registries yet.");
            }
            else await components.Items.OfType<MudBlazor.MudButton>().Single(item => item.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Cancel registry operation")).OnClick.InvokeAsync(new MouseEventArgs());
            access.Token.IsCancellationRequested.ShouldBeTrue();
            if (fail) access.Completion.SetException(new InvalidOperationException("Late failure"));
            else access.Completion.SetResult(new(true, "Late success"));
            await pending;
            view.ToHtmlString().ShouldNotContain("Late success");
            view.ToHtmlString().ShouldNotContain("Late failure");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
        });
    }

    private sealed class PendingRegistryAccess : FluxFlow.Environments.IRegistryAccess
    {
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource<FluxFlow.Environments.RegistryAccessResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<FluxFlow.Environments.RegistryAccessResult> TestAsync(FluxFlow.Environments.ImageRegistry registry, CancellationToken cancellationToken) { Token = cancellationToken; return Completion.Task; }
        public Task<FluxFlow.Environments.RegistryAccessResult> SignInAsync(FluxFlow.Environments.RegistrySignIn request, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<FluxFlow.Environments.RegistryAccessResult> SignOutAsync(FluxFlow.Environments.ImageRegistry registry, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    [Theory]
    [InlineData("custom-broker")]
    [InlineData("wiremock")]
    public async Task Environment_sections_show_only_the_selected_settings_without_editing_the_project(string incompatibleProvider)
    {
        await using var provider = CreateServices().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client","BaseAddress":"http://original/"},"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883"}},"Workflows":{"main":{}}}""");
        graph.SetEnvironment(new() { Resources = new() { ["Resources:api"] = new()
            { Provider = "wiremock", Image = "registry.example/team/http:1", PullIfMissing = false },
            ["Resources:broker"] = new() { Provider = incompatibleProvider, Image = "registry.example/team/broker:1" } } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            foreach (var section in new[] { "Registries", "Defaults", "Resources:api", "Resources:broker" })
            {
                var view = section.StartsWith("Resources:", StringComparison.Ordinal)
                    ? await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["ResourcePath"] = section }))
                    : await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.EnvironmentPage>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["InitialSection"] = section }));
                var html = view.ToHtmlString();
                html.ShouldNotContain("Project resources");
                html.ShouldNotContain("Project setup");
                if (section == "Resources:api") { html.ShouldContain("Edit endpoints"); html.ShouldContain("team/http"); }
                else html.ShouldNotContain("Edit endpoints");
                if (section == "Registries")
                {
                    html.ShouldContain("aria-label=\"Registries\"");
                    html.ShouldContain("Add registry");
                    html.ShouldNotContain("Add profile");
                }
                if (section == "Defaults")
                {
                    html.ShouldContain("aria-label=\"Resource profiles\"");
                    html.ShouldContain("Configure registries");
                    html.ShouldNotContain("Select profile Mosquitto");
                    html.ShouldContain("Add profile");
                    html.ShouldNotContain("Add registry");
                }
                if (section == "Resources:broker") html.ShouldContain("(unavailable)");
                graph.ToProjectJson().ShouldBe(before);
                graph.Revision.ShouldBe(revision);
                graph.CanUndo.ShouldBeFalse();
            }
        });
    }

    [Fact]
    public async Task Applying_a_registry_preserves_digest_customization_and_history()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"KeepCase":42,"Resources":{"api":{"Type":"http.client","BaseAddress":"http://original/"},"broker":{"Type":"mqtt.client"}},"Workflows":{"main":{}}}""");
        var digest = "sha256:" + new string('a', 64);
        var binding = new FluxFlow.Environments.ResourceBinding { Provider = "wiremock", Image = "old.example:5000/team/mock@" + digest,
            PullIfMissing = false, Mappings = "[]", Files = new() { ["/app/test.txt"] = "KeepCase" }, Variables = new() { ["CaseSensitive"] = "Value" } };
        graph.SetEnvironment(new() { Resources = new() { ["Resources:api"] = binding, ["Resources:broker"] = new() { Provider = "mosquitto" } } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var registry = new FluxFlow.Environments.ImageRegistry { Id = "private", Name = "Private", Address = "new.example:5001" };
        await preferences.SaveAsync(preferences.Value with { Environments = preferences.Value.Environments with { Registries = [registry] } });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ResourcePath"] = "Resources:api" }));
            await components.Items.OfType<MudBlazor.MudSelect<string>>().Single(item => Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Registry")).ValueChanged.InvokeAsync(registry.Id);
            var after = graph.ToProjectJson();
            JsonSerializer.Serialize(graph.GetEnvironment().Resources["Resources:api"]).ShouldBe(
                JsonSerializer.Serialize(binding with { Image = "new.example:5001/team/mock@" + digest }));
            graph.GetEnvironment().Resources["Resources:broker"].Provider.ShouldBe("mosquitto");
            graph.ExportConfiguration().ShouldBe(configuration);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(after);
            await preferences.SaveAsync(preferences.Value with { Environments = preferences.Value.Environments with { Registries = [] } });
            await preferences.SaveAsync(preferences.Value with { Environments = preferences.Value.Environments with { Registries = [registry with { Id = "replacement" }] } });
            graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Fact]
    public async Task Environment_image_edits_preserve_customization_and_other_resources_through_undo_redo()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var registry = new FluxFlow.Environments.ImageRegistry { Address = "registry.example" };
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [registry] } });
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"KeepCase":{"Value":42},"Resources":{"api":{"Type":"http.client","BaseAddress":"http://original/"},"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883"}},"Workflows":{"main":{}}}""");
        graph.SetEnvironment(new() { Resources = new()
        {
            ["Resources:api"] = new() { Provider = "wiremock", Image = "registry.example/http:1",
                Mappings = """[{"Request":{"Path":"/test"},"Response":{"StatusCode":201}}]""",
                Files = new() { ["/app/example.txt"] = "KeepCase" }, Variables = new() { ["Custom"] = "Value" } },
            ["Resources:broker"] = new() { Provider = "mosquitto", Image = "registry.example/mqtt:1" }
        } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        var original = graph.GetEnvironment();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ResourcePath"] = "Resources:api" }));
            await components.Items.OfType<MudBlazor.MudSelect<string>>().Single(field => Equals(field.UserAttributes.GetValueOrDefault("aria-label"), "Run using"))
                .ValueChanged.InvokeAsync("wiremock");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            var imageField = components.Items.OfType<MudBlazor.MudTextField<string>>().Single(field => Equals(field.UserAttributes.GetValueOrDefault("aria-label"), "Image name"));
            foreach (var invalid in new string?[] { null, "", "invalid image" })
            {
                await imageField.ValueChanged.InvokeAsync(invalid);
                graph.ToProjectJson().ShouldBe(before);
                graph.CanUndo.ShouldBeFalse();
            }
            await imageField.ValueChanged.InvokeAsync("team/http");
            var afterImage = graph.ToProjectJson();
            await components.Items.OfType<MudBlazor.MudCheckBox<bool>>().Single().ValueChanged.InvokeAsync(false);
            var afterPullPolicy = graph.ToProjectJson();
            var saved = graph.GetEnvironment();
            saved.Resources["Resources:api"].Image.ShouldBe("registry.example/team/http:1");
            saved.Resources["Resources:api"].PullIfMissing.ShouldBeFalse();
            saved.Resources["Resources:api"].Mappings.ShouldBe(original.Resources["Resources:api"].Mappings);
            saved.Resources["Resources:api"].Files.ShouldBe(original.Resources["Resources:api"].Files);
            saved.Resources["Resources:api"].Variables.ShouldBe(original.Resources["Resources:api"].Variables);
            JsonSerializer.Serialize(saved.Resources["Resources:broker"]).ShouldBe(JsonSerializer.Serialize(original.Resources["Resources:broker"]));
            graph.Undo();
            graph.ToProjectJson().ShouldBe(afterImage);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(afterImage);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(afterPullPolicy);
            graph.ExportConfiguration().ShouldBe(configuration);
        });
    }

    [Theory]
    [InlineData("http.client", "BaseAddress", "http://localhost:8080/")]
    [InlineData("amqp.connection", "Endpoint", "amqp://localhost:5672")]
    public async Task Service_setup_hides_removed_resources_without_changing_configuration(string type, string property, string address)
    {
        await using var provider = CreateServices().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(JsonSerializer.Serialize(new
        {
            Resources = new { api = new Dictionary<string, string> { ["Type"] = type, [property] = address } },
            Workflows = new { main = new { } }
        }));
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ResourcePath"] = "Resources:api" }));
            view.ToHtmlString().ShouldContain("Configured address");
            view.ToHtmlString().ShouldContain(address);
            graph.LoadJson("""{"Workflows":{"main":{}}}""");
            graph.ResetHistory();
            var before = graph.ToProjectJson();
            var revision = graph.Revision;
            var html = view.ToHtmlString();
            html.ShouldNotContain("Run using");
            html.ShouldNotContain("Configured endpoint");
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Service_setup_updates_after_undo_and_redo_without_changing_configuration()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [new() { Name = "Docker Hub" }] } });
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client","BaseAddress":"http://original/"}},"Workflows":{"main":{}}}""");
        graph.ResetHistory();
        var before = graph.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Environments.Services.ServiceSettingsEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["ResourcePath"] = "Resources:api" }));
            var select = components.Items.OfType<MudBlazor.MudSelect<string>>().Single(field => Equals(field.UserAttributes.GetValueOrDefault("aria-label"), "Run using"));
            select.Value.ShouldBe("existing");
            components.Items.OfType<MudBlazor.MudSelectItem<string>>().Select(item => item.Value).ShouldContain("wiremock");
            var initial = select.ValueChanged;
            await initial.InvokeAsync("mosquitto");
            await initial.InvokeAsync("missing-provider");
            graph.CanUndo.ShouldBeFalse();
            await select.ValueChanged.InvokeAsync("wiremock");
            select.Value.ShouldBe("wiremock");
            view.ToHtmlString().ShouldContain("sheyenrath/wiremock.net-alpine");
            graph.Undo();
            view.ToHtmlString().ShouldNotContain("Container image");
            graph.Redo();
            view.ToHtmlString().ShouldContain("sheyenrath/wiremock.net-alpine");
            var managed = graph.ToProjectJson();
            await initial.InvokeAsync("existing");
            graph.ToProjectJson().ShouldBe(managed);
            await select.ValueChanged.InvokeAsync("existing");
            graph.GetEnvironment().Resources.ContainsKey("Resources:api").ShouldBeFalse();
            graph.Undo();
            graph.ToProjectJson().ShouldBe(managed);
            graph.Redo();
            graph.GetEnvironment().Resources.ContainsKey("Resources:api").ShouldBeFalse();
            graph.ExportConfiguration().ShouldBe(before);
        });
    }

    [Theory]
    [InlineData("\"reply\"", "reply")]
    [InlineData("1", "1")]
    [InlineData("\"FutureMode\"", "FutureMode")]
    public async Task Select_fields_show_imported_values_without_normalizing_configuration(string configured, string displayed)
    {
        await using var provider = CreateServices().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"trigger":{"Type":"http.trigger","Route":"/test"}}}}""");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        node.Configuration["responseMode"] = JsonSerializer.Deserialize<JsonElement>(configured);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var option = node.Description.Inspector.Sections.SelectMany(section => section.Options).Single(option => option.Name == "ResponseMode");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.Configuration)] = node.Configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, () => throw new InvalidOperationException("Displaying must not normalize values."))
            }));
            System.Text.RegularExpressions.Regex.Matches(view.ToHtmlString(), "<(input|div)\\b[^>]*role=\"combobox\"[^>]*>([^<]*)")
                .Any(control => !control.Value.Contains("type=\"hidden\"") &&
                    (control.Value.Contains($"value=\"{displayed}\"") || control.Groups[2].Value == System.Net.WebUtility.HtmlEncode(displayed)))
                .ShouldBeTrue(view.ToHtmlString());
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Palette_groups_collapse_independently_and_search_reveals_matches_without_editing()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<ComponentPalette>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ComponentPalette.Items)] = graph.Components.Select(item => item.Palette).ToArray(),
                [nameof(ComponentPalette.OnSelect)] = EventCallback.Factory.Create<string>(this, _ => throw new InvalidOperationException("Collapsing must not add nodes."))
            }));
            var filtering = components.Items.OfType<MudBlazor.MudButton>().Single(button =>
                button.UserAttributes.TryGetValue("aria-label", out var name) && Equals(name, "Filtering"));
            await filtering.OnClick.InvokeAsync(new MouseEventArgs());
            view.ToHtmlString().ShouldNotContain(">Change Filter</span>");
            view.ToHtmlString().ShouldContain(">HTTP Client</span>");
            filtering.UserAttributes["aria-expanded"].ShouldBe("false");
            await filtering.OnClick.InvokeAsync(new MouseEventArgs());
            view.ToHtmlString().ShouldContain(">Change Filter</span>");
            await filtering.OnClick.InvokeAsync(new MouseEventArgs());
            var search = components.Items.OfType<MudBlazor.MudTextField<string>>().Single();
            await search.ValueChanged.InvokeAsync("Change Filter");
            view.ToHtmlString().ShouldContain(">Change Filter</span>");
            view.ToHtmlString().ShouldNotContain(">HTTP Client</span>");
            await search.ValueChanged.InvokeAsync("");
            view.ToHtmlString().ShouldContain(">HTTP Client</span>");
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();
        });
    }

    [Theory]
    [InlineData("""{"Hot":"true","hot":"false"}""")]
    [InlineData("""{"Hot":"true","Hot":"false"}""")]
    [InlineData("""{"Nested":{"a":1,"A":2}}""")]
    public async Task JSON_dialog_rejects_duplicate_configuration_keys_before_changes_reach_the_graph(string invalid)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<MudBlazor.MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<MudBlazor.IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference =>
            {
                reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
                return Task.CompletedTask;
            };
            var dialog = await dialogs.ShowAsync<TextEditorDialog>("Routes", new MudBlazor.DialogParameters<TextEditorDialog>
            { { field => field.Value, invalid }, { field => field.Json, true } });
            var save = components.Items.OfType<MudBlazor.MudButton>().Single(button => button.Variant == MudBlazor.Variant.Filled);
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            dialog.Result.IsCompleted.ShouldBeFalse();
            components.Items.OfType<MudBlazor.MudAlert>().ShouldNotBeEmpty();
            dialog.Close(MudBlazor.DialogResult.Cancel());
            (await dialog.Result)!.Canceled.ShouldBeTrue();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
        });
    }

    [Theory]
    [InlineData("""{"Topic":"Sensors/#","QoS":1}""", false)]
    [InlineData("""["Sensors/#","Commands/+"]""", false)]
    [InlineData("\"Sensors/#\"", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    [InlineData("""{"Topic":"Discard"}""", true)]
    public async Task Structured_property_editor_preserves_JSON_spelling_cancellation_and_history(string edited, bool cancel)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"KeepCase":true,"Workflows":{"main":{"sub":{"Type":"mqtt.subscriber","Connection":"broker","tOpIcS":"Old/#","Custom":{"InnerCase":42}}}}}""");
        graph.ResetHistory();
        var before = graph.ToJson();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        var option = node.Description.Inspector.Sections.SelectMany(section => section.Options).Single(option => option.Name == "Topics");
        var dialogs = scope.ServiceProvider.GetRequiredService<MudBlazor.IDialogService>();
        dialogs.DialogInstanceAddedAsync += reference =>
        {
            // Static rendering has no dialog paint lifecycle; supply the user's completed dialog result.
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(cancel ? MudBlazor.DialogResult.Cancel() : MudBlazor.DialogResult.Ok(edited));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.Configuration)] = node.Configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            graph.ToJson().ShouldBe(before);
            await components.Items.OfType<OptionEditorField>().Single().OpenEditorAsync();
            if (cancel)
            {
                graph.ToJson().ShouldBe(before);
                graph.CanUndo.ShouldBeFalse();
                return;
            }
            var after = graph.ToJson();
            using var saved = JsonDocument.Parse(after);
            var settings = saved.RootElement.GetProperty("Workflows").GetProperty("main").GetProperty("sub");
            settings.GetProperty("Custom").GetProperty("InnerCase").GetInt32().ShouldBe(42);
            settings.TryGetProperty("Topics", out _).ShouldBeFalse();
            if (edited.Length == 0) settings.TryGetProperty("tOpIcS", out _).ShouldBeFalse();
            else
            {
                var expected = System.Text.Json.Nodes.JsonNode.Parse(edited);
                System.Text.Json.Nodes.JsonNode.DeepEquals(expected,
                    System.Text.Json.Nodes.JsonNode.Parse(settings.GetProperty("tOpIcS").GetRawText())).ShouldBeTrue();
            }
            graph.Undo();
            graph.ToJson().ShouldBe(before);
            graph.Redo();
            graph.ToJson().ShouldBe(after);
        });
    }

    [Theory]
    [InlineData(null, "128")]
    [InlineData("64", "64")]
    [InlineData("0", "0")]
    [InlineData("null", "")]
    public async Task Property_editor_displays_defaults_only_for_omitted_settings_without_writing_them(string? configured, string displayed)
    {
        await using var provider = CreateServices().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"schedule":{"Type":"scheduler","Interval":"00:00:01"}}}}""");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        if (configured is not null) node.Configuration["oUtPuTcApAcItY"] = JsonSerializer.Deserialize<JsonElement>(configured);
        graph.RecordEdit();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        var canUndo = graph.CanUndo;
        var option = node.Description.Inspector.Sections.SelectMany(section => section.Options)
            .Single(option => option.Name == "OutputCapacity");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var field = await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.Configuration)] = node.Configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, () => throw new InvalidOperationException("Rendering must not edit configuration."))
            }));
            field.ToHtmlString().ShouldContain($"value=\"{displayed}\"");
            if (configured is null) field.ToHtmlString().ShouldContain("placeholder=\"128\"");
            else field.ToHtmlString().ShouldNotContain("placeholder=\"128\"");
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBe(canUndo);
        });
    }

    [Fact]
    public async Task Shared_page_renders_with_host_services_and_preserves_configuration_and_history()
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        const string configuration = """
            {"KeepCase":{"External":true},"Workflows":{"main":{"map":{"Type":"mapper","Engine":"jsonata","Expression":"$","Custom":{"UPPER":42}}}}}
            """;
        graph.LoadJson(configuration);
        var before = graph.ToJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = await renderer.RenderComponentAsync<DesignerPage>();
            page.ToHtmlString().ShouldContain("aria-label=\"Main menu\"");
            page.ToHtmlString().ShouldContain("JSONata Mapper");
            page.ToHtmlString().ShouldContain("Open chat");
            page.ToHtmlString().ShouldContain("Workflow chat");
            page.ToHtmlString().ShouldNotContain("Propose changes");
            page.ToHtmlString().ShouldNotContain(">Proposal</button>");
            graph.ToJson().ShouldBe(before);

            graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
            graph.RenameSelectedNode("renamed");
            graph.RecordEdit();
            var edited = graph.ToProjectJson();
            var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
            await preferences.SaveAsync(preferences.Value with { Theme = "Light", ProposalConnection = new() { Model = "test" } });
            var reopened = new StudioPreferences(scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>());
            await reopened.LoadAsync();
            JsonSerializer.Serialize(reopened.Value, StudioJsonContext.Default.StudioSettings).ShouldBe(JsonSerializer.Serialize(preferences.Value, StudioJsonContext.Default.StudioSettings));
            graph.ToProjectJson().ShouldBe(edited);
            graph.Undo();
            graph.ToJson().ShouldBe(before);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(edited);
        });
        await using var other = provider.CreateAsyncScope();
        other.ServiceProvider.GetRequiredService<DesignerGraphState>().NodeCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recreating_the_page_preserves_existing_undo_and_redo(bool undone)
    {
        await using var provider = CreateServices().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"map":{"Type":"mapper","Engine":"jsonata","Expression":"$"}}}}""");
        await using (var first = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>()))
        {
            await first.Dispatcher.InvokeAsync(async () =>
            {
                await first.RenderComponentAsync<DesignerPage>();
                graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
                graph.RenameSelectedNode("renamed");
                graph.RecordEdit();
                if (undone) graph.Undo();
            });
        }
        var saved = graph.ToProjectJson();
        var file = new StudioFile("saved-project.fluxflow", saved, "host-file-location");
        graph.ProjectFile = file;
        await using var second = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await second.Dispatcher.InvokeAsync(async () =>
        {
            var page = await second.RenderComponentAsync<DesignerPage>();
            page.ToHtmlString().ShouldContain(file.Name);
            graph.ProjectFile.ShouldBe(file);
            graph.ToProjectJson().ShouldBe(saved);
            graph.CanUndo.ShouldBe(!undone);
            graph.CanRedo.ShouldBe(undone);
            if (undone) graph.Redo(); else graph.Undo();
            graph.ToProjectJson().ShouldNotBe(saved);
            graph.ProjectFile.ShouldBe(file);
        });
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"Theme\":\"Unknown\"}")]
    [InlineData("{\"PortColors\":null}")]
    [InlineData("{\"Theme\":\"Dark\",\"FutureSetting\":42}")]
    [InlineData("{\"ProposalConnection\":{\"Endpoint\":\"https://example.com\"}}")]
    public async Task Invalid_stored_settings_do_not_replace_the_current_preferences(string json)
    {
        var storage = new MemorySettings();
        var preferences = new StudioPreferences(storage);
        var current = new StudioSettings { Theme = "Dark" };
        await preferences.SaveAsync(current);
        await storage.WriteAsync(json);
        await Should.ThrowAsync<Exception>(() => preferences.LoadAsync());
        preferences.Value.ShouldBe(current);
        await Should.ThrowAsync<InvalidOperationException>(() => preferences.SaveAsync(current with { Theme = "Light" }));
        (await storage.ReadAsync()).ShouldBe(json);
        await storage.WriteAsync(JsonSerializer.Serialize(current, StudioJsonContext.Default.StudioSettings));
        await preferences.LoadAsync();
        await preferences.SaveAsync(preferences.Value with { Theme = "Light" });
        await preferences.LoadAsync();
        preferences.Value.Theme.ShouldBe("Light");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_autosave_keeps_current_settings_until_storage_completes(bool fail)
    {
        var storage = new MemorySettings();
        var preferences = new StudioPreferences(storage);
        var original = new StudioSettings { Theme = "Dark" };
        await preferences.SaveAsync(original);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.WriteDelay = completion.Task;
        var changed = original with { ProposalConnection = new() { Model = "test" } };
        var saving = preferences.SaveAsync(changed);
        preferences.IsSaving.ShouldBeTrue();
        preferences.SaveStatus.ShouldBe("Saving…");
        preferences.Value.ShouldBe(original);
        if (fail)
        {
            var error = new IOException("Storage unavailable");
            completion.SetException(error);
            (await Should.ThrowAsync<IOException>(() => saving)).ShouldBeSameAs(error);
        }
        else
        {
            completion.SetResult();
            await saving;
        }
        preferences.IsSaving.ShouldBeFalse();
        preferences.Value.ShouldBe(fail ? original : changed);
        preferences.SaveStatus.ShouldBe(fail ? "Settings could not be saved" : "Saved");
        var reopened = new StudioPreferences(storage);
        await reopened.LoadAsync();
        JsonSerializer.Serialize(reopened.Value, StudioJsonContext.Default.StudioSettings).ShouldBe(JsonSerializer.Serialize(preferences.Value, StudioJsonContext.Default.StudioSettings));
    }

    [Fact]
    public async Task Connection_page_loads_host_defaults_without_writing_preferences()
    {
        var services = CreateServices();
        services.AddSingleton<IProposalConnectionService, FixedConnection>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var page = await renderer.RenderComponentAsync<ProposalConnectionEditor>();
            page.ToHtmlString().ShouldContain("Test connection");
            page.ToHtmlString().ShouldContain("1 installed models available.");
            preferences.Value.ProposalConnection.ShouldBeNull();
            preferences.SaveStatus.ShouldBeNull();
        });
        (await scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>().ReadAsync()).ShouldBeNull();
    }

    private sealed class FixedConnection : IProposalConnectionService
    {
        public Task<ProposalConnectionStatus> GetModelsAsync(ProposalConnection? connection, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProposalConnectionStatus { Connection = new() { Model = "test" }, Models = ["test"] });
        public Task<ProposalConnectionStatus> TestAsync(ProposalConnection? connection, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Rendering must not test a connection.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsaved_connection_draft_blocks_checks_and_recovers_without_changing_history(bool storageFailure)
    {
        var services = CreateServices();
        services.AddSingleton<IProposalConnectionService, FixedConnection>();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var storage = (MemorySettings)scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>();
        var original = new StudioSettings { Theme = "Dark", ProposalConnection = new() { Model = "test" } };
        await preferences.SaveAsync(original);
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"map":{"Type":"mapper","Engine":"jsonata","Expression":"$"}}}}""");
        graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
        graph.RenameSelectedNode("renamed");
        graph.RecordEdit();
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        var canUndo = graph.CanUndo;
        var canRedo = graph.CanRedo;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var page = await renderer.RenderComponentAsync<ProposalConnectionEditor>();
            var endpoint = components.Items.OfType<MudBlazor.MudTextField<string>>().Single();
            var buttons = components.Items.OfType<MudBlazor.MudButton>().ToArray();
            if (storageFailure) storage.WriteDelay = Task.FromException(new IOException("Storage unavailable"));
            var draft = storageFailure ? "http://localhost:11434/" : "invalid endpoint";
            await endpoint.ValueChanged.InvokeAsync(draft);
            page.ToHtmlString().ShouldContain(draft);
            page.ToHtmlString().ShouldContain("Could not save connection:");
            buttons.ShouldAllBe(button => button.Disabled);
            // Even a queued click must not test the previous saved connection.
            foreach (var button in buttons) await button.OnClick.InvokeAsync(new MouseEventArgs());
            preferences.Value.ShouldBe(original);
            var reopened = new StudioPreferences(storage);
            await reopened.LoadAsync();
            JsonSerializer.Serialize(reopened.Value, StudioJsonContext.Default.StudioSettings).ShouldBe(JsonSerializer.Serialize(original, StudioJsonContext.Default.StudioSettings));

            storage.WriteDelay = Task.CompletedTask;
            var corrected = "http://localhost:11434/";
            await endpoint.ValueChanged.InvokeAsync(corrected);
            preferences.Value.ProposalConnection!.Endpoint.ShouldBe(corrected);
            preferences.Value.Theme.ShouldBe(original.Theme);
            preferences.SaveStatus.ShouldBe("Saved");
            buttons.ShouldAllBe(button => !button.Disabled);
            page.ToHtmlString().ShouldNotContain("Could not save connection:");
            graph.ToProjectJson().ShouldBe(project);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBe(canUndo);
            graph.CanRedo.ShouldBe(canRedo);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_chat_controls_preview_apply_and_discard_without_saving_settings(bool automatic)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var applied = 0;
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["OnApplied"] = EventCallback.Factory.Create(this, () => applied++)
                }));
            var composer = components.Items.OfType<MudBlazor.MudTextField<string>>().Single();
            if (automatic)
                await components.Items.OfType<MudBlazor.MudSelect<ChatEditingMode>>().Single().ValueChanged.InvokeAsync(ChatEditingMode.Automatic);
            await composer.ValueChanged.InvokeAsync("Add a source and mapper.");
            var send = components.Items.OfType<MudBlazor.MudIconButton>()
                .Single(button => button.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Send"));
            send.Disabled.ShouldBeFalse();
            await send.OnClick.InvokeAsync(new MouseEventArgs());
            if (!automatic)
            {
                view.ToHtmlString().ShouldContain("ready to review");
                view.ToHtmlString().ShouldContain("Proposed configuration");
                graph.ToProjectJson().ShouldBe(before);
                var preview = components.Items.OfType<FluxFlow.Studio.UI.Features.Chat.ProposalPreview>().Single();
                await preview.OnDiscard.InvokeAsync();
                view.ToHtmlString().ShouldContain("Proposal discarded");
                graph.ToProjectJson().ShouldBe(before);
                await composer.ValueChanged.InvokeAsync("Try again.");
                await send.OnClick.InvokeAsync(new MouseEventArgs());
                await preview.OnApply.InvokeAsync();
            }
            applied.ShouldBe(1);
            if (automatic) components.Items.OfType<BlazorMonaco.Editor.StandaloneCodeEditor>().ShouldBeEmpty();
            view.ToHtmlString().ShouldContain("Changes applied");
            var after = graph.ToProjectJson();
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(after);
        });
        (await scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>().ReadAsync()).ShouldBeNull();
    }

    [Theory]
    [InlineData(ChatEditingMode.Ask)]
    [InlineData(ChatEditingMode.Automatic)]
    public async Task Compact_chat_pins_the_header_during_generation_and_stop_preserves_history(ChatEditingMode mode)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var generator = new PendingChatGenerator();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IProposalGenerator>(generator);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.ResetHistory();
        graph.ApplyEdits(graph.PrepareEdits(ConfigurationEditingTests.Batch()));
        graph.Undo();
        var before = graph.ToProjectJson();
        var configuration = graph.ToJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Task sending = Task.CompletedTask;
        Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent view = default;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            view.ToHtmlString().ShouldNotContain("aria-label=\"Attach main.source\"");
            graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
            await AttachButton().OnClick.InvokeAsync(new MouseEventArgs());
            view.ToHtmlString().ShouldContain("main.source");
            var chip = components.Items.OfType<MudBlazor.MudChip<string>>().Last(item => item.OnClose.HasDelegate);
            await chip.OnClose.InvokeAsync(chip);
            view.ToHtmlString().ShouldNotContain("chat-context-chip");
            await AttachButton().OnClick.InvokeAsync(new MouseEventArgs());
            graph.ToJson().ShouldBe(configuration);
            graph.Revision.ShouldBe(revision);
            await components.Items.OfType<MudBlazor.MudSelect<ChatEditingMode>>().Single().ValueChanged.InvokeAsync(mode);
            await components.Items.OfType<MudBlazor.MudTextField<string>>().Single().ValueChanged.InvokeAsync("Add a component.");
            sending = Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            graph.SelectWorkflow("other");
            generator.Request!.SelectedComponents.ShouldBe(new[] { "main.source" });
            generator.Request.Configuration.ShouldBe(configuration);
            before = graph.ToProjectJson();
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var header = components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Workspace.WorkspacePanelHeader>().Single();
            header.Title.ShouldBe("Chat");
            header.Caption.ShouldBe("Chat — main");
            System.Net.WebUtility.HtmlDecode(view.ToHtmlString()).ShouldContain("Move Chat — main panel");
            await Button("Stop").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Token.IsCancellationRequested.ShouldBeTrue();
            generator.Completion.SetResult(FixedProposalGenerator.Result());
        });
        await sending;
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Workspace.WorkspacePanelHeader>().Single().Caption.ShouldBe("Chat — other");
            view.ToHtmlString().ShouldContain("Request cancelled");
            graph.ToProjectJson().ShouldBe(before);
            graph.ToJson().ShouldBe(configuration);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();
            graph.CanRedo.ShouldBeTrue();
        });

        MudBlazor.MudChip<string> AttachButton() => components.Items.OfType<MudBlazor.MudChip<string>>().Last(chip => chip.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, "Attach main.source"));

        MudBlazor.MudIconButton Button(string label) => components.Items.OfType<MudBlazor.MudIconButton>()
            .Last(button => button.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    [Fact]
    public async Task Attached_component_uses_send_time_values_and_later_edits_reject_the_response()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var generator = new PendingChatGenerator();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IProposalGenerator>(generator);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.AddNode(graph.FindDescription("mapper")!);
        graph.RecordEdit();
        graph.ResetHistory();
        var original = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "source");
        var other = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName != "source");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Task sending = Task.CompletedTask;
        string beforeSend = "", afterEdit = "";
        Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent view = default;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            graph.Diagram.SelectModel(original, true);
            await AttachButton().OnClick.InvokeAsync(new MouseEventArgs());
            graph.Diagram.SelectModel(other, true);
            original.Configuration["Custom"] = ConfigurationEditingTests.Json("{\"KeepCASE\":\"before send\"}");
            graph.RecordEdit();
            beforeSend = graph.ToJson();
            await components.Items.OfType<MudBlazor.MudTextField<string>>().Single().ValueChanged.InvokeAsync("Explain this component.");
            sending = Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Request!.SelectedComponents.ShouldBe(new[] { "main.source" });
            generator.Request.Configuration.ShouldBe(beforeSend);
            generator.Request.Configuration.ShouldContain("before send");
            graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "source")
                .Configuration["Custom"] = ConfigurationEditingTests.Json("{\"KeepCASE\":\"after send\"}");
            graph.RecordEdit();
            afterEdit = graph.ToProjectJson();
            generator.Request.Configuration.ShouldBe(beforeSend);
            generator.Completion.SetResult(FixedProposalGenerator.Result());
        });
        await sending;
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            view.ToHtmlString().ShouldContain("design changed");
            graph.ToProjectJson().ShouldBe(afterEdit);
            graph.Undo();
            graph.ToJson().ShouldBe(beforeSend);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(afterEdit);
        });
        (await scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>().ReadAsync()).ShouldBeNull();

        MudBlazor.MudChip<string> AttachButton() => components.Items.OfType<MudBlazor.MudChip<string>>().Last(chip => chip.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, "Attach main.source"));

        MudBlazor.MudIconButton Button(string label) => components.Items.OfType<MudBlazor.MudIconButton>()
            .Last(button => button.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsent_attachment_clears_on_navigation_or_same_name_document_replacement(bool replace)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.ResetHistory();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent view = default;
        string expected = "";
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
            var attach = components.Items.OfType<MudBlazor.MudChip<string>>()
                .Single(button => button.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Attach main.source"));
            await attach.OnClick.InvokeAsync(new MouseEventArgs());
            view.ToHtmlString().ShouldContain("chat-context-chip");
            if (replace)
            {
                graph.LoadJson(ConfigurationEditingTests.Original, "Application");
                components.Items.OfType<FluxFlow.Studio.UI.Features.Chat.ChatPanel>().Single().Clear();
            }
            else graph.SelectWorkflow("other");
            expected = graph.ToProjectJson();
        });
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            view.ToHtmlString().ShouldNotContain("chat-context-chip");
            graph.ToProjectJson().ShouldBe(expected);
            graph.CanUndo.ShouldBeFalse();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Chat_attaches_multiple_components_without_duplicates_and_removes_only_one(bool reattach)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var generator = new PendingChatGenerator();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IProposalGenerator>(generator);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.AddNode(graph.FindDescription("scheduler")!);
        graph.ResetHistory();
        var nodes = graph.Diagram.Nodes.OfType<FlowNodeModel>().ToArray();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Task sending = Task.CompletedTask;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            view.ToHtmlString().ShouldNotContain("Attach selection");
            graph.Diagram.SelectModel(nodes[0], true);
            view.ToHtmlString().ShouldContain("chat-context-suggestion");
            view.ToHtmlString().ShouldNotContain("chat-context-chip");
            await Attach(nodes[0].NodeName).OnClick.InvokeAsync(new MouseEventArgs());
            graph.Diagram.SelectModel(nodes[1], true);
            await Attach(nodes[1].NodeName).OnClick.InvokeAsync(new MouseEventArgs());
            graph.Diagram.SelectModel(nodes[0], false);
            Chips().Length.ShouldBe(2);
            view.ToHtmlString().ShouldNotContain("aria-label=\"Attach main.source\"");
            var first = components.Items.OfType<MudBlazor.MudChip<string>>().First(chip => chip.OnClose.HasDelegate);
            first.OnClick.HasDelegate.ShouldBeFalse();
            await first.OnClose.InvokeAsync(first);
            Chips().Length.ShouldBe(1);
            Chips().Single().ShouldBe("main." + nodes[1].NodeName);
            view.ToHtmlString().ShouldContain("chat-context-suggestion");
            Attach(nodes[0].NodeName).OnClose.HasDelegate.ShouldBeFalse();
            if (reattach)
            {
                await Attach(nodes[0].NodeName).OnClick.InvokeAsync(new MouseEventArgs());
                Chips().Length.ShouldBe(2);
            }
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();
            await components.Items.OfType<MudBlazor.MudTextField<string>>().Single().ValueChanged.InvokeAsync("Explain these components.");
            sending = components.Items.OfType<MudBlazor.MudIconButton>().Last(button =>
                button.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Send")).OnClick.InvokeAsync(new MouseEventArgs());
            var expected = reattach ? nodes : [nodes[1]];
            generator.Request!.SelectedComponents.Order().ShouldBe(expected.Select(node => "main." + node.NodeName).Order());
            generator.Request.Configuration.ShouldBe(graph.ToJson());
            components.Items.OfType<FluxFlow.Studio.UI.Features.Chat.ChatPanel>().Single().Clear();
            generator.Token.IsCancellationRequested.ShouldBeTrue();
            generator.Completion.SetResult(FixedProposalGenerator.Result());

            MudBlazor.MudChip<string> Attach(string name) => components.Items.OfType<MudBlazor.MudChip<string>>().Last(chip =>
                chip.UserAttributes.TryGetValue("aria-label", out var label) && Equals(label, "Attach main." + name));
            string[] Chips() => System.Text.RegularExpressions.Regex.Matches(view.ToHtmlString(), "title=\"(main\\.[^\"]+)\"")
                .Select(match => match.Groups[1].Value).ToArray();
        });
        await sending;
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        (await scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>().ReadAsync()).ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Workflow_chips_survive_navigation_capture_the_active_target_and_cancel_without_edits(bool removeOther)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var generator = new PendingChatGenerator();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IProposalGenerator>(generator);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.SelectWorkflow("other"); graph.SelectWorkflow("main");
        graph.ResetHistory();
        var configuration = graph.ToJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Task sending = Task.CompletedTask;
        Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent view = default;
        string before = "";
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            await Suggestion("Attach workflow main").OnClick.InvokeAsync(new MouseEventArgs());
            graph.SelectWorkflow("other");
            view.ToHtmlString().ShouldContain("Workflow: main");
            await Suggestion("Attach workflow other").OnClick.InvokeAsync(new MouseEventArgs());
            graph.SelectWorkflow("main");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Attach workflow main\"");
            if (removeOther)
            {
                var chip = components.Items.OfType<MudBlazor.MudChip<string>>().Last(item => item.OnClose.HasDelegate &&
                    item.UserAttributes.TryGetValue("title", out var title) && Equals(title, "Workflow: other"));
                await chip.OnClose.InvokeAsync(chip);
            }
            graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
            await Suggestion("Attach main.source").OnClick.InvokeAsync(new MouseEventArgs());
            await components.Items.OfType<MudBlazor.MudTextField<string>>().Single().ValueChanged.InvokeAsync("Compare these workflows.");
            sending = Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Request!.Workflow.ShouldBe("main");
            generator.Request.SelectedWorkflows.ShouldBe(removeOther ? new[] { "main" } : new[] { "main", "other" });
            generator.Request.SelectedComponents.ShouldBe(new[] { "main.source" });
            generator.Request.Configuration.ShouldBe(configuration);
            graph.SelectWorkflow("other");
            view.ToHtmlString().ShouldContain("Chat &#x2014; main");
            await Button("Stop").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Token.IsCancellationRequested.ShouldBeTrue();
            await Suggestion("Attach workflow other").OnClick.InvokeAsync(new MouseEventArgs());
            await components.Items.OfType<MudBlazor.MudTextField<string>>().Single().ValueChanged.InvokeAsync("Explain this workflow next.");
            before = graph.ToProjectJson();
            generator.Completion.SetResult(FixedProposalGenerator.Result());
        });
        await sending;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            view.ToHtmlString().ShouldContain("Request cancelled");
            view.ToHtmlString().ShouldNotContain("aria-label=\"Attach workflow other\"");
            view.ToHtmlString().ShouldContain("Explain this workflow next.");
            var attachment = components.Items.OfType<MudBlazor.MudChip<string>>().Last(item => item.OnClose.HasDelegate &&
                item.UserAttributes.TryGetValue("title", out var title) && Equals(title, "Workflow: other"));
            attachment.OnClick.HasDelegate.ShouldBeFalse();
            await attachment.OnClose.InvokeAsync(attachment);
            view.ToHtmlString().ShouldContain("aria-label=\"Attach workflow other\"");
            graph.ToProjectJson().ShouldBe(before);
            graph.ToJson().ShouldBe(configuration);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();
            components.Items.OfType<FluxFlow.Studio.UI.Features.Chat.ChatPanel>().Single().Clear();
            view.ToHtmlString().ShouldNotContain("chat-context-chip");
        });
        (await scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>().ReadAsync()).ShouldBeNull();

        MudBlazor.MudChip<string> Suggestion(string label) => components.Items.OfType<MudBlazor.MudChip<string>>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
        MudBlazor.MudIconButton Button(string label) => components.Items.OfType<MudBlazor.MudIconButton>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    [Fact]
    public async Task File_and_image_chips_capture_contents_support_removal_and_preserve_the_next_draft_after_cancellation()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var generator = new PendingChatGenerator();
        var files = new QueuedFiles();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IProposalGenerator>(generator);
        services.AddSingleton<IStudioFileAccess>(files);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent view = default;
        Task sending = Task.CompletedTask;
        const string sample = " {\"Reading\":12,\"reading\":3} \n";
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            await Pick(null);
            view.ToHtmlString().ShouldNotContain("chat-context-chip");
            await Pick(new("payload.json", "{}", "private-location"));
            await Pick(new("notes.txt", "Keep exact case."));
            await Pick(new("schema.txt", "Schema reference"));
            await Pick(new("example.txt", "Another example"));
            var picker = System.Text.RegularExpressions.Regex.Match(view.ToHtmlString(), "<button\\b[^>]*aria-label=\"Attach file\"[^>]*>").Value;
            picker.ShouldNotBeEmpty();
            picker.ShouldNotContain("disabled");
            await Pick(new("extra.txt", "Fifth distinct file"));
            view.ToHtmlString().ShouldContain("Attach up to four");
            view.ToHtmlString().ShouldNotContain("title=\"extra.txt\"");
            await Pick(new("payload.json", sample));
            foreach (var name in new[] { "notes.txt", "schema.txt", "example.txt" })
            {
                var removed = Chip(name);
                await removed.OnClose.InvokeAsync(removed);
                view.ToHtmlString().ShouldNotContain(name);
            }
            await Pick(new("image.png", "not text"));
            view.ToHtmlString().ShouldContain("Choose a .json or .txt file");
            await Pick(new("large.txt", new string('x', TextAttachment.MaxFileBytes + 1)));
            view.ToHtmlString().ShouldContain("32 KB or smaller");
            view.ToHtmlString().ShouldNotContain("title=\"large.txt\"");
            await PickImage(null);
            await PickImage(ImageSamples.Png);
            await PickImage(ImageSamples.Jpeg);
            System.Net.WebUtility.HtmlDecode(view.ToHtmlString()).ShouldContain($"src=\"data:image/png;base64,{ImageSamples.Png.Base64}\"");
            view.ToHtmlString().ShouldContain("data:image/jpeg;base64,");
            await PickImage(ImageSamples.Png with { Name = "third.png" });
            view.ToHtmlString().ShouldContain("at most two images");
            view.ToHtmlString().ShouldNotContain("third.png");
            await PickImage(ImageSamples.Png);
            var jpeg = Chip(ImageSamples.Jpeg.Name);
            await jpeg.OnClose.InvokeAsync(jpeg);
            view.ToHtmlString().ShouldNotContain("data:image/jpeg;base64,");
            await components.Items.OfType<MudBlazor.MudTextField<string>>().Single().ValueChanged.InvokeAsync("Map this sample.");
            sending = Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Request!.Attachments.ShouldBe(new[] { new TextAttachment("payload.json", sample) });
            generator.Request.Images.ShouldBe(new[] { ImageSamples.Png });
            generator.Request.Configuration.ShouldBe(graph.ToJson());
            await Button("Stop").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Token.IsCancellationRequested.ShouldBeTrue();
            await Pick(new("next.txt", "Next draft"));
            await PickImage(ImageSamples.Jpeg);
            generator.Completion.SetResult(FixedProposalGenerator.Result());
        });
        await sending;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            view.ToHtmlString().ShouldContain("Request cancelled");
            view.ToHtmlString().ShouldContain("next.txt");
            view.ToHtmlString().ShouldContain("payload.json");
            generator.Request!.Attachments.ShouldBe(new[] { new TextAttachment("payload.json", sample) });
            generator.Request.Images.ShouldBe(new[] { ImageSamples.Png });
            var sentImage = components.Items.OfType<FluxFlow.Studio.UI.Features.Chat.ChatImageChip>().Last(item => item.Image.Name == ImageSamples.Png.Name);
            sentImage.OnRemove.HasDelegate.ShouldBeFalse();
            var nextImage = Chip(ImageSamples.Jpeg.Name);
            await nextImage.OnClose.InvokeAsync(nextImage);
            view.ToHtmlString().ShouldNotContain("data:image/jpeg;base64,");
            var next = Chip("next.txt");
            await next.OnClose.InvokeAsync(next);
            view.ToHtmlString().ShouldNotContain("next.txt");
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();
            components.Items.OfType<FluxFlow.Studio.UI.Features.Chat.ChatPanel>().Single().Clear();
            view.ToHtmlString().ShouldNotContain("chat-context-chip");
            view.ToHtmlString().ShouldNotContain("chat-thumbnail");
        });
        (await scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>().ReadAsync()).ShouldBeNull();

        async Task Pick(StudioFile? file)
        {
            files.Results.Enqueue(Task.FromResult(file));
            await (await AttachmentItem(renderer, components, "Attach text file")).OnClick.InvokeAsync(new MouseEventArgs());
        }
        async Task PickImage(ImageAttachment? image)
        {
            files.Images.Enqueue(Task.FromResult(image is null ? null : new StudioImageFile(image.Name, image.MediaType, image.Base64)));
            await (await AttachmentItem(renderer, components, "Attach image")).OnClick.InvokeAsync(new MouseEventArgs());
        }
        MudBlazor.MudChip<string> Chip(string name) => components.Items.OfType<MudBlazor.MudChip<string>>().Last(item =>
            item.OnClose.HasDelegate &&
            (item.UserAttributes.TryGetValue("title", out var value) || item.UserAttributes.TryGetValue("aria-label", out value)) && Equals(value, name));
        MudBlazor.MudIconButton Button(string label) => components.Items.OfType<MudBlazor.MudIconButton>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task Clearing_chat_or_cancelling_edit_ignores_a_pending_file_picker_result_or_failure(bool fail, bool image, bool cancelEdit)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var files = new QueuedFiles();
        var pending = new TaskCompletionSource<StudioFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingImage = new TaskCompletionSource<StudioImageFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (image) files.Images.Enqueue(pendingImage.Task);
        else files.Results.Enqueue(pending.Task);
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IStudioFileAccess>(files);
        var generator = new QueuedChatGenerator();
        generator.Results.Enqueue(Task.FromResult(new ProposalResult { Message = "Reviewed." }));
        services.AddSingleton<IProposalGenerator>(generator);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            var message = components.Items.OfType<MudBlazor.MudTextField<string>>().Single();
            if (cancelEdit)
            {
                await message.ValueChanged.InvokeAsync("Explain this workflow.");
                await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
                await message.ValueChanged.InvokeAsync("Keep my unfinished draft");
                await Button("Edit and resend").OnClick.InvokeAsync(new MouseEventArgs());
            }
            var button = await AttachmentItem(renderer, components, image ? "Attach image" : "Attach text file");
            var picking = button.OnClick.InvokeAsync(new MouseEventArgs());
            if (cancelEdit) await Button("Cancel edit").OnClick.InvokeAsync(new MouseEventArgs());
            else components.Items.OfType<FluxFlow.Studio.UI.Features.Chat.ChatPanel>().Single().Clear();
            if (image) files.Images.Enqueue(Task.FromResult<StudioImageFile?>(new("current.png", "image/png", ImageSamples.Png.Base64)));
            else files.Results.Enqueue(Task.FromResult<StudioFile?>(new("current.txt", "Current draft")));
            await button.OnClick.InvokeAsync(new MouseEventArgs());
            if (image)
            {
                if (fail) pendingImage.SetException(new IOException("Old picker error"));
                else pendingImage.SetResult(new("old.png", "image/png", ImageSamples.Png.Base64));
            }
            else if (fail) pending.SetException(new IOException("Old picker error"));
            else pending.SetResult(new("old.txt", "Old draft"));
            await picking;
            view.ToHtmlString().ShouldContain(image ? "current.png" : "current.txt");
            view.ToHtmlString().ShouldNotContain("old.txt");
            view.ToHtmlString().ShouldNotContain("old.png");
            view.ToHtmlString().ShouldNotContain("Old picker error");
            view.ToHtmlString().ShouldNotContain("Editing message");
            message.GetState(x => x.Value).ShouldBe(cancelEdit ? "Keep my unfinished draft" : "");
            graph.ToProjectJson().ShouldBe(before);
        });

        MudBlazor.MudIconButton Button(string label) => components.Items.OfType<MudBlazor.MudIconButton>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Edit_and_resend_restores_captured_attachments_and_preserves_the_unfinished_draft(bool lateFailure)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var generator = new QueuedChatGenerator();
        var files = new QueuedFiles();
        var pending = new TaskCompletionSource<ProposalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        generator.Results.Enqueue(Task.FromResult(new ProposalResult { Message = "Reviewed." }));
        generator.Results.Enqueue(pending.Task);
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IProposalGenerator>(generator);
        services.AddSingleton<IStudioFileAccess>(files);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.SelectWorkflow("other"); graph.SelectWorkflow("main");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent view = default;
        Task sending = Task.CompletedTask;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            await Pick(new("sample.json", " {\"KeepCASE\":42} \n"), ImageSamples.Png);
            await Suggestion("Attach workflow main").OnClick.InvokeAsync(new MouseEventArgs());
            graph.SelectWorkflow("other");
            await Suggestion("Attach workflow other").OnClick.InvokeAsync(new MouseEventArgs());
            graph.SelectWorkflow("main");
            graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
            await Suggestion("Attach main.source").OnClick.InvokeAsync(new MouseEventArgs());
            await Message().ValueChanged.InvokeAsync("Explain this sample.");
            await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());

            await Message().ValueChanged.InvokeAsync("My unfinished draft");
            await Pick(new("draft.txt", "Keep this draft file."), ImageSamples.Jpeg);
            await Suggestion("Attach workflow main").OnClick.InvokeAsync(new MouseEventArgs());
            await Suggestion("Attach main.source").OnClick.InvokeAsync(new MouseEventArgs());
            await Button("Edit and resend").OnClick.InvokeAsync(new MouseEventArgs());
            Message().GetState(x => x.Value).ShouldBe("Explain this sample.");
            view.ToHtmlString().ShouldContain("Editing message");
            Button("Edit and resend").Disabled.ShouldBeTrue();
            Chip("sample.json").OnClose.HasDelegate.ShouldBeTrue();
            Chip(ImageSamples.Png.Name).OnClose.HasDelegate.ShouldBeTrue();
            Chip("Workflow: other").OnClose.HasDelegate.ShouldBeTrue();
            Chip("main.source").OnClose.HasDelegate.ShouldBeTrue();
            await Message().ValueChanged.InvokeAsync("An abandoned revision");
            await Button("Cancel edit").OnClick.InvokeAsync(new MouseEventArgs());
            Message().GetState(x => x.Value).ShouldBe("My unfinished draft");
            Chip("draft.txt").OnClose.HasDelegate.ShouldBeTrue();
            Chip(ImageSamples.Jpeg.Name).OnClose.HasDelegate.ShouldBeTrue();
            Chip("main.source").OnClose.HasDelegate.ShouldBeTrue();
            view.ToHtmlString().ShouldNotContain("Editing message");
            generator.Requests.Count.ShouldBe(1);

            await Button("Edit and resend").OnClick.InvokeAsync(new MouseEventArgs());
            await Message().ValueChanged.InvokeAsync("Explain the revised sample.");
            sending = Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Requests.Count.ShouldBe(2);
            var original = generator.Requests[0];
            var resend = generator.Requests[1];
            resend.Instructions.ShouldContain("Explain the revised sample.");
            resend.Attachments.ShouldBe(original.Attachments);
            resend.Images.ShouldBe(original.Images);
            resend.SelectedComponents.ShouldBe(original.SelectedComponents);
            resend.SelectedWorkflows.ShouldBe(original.SelectedWorkflows);
            Message().GetState(x => x.Value).ShouldBe("My unfinished draft");
            Chip("draft.txt").OnClose.HasDelegate.ShouldBeTrue();
            Chip(ImageSamples.Jpeg.Name).OnClose.HasDelegate.ShouldBeTrue();
            Chip("main.source").OnClose.HasDelegate.ShouldBeTrue();
            await Message().ValueChanged.InvokeAsync("Continue my unfinished draft");
            await Button("Stop").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Token.IsCancellationRequested.ShouldBeTrue();
            if (lateFailure) pending.SetException(new HttpRequestException("Late response failure"));
            else pending.SetResult(FixedProposalGenerator.Result());
        });
        await sending;
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Message().GetState(x => x.Value).ShouldBe("Continue my unfinished draft");
            view.ToHtmlString().ShouldContain("Request cancelled");
            view.ToHtmlString().ShouldNotContain("Late response failure");
            view.ToHtmlString().ShouldContain("Explain this sample.");
            Chip("draft.txt").OnClose.HasDelegate.ShouldBeTrue();
            Chip(ImageSamples.Jpeg.Name).OnClose.HasDelegate.ShouldBeTrue();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.CanRedo.ShouldBeFalse();
        });
        (await scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>().ReadAsync()).ShouldBeNull();

        async Task Pick(StudioFile file, ImageAttachment image)
        {
            files.Results.Enqueue(Task.FromResult<StudioFile?>(file));
            await (await AttachmentItem(renderer, components, "Attach text file")).OnClick.InvokeAsync(new MouseEventArgs());
            files.Images.Enqueue(Task.FromResult<StudioImageFile?>(new(image.Name, image.MediaType, image.Base64)));
            await (await AttachmentItem(renderer, components, "Attach image")).OnClick.InvokeAsync(new MouseEventArgs());
        }
        MudBlazor.MudTextField<string> Message() => components.Items.OfType<MudBlazor.MudTextField<string>>().Single();
        MudBlazor.MudChip<string> Suggestion(string label) => components.Items.OfType<MudBlazor.MudChip<string>>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
        MudBlazor.MudChip<string> Chip(string name) => components.Items.OfType<MudBlazor.MudChip<string>>().Last(item => item.OnClose.HasDelegate &&
            (item.UserAttributes.TryGetValue("title", out var value) || item.UserAttributes.TryGetValue("aria-label", out value)) && Equals(value, name));
        MudBlazor.MudIconButton Button(string label) => components.Items.OfType<MudBlazor.MudIconButton>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    [Theory]
    [InlineData(ChatEditingMode.Ask)]
    [InlineData(ChatEditingMode.Automatic)]
    public async Task Resend_revalidates_target_and_context_then_uses_current_configuration_and_existing_edit_history(ChatEditingMode mode)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var generator = new QueuedChatGenerator();
        generator.Results.Enqueue(Task.FromResult(new ProposalResult { Message = "Reviewed." }));
        generator.Results.Enqueue(Task.FromResult(FixedProposalGenerator.Result()));
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IProposalGenerator>(generator);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.ResetHistory();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
            var attach = components.Items.OfType<MudBlazor.MudChip<string>>().Single(item =>
                item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, "Attach main.source"));
            await attach.OnClick.InvokeAsync(new MouseEventArgs());
            await Message().ValueChanged.InvokeAsync("Explain this component.");
            await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            await Message().ValueChanged.InvokeAsync("Keep my draft");
            await Button("Edit and resend").OnClick.InvokeAsync(new MouseEventArgs());
            var rejectedMessage = new string('x', 8001);
            await Message().ValueChanged.InvokeAsync(rejectedMessage);
            await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            view.ToHtmlString().ShouldContain("8,000 characters");
            Message().GetState(x => x.Value).ShouldBe(rejectedMessage);
            generator.Requests.Count.ShouldBe(1);
            await Button("Cancel edit").OnClick.InvokeAsync(new MouseEventArgs());
            Message().GetState(x => x.Value).ShouldBe("Keep my draft");
            await Button("Edit and resend").OnClick.InvokeAsync(new MouseEventArgs());
            graph.SelectWorkflow("other");
            await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            view.ToHtmlString().ShouldContain("before resending");
            generator.Requests.Count.ShouldBe(1);
            Message().GetState(x => x.Value).ShouldBe("Explain this component.");
            graph.SelectWorkflow("main");
            graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
            graph.RenameSelectedNode("renamed");
            var beforeSend = graph.ToProjectJson();
            await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            view.ToHtmlString().ShouldContain("no longer in the active workflow");
            view.ToHtmlString().ShouldContain("Editing message");
            generator.Requests.Count.ShouldBe(1);
            graph.ToProjectJson().ShouldBe(beforeSend);
            var stale = components.Items.OfType<MudBlazor.MudChip<string>>().Last(item => item.OnClose.HasDelegate &&
                item.UserAttributes.TryGetValue("title", out var value) && Equals(value, "main.source"));
            await stale.OnClose.InvokeAsync(stale);
            await Message().ValueChanged.InvokeAsync("Add a mapper.");
            await components.Items.OfType<MudBlazor.MudSelect<ChatEditingMode>>().Single().ValueChanged.InvokeAsync(mode);
            var configuration = graph.ToJson();
            await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Requests.Count.ShouldBe(2);
            generator.Requests[1].SelectedComponents.ShouldBeEmpty();
            generator.Requests[1].Workflow.ShouldBe("main");
            generator.Requests[1].Configuration.ShouldBe(configuration);
            Message().GetState(x => x.Value).ShouldBe("Keep my draft");
            if (mode == ChatEditingMode.Ask)
            {
                graph.ToProjectJson().ShouldBe(beforeSend);
                Button("Edit and resend").Disabled.ShouldBeTrue();
                await components.Items.OfType<ProposalPreview>().Single().OnApply.InvokeAsync();
            }
            var applied = graph.ToProjectJson();
            applied.ShouldNotBe(beforeSend);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(beforeSend);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(applied);
            Message().GetState(x => x.Value).ShouldBe("Keep my draft");
        });

        MudBlazor.MudTextField<string> Message() => components.Items.OfType<MudBlazor.MudTextField<string>>().Single();
        MudBlazor.MudIconButton Button(string label) => components.Items.OfType<MudBlazor.MudIconButton>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Automatic_chat_stop_between_steps_preserves_completed_edits_draft_and_attachments(bool lateFailure)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var files = new QueuedFiles();
        var generator = new QueuedChatGenerator();
        var nextStep = new TaskCompletionSource<ProposalRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource<ProposalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStep = FixedProposalGenerator.Result() with { Continue = true };
        generator.Results.Enqueue(Task.FromResult(new ProposalResult { Message = "Reviewed." }));
        generator.Results.Enqueue(Task.FromResult(firstStep));
        generator.Results.Enqueue(late.Task);
        generator.Results.Enqueue(Task.FromResult(new ProposalResult { Message = "Reviewed draft." }));
        generator.RequestReceived = request => { if (generator.Requests.Count == 3) nextStep.SetResult(request); };
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IStudioFileAccess>(files);
        services.AddSingleton<IProposalGenerator>(generator);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var sample = new StudioFile("sample.json", " {\"KeepCASE\":42} \n");
        var draftFile = new StudioFile("draft.txt", "Keep this unfinished attachment.");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            await Pick(sample, ImageSamples.Png);
            await Message().ValueChanged.InvokeAsync("Review this sample.");
            await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            await Message().ValueChanged.InvokeAsync("My unfinished draft");
            await Pick(draftFile, ImageSamples.Jpeg);
            await components.Items.OfType<MudBlazor.MudSelect<ChatEditingMode>>().Single().ValueChanged.InvokeAsync(ChatEditingMode.Automatic);
            await Button("Edit and resend").OnClick.InvokeAsync(new MouseEventArgs());
            await Message().ValueChanged.InvokeAsync("Add the components in steps.");
            var sending = Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            var continuation = await nextStep.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var applied = graph.ToProjectJson();
            applied.ShouldNotBe(before);
            graph.CanUndo.ShouldBeTrue();
            graph.CanRedo.ShouldBeFalse();
            continuation.Configuration.ShouldBe(graph.ToJson());
            continuation.Steps.Single().Edits.ShouldBe(firstStep.Edits);
            continuation.Steps.Single().Outcome.ShouldBe("Applied.");
            continuation.Attachments.ShouldBe(new[] { new TextAttachment(sample.Name, sample.Content) });
            continuation.Images.ShouldBe(new[] { ImageSamples.Png });
            Message().GetState(x => x.Value).ShouldBe("My unfinished draft");
            var revision = graph.Revision;
            await Message().ValueChanged.InvokeAsync("Continue my unfinished draft");
            await Button("Stop").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Token.IsCancellationRequested.ShouldBeTrue();
            graph.ToProjectJson().ShouldBe(applied);
            if (lateFailure) late.SetException(new HttpRequestException("Late provider error"));
            else late.SetResult(new ProposalResult { Edits = ConfigurationEditingTests.Batch(), Continue = true });
            await sending;
            generator.Requests.Count.ShouldBe(3);
            view.ToHtmlString().ShouldContain("Request cancelled. Completed changes are retained.");
            view.ToHtmlString().ShouldNotContain("Late provider error");
            Message().GetState(x => x.Value).ShouldBe("Continue my unfinished draft");
            graph.ToProjectJson().ShouldBe(applied);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeTrue();
            graph.CanRedo.ShouldBeFalse();
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(applied);

            // Sending the restored draft verifies its text and attachment contents after cancellation.
            await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Requests.Count.ShouldBe(4);
            var restored = generator.Requests[3];
            restored.Instructions.ShouldEndWith("Continue my unfinished draft");
            restored.Configuration.ShouldBe(graph.ToJson());
            restored.Attachments.ShouldBe(new[] { new TextAttachment(draftFile.Name, draftFile.Content) });
            restored.Images.ShouldBe(new[] { ImageSamples.Jpeg });
            graph.ToProjectJson().ShouldBe(applied);
            graph.CanUndo.ShouldBeTrue();
            graph.CanRedo.ShouldBeFalse();
        });
        (await scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>().ReadAsync()).ShouldBeNull();

        async Task Pick(StudioFile file, ImageAttachment image)
        {
            files.Results.Enqueue(Task.FromResult<StudioFile?>(file));
            await (await AttachmentItem(renderer, components, "Attach text file")).OnClick.InvokeAsync(new MouseEventArgs());
            files.Images.Enqueue(Task.FromResult<StudioImageFile?>(new(image.Name, image.MediaType, image.Base64)));
            await (await AttachmentItem(renderer, components, "Attach image")).OnClick.InvokeAsync(new MouseEventArgs());
        }
        MudBlazor.MudTextField<string> Message() => components.Items.OfType<MudBlazor.MudTextField<string>>().Single();
        MudBlazor.MudIconButton Button(string label) => components.Items.OfType<MudBlazor.MudIconButton>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Failed_chat_recovers_through_resend_and_late_responses_preserve_approval_drafts_and_history(bool timeout, bool lateFailure)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var files = new QueuedFiles();
        var generator = new QueuedChatGenerator();
        var failed = new TaskCompletionSource<ProposalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource<ProposalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        generator.Results.Enqueue(failed.Task);
        generator.Results.Enqueue(late.Task);
        generator.Results.Enqueue(Task.FromResult(FixedProposalGenerator.Result()));
        generator.Results.Enqueue(Task.FromResult(new ProposalResult { Message = "Reviewed." }));
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IStudioFileAccess>(files);
        services.AddSingleton<IProposalGenerator>(generator);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.ResetHistory();
        graph.ApplyEdits(graph.PrepareEdits(ConfigurationEditingTests.Batch()));
        graph.Undo();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        var sample = new StudioFile("sample.json", " {\"KeepCASE\":42} \n");
        var draftFile = new StudioFile("draft.txt", "Keep this unfinished attachment.");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            await components.Items.OfType<MudBlazor.MudSelect<ChatEditingMode>>().Single().ValueChanged.InvokeAsync(ChatEditingMode.Ask);
            await Pick(sample, ImageSamples.Png);
            await Message().ValueChanged.InvokeAsync("Add a source and mapper using this sample.");
            var sending = Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            await Message().ValueChanged.InvokeAsync("My unfinished draft");
            var failure = timeout ? "Provider request timed out." : "Provider is unavailable.";
            if (timeout) failed.SetResult(new ProposalResult { Error = failure });
            else failed.SetException(new HttpRequestException(failure));
            await sending;
            view.ToHtmlString().ShouldContain(failure);
            Message().GetState(x => x.Value).ShouldBe("My unfinished draft");
            Button("Edit and resend").Disabled.ShouldBeFalse();
            var preview = components.Items.OfType<ProposalPreview>().Single();
            preview.Preview.Proposal.ShouldBeNull();
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();
            graph.CanRedo.ShouldBeTrue();
            generator.Requests.Count.ShouldBe(1);
            var original = generator.Requests[0];
            original.Attachments.ShouldBe(new[] { new TextAttachment(sample.Name, sample.Content) });
            original.Images.ShouldBe(new[] { ImageSamples.Png });

            await Pick(draftFile, ImageSamples.Jpeg);
            await Button("Edit and resend").OnClick.InvokeAsync(new MouseEventArgs());
            Message().GetState(x => x.Value).ShouldBe("Add a source and mapper using this sample.");
            await Message().ValueChanged.InvokeAsync("Retry with the same sample.");
            var cancelledSend = Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Requests.Count.ShouldBe(2);
            generator.Requests[1].Instructions.ShouldEndWith("Retry with the same sample.");
            generator.Requests[1].Attachments.ShouldBe(original.Attachments);
            generator.Requests[1].Images.ShouldBe(original.Images);
            Message().GetState(x => x.Value).ShouldBe("My unfinished draft");
            await Button("Stop").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Token.IsCancellationRequested.ShouldBeTrue();

            // Retry before the cancelled provider completes, leaving the new proposal awaiting approval.
            await Button("Edit and resend").OnClick.InvokeAsync(new MouseEventArgs());
            await Message().ValueChanged.InvokeAsync("Try again after reconnecting.");
            await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Requests.Count.ShouldBe(3);
            var retry = generator.Requests[2];
            retry.Instructions.ShouldEndWith("Try again after reconnecting.");
            retry.Workflow.ShouldBe(original.Workflow);
            retry.Configuration.ShouldBe(original.Configuration);
            retry.Attachments.ShouldBe(original.Attachments);
            retry.Images.ShouldBe(original.Images);
            var proposal = preview.Preview.Proposal.ShouldNotBeNull();
            preview.Preview.IsStale.ShouldBeFalse();
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
            graph.CanRedo.ShouldBeTrue();
            Message().GetState(x => x.Value).ShouldBe("My unfinished draft");
            await Message().ValueChanged.InvokeAsync("Continue my unfinished draft");
            var responses = components.Items.OfType<FluxFlow.Studio.UI.Components.Markdown.MarkdownView>()
                .Select(response => response.Text).ToArray();
            if (lateFailure) late.SetException(new HttpRequestException("Late provider error"));
            else late.SetResult(FixedProposalGenerator.Result());
            await cancelledSend;
            preview.Preview.Proposal.ShouldBeSameAs(proposal);
            preview.Preview.IsStale.ShouldBeFalse();
            components.Items.OfType<FluxFlow.Studio.UI.Components.Markdown.MarkdownView>()
                .Select(response => response.Text).ShouldBe(responses);
            Message().GetState(x => x.Value).ShouldBe("Continue my unfinished draft");
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();
            graph.CanRedo.ShouldBeTrue();

            await preview.OnApply.InvokeAsync();
            preview.Preview.Proposal.ShouldBeNull();
            var applied = graph.ToProjectJson();
            applied.ShouldNotBe(before);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(applied);
            Message().GetState(x => x.Value).ShouldBe("Continue my unfinished draft");

            // Verify that recovery retained the draft's actual attachments, not only their labels.
            await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Requests.Count.ShouldBe(4);
            var restored = generator.Requests[3];
            restored.Instructions.ShouldEndWith("Continue my unfinished draft");
            restored.Attachments.ShouldBe(new[] { new TextAttachment(draftFile.Name, draftFile.Content) });
            restored.Images.ShouldBe(new[] { ImageSamples.Jpeg });
            graph.ToProjectJson().ShouldBe(applied);
            graph.CanUndo.ShouldBeTrue();
            graph.CanRedo.ShouldBeFalse();
        });
        (await scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>().ReadAsync()).ShouldBeNull();

        async Task Pick(StudioFile file, ImageAttachment image)
        {
            files.Results.Enqueue(Task.FromResult<StudioFile?>(file));
            await (await AttachmentItem(renderer, components, "Attach text file")).OnClick.InvokeAsync(new MouseEventArgs());
            files.Images.Enqueue(Task.FromResult<StudioImageFile?>(new(image.Name, image.MediaType, image.Base64)));
            await (await AttachmentItem(renderer, components, "Attach image")).OnClick.InvokeAsync(new MouseEventArgs());
        }
        MudBlazor.MudTextField<string> Message() => components.Items.OfType<MudBlazor.MudTextField<string>>().Single();
        MudBlazor.MudIconButton Button(string label) => components.Items.OfType<MudBlazor.MudIconButton>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dictated_chat_with_attachments_requires_approval_and_cancelled_resend_preserves_draft_and_history(bool changeColor)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var dictation = new PendingDictation();
        var files = new QueuedFiles();
        var generator = new QueuedChatGenerator();
        var lateResponse = new TaskCompletionSource<ProposalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var proposal = changeColor ? new ProposalResult
        {
            Tool = new("set_component_color", JsonSerializer.SerializeToElement(new { Address = "main.source", Color = "#0000FF" })),
            Message = "Change component color."
        } : FixedProposalGenerator.Result();
        generator.Results.Enqueue(Task.FromResult(proposal));
        generator.Results.Enqueue(lateResponse.Task);
        generator.Results.Enqueue(Task.FromResult(new ProposalResult { Message = "Reviewed." }));
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Studio.UI.Features.Chat.Dictation.IDictationService>(dictation);
        services.AddSingleton<IStudioFileAccess>(files);
        services.AddSingleton<IProposalGenerator>(generator);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.ResetHistory();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        await preferences.SaveAsync(preferences.Value with { Dictation = new() { Enabled = true, Model = "installed-transcriber" } });
        var storage = scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>();
        var savedSettings = await storage.ReadAsync();
        var sample = new StudioFile("sample.json", " {\"KeepCASE\":42} \n");
        var unfinishedFile = new StudioFile("draft.txt", "Keep this unfinished attachment.");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            await Pick(sample, ImageSamples.Png);
            await Suggestion("Attach workflow main").OnClick.InvokeAsync(new MouseEventArgs());
            graph.SelectWorkflow("other");
            await Suggestion("Attach workflow other").OnClick.InvokeAsync(new MouseEventArgs());
            graph.SelectWorkflow("main");
            graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
            await Suggestion("Attach main.source").OnClick.InvokeAsync(new MouseEventArgs());
            await components.Items.OfType<MudBlazor.MudSelect<ChatEditingMode>>().Single().ValueChanged.InvokeAsync(ChatEditingMode.Ask);
            var before = graph.ToProjectJson();
            var configuration = graph.ToJson();

            await Message().ValueChanged.InvokeAsync("Use the attached sample.");
            await Button("Start dictation").OnClick.InvokeAsync(new MouseEventArgs());
            Button("Send").Disabled.ShouldBeTrue();
            var transcribing = Button("Finish dictation").OnClick.InvokeAsync(new MouseEventArgs());
            await Message().ValueChanged.InvokeAsync("Use the attached sample and keep its casing.");
            dictation.Completion.SetResult("Add a source and mapper.");
            await transcribing;
            const string requestText = "Use the attached sample and keep its casing. Add a source and mapper.";
            Message().GetState(x => x.Value).ShouldBe(requestText);
            generator.Requests.ShouldBeEmpty();
            dictation.Cancels.ShouldBe(1);
            graph.ToProjectJson().ShouldBe(before);

            await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Requests.Count.ShouldBe(1);
            var captured = generator.Requests[0];
            captured.Instructions.ShouldContain(requestText);
            captured.Workflow.ShouldBe("main");
            captured.Configuration.ShouldBe(configuration);
            captured.SelectedComponents.ShouldBe(new[] { "main.source" });
            captured.SelectedWorkflows.ShouldBe(new[] { "main", "other" });
            captured.Attachments.ShouldBe(new[] { new TextAttachment(sample.Name, sample.Content) });
            captured.Images.ShouldBe(new[] { ImageSamples.Png });
            Message().GetState(x => x.Value).ShouldBe("");
            var preview = components.Items.OfType<ProposalPreview>().Single();
            preview.Preview.Proposal.ShouldNotBeNull();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            await preview.OnApply.InvokeAsync();
            var applied = graph.ToProjectJson();
            applied.ShouldNotBe(before);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(applied);
            graph.Undo();
            var revision = graph.Revision;

            await Message().ValueChanged.InvokeAsync("Keep my unfinished draft");
            await Pick(unfinishedFile, ImageSamples.Jpeg);
            await Button("Edit and resend").OnClick.InvokeAsync(new MouseEventArgs());
            Message().GetState(x => x.Value).ShouldBe(requestText);
            await Message().ValueChanged.InvokeAsync("Try the same change again.");
            var sending = Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Requests.Count.ShouldBe(2);
            var resend = generator.Requests[1];
            resend.Instructions.ShouldEndWith("Try the same change again.");
            resend.Configuration.ShouldBe(configuration);
            resend.Workflow.ShouldBe("main");
            resend.SelectedComponents.ShouldBe(captured.SelectedComponents);
            resend.SelectedWorkflows.ShouldBe(captured.SelectedWorkflows);
            resend.Attachments.ShouldBe(captured.Attachments);
            resend.Images.ShouldBe(captured.Images);
            Message().GetState(x => x.Value).ShouldBe("Keep my unfinished draft");
            await Message().ValueChanged.InvokeAsync("Continue my unfinished draft");
            await Button("Stop").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Token.IsCancellationRequested.ShouldBeTrue();
            lateResponse.SetResult(proposal);
            await sending;

            view.ToHtmlString().ShouldContain("Request cancelled");
            preview.Preview.Proposal.ShouldBeNull();
            Message().GetState(x => x.Value).ShouldBe("Continue my unfinished draft");
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();
            graph.CanRedo.ShouldBeTrue();
            graph.Redo();
            graph.ToProjectJson().ShouldBe(applied);
            graph.Undo();
            graph.ToProjectJson().ShouldBe(before);
            // Verify restored attachment contents through the next request.
            await Button("Send").OnClick.InvokeAsync(new MouseEventArgs());
            generator.Requests.Count.ShouldBe(3);
            var resumed = generator.Requests[2];
            resumed.Instructions.ShouldEndWith("Continue my unfinished draft");
            resumed.Workflow.ShouldBe("main");
            resumed.Configuration.ShouldBe(configuration);
            resumed.SelectedComponents.ShouldBeEmpty();
            resumed.SelectedWorkflows.ShouldBeEmpty();
            resumed.Attachments.ShouldBe(new[] { new TextAttachment(unfinishedFile.Name, unfinishedFile.Content) });
            resumed.Images.ShouldBe(new[] { ImageSamples.Jpeg });
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.CanRedo.ShouldBeTrue();
        });
        (await storage.ReadAsync()).ShouldBe(savedSettings);

        async Task Pick(StudioFile file, ImageAttachment image)
        {
            files.Results.Enqueue(Task.FromResult<StudioFile?>(file));
            await (await AttachmentItem(renderer, components, "Attach text file")).OnClick.InvokeAsync(new MouseEventArgs());
            files.Images.Enqueue(Task.FromResult<StudioImageFile?>(new(image.Name, image.MediaType, image.Base64)));
            await (await AttachmentItem(renderer, components, "Attach image")).OnClick.InvokeAsync(new MouseEventArgs());
        }
        MudBlazor.MudTextField<string> Message() => components.Items.OfType<MudBlazor.MudTextField<string>>().Single();
        MudBlazor.MudChip<string> Suggestion(string label) => components.Items.OfType<MudBlazor.MudChip<string>>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
        MudBlazor.MudIconButton Button(string label) => components.Items.OfType<MudBlazor.MudIconButton>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("fa")]
    [InlineData("en")]
    public async Task Dictation_language_autosaves_from_the_existing_settings_control(string language)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>();
        var saved = JsonSerializer.SerializeToNode(new StudioSettings { Dictation = new() { Enabled = true, Model = "installed-transcriber" } },
            StudioJsonContext.Default.StudioSettings)!;
        saved["Dictation"]!.AsObject().Remove("Language");
        await storage.WriteAsync(saved.ToJsonString());
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        await preferences.LoadAsync();
        preferences.Value.Dictation.Language.ShouldBe("auto");
        var original = preferences.Value;
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<DictationSettingsEditor>();
            var selector = components.Items.OfType<MudBlazor.MudSelect<string>>().Single(item => item.Label == "Language");
            await selector.ValueChanged.InvokeAsync(language);
            preferences.Value.ShouldBe(original with { Dictation = original.Dictation with { Language = language } });
            preferences.SaveStatus.ShouldBe("Saved");
        });
        var reopened = new StudioPreferences(storage);
        await reopened.LoadAsync();
        JsonSerializer.Serialize(reopened.Value, StudioJsonContext.Default.StudioSettings).ShouldBe(JsonSerializer.Serialize(preferences.Value, StudioJsonContext.Default.StudioSettings));
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        graph.CanRedo.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Local", "auto")]
    [InlineData("Local", "fa")]
    [InlineData("Local", "en")]
    [InlineData("Remote", "auto")]
    [InlineData("Remote", "fa")]
    [InlineData("Remote", "en")]
    public async Task Dictation_appends_to_the_current_draft_without_sending_or_changing_attachments_or_configuration(string location, string language)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var dictation = new PendingDictation();
        var files = new QueuedFiles();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Studio.UI.Features.Chat.Dictation.IDictationService>(dictation);
        services.AddSingleton<IStudioFileAccess>(files);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        await preferences.SaveAsync(preferences.Value with { Dictation = new() { Enabled = true, Location = location, Language = language, Model = "installed-transcriber" } });
        var reopened = new StudioPreferences(scope.ServiceProvider.GetRequiredService<IStudioSettingsStorage>());
        await reopened.LoadAsync();
        JsonSerializer.Serialize(reopened.Value, StudioJsonContext.Default.StudioSettings).ShouldBe(JsonSerializer.Serialize(preferences.Value, StudioJsonContext.Default.StudioSettings));
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            files.Results.Enqueue(Task.FromResult<StudioFile?>(new("notes.txt", "Unchanged attachment")));
            await (await AttachmentItem(renderer, components, "Attach text file")).OnClick.InvokeAsync(new MouseEventArgs());
            var message = components.Items.OfType<MudBlazor.MudTextField<string>>().Single();
            await message.ValueChanged.InvokeAsync("Existing draft.");
            await Button("Start dictation").OnClick.InvokeAsync(new MouseEventArgs());
            Button("Send").Disabled.ShouldBeTrue();
            var stopping = Button("Finish dictation").OnClick.InvokeAsync(new MouseEventArgs());
            await message.ValueChanged.InvokeAsync("Edited draft while transcribing.");
            dictation.Completion.SetResult("New dictated text.");
            await stopping;
            message.GetState(x => x.Value).ShouldBe("Edited draft while transcribing. New dictated text.");
            dictation.Settings!.Model.ShouldBe("installed-transcriber");
            dictation.Settings.Location.ShouldBe(location);
            dictation.Settings.Language.ShouldBe(language);
            view.ToHtmlString().ShouldContain("notes.txt");
            view.ToHtmlString().ShouldContain("Work on your workflow");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            graph.CanRedo.ShouldBeFalse();
            dictation.Cancels.ShouldBe(1);
        });
        MudBlazor.MudIconButton Button(string label) => components.Items.OfType<MudBlazor.MudIconButton>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    [Theory]
    [InlineData("cancel", false)]
    [InlineData("cancel-recording", false)]
    [InlineData("clear", false)]
    [InlineData("workflow", false)]
    [InlineData("settings", false)]
    [InlineData("location", false)]
    [InlineData("model", false)]
    [InlineData("language", false)]
    [InlineData("cancel", true)]
    [InlineData("settings", true)]
    [InlineData("language", true)]
    public async Task Dictation_cancellation_ignores_late_results_and_preserves_the_draft(string action, bool waitingForPermission)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var dictation = new PendingDictation();
        if (waitingForPermission) dictation.Startup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<FluxFlow.Studio.UI.Features.Chat.Dictation.IDictationService>(dictation);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.SelectWorkflow("other"); graph.SelectWorkflow("main");
        graph.ResetHistory();
        var before = graph.ToJson();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        await preferences.SaveAsync(preferences.Value with { Dictation = new() { Enabled = true, Model = "installed-transcriber" } });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Features.Chat.ChatPanel>();
            var message = components.Items.OfType<MudBlazor.MudTextField<string>>().Single();
            await message.ValueChanged.InvokeAsync("Keep draft");
            var pending = Button("Start dictation").OnClick.InvokeAsync(new MouseEventArgs());
            if (!waitingForPermission)
            {
                await pending;
                if (action != "cancel-recording") pending = Button("Finish dictation").OnClick.InvokeAsync(new MouseEventArgs());
            }
            if (action is "cancel" or "cancel-recording") await Button("Cancel dictation").OnClick.InvokeAsync(new MouseEventArgs());
            else if (action == "clear") components.Items.OfType<FluxFlow.Studio.UI.Features.Chat.ChatPanel>().Single().Clear();
            else if (action == "workflow") graph.SelectWorkflow("other");
            else
            {
                var settings = preferences.Value.Dictation;
                await preferences.SaveAsync(preferences.Value with { Dictation = action switch
                {
                    "location" => settings with { Location = "Remote" },
                    "model" => settings with { Model = "another-transcriber" },
                    "language" => settings with { Language = "fa" },
                    _ => settings with { Enabled = false }
                } });
            }
            dictation.Token.IsCancellationRequested.ShouldBeTrue();
            await message.ValueChanged.InvokeAsync("Current draft");
            dictation.Completion.SetResult("Late text must not appear");
            dictation.Startup?.SetResult();
            await pending;
            message.GetState(x => x.Value).ShouldBe("Current draft");
            graph.ToJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            dictation.Cancels.ShouldBe(1);
        });
        MudBlazor.MudIconButton Button(string label) => components.Items.OfType<MudBlazor.MudIconButton>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    private sealed class PendingDictation : FluxFlow.Studio.UI.Features.Chat.Dictation.IDictationService
    {
        public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Startup;
        public CancellationToken Token;
        public FluxFlow.Conversations.Dictation.DictationSettings? Settings;
        public int Cancels;
        public Task StartAsync(CancellationToken cancellationToken) { Token = cancellationToken; return Startup?.Task ?? Task.CompletedTask; }
        public Task<string> StopAsync(FluxFlow.Conversations.Dictation.DictationSettings settings, CancellationToken cancellationToken)
        { Settings = settings; Token = cancellationToken; return Completion.Task; }
        public ValueTask CancelAsync() { Cancels++; return ValueTask.CompletedTask; }
    }

    private sealed class QueuedChatGenerator : IProposalGenerator
    {
        public Queue<Task<ProposalResult>> Results { get; } = new();
        public List<ProposalRequest> Requests { get; } = [];
        public Action<ProposalRequest>? RequestReceived { get; set; }
        public CancellationToken Token { get; private set; }
        public Task<ProposalResult> GenerateAsync(ProposalRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Token = cancellationToken;
            var result = Results.Dequeue();
            RequestReceived?.Invoke(request);
            return result;
        }
    }

    private static async Task<MudBlazor.MudMenuItem> AttachmentItem(HtmlRenderer renderer, CapturingComponents components, string label)
    {
        // Static rendering has no browser popover lifecycle; render the existing menu contents directly.
        var menu = components.Items.OfType<MudBlazor.MudMenu>().Single(item => item.AriaLabel == "Attach file");
        await renderer.RenderComponentAsync<MudBlazor.MudElement>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(MudBlazor.MudElement.ChildContent)] = menu.ChildContent }));
        return components.Items.OfType<MudBlazor.MudMenuItem>().Last(item =>
            item.UserAttributes.TryGetValue("aria-label", out var value) && Equals(value, label));
    }

    private sealed class QueuedFiles : IStudioFileAccess
    {
        public Queue<Task<StudioFile?>> Results { get; } = new();
        public Queue<Task<StudioImageFile?>> Images { get; } = new();
        public Task<StudioImageFile?> OpenImageAsync() => Images.Dequeue();
        public Task<StudioFile?> OpenAsync(string extension)
        {
            extension.ShouldBe(".json,.txt");
            return Results.Dequeue();
        }
        public Task<StudioFile?> SaveAsync(StudioFile file, bool saveAs = false) => throw new InvalidOperationException("Attachments must not save files.");
    }

    private sealed class PendingChatGenerator : IProposalGenerator
    {
        public ProposalRequest? Request { get; private set; }
        public TaskCompletionSource<ProposalResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Task<ProposalResult> GenerateAsync(ProposalRequest request, CancellationToken cancellationToken = default)
        { Request = request; Token = cancellationToken; return Completion.Task; }
    }

    private sealed class CapturingComponents : IComponentActivator
    {
        public List<IComponent> Items { get; } = [];
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            Items.Add(component);
            return component;
        }
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJSRuntime, StubJavaScript>();
        services.AddSingleton<NavigationManager, TestNavigation>();
        services.AddSingleton<IStudioSettingsStorage, MemorySettings>();
        services.AddSingleton<IStudioFileAccess, UnusedFiles>();
        services.AddSingleton<FluxFlow.Conversations.IProposalGenerator, FixedProposalGenerator>();
        var bluetooth = new FluxFlow.Plugins.Bluetooth.BluetoothPlugin();
        services.AddStudioUI([.. StudioTestServices.BrokerCatalog, bluetooth.Description]);
        services.AddScoped<FluxFlow.Components.Bluetooth.IBleExplorer, FluxFlow.Components.Bluetooth.UnavailableBleExplorer>();
        FluxFlow.Plugins.Bluetooth.Design.BleInfrastructure.Register(services);
        FluxFlow.Studio.UI.Features.Plugins.PortableProperties.Configure(services, bluetooth.Description, new FluxFlow.Plugins.Bluetooth.Design.BluetoothDesign(), inProcess: true);
        StudioTestServices.AddSourcesPlugin(services);
        StudioTestServices.AddLoggingPlugin(services);
        StudioTestServices.AddRoutingPlugin(services);
        StudioTestServices.AddTransformationPlugin(services);
        StudioTestServices.AddFlowControlPlugin(services);
        StudioTestServices.AddHttpPlugin(services);
        StudioTestServices.AddRosPlugin(services);
        StudioTestServices.AddSerialPlugin(services);
        StudioTestServices.AddMqttDesign(services);
        StudioTestServices.AddRabbitDesign(services);
        StudioTestServices.AddNatsDesign(services);
        return services;
    }

    private sealed class MemorySettings : IStudioSettingsStorage
    {
        private string? _json;
        public Task WriteDelay { get; set; } = Task.CompletedTask;
        public Task<string?> ReadAsync() => Task.FromResult(_json);
        public async Task WriteAsync(string json) { await WriteDelay; _json = json; }
    }

    private sealed class UnusedFiles : IStudioFileAccess
    {
        public Task<StudioImageFile?> OpenImageAsync() => throw new InvalidOperationException("Rendering must not open images.");
        public Task<StudioFile?> OpenAsync(string extension) => throw new InvalidOperationException("Rendering must not open files.");
        public Task<StudioFile?> SaveAsync(StudioFile file, bool saveAs = false) => throw new InvalidOperationException("Rendering must not save files.");
    }

    [Theory]
    [InlineData(false, "success")]
    [InlineData(true, "success")]
    [InlineData(false, "failure")]
    [InlineData(true, "failure")]
    [InlineData(false, "cancelled")]
    [InlineData(true, "cancelled")]
    public async Task Telemetry_copy_handles_failure_retry_and_late_completion(bool disposeWhileCopying, string outcome)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var clipboard = new RecordingClipboard { Write = Task.FromException(new JSException("Permission denied")) };
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IJSRuntime>(clipboard);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var snackbar = scope.ServiceProvider.GetRequiredService<MudBlazor.ISnackbar>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            string? text = "{\"message\":\"original\"}";
            await renderer.RenderComponentAsync<TelemetryCopyButton>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["GetText"] = (Func<string?>)(() => text) }));
            var button = components.Items.OfType<StudioIconButton>().Single();
            await button.OnClick.InvokeAsync();
            snackbar.ShownSnackbars.Single().Severity.ShouldBe(MudBlazor.Severity.Error);
            snackbar.Clear();
            clipboard.Write = Task.CompletedTask;
            await button.OnClick.InvokeAsync();
            snackbar.ShownSnackbars.Single().Severity.ShouldBe(MudBlazor.Severity.Success);
            snackbar.Clear();
            text = null;
            await button.OnClick.InvokeAsync();
            clipboard.Texts.Count.ShouldBe(2);
            text = "retained";
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            clipboard.Write = pending.Task;
            var copying = button.OnClick.InvokeAsync();
            text = "changed after copying began";
            await button.OnClick.InvokeAsync();
            clipboard.Texts.Count.ShouldBe(3);
            clipboard.Texts.Last().ShouldBe("retained");
            snackbar.ShownSnackbars.ShouldBeEmpty();
            if (disposeWhileCopying) components.Items.OfType<TelemetryCopyButton>().Single().Dispose();
            if (outcome == "failure") pending.SetException(new JSException("Permission denied"));
            else if (outcome == "cancelled") pending.SetCanceled();
            else pending.SetResult();
            await copying;
            snackbar.ShownSnackbars.Count().ShouldBe(disposeWhileCopying ? 0 : 1);
            if (!disposeWhileCopying)
                snackbar.ShownSnackbars.Single().Severity.ShouldBe(outcome == "failure" ? MudBlazor.Severity.Error
                    : outcome == "cancelled" ? MudBlazor.Severity.Info : MudBlazor.Severity.Success);
            snackbar.Clear();
            clipboard.Write = Task.CompletedTask;
            await button.OnClick.InvokeAsync();
            clipboard.Texts.Count.ShouldBe(disposeWhileCopying ? 3 : 4);
            if (!disposeWhileCopying)
            {
                clipboard.Texts.Last().ShouldBe(text);
                snackbar.ShownSnackbars.Single().Severity.ShouldBe(MudBlazor.Severity.Success);
            }
            else snackbar.ShownSnackbars.ShouldBeEmpty();
        });
    }

    private sealed class RecordingClipboard : IJSRuntime
    {
        public List<string> Texts { get; } = [];
        public Task Write { get; set; } = Task.CompletedTask;
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "navigator.clipboard.writeText")
            {
                Texts.Add((string)args![0]!);
                await Write;
            }
            return default!;
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class StubJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/");
    }
}
