using System.Threading.Tasks.Dataflow;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using System.Text;
using System.Text.Json;
using FluxFlow.Configuration.Components.Scheduling;
using FluxFlow.Application;
using FluxFlow.Configuration.Application.Links;
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
        var schedule = new Dictionary<string, object?>
        {
            ["type"] = "scheduler"
        };
        if (mode == "interval")
            schedule["interval"] = "00:00:05";
        if (mode == "at")
            schedule["at"] = "2026-01-01T00:00:05Z";
        if (mode == "cron")
        {
            schedule["cron"] = "*/5 * * * * *";
            schedule["includeSeconds"] = true;
            schedule["timeZone"] = "UTC";
        }

        var sink = new Dictionary<string, object?>
        {
            ["type"] = "sink"
        };
        if (fromInput)
            sink["input"] = source;
        else
            schedule["output"] = target;
        var demo = new Dictionary<string, object?>
        {
            ["clock"] = schedule
        };
        var workflows = new Dictionary<string, object?>
        {
            ["demo"] = demo
        };
        if (crossWorkflow)
            workflows["other"] = new
            {
                sink
            };
        else
            demo["sink"] = sink;
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
            registrations.AddComponentFactory(ComponentAddress.Parse(address.Value), _ => new Sink(received));
            registrations.AddInputPort<Sink>(ComponentAddress.Parse(address.Value), "Input", node => node.Input);
            registrations.AddInputLinks(address.Port("Input"), section.ReadConfiguration<TestPortConfiguration>()!.Input);
        });
        services.AddFluxFlowApplication(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        app.Workflows.Select(workflow => workflow.Name).ShouldContain("demo");
        received.Task.IsCompleted.ShouldBeFalse();
        await app.StartAsync();
        app.GetComponent(ComponentAddress.Parse("demo.clock")).ShouldNotBeNull();
        app.GetComponent(ComponentAddress.Parse(crossWorkflow ? "other.sink" : "demo.sink")).ShouldBeOfType<Sink>();
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
    public void Invalid_settings_fail_before_start_with_component_address(string settings)
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, object?>>(settings)!;
        properties["type"] = "scheduler";
        var json = JsonSerializer.Serialize(new { workflows = new { demo = new { clock = properties } } });
        var services = new ServiceCollection().AddScheduler();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var error = Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(configuration));
        error.Message.ShouldContain("demo.clock");
    }

    [Theory]
    [InlineData("missing-type", null)]
    [InlineData("scheduler", "missing.Input")]
    public async Task Construction_failure_stops_components_and_provider_disposes_them(string type, string? output)
    {
        var json = JsonSerializer.Serialize(new { workflows = new { demo = new { a = new { type = "sink" }, z = new { type, interval = "00:00:01", output } } } });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = (IDisposable)configuration;
        Sink? created = null;
        var services = new ServiceCollection().AddScheduler();
        services.AddKeyedSingleton<ComponentRegistration>("sink", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            registrations.AddComponentFactory(ComponentAddress.Parse(address.Value), _ => created = new Sink(new TaskCompletionSource<FlowMessage>()));
            registrations.AddInputPort<Sink>(ComponentAddress.Parse(address.Value), "Input", node => node.Input);
            registrations.AddInputLinks(address.Port("Input"), section.ReadConfiguration<TestPortConfiguration>()!.Input);
        });
        if (type == "missing-type")
        {
            Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(configuration));
            created.ShouldBeNull();
            services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IFlowNode));
            return;
        }

        services.AddFluxFlowApplication(configuration);
        await using var provider = services.BuildServiceProvider();
        created.ShouldBeNull();
        await Should.ThrowAsync<Exception>(() => provider.BuildApplicationAsync());
        created.ShouldNotBeNull();
        created.Completion.IsCompleted.ShouldBeTrue();
        created.Disposed.ShouldBeTrue();
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
        services.AddFluxFlowApplication(configuration);
        var count = services.Count;
        Should.Throw<ArgumentException>(() => services.AddFluxFlowApplication(configuration));
        services.Count.ShouldBe(count);
        configuration["workflows:demo:clock:interval"] = "invalid after registration";
        await using var provider = services.BuildServiceProvider();
        await using var otherHost = services.BuildServiceProvider();
        services.Single(item => item.IsKeyedService && item.ServiceType == typeof(IFlowNode) && Equals(item.ServiceKey, "demo.clock")).KeyedImplementationFactory.ShouldNotBeNull();
        await using var first = await provider.BuildApplicationAsync();
        await using var second = await otherHost.BuildApplicationAsync();
        first.Workflows.Select(workflow => workflow.Name).ShouldContain("demo");
        second.Workflows.Select(workflow => workflow.Name).ShouldContain("demo");
        await first.StartAsync();
        await second.StartAsync();
        first.GetComponent(ComponentAddress.Parse("demo.clock")).ShouldNotBeSameAs(second.GetComponent(ComponentAddress.Parse("demo.clock")));
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
        services.AddFluxFlowApplication(configuration.GetSection("MyFlow"));
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        app.GetComponent(ComponentAddress.Parse("demo.clock")).ShouldNotBeNull();
    }

    private sealed class Sink : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Sink(TaskCompletionSource<FlowMessage> received)
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            this.received = received;
            Lifetime.Observe();
        }

        private readonly TaskCompletionSource<FlowMessage> received;
        public bool Disposed { get; private set; }

        protected override ValueTask OnDisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        {
            received.TrySetResult(message);
            return Task.FromResult<IEnumerable<FlowMessage>>([]);
        }
    }
}
