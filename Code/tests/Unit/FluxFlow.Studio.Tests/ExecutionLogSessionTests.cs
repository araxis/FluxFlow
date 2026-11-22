using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Environments;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ExecutionLogSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_poll_during_startup_retries_and_an_older_start_snapshot_does_not_replace_live_logs(bool sameOutputSequence)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.ResetHistory();
        var project = graph.ToProjectJson();
        var runner = new LogRunner { DelayStart = true, FailFirstPoll = true };
        await using var session = new StudioRunSession(runner);
        var start = session.StartAsync(graph);
        var id = session.Status!.Id;
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Changed += () => { if (session.Logs.Records.LastOrDefault()?.Sequence == 3) updated.TrySetResult(); };
        try
        {
            await runner.PollRetried.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var live = Status(id, "Preparing", 3, "Live startup detail");
            live = live with { Outputs = live.Outputs! with { Dropped = 7 } };
            runner.PollRelease.SetResult(live);
            await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            session.Busy.ShouldBeTrue();
            var older = Status(id, "Running", 1, "Older startup snapshot");
            if (sameOutputSequence) older = older with { Outputs = live.Outputs with { Dropped = 0 } };
            runner.StartRelease.SetResult(older);
            await start;
            session.Status!.State.ShouldBe("Running");
            session.Logs.Records.Single().Message.ShouldBe("Live startup detail");
            await session.StopAsync();
            // The simulated final read is older, too; keep the latest captured records.
            session.Logs.Records.Single().Sequence.ShouldBe(3);
            session.Diagnostics.Records.Single().Sequence.ShouldBe(3);
            session.Outputs.Records.Single().Sequence.ShouldBe(3);
            session.Outputs.Dropped.ShouldBe(7);
            session.Traces.Records.Single().Sequence.ShouldBe(3);
            graph.ToProjectJson().ShouldBe(project);
            graph.CanUndo.ShouldBeFalse();
        }
        finally
        {
            runner.StartRelease.TrySetResult(new(id, "Running", []));
            runner.PollRelease.TrySetResult(new(id, "Running", []));
            await start;
        }
    }

    [Fact]
    public async Task Late_records_after_Stop_do_not_replace_final_observations()
    {
        var runner = new LogRunner();
        await using var session = new StudioRunSession(runner);
        var graph = StudioTestServices.CreateGraph();
        var original = graph.ToProjectJson();
        await session.StartAsync(graph);
        var id = session.Status!.Id;
        await runner.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopAsync();
        runner.PollRelease.SetResult(Status(id, "Running", 99, "Late"));
        session.Status!.State.ShouldBe("Stopped");
        session.Logs.Records.Single().Message.ShouldBe("Shutdown");
        session.Diagnostics.Records.Single().Message.ShouldBe("Shutdown");
        session.Outputs.Records.Single().Sequence.ShouldBe(2);
        session.Traces.Records.Single().Name.ShouldBe("Shutdown");
        graph.ToProjectJson().ShouldBe(original);
        graph.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public async Task Shutdown_logs_remain_visible_and_late_poll_cannot_replace_the_next_run()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 123 });
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        var runner = new LogRunner();
        await using var session = new StudioRunSession(runner);
        await session.StartAsync(graph);
        var old = session.Status!.Id;
        await runner.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await session.StopAsync();
        session.Logs.Records.Single().Message.ShouldBe("Shutdown");
        session.Diagnostics.Records.Single().Message.ShouldBe("Shutdown");
        session.Outputs.Records.Single().Message!.Value.GetString().ShouldBe("Shutdown");
        session.Traces.Records.Single().Name.ShouldBe("Shutdown");
        session.HasRun.ShouldBeFalse();
        session.CleanupError.ShouldBeNull();
        await session.StartAsync(graph);
        var current = session.Status!.Id;
        runner.PollRelease.SetResult(Status(old, "Failed", 99, "Late log") with { Error = "Late failure" });
        session.Status.Id.ShouldBe(current);
        session.Failure.ShouldBeNull();
        session.Logs.Records.Single().Message.ShouldBe("Startup");
        session.Diagnostics.Records.Single().Message.ShouldBe("Startup");
        session.Outputs.Records.Single().Message!.Value.GetString().ShouldBe("Startup");
        session.Traces.Records.Single().Name.ShouldBe("Startup");
        await session.StopAsync();
        graph.ToProjectJson().ShouldBe(project);
        graph.Revision.ShouldBe(revision);
        graph.Undo();
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldNotBe(123);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(project);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Final_read_failure_or_wrong_run_preserves_captured_logs_without_reporting_cleanup_failure(bool wrongRun)
    {
        var runner = new LogRunner { FinalReadFails = !wrongRun, WrongRun = wrongRun };
        await using var session = new StudioRunSession(runner);
        await session.StartAsync(StudioTestServices.CreateGraph());
        await session.StopAsync();
        session.Status!.State.ShouldBe("Stopped");
        session.Logs.Records.Single().Message.ShouldBe("Startup");
        session.Diagnostics.Records.Single().Message.ShouldBe("Startup");
        session.Outputs.Records.Single().Message!.Value.GetString().ShouldBe("Startup");
        session.Traces.Records.Single().Name.ShouldBe("Startup");
        session.CleanupError.ShouldBeNull();
        if (!wrongRun) session.LogReadError.ShouldContain("Read failed");
    }

    private static ExecutionLogSnapshot Snapshot(long sequence, string message) =>
        new([new(sequence, DateTimeOffset.UtcNow, "Warning", "Original.Category", 0, null, message, null)], 0);

    private static EnvironmentRunStatus Status(Guid id, string state, long sequence, string message) => new(id, state, [],
        Logs: Snapshot(sequence, message), Diagnostics: Snapshot(sequence, message),
        Outputs: new([new(sequence, DateTimeOffset.UtcNow, "main.mapper.Output", System.Text.Json.JsonSerializer.SerializeToElement(message), null)], 0),
        Traces: new([new(sequence, message, "FluxFlow.Core", null,
            "0123456789abcdef0123456789abcdef", "0123456789abcdef", null, null, 1,
            "Consumer", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(1), "Ok", null,
            new Dictionary<string, System.Text.Json.JsonElement>(), [], [], new Dictionary<string, System.Text.Json.JsonElement>())], 0));

    private sealed class LogRunner : IEnvironmentRunner
    {
        public TaskCompletionSource PollStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PollRetried { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<EnvironmentRunStatus> StartRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Inline continuations let the test inspect state after the delayed poll is handled.
        public TaskCompletionSource<EnvironmentRunStatus> PollRelease { get; } = new();
        private readonly HashSet<Guid> _stopped = [];
        public bool FinalReadFails { get; init; }
        public bool WrongRun { get; init; }
        public bool DelayStart { get; init; }
        public bool FailFirstPoll { get; init; }
        private int _polls;
        public Task<EnvironmentCheckResult> CheckAsync(EnvironmentRunRequest request, CancellationToken cancellationToken) => Task.FromResult(new EnvironmentCheckResult(true, "Ready"));
        public Task<EnvironmentRunStatus> StartAsync(Guid id, EnvironmentRunRequest request, CancellationToken cancellationToken) =>
            DelayStart ? StartRelease.Task : Task.FromResult(Status(id, "Running", 1, "Startup"));
        public Task StopAsync(Guid id, CancellationToken cancellationToken) { _stopped.Add(id); return Task.CompletedTask; }
        public Task<EnvironmentRunStatus> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            if (_stopped.Contains(id))
            {
                if (FinalReadFails) throw new IOException("Read failed");
                return Task.FromResult(Status(WrongRun ? Guid.NewGuid() : id, "Stopped", 2, "Shutdown"));
            }
            PollStarted.TrySetResult();
            if (++_polls == 1 && FailFirstPoll) throw new IOException("Status temporarily unavailable during startup");
            if (_polls > 1) PollRetried.TrySetResult();
            return PollRelease.Task;
        }
    }
}
