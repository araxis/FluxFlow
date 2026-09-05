using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.HealthChecks.Tests;

public sealed class FluxFlowApplicationHealthCheckTests
{
    private const string HealthyDescription =
        "An active FluxFlow application revision is available.";
    private const string DegradedDescription =
        "The active FluxFlow application revision remains available after the latest update was rejected.";
    private const string UnavailableDescription =
        "The FluxFlow application has no active ready revision.";
    private const string StoppedDescription =
        "The FluxFlow application is stopped and is not ready.";

    [Fact]
    public async Task Active_running_application_reports_healthy_with_exact_bounded_data()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(
            ReadyDefinition(),
            options =>
            {
                options.StartWithHost = false;
                options.InitialRevisionId = "active-7";
            });
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        var started = await application.StartAsync();

        var result = await CheckAsync(application);

        started.Status.ShouldBe(ApplicationUpdateStatus.Applied);
        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Description.ShouldBe(HealthyDescription);
        result.Exception.ShouldBeNull();
        AssertData(
            result,
            ("applicationState", "Running"),
            ("activeRevisionId", "active-7"),
            ("activeSequence", 1L),
            ("requestedRevisionId", "active-7"),
            ("lastUpdateStatus", "Applied"));
    }

    [Fact]
    public async Task Unchanged_active_revision_remains_healthy()
    {
        var source = new MutableDefinitionSource(ReadyDefinition());
        var services = new ServiceCollection();
        services.AddFluxFlow(source, options =>
        {
            options.StartWithHost = false;
            options.InitialRevisionId = "initial";
        });
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        await application.StartAsync();
        var active = application.Current;
        source.Definition = ReadyDefinition();

        var unchanged = await application.ReloadAsync("equivalent");
        var result = await CheckAsync(application);

        unchanged.Status.ShouldBe(ApplicationUpdateStatus.Unchanged);
        application.Current.ShouldBeSameAs(active);
        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Description.ShouldBe(HealthyDescription);
        AssertData(
            result,
            ("applicationState", "Running"),
            ("activeRevisionId", "initial"),
            ("activeSequence", 1L),
            ("requestedRevisionId", "equivalent"),
            ("lastUpdateStatus", "Unchanged"));
    }

    [Fact]
    public async Task Rejected_reload_with_active_revision_reports_degraded_and_preserves_active_revision()
    {
        var source = new MutableDefinitionSource(ReadyDefinition());
        var services = new ServiceCollection();
        services.AddFluxFlow(source, options =>
        {
            options.StartWithHost = false;
            options.InitialRevisionId = "retained";
        });
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        await application.StartAsync();
        var active = application.Current;
        var definition = application.CurrentDefinition;
        source.Failure = new InvalidOperationException("secret diagnostic message");

        var rejected = await application.ReloadAsync("rejected-request");
        var result = await CheckAsync(application);

        rejected.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        application.State.ShouldBe(ApplicationState.Running);
        application.Current.ShouldBeSameAs(active);
        application.CurrentDefinition.ShouldBeSameAs(definition);
        application.LastUpdate.ShouldBeSameAs(rejected);
        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description.ShouldBe(DegradedDescription);
        result.Exception.ShouldBeNull();
        AssertData(
            result,
            ("applicationState", "Running"),
            ("activeRevisionId", "retained"),
            ("activeSequence", 1L),
            ("requestedRevisionId", "rejected-request"),
            ("lastUpdateStatus", "Rejected"),
            ("diagnosticStage", "Source"),
            ("diagnosticCode", "revision.source.load_failed"));
        Flatten(result).ShouldNotContain("secret diagnostic message");
        Flatten(result).ShouldNotContain(typeof(InvalidOperationException).FullName!);
    }

    [Fact]
    public async Task Successful_update_after_rejection_restores_healthy_readiness()
    {
        var source = new MutableDefinitionSource(ReadyDefinition());
        var services = new ServiceCollection();
        services.AddFluxFlow(source, options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        await application.StartAsync();
        source.Failure = new InvalidOperationException("temporarily unavailable");
        (await application.ReloadAsync("rejected")).IsRejected.ShouldBeTrue();
        (await CheckAsync(application)).Status.ShouldBe(HealthStatus.Degraded);
        source.Failure = null;
        source.Definition = ReadyDefinition();

        var recovered = await application.ReloadAsync("recovered");
        var result = await CheckAsync(application);

        recovered.Status.ShouldBe(ApplicationUpdateStatus.Unchanged);
        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Description.ShouldBe(HealthyDescription);
        result.Data["requestedRevisionId"].ShouldBe("recovered");
        result.Data["lastUpdateStatus"].ShouldBe("Unchanged");
        result.Data.ContainsKey("diagnosticStage").ShouldBeFalse();
        result.Data.ContainsKey("diagnosticCode").ShouldBeFalse();
    }

    [Fact]
    public async Task Active_revision_stays_healthy_and_operational_while_candidate_reload_is_held_in_preparation()
    {
        var initial = IdentityDefinition(CreateIdentityContract(
            static _ => ValueTask.FromResult(new HealthIdentityNode())));
        var source = new MutableDefinitionSource(initial.Definition);
        var services = new ServiceCollection();
        services.AddFluxFlow(source, options =>
        {
            options.StartWithHost = false;
            options.InitialRevisionId = "active";
        });
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        (await application.StartAsync()).IsApplied.ShouldBeTrue();
        var active = application.Current;
        var activeDefinition = application.CurrentDefinition;
        var gate = new PreparationGate();
        source.Definition = IdentityDefinition(CreateIdentityContract(
            _ => gate.CreateNodeAsync())).Definition;

        var reload = application.ReloadAsync("candidate").AsTask();
        await gate.Entered;
        try
        {
            application.State.ShouldBe(ApplicationState.Reloading);
            application.Current.ShouldBeSameAs(active);
            application.CurrentDefinition.ShouldBeSameAs(activeDefinition);

            var result = await CheckAsync(application);
            result.Status.ShouldBe(HealthStatus.Healthy);
            result.Description.ShouldBe(HealthyDescription);
            AssertData(
                result,
                ("applicationState", "Reloading"),
                ("activeRevisionId", "active"),
                ("activeSequence", 1L),
                ("requestedRevisionId", "active"),
                ("lastUpdateStatus", "Applied"));

            var receive = application.Ports.ReceiveAsync(
                initial.Handle.Output,
                TimeSpan.FromSeconds(5));
            var sent = await application.Ports.SendAsync(
                initial.Handle.Input,
                FlowMessage.Create("still-serving"));
            var received = await receive;

            sent.Status.ShouldBe(PortSendStatus.Accepted);
            received.Status.ShouldBe(PortReceiveStatus.Received);
            received.Message!.Value.ShouldBe("still-serving");
        }
        finally
        {
            gate.Release();
        }

        var applied = await reload;
        applied.IsApplied.ShouldBeTrue();
        application.State.ShouldBe(ApplicationState.Running);
        application.Current!.RevisionId.ShouldBe("candidate");
    }

    [Fact]
    public async Task Rejected_initial_start_without_active_revision_reports_unhealthy()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(
            new FailingDefinitionSource("private source failure"),
            options =>
            {
                options.StartWithHost = false;
                options.InitialRevisionId = "failed-start";
            });
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();

        var rejected = await application.StartAsync();
        var result = await CheckAsync(application);

        rejected.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        application.State.ShouldBe(ApplicationState.Degraded);
        application.Current.ShouldBeNull();
        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe(UnavailableDescription);
        result.Exception.ShouldBeNull();
        AssertData(
            result,
            ("applicationState", "Degraded"),
            ("requestedRevisionId", "failed-start"),
            ("lastUpdateStatus", "Rejected"),
            ("diagnosticStage", "Source"),
            ("diagnosticCode", "revision.source.load_failed"));
        Flatten(result).ShouldNotContain("private source failure");
    }

    [Fact]
    public async Task Empty_application_without_active_revision_reports_unhealthy_without_starting_it()
    {
        var source = new CountingDefinitionSource();
        var services = new ServiceCollection();
        services.AddFluxFlow(source, options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();

        var result = await CheckAsync(application);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe(UnavailableDescription);
        AssertData(result, ("applicationState", "Empty"));
        source.LoadCount.ShouldBe(0);
        application.State.ShouldBe(ApplicationState.Empty);
        application.Current.ShouldBeNull();
        application.LastUpdate.ShouldBeNull();
    }

    [Theory]
    [InlineData(ApplicationState.Empty)]
    [InlineData(ApplicationState.Starting)]
    [InlineData(ApplicationState.Degraded)]
    public void Non_ready_states_fail_closed_even_with_an_impossible_current_snapshot(
        ApplicationState state)
    {
        var result = FluxFlowApplicationHealthCheck.CreateResult(
            state,
            Snapshot("active"),
            Update(ApplicationUpdateStatus.Applied, "requested"));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe(UnavailableDescription);
        result.Data["applicationState"].ShouldBe(state.ToString());
        result.Data.Count.ShouldBeLessThanOrEqualTo(7);
    }

    [Theory]
    [InlineData(ApplicationState.Stopping)]
    [InlineData(ApplicationState.Stopped)]
    public void Stopping_and_stopped_states_fail_closed_even_with_an_impossible_current_snapshot(
        ApplicationState state)
    {
        var result = FluxFlowApplicationHealthCheck.CreateResult(
            state,
            Snapshot("active"),
            Update(ApplicationUpdateStatus.Applied, "requested"));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe(StoppedDescription);
        result.Data["applicationState"].ShouldBe(state.ToString());
        result.Data.Count.ShouldBeLessThanOrEqualTo(7);
    }

    [Theory]
    [InlineData(ApplicationState.Running)]
    [InlineData(ApplicationState.Reloading)]
    public void Running_or_reloading_without_current_fails_closed(ApplicationState state)
    {
        var result = FluxFlowApplicationHealthCheck.CreateResult(
            state,
            current: null,
            Update(ApplicationUpdateStatus.Applied, "requested"));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe(UnavailableDescription);
        result.Data.ContainsKey("activeRevisionId").ShouldBeFalse();
        result.Data.ContainsKey("activeSequence").ShouldBeFalse();
        result.Data.Count.ShouldBeLessThanOrEqualTo(7);
    }

    [Theory]
    [InlineData(ApplicationState.Running)]
    [InlineData(ApplicationState.Reloading)]
    public void Running_or_reloading_with_current_and_rejected_update_reports_degraded(
        ApplicationState state)
    {
        var result = FluxFlowApplicationHealthCheck.CreateResult(
            state,
            Snapshot("active"),
            Update(ApplicationUpdateStatus.Rejected, "requested"));

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description.ShouldBe(DegradedDescription);
        result.Data["activeRevisionId"].ShouldBe("active");
        result.Data["lastUpdateStatus"].ShouldBe("Rejected");
    }

    [Theory]
    [InlineData(ApplicationState.Running, ApplicationUpdateStatus.Applied)]
    [InlineData(ApplicationState.Running, ApplicationUpdateStatus.Unchanged)]
    [InlineData(ApplicationState.Reloading, ApplicationUpdateStatus.Applied)]
    [InlineData(ApplicationState.Reloading, ApplicationUpdateStatus.Unchanged)]
    public void Running_or_reloading_with_current_and_non_rejected_update_reports_healthy(
        ApplicationState state,
        ApplicationUpdateStatus updateStatus)
    {
        var result = FluxFlowApplicationHealthCheck.CreateResult(
            state,
            Snapshot("active"),
            Update(updateStatus, "requested"));

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Description.ShouldBe(HealthyDescription);
        result.Data["activeRevisionId"].ShouldBe("active");
        result.Data["lastUpdateStatus"].ShouldBe(updateStatus.ToString());
    }

    [Fact]
    public async Task Stopped_and_disposed_application_report_unhealthy_deterministically()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(ReadyDefinition(), options => options.StartWithHost = false);
        var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        await application.StartAsync();
        var check = new FluxFlowApplicationHealthCheck(application);

        await application.StopAsync();
        var stopped = await check.CheckHealthAsync(new HealthCheckContext());
        await application.DisposeAsync();
        var disposed = await check.CheckHealthAsync(new HealthCheckContext());

        application.State.ShouldBe(ApplicationState.Stopped);
        application.Current.ShouldBeNull();
        stopped.Status.ShouldBe(HealthStatus.Unhealthy);
        stopped.Description.ShouldBe(StoppedDescription);
        disposed.Status.ShouldBe(HealthStatus.Unhealthy);
        disposed.Description.ShouldBe(StoppedDescription);
        AssertData(
            stopped,
            ("applicationState", "Stopped"),
            ("requestedRevisionId", "initial"),
            ("lastUpdateStatus", "Applied"));
        AssertData(
            disposed,
            ("applicationState", "Stopped"),
            ("requestedRevisionId", "initial"),
            ("lastUpdateStatus", "Applied"));
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task Health_check_honors_pre_canceled_token()
    {
        var check = new FluxFlowApplicationHealthCheck(application: null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            check.CheckHealthAsync(new HealthCheckContext(), cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
    }

    [Fact]
    public void Health_check_data_is_bounded_uses_only_the_last_diagnostic_and_excludes_sensitive_details()
    {
        const string secret = "credential=not-for-health-data";
        var snapshot = new ApplicationSnapshot
        {
            Sequence = 42,
            RevisionId = "active-revision",
            ActivatedAt = new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero),
            Definition = ApplicationDefinitionJson.Deserialize(
                $$"""
                {
                  "Resources": {
                    "private-resource": {
                      "Type": "private.type",
                      "Secret": "{{secret}}"
                    }
                  },
                  "Workflows": {}
                }
                """)
        };
        var update = new ApplicationUpdateResult
        {
            Status = ApplicationUpdateStatus.Rejected,
            RequestedRevisionId = "requested-revision",
            ActiveRevision = snapshot,
            Diagnostics =
            [
                Diagnostic(ApplicationUpdateStage.Planning, "first.code", "first message"),
                Diagnostic(ApplicationUpdateStage.Activation, "last.code", secret)
            ]
        };

        var result = FluxFlowApplicationHealthCheck.CreateResult(
            ApplicationState.Running,
            snapshot,
            update);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Exception.ShouldBeNull();
        AssertData(
            result,
            ("applicationState", "Running"),
            ("activeRevisionId", "active-revision"),
            ("activeSequence", 42L),
            ("requestedRevisionId", "requested-revision"),
            ("lastUpdateStatus", "Rejected"),
            ("diagnosticStage", "Activation"),
            ("diagnosticCode", "last.code"));
        var flattened = Flatten(result);
        flattened.ShouldNotContain("first.code");
        flattened.ShouldNotContain("first message");
        flattened.ShouldNotContain(secret);
        flattened.ShouldNotContain("private-resource");
        flattened.ShouldNotContain("private.type");
        flattened.ShouldNotContain(nameof(FlowError.Details));
    }

    [Fact]
    public async Task Health_check_observation_does_not_mutate_application_state_revision_ports_or_last_update()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(ReadyDefinition(), options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        await application.StartAsync();
        var state = application.State;
        var current = application.Current;
        var definition = application.CurrentDefinition;
        var lastUpdate = application.LastUpdate;
        var ports = application.Ports;

        var first = await CheckAsync(application);
        var second = await CheckAsync(application);

        first.Status.ShouldBe(HealthStatus.Healthy);
        second.Status.ShouldBe(first.Status);
        second.Description.ShouldBe(first.Description);
        second.Data.Count.ShouldBe(first.Data.Count);
        foreach (var (key, value) in first.Data)
            second.Data[key].ShouldBe(value);
        application.State.ShouldBe(state);
        application.Current.ShouldBeSameAs(current);
        application.CurrentDefinition.ShouldBeSameAs(definition);
        application.LastUpdate.ShouldBeSameAs(lastUpdate);
        application.Ports.ShouldBeSameAs(ports);
    }

    private static Task<HealthCheckResult> CheckAsync(FluxFlowApplication application)
        => new FluxFlowApplicationHealthCheck(application)
            .CheckHealthAsync(new HealthCheckContext());

    private static ApplicationDefinition ReadyDefinition()
        => ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {}
              }
            }
            """);

    private static ComponentContract<InputOutputComponentHandle<string, string>>
        CreateIdentityContract(
            Func<ComponentActivationContext, ValueTask<HealthIdentityNode>> factory)
        => ComponentContract.Create(
            "test.health.identity",
            component => component
                .UseFactory(factory)
                .HasInput("Input", static node => node.Input)
                .HasOutput("Output", static node => node.Output)
                .HasEvents("Events", static node => node.Events),
            static component => new InputOutputComponentHandle<string, string>(
                component,
                "Input",
                "Output",
                "Events"));

    private static (
        ApplicationDefinition Definition,
        InputOutputComponentHandle<string, string> Handle) IdentityDefinition(
            ComponentContract<InputOutputComponentHandle<string, string>> contract)
    {
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var handle = workflow.AddComponent("Value", contract);
        return (application.Build(), handle);
    }

    private static ApplicationSnapshot Snapshot(string revisionId)
        => new()
        {
            Sequence = 7,
            RevisionId = revisionId,
            ActivatedAt = new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero),
            Definition = new ApplicationDefinition()
        };

    private static ApplicationUpdateResult Update(
        ApplicationUpdateStatus status,
        string revisionId)
        => new()
        {
            Status = status,
            RequestedRevisionId = revisionId
        };

    private static ApplicationUpdateDiagnostic Diagnostic(
        ApplicationUpdateStage stage,
        string code,
        string secret)
        => new()
        {
            Stage = stage,
            Error = new FlowError(
                code,
                secret,
                "secret-source",
                false,
                JsonSerializer.SerializeToElement(new
                {
                    secret,
                    exceptionType = "Secret.Exception"
                }))
        };

    private static void AssertData(
        HealthCheckResult result,
        params (string Key, object Value)[] expected)
    {
        result.Data.Count.ShouldBe(expected.Length);
        result.Data.Count.ShouldBeLessThanOrEqualTo(7);
        result.Data.Keys.Order(StringComparer.Ordinal).ShouldBe(
            expected.Select(static item => item.Key).Order(StringComparer.Ordinal));
        foreach (var (key, value) in expected)
            result.Data[key].ShouldBe(value);
    }

    private static string Flatten(HealthCheckResult result)
        => string.Join(
            "|",
            new[] { result.Description }
                .Concat(result.Data.Keys)
                .Concat(result.Data.Values.Select(static value => value.ToString())));

    private sealed class MutableDefinitionSource(ApplicationDefinition definition)
        : IApplicationDefinitionSource
    {
        public ApplicationDefinition Definition { get; set; } = definition;

        public Exception? Failure { get; set; }

        public ValueTask<ApplicationDefinition> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure is null
                ? ValueTask.FromResult(Definition)
                : ValueTask.FromException<ApplicationDefinition>(Failure);
        }
    }

    private sealed class FailingDefinitionSource(string message)
        : IApplicationDefinitionSource
    {
        public ValueTask<ApplicationDefinition> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromException<ApplicationDefinition>(
                new InvalidOperationException(message));
        }
    }

    private sealed class CountingDefinitionSource : IApplicationDefinitionSource
    {
        public int LoadCount { get; private set; }

        public ValueTask<ApplicationDefinition> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            return ValueTask.FromResult(new ApplicationDefinition());
        }
    }

    private sealed class PreparationGate
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public async ValueTask<HealthIdentityNode> CreateNodeAsync()
        {
            _entered.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            return new HealthIdentityNode();
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class HealthIdentityNode : FlowNode<string, string>
    {
        protected override async Task ProcessAsync(FlowMessage<string> message)
            => await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}
