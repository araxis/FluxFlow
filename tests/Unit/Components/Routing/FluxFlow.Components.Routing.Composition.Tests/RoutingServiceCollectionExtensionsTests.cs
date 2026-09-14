using FluxFlow.Composition.Definition;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Composition;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Routing.Composition.Tests;

public sealed class RoutingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRouting_registers_only_direct_factories_and_is_idempotent()
    {
        var services = new ServiceCollection().AddRouting().AddRouting();
        using var provider = services.BuildServiceProvider();
        FluxFlowRegistrationExtensions.CreateFactoryMap(provider).Keys.OrderBy(x => x)
            .ShouldBe(new[] { "flow.correlate", "flow.gate", "flow.join", "flow.window" });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Typed_window_uses_embedded_factory_with_or_without_family_DI(bool registerFamily)
    {
        var builder = new ApplicationDefinitionBuilder();
        builder.AddWorkflow("main").AddWindow("window", options => options.MaxItems = 1).Events.DiscardUnmatched();
        var setup = builder.Build();
        setup.ComponentFactories.Keys.ShouldBe(new[] { "flow.window" });
        var services = new ServiceCollection();
        if (registerFamily) services.AddRouting();
        await using var provider = services.BuildServiceProvider();
        await using var app = await setup.BuildApplicationAsync(provider);
        await app.StartAsync();
        var reply = await app.RequestAsync("main.window.Input", "main.window.Output",
            FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(23, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), TimeSpan.FromSeconds(5));
        reply.Value!.Json.Deserialize<FlowWindow<JsonElement>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Items.Single().GetInt32().ShouldBe(23);
        await app.StopAsync();
    }

    [Fact]
    public async Task Hosted_window_binds_options_and_uses_keyed_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T12:30:00Z");
        await using var host = await StartNodeAsync(
            RoutingComponentDefinition.Types.Window,
            Properties(
                (RoutingComponentDefinition.Resources.Clock, "Resources.fixed"),
                ("maxItems", 2),
                ("boundedCapacity", 8)),
            ["fixed"],
            registry => registry.AddRouting(),
            services => services.AddExternalFluxFlowResource<TimeProvider>(
                ApplicationAddress.Resource("fixed"),
                new FakeTimeProvider(timestamp)));
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var outputResult = ports.ReceiveAsync<FlowWindow<JsonElement>>(
            Port(RoutingComponentDefinition.Ports.Output),
            Timeout);
        var first = FlowMessage.Create(
            JsonSerializer.SerializeToElement(10),
            new CorrelationId("window"));

        (await ports.SendAsync(Port(RoutingComponentDefinition.Ports.Input), first))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(
            Port(RoutingComponentDefinition.Ports.Input),
            FlowMessage.Create(JsonSerializer.SerializeToElement(20))))
            .IsAccepted.ShouldBeTrue();

        var window = (await outputResult).Message.ShouldNotBeNull();

        window.CorrelationId.ShouldBe(first.CorrelationId);
        window.IsError.ShouldBeFalse();
        window.Value.Items.Select(item => item.GetInt32()).ShouldBe([10, 20]);
        window.Value.StartedAt.ShouldBe(timestamp);
        window.Value.EmittedAt.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Hosted_join_resolves_selectors_and_routes_matches()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:30:00Z");
        await using var host = await StartNodeAsync(
            RoutingComponentDefinition.Types.Join,
            Properties(
                (RoutingComponentDefinition.Resources.LeftKeySelector, "Resources.left"),
                (RoutingComponentDefinition.Resources.RightKeySelector, "Resources.right"),
                (RoutingComponentDefinition.Resources.Clock, "Resources.fixed"),
                ("boundedCapacity", 8),
                ("timeoutMilliseconds", 5000)),
            ["left", "right", "fixed"],
            registry => registry.AddRouting(),
            services =>
            {
                services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                    ApplicationAddress.Resource("left"),
                    input => input.GetProperty("key").GetString());
                services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                    ApplicationAddress.Resource("right"),
                    input => input.GetProperty("key").GetString());
                services.AddExternalFluxFlowResource<TimeProvider>(
                    ApplicationAddress.Resource("fixed"),
                    new FakeTimeProvider(timestamp));
            });
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var outputResult = ports.ReceiveAsync<FlowValue>(
            Port(RoutingComponentDefinition.Ports.Output), Timeout);
        var leftMessage = FlowMessage.Create(
            RoutingItem("A-400", "left", "left"),
            new CorrelationId("left"));

        (await ports.SendAsync(Port(RoutingComponentDefinition.Ports.Left), leftMessage))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Port(RoutingComponentDefinition.Ports.Right), FlowMessage.Create(
                RoutingItem("A-400", "right", "right"),
                new CorrelationId("right"))))
            .IsAccepted.ShouldBeTrue();

        var result = (await outputResult).Message.ShouldNotBeNull();

        result.CorrelationId.ShouldBe(leftMessage.CorrelationId);
        result.IsError.ShouldBeFalse();
        var match = result.Value.Json.GetProperty("match");
        match.GetProperty("key").GetString().ShouldBe("A-400");
        match.GetProperty("left").GetProperty("value").GetString().ShouldBe("left");
        match.GetProperty("right").GetProperty("value").GetString().ShouldBe("right");
        match.GetProperty("joinedAt").GetDateTimeOffset().ShouldBe(timestamp);
    }

    [Fact]
    public async Task Missing_required_selector_surfaces_factory_diagnostic()
    {
        await using var host = await StartNodeAsync(
            RoutingComponentDefinition.Types.Correlation,
            Properties((RoutingComponentDefinition.Resources.SideSelector, "Resources.side")),
            ["side"],
            registry => registry.AddRouting(),
            services => services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                ApplicationAddress.Resource("side"),
                input => input.GetProperty("side").GetString()));

        AssertPreparationFailure(host, RoutingComponentDefinition.Resources.KeySelector);
    }

    [Fact]
    public async Task Invalid_routing_options_surface_factory_diagnostic()
    {
        await AssertFactoryDiagnosticAsync(
            RoutingComponentDefinition.Types.Window,
            Properties(("maxItems", -1)),
            null,
            null,
            registry => registry.AddRouting(),
            "MaxItems");

        await AssertFactoryDiagnosticAsync(
            RoutingComponentDefinition.Types.Correlation,
            Properties(
                (RoutingComponentDefinition.Resources.KeySelector, "Resources.key"),
                (RoutingComponentDefinition.Resources.SideSelector, "Resources.side"),
                ("timeoutMilliseconds", 0)),
            ["key", "side"],
            services =>
            {
                services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                    ApplicationAddress.Resource("key"),
                    input => input.GetProperty("key").GetString());
                services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                    ApplicationAddress.Resource("side"),
                    input => input.GetProperty("side").GetString());
            },
            registry => registry.AddRouting(),
            "TimeoutMilliseconds");
    }

    [Fact]
    public async Task Hosted_gate_binds_flat_configuration_and_preserves_the_message()
    {
        await using var host = await StartNodeAsync("flow.gate",
            Properties(("initiallyOpen", true), ("boundedCapacity", 4)), null,
            services => services.AddRouting());
        host.StartResult.Succeeded.ShouldBeTrue();
        var ports = host.GetRequiredPorts();
        var response = ports.ReceiveAsync<int>(Port("Output"), Timeout);
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web))), CorrelationId.New());
        (await ports.SendAsync(Port("Input"), message)).IsAccepted.ShouldBeTrue();
        var received = (await response).Message.ShouldNotBeNull();
        received.Value.ShouldBe(42);
        received.CorrelationId.ShouldBe(message.CorrelationId);
    }

    [Fact]
    public async Task Invalid_gate_capacity_is_reported_by_the_component_factory()
        => await AssertFactoryDiagnosticAsync("flow.gate", Properties(("boundedCapacity", 0)),
            null, null, services => services.AddRouting(), "InputCapacity");

    private static async Task AssertFactoryDiagnosticAsync(
        string componentType,
        IReadOnlyDictionary<string, object?> properties,
        IReadOnlyList<string>? resources,
        Action<IServiceCollection>? configureRuntimeServices,
        Action<IServiceCollection> addComponents,
        string expectedMessage)
    {
        await using var host = await StartNodeAsync(
            componentType,
            properties,
            resources,
            addComponents,
            configureRuntimeServices);
        AssertPreparationFailure(host, expectedMessage);
    }

    private static ValueTask<CanonicalApplicationTestHost> StartNodeAsync(
        string componentType,
        IReadOnlyDictionary<string, object?> properties,
        IReadOnlyList<string>? resources,
        Action<IServiceCollection> addComponents,
        Action<IServiceCollection>? configureRuntimeServices = null)
        => CanonicalApplicationTestHost.StartAsync(
            SingleComponent(componentType, properties, resources, discardUnmatched: ["Output", "Events"]),
            addComponents,
            registerResources: configureRuntimeServices is null
                ? null
                : context => configureRuntimeServices(context.Services));

    private static ApplicationAddress Port(string name)
        => ApplicationAddress.WorkflowPort("main", "node", name);

    private static void AssertPreparationFailure(
        CanonicalApplicationTestHost host,
        string expectedMessage)
    {
        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        host.StartResult.Update.Diagnostics.ShouldContain(failure =>
            failure.Stage == ApplicationUpdateStage.ComponentPreparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static JsonElement RoutingItem(string key, string side, string value)
        => JsonSerializer.SerializeToElement(new { key, side, value });

}
