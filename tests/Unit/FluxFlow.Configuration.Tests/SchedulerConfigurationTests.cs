using System.Text;
using System.Text.Json;
using FluxFlow.Configuration.Scheduling;
using FluxFlow.Configuration;
using FluxFlow.Core.Application;

using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;

public sealed class SchedulerConfigurationTests
{
    [Theory]
    [InlineData(false, false, "interval")]
    [InlineData(true, false, "interval")]
    [InlineData(false, true, "interval")]
    [InlineData(true, true, "interval")]
    [InlineData(false, false, "cron")]
    [InlineData(false, false, "at")]
    public async Task Configuration_builds_addresses_links_and_stops(bool fromInput, bool crossWorkflow, string mode)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var received = new TaskCompletionSource<FlowMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = crossWorkflow ? "other.sink.Input" : "sink.Input";
        var source = crossWorkflow ? "demo.clock.Output" : "clock.Output";
        var schedule = new Dictionary<string, object?> { ["type"] = "scheduler" };
        if (mode == "interval") schedule["interval"] = "00:00:05";
        if (mode == "at") schedule["at"] = "2026-01-01T00:00:05Z";
        if (mode == "cron")
        {
            schedule["cron"] = "*/5 * * * * *";
            schedule["includeSeconds"] = true;
            schedule["timeZone"] = "UTC";
        }
        var sink = new Dictionary<string, object?> { ["type"] = "sink" };
        if (fromInput) sink["input"] = source;
        else schedule["output"] = target;
        var demo = new Dictionary<string, object?> { ["clock"] = schedule };
        var workflows = new Dictionary<string, object?> { ["demo"] = demo };
        if (crossWorkflow) workflows["other"] = new { sink };
        else demo["sink"] = sink;
        var json = JsonSerializer.Serialize(new { workflows });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddScheduler().AddScheduler(); // Repeated identical registration is harmless.
        services.AddKeyedSingleton<ComponentRegistration>("sink", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            registrations.AddComponentFactory(address, _ => new Sink(received));
            registrations.AddInputPort<Sink>(address, "Input", node => node.Input);
        });
        await services.AddFluxFlowApplicationAsync(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        app.Workflows.Keys.ShouldContain("demo");
        received.Task.IsCompleted.ShouldBeFalse();
        await app.StartAsync();
        app.GetComponent("demo.clock").ShouldNotBeNull();
        app.GetComponent(crossWorkflow ? "other.sink" : "demo.sink").ShouldBeOfType<Sink>();
        clock.Advance(TimeSpan.FromSeconds(5));
        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        message.Value.Json.ValueKind.ShouldBe(JsonValueKind.Null);
        await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await app.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"interval\":\"00:00:01\",\"cron\":\"* * * * *\"}")]
    [InlineData("{\"interval\":\"00:00:00\"}")]
    [InlineData("{\"interval\":\"00:00:01\",\"includeSeconds\":true}")]
    [InlineData("{\"cron\":\"invalid\"}")]
    [InlineData("{\"cron\":\"* * * * *\",\"timeZone\":\"not-a-zone\"}")]
    [InlineData("{\"interval\":\"00:00:01\",\"outputCapacity\":0}")]
    public async Task Invalid_settings_fail_before_start_with_component_address(string settings)
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, object?>>(settings)!;
        properties["type"] = "scheduler";
        var json = JsonSerializer.Serialize(new { workflows = new { demo = new { clock = properties } } });
        var services = new ServiceCollection().AddScheduler();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var error = await Should.ThrowAsync<InvalidOperationException>(() => services.AddFluxFlowApplicationAsync(configuration).AsTask());
        error.Message.ShouldContain("demo.clock");
    }

    [Theory]
    [InlineData("missing-type", null)]
    [InlineData("scheduler", "missing.Input")]
    public async Task Construction_failure_stops_components_and_provider_disposes_them(string type, string? output)
    {
        var json = JsonSerializer.Serialize(new
        {
            workflows = new
            {
                demo = new
                {
                    a = new { type = "sink" },
                    z = new { type, interval = "00:00:01", output }
                }
            }
        });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = (IDisposable)configuration;
        Sink? created = null;
        var services = new ServiceCollection().AddScheduler();
        services.AddKeyedSingleton<ComponentRegistration>("sink", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            registrations.AddComponentFactory(address, _ => created = new Sink(new TaskCompletionSource<FlowMessage>()));
            registrations.AddInputPort<Sink>(address, "Input", node => node.Input);
        });
        if (type == "missing-type")
        {
            await Should.ThrowAsync<InvalidOperationException>(() => services.AddFluxFlowApplicationAsync(configuration).AsTask());
            created.ShouldBeNull();
            services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(ComponentFactory));
            return;
        }
        await services.AddFluxFlowApplicationAsync(configuration);
        await using var provider = services.BuildServiceProvider();
        created.ShouldBeNull();
        await Should.ThrowAsync<Exception>(() => provider.BuildApplicationAsync());
        created.ShouldNotBeNull();
        created.Completion.IsCompleted.ShouldBeTrue();
        created.Disposed.ShouldBeFalse();
        await provider.DisposeAsync();
        created.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Registration_captures_settings_and_independent_hosts_get_fresh_components()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            { "workflows": { "demo": {
                "clock": { "type": "scheduler", "interval": "00:00:05" }
            } } }
            """));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var services = new ServiceCollection().AddScheduler();
        await services.AddFluxFlowApplicationAsync(configuration);
        var count = services.Count;
        await Should.ThrowAsync<ArgumentException>(() => services.AddFluxFlowApplicationAsync(configuration).AsTask());
        services.Count.ShouldBe(count);
        configuration["workflows:demo:clock:interval"] = "invalid after registration";
        await using var provider = services.BuildServiceProvider();
        await using var otherHost = services.BuildServiceProvider();
        provider.GetRequiredKeyedService<ComponentFactory>("demo.clock").ShouldNotBeNull();
        await using var first = await provider.BuildApplicationAsync();
        await using var second = await otherHost.BuildApplicationAsync();
        first.Workflows.Keys.ShouldContain("demo");
        second.Workflows.Keys.ShouldContain("demo");
        await first.StartAsync();
        await second.StartAsync();
        first.GetComponent("demo.clock").ShouldNotBeSameAs(second.GetComponent("demo.clock"));
    }

    [Fact]
    public async Task Host_can_explicitly_select_a_nested_section()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            { "MyFlow": { "Workflows": { "demo": {
                "clock": { "type": "scheduler", "interval": "00:00:05" }
            } } } }
            """));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var services = new ServiceCollection().AddScheduler();
        await services.AddFluxFlowApplicationAsync(configuration.GetSection("MyFlow"));
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        app.GetComponent("demo.clock").ShouldNotBeNull();
    }

    private sealed class Sink(TaskCompletionSource<FlowMessage> received) : FlowTransformNode
    {
        public bool Disposed { get; private set; }
        protected override ValueTask OnDisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        {
            received.TrySetResult(message);
            return Task.FromResult<IEnumerable<FlowMessage>>([]);
        }
    }
}
