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
