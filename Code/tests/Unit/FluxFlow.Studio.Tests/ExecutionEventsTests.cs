using System.Reflection;
using System.Text.Json;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Components.Grid;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Environments;
using FluxFlow.Studio.UI.Features.Observations;
using FluxFlow.Studio.UI.Features.Observations.Events;
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
    private static ExecutionOutput ComponentEvent(long sequence, string name, string instance = "one") => new(
        sequence, DateTimeOffset.Parse("2026-11-03T13:01:00Z"), "main.consumer.Events",
        JsonSerializer.SerializeToElement(new
        {
            Timestamp = "2026-11-03T13:00:01Z", MessageId = "original-message", CorrelationId = "original-correlation",
            Headers = new Dictionary<string, string> { ["MiXeD"] = "retained" },
            Value = new
            {
                Name = name, Kind = "diagnostic", Level = "warning", Timestamp = "2026-11-03T15:00:00+02:00",
                Message = "Original <script>alert(1)</script>\nsecond line",
                Details = new { SettlementToken = "original-token", Exception = "Complete exception\nInner detail" },
                Attributes = new { Number = 42 }, Dimensions = new { Host = "host-a" }, Measurements = new { Count = 1.25 }
            }
        }), null) { InstanceId = instance };

    [Fact]
    public async Task Component_events_use_shared_grid_and_preserve_full_original_data_under_filters()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var record = ComponentEvent(1, "amqp.settlement_rejected");
        var other = ComponentEvent(2, "other", "two");
        var snapshot = new ExecutionOutputSnapshot([record, other,
            record with { Port = "main.mapper.Output" }, record with { Port = "main.Events.Output" },
            record with { Port = "main.consumer.EventsExtra" }], 3);
        var original = JsonSerializer.Serialize(snapshot);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ExecutionEvents>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["Value"] = snapshot, ["ReadError"] = "Temporary read failure" }));
            var html = view.ToHtmlString();
            html.ShouldContain("amqp.settlement_rejected");
            html.ShouldContain("warning");
            System.Net.WebUtility.HtmlDecode(html).ShouldContain("13:00:00.000 +00:00");
            System.Net.WebUtility.HtmlDecode(html).ShouldContain("2026-11-03T15:00:00.0000000+02:00");
            html.ShouldContain("original-token");
            html.ShouldContain("original-correlation");
            html.ShouldContain("MiXeD");
            html.ShouldContain("Inner detail");
            html.ShouldContain("Measurements");
            html.ShouldNotContain("<script>");
            html.ShouldContain("3 output samples");
            html.ShouldContain("Temporary read failure");
            var grid = components.Items.OfType<StudioDataGrid<ExecutionEventRow>>().Single();
            grid.Items!.Count().ShouldBe(2);
            grid.Items!.First().Record.ShouldBeSameAs(record);
            grid.Items!.First().FullData.ShouldBe(record.Message!.Value.GetRawText());
            grid.ColumnResizeMode.ShouldBe(ResizeMode.Column);
            grid.FilterMode.ShouldBe(DataGridFilterMode.ColumnFilterMenu);
            grid.ReadOnly.ShouldBeTrue();
            grid.RenderedColumns.ShouldContain(column => column.Title == "Instance");
            await grid.AddFilterAsync(new FilterDefinition<ExecutionEventRow>
            {
                Column = grid.RenderedColumns.Single(column => column.Title == "Event"),
                Operator = FilterOperator.String.Contains, Value = "absent"
            });
            grid.FilteredItems.ShouldBeEmpty();
            await grid.ClearFiltersAsync();
            await grid.SetParametersAsync(ParameterView.Empty);
            grid.FilteredItems.Count().ShouldBe(2);
        });
        JsonSerializer.Serialize(snapshot).ShouldBe(original);
    }

    [Fact]
    public void Component_events_keep_display_identity_across_deserialized_polls()
    {
        var original = ComponentEvent(1, "original");
        var polled = JsonSerializer.Deserialize<ExecutionOutput>(JsonSerializer.Serialize(original))!;
        var first = new ExecutionEventRow(original);
        var next = new ExecutionEventRow(polled);
        next.Record.ShouldNotBeSameAs(original);
        next.ShouldBe(first);
        next.GetHashCode().ShouldBe(first.GetHashCode());
        next.FullData.ShouldBe(first.FullData);
        new ExecutionEventRow(original with { Sequence = 2 }).ShouldNotBe(first);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{\"Value\":\"unstructured event\"}")]
    [InlineData("{\"Value\":{\"Name\":42,\"Timestamp\":\"invalid\",\"Level\":null}}")]
    public void Component_events_keep_unstructured_payloads_and_capture_errors(string json)
    {
        var original = new ExecutionOutput(1, DateTimeOffset.UtcNow, "main.source.Events", JsonDocument.Parse(json).RootElement.Clone(), null);
        var row = new ExecutionEventRow(original);
        row.Name.ShouldBeNull();
        row.Level.ShouldBeNull();
        row.Timestamp.ShouldBe(original.Timestamp);
        row.FullData.ShouldBe(json);
        var failed = new ExecutionEventRow(original with { Message = null, Error = "Original capture error\nFull stack" });
        failed.Message.ShouldBe("Original capture error\nFull stack");
        failed.FullData.ShouldBe(failed.Message);
    }

    [Fact]
    public async Task Component_events_retain_Stop_snapshot_and_reset_grid_on_rerun_without_editing_project()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var runner = new ComponentEventRunner();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IEnvironmentRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<StudioRunSession>();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 123 });
        var saved = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ObservationPanel>();
            var panel = components.Items.OfType<ObservationPanel>().Single();
            typeof(ObservationPanel).GetField("_tab", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, "Events");
            await panel.SetParametersAsync(ParameterView.Empty);
            view.ToHtmlString().ShouldContain("No component events to display.");
            await session.StartAsync(graph);
            var firstRun = session.Status!.Id;
            var firstGrid = components.Items.OfType<StudioDataGrid<ExecutionEventRow>>().Last();
            firstGrid.Items!.Single().Name.ShouldBe("startup");
            await firstGrid.AddFilterAsync(new FilterDefinition<ExecutionEventRow>
            {
                Column = firstGrid.RenderedColumns.Single(column => column.Title == "Event"),
                Operator = FilterOperator.String.Contains, Value = "shutdown"
            });
            await runner.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await session.StopAsync();
            components.Items.OfType<StudioDataGrid<ExecutionEventRow>>().Last().ShouldBeSameAs(firstGrid);
            firstGrid.Items!.Single().Name.ShouldBe("shutdown");
            firstGrid.FilterDefinitions.Count.ShouldBe(1);
            view.ToHtmlString().ShouldContain("shutdown");
            await session.StartAsync(graph);
            var nextGrid = components.Items.OfType<StudioDataGrid<ExecutionEventRow>>().Last();
            nextGrid.ShouldNotBeSameAs(firstGrid);
            nextGrid.FilterDefinitions.ShouldBeEmpty();
            nextGrid.Items!.Single().Name.ShouldBe("startup");
            runner.LatePoll.SetResult(ComponentEventRunner.Status(firstRun, "Running", 99, "stale"));
            await Task.Yield();
            session.Outputs.Records.Single().Sequence.ShouldBe(1);
            view.ToHtmlString().ShouldNotContain("stale");
            await session.StopAsync();
        });
        graph.ToProjectJson().ShouldBe(saved);
        graph.Revision.ShouldBe(revision);
        graph.Undo();
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldNotBe(123);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(saved);
    }

    private sealed class ComponentEventRunner : IEnvironmentRunner
    {
        private readonly HashSet<Guid> _stopped = [];
        public TaskCompletionSource PollStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<EnvironmentRunStatus> LatePoll { get; } = new();
        public static EnvironmentRunStatus Status(Guid id, string state, long sequence, string name) =>
            new(id, state, [], Outputs: new([ComponentEvent(sequence, name)], 0));
        public Task<EnvironmentCheckResult> CheckAsync(EnvironmentRunRequest request, CancellationToken token) => Task.FromResult(new EnvironmentCheckResult(true, "Ready"));
        public Task<EnvironmentRunStatus> StartAsync(Guid id, EnvironmentRunRequest request, CancellationToken token) => Task.FromResult(Status(id, "Running", 1, "startup"));
        public Task StopAsync(Guid id, CancellationToken token) { _stopped.Add(id); return Task.CompletedTask; }
        public Task<EnvironmentRunStatus> GetAsync(Guid id, CancellationToken token)
        {
            if (_stopped.Contains(id)) return Task.FromResult(Status(id, "Stopped", 2, "shutdown"));
            PollStarted.TrySetResult();
            return LatePoll.Task;
        }
    }
}
