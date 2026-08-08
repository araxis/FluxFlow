using System.Reflection;
using System.Runtime.CompilerServices;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.HealthChecks.Tests;

public sealed class FluxFlowHealthCheckRegistrationTests
{
    [Fact]
    public void Public_surface_exposes_only_the_single_standard_builder_extension()
    {
        var assembly = typeof(FluxFlowHealthChecksBuilderExtensions).Assembly;
        var exported = assembly.GetExportedTypes();
        var extensionType = exported.ShouldHaveSingleItem();

        extensionType.ShouldBe(typeof(FluxFlowHealthChecksBuilderExtensions));
        var method = extensionType.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .ShouldHaveSingleItem();
        method.Name.ShouldBe(nameof(FluxFlowHealthChecksBuilderExtensions.AddFluxFlowApplication));
        method.ReturnType.ShouldBe(typeof(IHealthChecksBuilder));
        method.GetParameters().Select(static parameter => parameter.ParameterType)
            .ShouldBe(new[] { typeof(IHealthChecksBuilder) });
        method.GetCustomAttribute<ExtensionAttribute>().ShouldNotBeNull();
        typeof(FluxFlowApplicationHealthCheck).IsPublic.ShouldBeFalse();
        var marker = extensionType.GetNestedTypes(BindingFlags.NonPublic)
            .Single(static type =>
                type.Name == "FluxFlowApplicationHealthCheckRegistrationMarker");
        marker.IsNestedPrivate.ShouldBeTrue();
    }

    [Fact]
    public async Task Plain_AddFluxFlow_does_not_register_a_health_check()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(new ApplicationDefinition(), options => options.StartWithHost = false);
        services.AddHealthChecks();
        AddTestLogging(services);
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        options.Registrations.ShouldBeEmpty();
        report.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Health_check_registration_returns_the_standard_builder_and_resolves_the_application_singleton()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(
            ReadyDefinition(),
            options =>
            {
                options.StartWithHost = false;
                options.InitialRevisionId = "registered";
            });
        var builder = services.AddHealthChecks();

        var returned = builder.AddFluxFlowApplication();

        returned.ShouldBeSameAs(builder);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        await application.StartAsync();
        var registration = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.ShouldHaveSingleItem();
        var check = registration.Factory(provider);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        registration.Name.ShouldBe("fluxflow.application");
        registration.FailureStatus.ShouldBe(HealthStatus.Unhealthy);
        registration.Tags.Count.ShouldBe(2);
        registration.Tags.ShouldContain("fluxflow");
        registration.Tags.ShouldContain("ready");
        check.ShouldBeOfType<FluxFlowApplicationHealthCheck>();
        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data["activeRevisionId"].ShouldBe("registered");
    }

    [Fact]
    public async Task Repeated_health_check_registration_is_idempotent_and_preserves_unrelated_checks()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(ReadyDefinition(), options => options.StartWithHost = false);
        var builder = services.AddHealthChecks();
        AddTestLogging(services);
        builder.AddCheck(
            "sentinel",
            () => HealthCheckResult.Healthy("sentinel healthy"),
            tags: ["other"]);

        builder.AddFluxFlowApplication();
        builder.AddFluxFlowApplication();

        CountPrivateRegistrationMarkers(services).ShouldBe(1);
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<FluxFlowApplication>().StartAsync();
        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        registrations.Count.ShouldBe(2);
        registrations.Count(static registration =>
            registration.Name == "fluxflow.application").ShouldBe(1);
        registrations.Count(static registration => registration.Name == "sentinel").ShouldBe(1);
        report.Entries.Keys.Order(StringComparer.Ordinal)
            .ShouldBe(new[] { "fluxflow.application", "sentinel" });
        report.Entries["fluxflow.application"].Status.ShouldBe(HealthStatus.Healthy);
        report.Entries["sentinel"].Status.ShouldBe(HealthStatus.Healthy);
        report.Entries["sentinel"].Description.ShouldBe("sentinel healthy");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Health_check_registration_is_order_independent_from_AddFluxFlow(
        bool registerHealthFirst)
    {
        var services = new ServiceCollection();
        if (registerHealthFirst)
            services.AddHealthChecks().AddFluxFlowApplication();

        services.AddFluxFlow(ReadyDefinition(), options => options.StartWithHost = false);

        if (!registerHealthFirst)
            services.AddHealthChecks().AddFluxFlowApplication();

        AddTestLogging(services);

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<FluxFlowApplication>().StartAsync();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries.ShouldHaveSingleItem().Key.ShouldBe("fluxflow.application");
        report.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Missing_application_registration_reports_unhealthy_with_exact_unavailable_data()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddFluxFlowApplication();
        AddTestLogging(services);
        await using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        var entry = report.Entries.ShouldHaveSingleItem();

        entry.Key.ShouldBe("fluxflow.application");
        entry.Value.Status.ShouldBe(HealthStatus.Unhealthy);
        entry.Value.Description.ShouldBe("FluxFlow application services are not registered.");
        entry.Value.Exception.ShouldBeNull();
        entry.Value.Data.Count.ShouldBe(1);
        entry.Value.Data["applicationState"].ShouldBe("Unavailable");
    }

    [Fact]
    public void Health_check_registration_adds_no_hosted_service_background_worker_or_polling_state()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(new ApplicationDefinition());
        var builder = services.AddHealthChecks();
        var hostedBefore = services.Count(static descriptor =>
            descriptor.ServiceType == typeof(IHostedService));

        builder.AddFluxFlowApplication().AddFluxFlowApplication();

        services.Count(static descriptor => descriptor.ServiceType == typeof(IHostedService))
            .ShouldBe(hostedBefore);
        CountPrivateRegistrationMarkers(services).ShouldBe(1);
    }

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

    private static void AddTestLogging(IServiceCollection services)
    {
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    private static int CountPrivateRegistrationMarkers(IServiceCollection services)
        => services.Count(static descriptor =>
            descriptor.ImplementationInstance?.GetType().Name ==
            "FluxFlowApplicationHealthCheckRegistrationMarker");
}
