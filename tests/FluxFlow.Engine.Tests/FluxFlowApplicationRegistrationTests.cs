using System.Text;
using FluxFlow.Composition;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class FluxFlowApplicationRegistrationTests
{
    [Fact]
    public void Retained_AddFluxFlow_shapes_return_the_flat_registration_builder()
    {
        AssertBuilder(
            static services => services.AddFluxFlow(new ApplicationDefinition()));
        AssertBuilder(
            services => services.AddFluxFlow(CreateConfiguration(nested: false)));
        AssertBuilder(
            static services => services.AddFluxFlow(new TestDefinitionSource()));
        AssertBuilder(
            static services => services.AddFluxFlow<TestDefinitionSource>());
    }

    [Fact]
    public async Task Configuration_registration_reads_the_root_definition_by_default()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlow(CreateConfiguration(nested: false));
        using var provider = services.BuildServiceProvider();

        var definition = await provider
            .GetRequiredService<IApplicationDefinitionSource>()
            .LoadAsync();

        builder.Services.ShouldBeSameAs(services);
        definition.Workflows.ContainsKey("Main").ShouldBeTrue();
    }

    [Fact]
    public async Task Configuration_registration_reads_an_explicit_custom_section()
    {
        var services = new ServiceCollection();
        var builder = services.AddFluxFlow(
            CreateConfiguration(nested: true),
            sectionName: "Application");
        using var provider = services.BuildServiceProvider();

        var definition = await provider
            .GetRequiredService<IApplicationDefinitionSource>()
            .LoadAsync();

        builder.Services.ShouldBeSameAs(services);
        definition.Workflows.ContainsKey("Main").ShouldBeTrue();
    }

    [Fact]
    public void Application_options_are_applied_and_capacities_reach_the_runtime_options()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(new ApplicationDefinition(), options =>
        {
            options.InitialRevisionId = "deployment-42";
            options.StartWithHost = false;
            options.StopWithHost = false;
            options.InputCapacity = 17;
            options.OutputCapacity = 23;
        });
        using var provider = services.BuildServiceProvider();

        var application = provider
            .GetRequiredService<IOptions<FluxFlowApplicationOptions>>()
            .Value;
        var runtime = provider
            .GetRequiredService<IOptions<ApplicationRuntimeAssemblerOptions>>()
            .Value;

        application.InitialRevisionId.ShouldBe("deployment-42");
        application.StartWithHost.ShouldBeFalse();
        application.StopWithHost.ShouldBeFalse();
        application.InputCapacity.ShouldBe(17);
        application.OutputCapacity.ShouldBe(23);
        runtime.InputCapacity.ShouldBe(17);
        runtime.OutputCapacity.ShouldBe(23);
    }

    [Theory]
    [InlineData(0, 128, nameof(FluxFlowApplicationOptions.InputCapacity))]
    [InlineData(-1, 128, nameof(FluxFlowApplicationOptions.InputCapacity))]
    [InlineData(128, 0, nameof(FluxFlowApplicationOptions.OutputCapacity))]
    [InlineData(128, -1, nameof(FluxFlowApplicationOptions.OutputCapacity))]
    public void Non_positive_capacities_are_rejected_at_the_options_boundary(
        int inputCapacity,
        int outputCapacity,
        string expectedOption)
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(new ApplicationDefinition(), options =>
        {
            options.InputCapacity = inputCapacity;
            options.OutputCapacity = outputCapacity;
        });
        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<FluxFlowApplicationOptions>>().Value);

        exception.Failures.Any(failure => failure.Contains(expectedOption, StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Fact]
    public void Blank_initial_revision_id_is_rejected_at_the_options_boundary()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(
            new ApplicationDefinition(),
            options => options.InitialRevisionId = "   ");
        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<FluxFlowApplicationOptions>>().Value);

        exception.Failures.Any(failure => failure.Contains(
                nameof(FluxFlowApplicationOptions.InitialRevisionId),
                StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    private static void AssertBuilder(
        Func<IServiceCollection, FluxFlowRegistrationBuilder> register)
    {
        var services = new ServiceCollection();

        var builder = register(services);

        builder.Services.ShouldBeSameAs(services);
    }

    private static IConfiguration CreateConfiguration(bool nested)
    {
        var json = nested
            ? """
              {
                "Application": {
                  "Resources": {},
                  "Workflows": { "Main": {} }
                }
              }
              """
            : """
              {
                "Resources": {},
                "Workflows": { "Main": {} }
              }
              """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }

    private sealed class TestDefinitionSource : IApplicationDefinitionSource
    {
        public ValueTask<ApplicationDefinition> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ApplicationDefinition());
        }
    }
}
