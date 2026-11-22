using System.Threading.Tasks.Dataflow;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using System.Text;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;
public sealed class ResourceBoundaryTests
{
    [Theory]
    [InlineData("Resources:group:bad")]
    [InlineData("Workflows:main:Resources:group:bad")]
    public void Scalar_resource_entries_are_rejected_without_changing_registrations(string path)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Workflows:main"] = null, [path] = "not a resource section" }).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var services = new ServiceCollection();
        services.AddSingleton(new object ());
        var original = services.ToArray();
        var error = Should.Throw<CompositionConfigurationException>(() => services.AddFluxFlowApplication(configuration));
        error.Message.ShouldContain(path);
        services.ToArray().ShouldBe(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resource_DTOs_support_grouped_local_and_application_addresses(bool local)
    {
        var resource = "\"Resources\":{\"connections\":{\"fast\":{\"Type\":\"test.connection\",\"Endpoint\":\"local\"}}},";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{" + (local ? "" : resource) + "\"Workflows\":{\"main\":{" + (local ? resource : "") + "\"processor\":{\"Type\":\"test.process\"}}}}"));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var services = new ServiceCollection();
        ConnectionConfiguration? resolved = null;
        services.AddKeyedSingleton<ResourceRegistration>("test.connection", (registrations, address, section) =>
        {
            var config = section.Get<ConnectionConfiguration>()!;
            registrations.AddFluxFlowResource(address, _ => config);
        });
        services.AddKeyedSingleton<ComponentRegistration>("test.process", (registrations, section) => registrations.AddComponentFactory(section.GetComponentAddress(), dependencies =>
        {
            resolved = dependencies.GetRequiredFlowResource<ConnectionConfiguration>("connections.fast", "main");
            return new Processor([]);
        }));
        services.AddFluxFlowApplication(configuration);
        resolved.ShouldBeNull();
        await using var provider = services.BuildServiceProvider();
        await using var application = await provider.BuildApplicationAsync();
        resolved.ShouldBe(new ConnectionConfiguration { Endpoint = "local" });
    }

    [Theory]
    [InlineData("unknown", "local")]
    [InlineData("test.connection", "")]
    public void Invalid_resources_fail_at_registration_without_changing_host_services(string type, string endpoint)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($$$"""
            {"Resources":{"bad":{"Type":"{{{type}}}","Endpoint":"{{{endpoint}}}"}},"Workflows":{}}
            """));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var services = new ServiceCollection();
        services.AddSingleton(new object ());
        services.AddKeyedSingleton<ResourceRegistration>("test.connection", (registrations, address, section) =>
        {
            var config = section.Get<ConnectionConfiguration>()!;
            ArgumentException.ThrowIfNullOrWhiteSpace(config.Endpoint);
            registrations.AddFluxFlowResource(address, _ => config);
        });
        var original = services.ToArray();
        var error = Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(configuration));
        error.Message.ShouldContain("resources.bad");
        services.ToArray().ShouldBe(original);
    }

    [Fact]
    public async Task Host_resource_registration_runs_once_before_build()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            {"Resources":{"fast":{"Type":"test.connection","Endpoint":"host-defined"}},
             "Workflows":{"main":{"processor":{"Type":"test.process"}}}}
            """));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var services = new ServiceCollection();
        var expected = new ConnectionConfiguration
        {
            Endpoint = "host-defined"
        };
        var calls = 0;
        services.AddKeyedSingleton<ResourceRegistration>("test.connection", (resources, address, _) =>
        {
            calls++;
            resources.AddFluxFlowResource(address, _ => expected);
        });
        services.AddKeyedSingleton<ComponentRegistration>("test.process", (registrations, section) => registrations.AddComponentFactory(section.GetComponentAddress(), dependencies =>
        {
            dependencies.GetRequiredFlowResource<ConnectionConfiguration>("fast").ShouldBeSameAs(expected);
            return new Processor([]);
        }));
        services.AddFluxFlowApplication(configuration);
        calls.ShouldBe(1);
        await using var provider = services.BuildServiceProvider();
        await using var application = await provider.BuildApplicationAsync();
        calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task Resources_are_created_at_build_and_owned_by_the_host_provider(bool local, int resourceCount)
    {
        var resources = string.Join(",", Enumerable.Range(1, resourceCount).Select(index => $"\"store{index}\":{{\"Type\":\"test.resource\"}}"));
        var section = "\"Resources\":{" + resources + "},";
        var json = "{" + (local ? "" : section) + "\"Workflows\":{\"main\":{" + (local ? section : "") + "\"processor\":{\"Type\":\"test.process\"}}}}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var log = new List<string>();
        var created = 0;
        var registrations = 0;
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ResourceRegistration>("test.resource", (resources, address, _) =>
        {
            registrations++;
            resources.AddFluxFlowResource(address, _ =>
            {
                created++;
                return new Resource(log);
            });
        });
        services.AddKeyedSingleton<ComponentRegistration>("test.process", (registrations, component) => registrations.AddComponentFactory(component.GetComponentAddress(), dependencies =>
        {
            foreach (var index in Enumerable.Range(1, resourceCount))
                dependencies.GetRequiredFlowResource<Resource>("store" + index, "main");
            return new Processor(log);
        }));
        services.AddFluxFlowApplication(configuration);
        created.ShouldBe(0);
        registrations.ShouldBe(resourceCount);
        await using var provider = services.BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        created.ShouldBe(resourceCount);
        registrations.ShouldBe(resourceCount);
        await app.DisposeAsync();
        log.Count(item => item == "node").ShouldBe(1);
        log.Count(item => item == "resource").ShouldBe(local ? resourceCount : 0);
        await provider.DisposeAsync();
        log.Count(item => item == "resource").ShouldBe(resourceCount);
        log[0].ShouldBe("node");
    }

    private sealed record ConnectionConfiguration
    {
        public string? Endpoint { get; init; }
    }

    private sealed class Resource(List<string> log) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            log.Add("resource");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Processor : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Processor(List<string> log)
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
            this.log = log;
            Lifetime.Observe();
        }

        private readonly List<string> log;
        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => Task.FromResult<IEnumerable<FlowMessage>>([message]);
        protected override ValueTask OnDisposeAsync()
        {
            log.Add("node");
            return ValueTask.CompletedTask;
        }
    }
}
