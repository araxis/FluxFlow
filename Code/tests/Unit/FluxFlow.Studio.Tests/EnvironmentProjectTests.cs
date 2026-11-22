using System.Text.Json.Nodes;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Environments;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class EnvironmentProjectTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_run_can_retry_before_or_after_cleanup_without_changing_history(bool stopFirst)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.ResetHistory();
        var original = graph.ToProjectJson();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
        var saved = graph.ToProjectJson();
        var revision = graph.Revision;
        var runner = new DelayedRunner();
        await using var session = new StudioRunSession(runner);
        var start = session.StartAsync(graph);
        var firstId = runner.Id;
        const string failure = "Startup failed\nOriginal exception: <broker unavailable>";
        runner.Release.SetResult(new(firstId, "Failed", [], failure));
        await start;
        session.Failure.ShouldBe(failure);
        session.CanRetry.ShouldBeTrue();
        if (stopFirst)
        {
            await session.StopAsync();
            session.Status!.State.ShouldBe("Stopped");
            session.Failure.ShouldBe(failure);
            session.CanRetry.ShouldBeTrue();
        }
        var retry = session.RetryAsync(graph);
        runner.Stopped.ShouldBe(new[] { firstId });
        runner.Id.ShouldNotBe(firstId);
        session.Status!.State.ShouldBe("Preparing");
        session.Failure.ShouldBeNull();
        session.CanRetry.ShouldBeFalse();
        runner.Release.SetResult(new(runner.Id, "Running", [new("Resources:broker", "mosquitto", "mqtt://127.0.0.1:1883", "container-id")]));
        await retry;
        session.Status.State.ShouldBe("Running");
        session.Status.Services.Single().ContainerId.ShouldBe("container-id");
        await session.StopAsync();
        session.Failure.ShouldBeNull();
        graph.Revision.ShouldBe(revision);
        graph.ToProjectJson().ShouldBe(saved);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(original);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(saved);
    }

    [Fact]
    public async Task Retry_does_not_start_another_run_when_cleanup_fails_and_preserves_both_errors()
    {
        var graph = StudioTestServices.CreateGraph();
        var runner = new DelayedRunner();
        await using var session = new StudioRunSession(runner);
        var start = session.StartAsync(graph);
        var id = runner.Id;
        runner.Release.SetResult(new(id, "Failed", [], "Original startup failure"));
        await start;
        runner.StopError = new IOException("Container cleanup unavailable");
        await session.RetryAsync(graph);
        runner.Id.ShouldBe(id);
        session.Status!.State.ShouldBe("Failed");
        session.Failure.ShouldBe("Original startup failure");
        session.CleanupError.ShouldBe(runner.StopError.ToString());
        session.CanRetry.ShouldBeTrue();
        runner.StopError = null;
        await session.StopAsync();
        session.Status.State.ShouldBe("Stopped");
        session.Failure.ShouldBe("Original startup failure");
        session.CleanupError.ShouldBeNull();
    }

    [Fact]
    public async Task Startup_cancellation_does_not_reenable_run_while_cleanup_is_pending()
    {
        var graph = StudioTestServices.CreateGraph();
        var runner = new DelayedRunner { StopRelease = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var session = new StudioRunSession(runner);
        var start = session.StartAsync(graph);
        var stop = session.StopAsync();
        runner.Release.SetCanceled(runner.Token);
        await start;
        session.Busy.ShouldBeTrue();
        session.Status!.State.ShouldBe("Stopping");
        session.CanRetry.ShouldBeFalse();
        runner.StopRelease.SetResult();
        await stop;
        session.Busy.ShouldBeFalse();
        session.Status.State.ShouldBe("Stopped");
        session.Failure.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Environment_check_captures_a_snapshot_and_does_not_change_configuration_or_history(bool changeDuringCheck)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.ResetHistory();
        var initial = graph.ToProjectJson();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 90 });
        var saved = graph.ToProjectJson();
        var revision = graph.Revision;
        var runner = new DelayedRunner();
        await using var session = new StudioRunSession(runner);
        var pending = session.CheckAsync(graph);
        session.Checking.ShouldBeTrue();
        runner.Request!.Configuration.ShouldBe(graph.ExportConfiguration());
        runner.Request.Environment.StartupTimeoutSeconds.ShouldBe(90);
        if (changeDuringCheck) graph.SetEnvironment(new() { StartupTimeoutSeconds = 120 });
        runner.Request.Environment.StartupTimeoutSeconds.ShouldBe(90);
        runner.CheckRelease.SetResult(new(true, "Valid"));
        await pending;
        session.Checking.ShouldBeFalse();
        session.Status.ShouldBeNull();
        if (changeDuringCheck)
        {
            session.CheckResult.ShouldBeNull();
            graph.GetEnvironment().StartupTimeoutSeconds.ShouldBe(120);
            graph.Undo();
        }
        else
        {
            session.CheckResult!.Success.ShouldBeTrue();
            graph.Revision.ShouldBe(revision);
        }
        graph.ToProjectJson().ShouldBe(saved);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(initial);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.ToProjectJson().ShouldBe(saved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_environment_check_allows_retry_and_ignores_a_late_response(bool lateFailure)
    {
        var graph = StudioTestServices.CreateGraph();
        var saved = graph.ToProjectJson();
        var runner = new DelayedRunner();
        await using var session = new StudioRunSession(runner);
        var pending = session.CheckAsync(graph);
        var firstResponse = runner.CheckRelease;
        session.CancelCheck();
        runner.Token.IsCancellationRequested.ShouldBeTrue();
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            session.CheckResult.ShouldBeNull();
            session.CheckRevision.ShouldBeNull();
            session.Checking.ShouldBeFalse();
            var retry = session.CheckAsync(graph);
            if (lateFailure) firstResponse.SetException(new IOException("Late failure"));
            else firstResponse.SetResult(new(true, "Late result"));
            session.Checking.ShouldBeTrue();
            session.CheckResult.ShouldBeNull();
            runner.CheckRelease.SetResult(new(true, "Current result"));
            await retry;
            session.CheckResult!.Message.ShouldBe("Current result");
            session.Checking.ShouldBeFalse();
            session.Status.ShouldBeNull();
            graph.ToProjectJson().ShouldBe(saved);
        }
        finally { firstResponse.TrySetResult(new(true, "Released")); }
    }

    [Fact]
    public void Projects_saved_before_environment_support_use_existing_services_by_default()
    {
        var graph = StudioTestServices.CreateGraph();
        var project = JsonNode.Parse(graph.ToProjectJson())!.AsObject();
        project.Remove("Environment");
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(project.ToJsonString());
        reopened.GetEnvironment().Resources.ShouldBeEmpty();
        reopened.GetEnvironment().StartupTimeoutSeconds.ShouldBe(60);
    }
    [Fact]
    public void Environment_edits_round_trip_as_one_undo_action_without_changing_engine_configuration()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"HostOption":"Keep","FluxFlow":{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883","ClientId":"test"},"api":{"Type":"http.client","BaseAddress":"http://original/"}},"Workflows":{"main":{}}}}""", "FluxFlow");
        graph.ResetHistory();
        var original = graph.ToJson();
        var exported = graph.ExportConfiguration();
        const string mappings = """[{"Request":{"Path":"/readings","Methods":["post"]},"Response":{"StatusCode":201,"Body":"KeepCase"}}]""";
        var profile = new EnvironmentProfile { Resources = new()
        {
            ["resources:BROKER"] = new() { Provider = "mosquitto", Image = "private.example/broker:1", Files = new() { ["/test.txt"] = "MixedCase" } },
            ["Resources:api"] = new() { Provider = "wiremock", Mappings = mappings, Variables = new() { ["CUSTOM_VALUE"] = "KeepCase" } }
        } };
        graph.SetEnvironment(profile);
        profile.Resources.Clear();
        graph.GetEnvironment().Resources.Count.ShouldBe(2);
        graph.GetEnvironment().Resources["Resources:broker"].Provider.ShouldBe("mosquitto");
        graph.ToJson().ShouldBe(original);
        graph.ExportConfiguration().ShouldBe(exported);
        var saved = graph.ToProjectJson();
        saved.ShouldContain("resources:BROKER");
        graph.Undo();
        graph.GetEnvironment().Resources.ShouldBeEmpty();
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.GetEnvironment().Resources["Resources:broker"].Files["/test.txt"].ShouldBe("MixedCase");
        graph.GetEnvironment().Resources["resources:API"].Mappings.ShouldBe(mappings);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(saved);
        reopened.GetEnvironment().Resources["Resources:broker"].Image.ShouldBe("private.example/broker:1");
        reopened.GetEnvironment().Resources["Resources:api"].Mappings.ShouldBe(mappings);
        reopened.GetEnvironment().Resources["Resources:api"].Variables["CUSTOM_VALUE"].ShouldBe("KeepCase");
        JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(reopened.ToJson())).ShouldBeTrue();
        reopened.ExportConfiguration().ShouldBe(exported);
    }

    [Fact]
    public async Task Late_start_from_a_stopped_run_does_not_clear_the_new_runs_cancellation()
    {
        var graph = StudioTestServices.CreateGraph();
        var original = graph.ToProjectJson();
        var runner = new DelayedRunner();
        await using var session = new StudioRunSession(runner);
        var first = session.StartAsync(graph);
        var firstResponse = runner.Release;
        var firstId = runner.Id;
        await session.StopAsync();

        var second = session.StartAsync(graph);
        var secondResponse = runner.Release;
        var secondId = runner.Id;
        var secondToken = runner.Token;
        try
        {
            firstResponse.SetResult(new(firstId, "Running", []));
            await first;
            session.Busy.ShouldBeTrue();
            session.Status!.Id.ShouldBe(secondId);
            session.Status.State.ShouldBe("Preparing");
            await session.StopAsync();
            secondToken.IsCancellationRequested.ShouldBeTrue();
        }
        finally
        {
            firstResponse.TrySetResult(new(firstId, "Running", []));
            secondResponse.TrySetResult(new(secondId, "Running", []));
            await Task.WhenAll(first, second);
        }
        session.Status!.State.ShouldBe("Stopped");
        graph.ToProjectJson().ShouldBe(original);
    }

    [Fact]
    public async Task Run_captures_configuration_without_changing_history_and_ignores_a_late_start_after_stop()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.ResetHistory();
        var revision = graph.Revision;
        var original = graph.ToProjectJson();
        var runner = new DelayedRunner();
        await using var session = new StudioRunSession(runner);
        var start = session.StartAsync(graph);
        runner.Request.ShouldNotBeNull();
        runner.Request.Configuration.ShouldBe(graph.ExportConfiguration());
        await session.StopAsync();
        runner.Token.IsCancellationRequested.ShouldBeTrue();
        runner.Release.TrySetResult(new(runner.Id, "Running", []));
        await start;
        session.Status!.State.ShouldBe("Stopped");
        graph.Revision.ShouldBe(revision);
        graph.ToProjectJson().ShouldBe(original);
        graph.CanUndo.ShouldBeFalse();
    }

    internal sealed class DelayedRunner : IEnvironmentRunner
    {
        public TaskCompletionSource<EnvironmentCheckResult> CheckRelease { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<EnvironmentCheckResult> CheckAsync(EnvironmentRunRequest request, CancellationToken cancellationToken)
        { Request = request; Token = cancellationToken; CheckRelease = new(TaskCreationOptions.RunContinuationsAsynchronously); return CheckRelease.Task; }
        public TaskCompletionSource<EnvironmentRunStatus> Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid Id { get; private set; }
        public EnvironmentRunRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<EnvironmentRunStatus> StartAsync(Guid id, EnvironmentRunRequest request, CancellationToken cancellationToken)
        { Id = id; Request = request; Token = cancellationToken; Release = new(TaskCreationOptions.RunContinuationsAsynchronously); return Release.Task; }
        public EnvironmentRunStatus? Snapshot { get; set; }
        public Task<EnvironmentRunStatus> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Snapshot ?? new EnvironmentRunStatus(id, "Preparing", []));
        public List<Guid> Stopped { get; } = [];
        public Exception? StopError { get; set; }
        public TaskCompletionSource? StopRelease { get; init; }
        public async Task StopAsync(Guid id, CancellationToken cancellationToken)
        {
            Stopped.Add(id);
            if (StopRelease is not null) await StopRelease.Task;
            if (StopError is not null) throw StopError;
        }
    }
}
