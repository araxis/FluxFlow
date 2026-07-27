using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class FluxFlowApplicationTests
{
    [Fact]
    public async Task One_registration_call_uses_the_resolved_application_for_hosted_lifecycle()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(
            Definition("boot"),
            options => options.InitialRevisionId = "boot-1");
        await using var provider = services.BuildServiceProvider();

        var application = provider.GetRequiredService<FluxFlowApplication>();
        provider.GetRequiredService<FluxFlowApplication>().ShouldBeSameAs(application);
        var hostedService = provider.GetServices<IHostedService>().Single();

        await hostedService.StartAsync(CancellationToken.None);

        application.State.ShouldBe(ApplicationState.Running);
        application.Current!.RevisionId.ShouldBe("boot-1");
        application.LastUpdate!.Status.ShouldBe(ApplicationUpdateStatus.Applied);
        application.Ports.Metadata.ShouldNotBeEmpty();

        await hostedService.StopAsync(CancellationToken.None);
        application.State.ShouldBe(ApplicationState.Stopped);
    }

    [Fact]
    public async Task Source_failure_is_a_rejected_update_and_direct_apply_recovers()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(
            new FailingDefinitionSource(),
            options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();

        var rejected = await application.StartAsync();
        var recovered = await application.ApplyAsync("manual-1", Definition("manual"));

        rejected.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        rejected.Diagnostics.Single().Stage.ShouldBe(ApplicationUpdateStage.Source);
        rejected.Diagnostics.Single().Error.Code.ShouldBe("revision.source.load_failed");
        recovered.Status.ShouldBe(ApplicationUpdateStatus.Applied);
        application.Current!.RevisionId.ShouldBe("manual-1");
        application.LastUpdate.ShouldBeSameAs(recovered);
    }

    [Fact]
    public async Task Generic_definition_source_registration_is_supported()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow<TestDefinitionSource>(options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<FluxFlowApplication>().StartAsync();

        result.Status.ShouldBe(ApplicationUpdateStatus.Applied);
        provider.GetRequiredService<IApplicationDefinitionSource>()
            .ShouldBeOfType<TestDefinitionSource>();
    }

    [Fact]
    public async Task Pre_canceled_start_does_not_enter_starting_or_load_the_source()
    {
        var source = new CountingDefinitionSource();
        var services = new ServiceCollection();
        services.AddFluxFlow(source, options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await application.StartAsync(cancellation.Token));

        application.State.ShouldBe(ApplicationState.Empty);
        application.Current.ShouldBeNull();
        application.LastUpdate.ShouldBeNull();
        source.LoadCount.ShouldBe(0);
    }

    [Fact]
    public async Task Reload_replaces_the_active_revision_and_reports_the_previous_snapshot()
    {
        var source = new MutableDefinitionSource(Definition("first"));
        var services = new ServiceCollection();
        services.AddFluxFlow(source, options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        var first = await application.StartAsync();
        source.Definition = Definition("second");

        var second = await application.ReloadAsync("reload-1");

        second.Status.ShouldBe(ApplicationUpdateStatus.Applied);
        second.PreviousRevision.ShouldBe(first.ActiveRevision);
        second.ActiveRevision!.RevisionId.ShouldBe("reload-1");
        application.Current.ShouldBe(second.ActiveRevision);
        application.State.ShouldBe(ApplicationState.Running);
    }

    [Fact]
    public async Task Reload_source_failure_preserves_the_active_revision()
    {
        var source = new MutableDefinitionSource(Definition("first"));
        var services = new ServiceCollection();
        services.AddFluxFlow(source, options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        var first = await application.StartAsync();
        source.Failure = new InvalidOperationException("source unavailable");

        var rejected = await application.ReloadAsync("reload-1");

        rejected.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        rejected.ActiveRevision.ShouldBe(first.ActiveRevision);
        application.Current.ShouldBe(first.ActiveRevision);
        application.State.ShouldBe(ApplicationState.Running);
    }

    [Fact]
    public async Task Dispose_after_stop_does_not_repeat_application_cleanup()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(Definition("boot"), options => options.StartWithHost = false);
        var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        await application.StartAsync();

        await application.StopAsync();
        await application.DisposeAsync();
        await application.DisposeAsync();

        application.State.ShouldBe(ApplicationState.Stopped);
        application.Current.ShouldBeNull();
        await provider.DisposeAsync();
    }

    private sealed class FailingDefinitionSource : IApplicationDefinitionSource
    {
        public ValueTask<ApplicationDefinition> LoadAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<ApplicationDefinition>(
                new InvalidOperationException("source unavailable"));
    }

    private sealed class TestDefinitionSource : IApplicationDefinitionSource
    {
        public ValueTask<ApplicationDefinition> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Definition("generic"));
        }
    }

    private sealed class CountingDefinitionSource : IApplicationDefinitionSource
    {
        public int LoadCount { get; private set; }

        public ValueTask<ApplicationDefinition> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return ValueTask.FromResult(Definition("counted"));
        }
    }

    private sealed class MutableDefinitionSource(ApplicationDefinition definition) :
        IApplicationDefinitionSource
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

    private static ApplicationDefinition Definition(string endpoint)
        => ApplicationDefinitionJson.Deserialize(
            $$"""
            {
              "Resources": {
                "resource": {
                  "Type": "test.resource",
                  "Endpoint": "{{endpoint}}"
                }
              },
              "Workflows": {}
            }
            """);
}
