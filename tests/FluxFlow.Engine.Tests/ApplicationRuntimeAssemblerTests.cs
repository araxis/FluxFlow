using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Hosting.Snapshots;
using FluxFlow.Composition.Model;
using FluxFlow.Composition.Revisions;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationRuntimeAssemblerTests
{
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("Orders", "First", "Input");
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("Orders", "Second", "Output");

    [Fact]
    public async Task Canonical_json_builds_direct_ports_links_resources_and_revisions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ResourceTracker>();
        services.AddSingleton<RevisionEventTracker>();
        services.AddFluxFlowApplication(Definition("one:"))
            .UseRuntimeAssembler(runtime => runtime
                .RegisterNodes(RegisterNodes)
                .ConfigureServices(RegisterResources));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IApplicationRevisionHost>();
        var access = provider.GetRequiredService<IApplicationRuntimeAccess>();
        var resources = provider.GetRequiredService<ResourceTracker>();
        var revisionEvents = provider.GetRequiredService<RevisionEventTracker>();
        access.Ports.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => access.GetRequiredPorts());

        var started = await host.StartApplicationAsync();

        started.Succeeded.ShouldBeTrue();
        started.Update!.IsActivated.ShouldBeTrue();
        started.Update.Snapshot!.ProviderSnapshots.Select(static value => value.Boundary)
            .ShouldBe([
                CompositionProviderBoundary.ResourceRevision,
                CompositionProviderBoundary.WorkflowRevision
            ]);
        await EventuallyAsync(() => revisionEvents.Phases.Count >= 3);
        revisionEvents.Phases.Take(3).ShouldBe([
            ApplicationRevisionPhase.Proposed.ToString(),
            ApplicationRevisionPhase.Accepted.ToString(),
            ApplicationRevisionPhase.Activated.ToString()
        ]);
        var ports = access.GetRequiredPorts();
        ports.Ports.Count(static port =>
            port.Address.Kind == ApplicationAddressKind.WorkflowPort).ShouldBe(5);
        ports.CurrentRevision!.RevisionId.ShouldBe("initial");

        var firstReceive = ports.ReceiveAsync<string>(Output, TimeSpan.FromSeconds(5));
        (await ports.SendAsync(Input, FlowMessage.Create("value")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        var first = await firstReceive;
        first.Status.ShouldBe(PortReceiveStatus.Received);
        first.Message!.Payload.ShouldBe("one:one:value");

        var revised = await host.ApplyAsync("revision-2", Definition("two:"));

        revised.IsActivated.ShouldBeTrue();
        resources.Disposed.ShouldBe(1);
        access.GetRequiredPorts().ShouldBeSameAs(ports);
        ports.CurrentRevision!.RevisionId.ShouldBe("revision-2");
        var secondReceive = ports.ReceiveAsync<string>(Output, TimeSpan.FromSeconds(5));
        (await ports.SendAsync(Input, FlowMessage.Create("next")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        var second = await secondReceive;
        second.Status.ShouldBe(PortReceiveStatus.Received);
        second.Message!.Payload.ShouldBe("two:two:next");

        await host.StopApplicationAsync();
        resources.Disposed.ShouldBe(2);
    }

    [Fact]
    public async Task Revision_that_changes_the_direct_port_surface_is_rejected()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ResourceTracker>();
        services.AddSingleton<RevisionEventTracker>();
        services.AddFluxFlowApplication(Definition("one:"))
            .UseRuntimeAssembler(runtime => runtime
                .RegisterNodes(RegisterNodes)
                .ConfigureServices(RegisterResources));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IApplicationRevisionHost>();
        var access = provider.GetRequiredService<IApplicationRuntimeAccess>();
        (await host.StartApplicationAsync()).Succeeded.ShouldBeTrue();
        var ports = access.GetRequiredPorts();

        var rejected = await host.ApplyAsync("expanded", Definition("two:", includeThird: true));

        rejected.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        rejected.Failures.ShouldHaveSingleItem().Stage
            .ShouldBe(ApplicationRevisionFailureStage.Preparation);
        host.Current!.RevisionId.ShouldBe("initial");
        ports.CurrentRevision!.RevisionId.ShouldBe("initial");
        var receive = ports.ReceiveAsync<string>(Output, TimeSpan.FromSeconds(5));
        (await ports.SendAsync(Input, FlowMessage.Create("still-active")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        var received = await receive;
        received.Message!.Payload.ShouldBe("one:one:still-active");

        await host.StopApplicationAsync();
    }

    [Fact]
    public async Task Descriptor_that_does_not_match_registration_is_disposed_on_rejection()
    {
        var tracker = new DescriptorTracker();
        var services = new ServiceCollection();
        services.AddFluxFlowApplication(ApplicationDefinitionJson.Deserialize(
                """
                {
                  "Resources": {},
                  "Workflows": {
                    "Orders": {
                      "Invalid": { "Type": "test.invalid" }
                    }
                  }
                }
                """))
            .UseRuntimeAssembler(runtime => runtime.RegisterNodes(registry =>
                registry.Register(
                    "test.invalid",
                    _ =>
                    {
                        var node = new TrackedNode(tracker);
                        return ValueTask.FromResult(ComposedNode.Create(node));
                    },
                    inputs: [CompositionPorts.Metadata<string>("Input")])));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IApplicationRevisionHost>();

        var result = await host.StartApplicationAsync();

        result.Succeeded.ShouldBeFalse();
        result.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        tracker.Disposed.ShouldBe(1);
        provider.GetRequiredService<IApplicationRuntimeAccess>().Ports.ShouldBeNull();
    }

    private static void RegisterNodes(CompositionNodeRegistry registry)
        => registry
            .Register(
                "test.prefix",
                static context =>
                {
                    var resource = context.GetRequiredResource<PrefixResource>("Prefix");
                    var node = new PrefixNode(resource.Value);
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        inputs: [CompositionPorts.Input<string>("Input", node.Input)],
                        outputs: [CompositionPorts.Output<string>("Output", node.Output)],
                        events: node.Events,
                        errors: node.Errors));
                },
                inputs: [CompositionPorts.Metadata<string>("Input")],
                outputs: [CompositionPorts.Metadata<string>("Output")])
            .Register(
                "test.revision-events",
                static context =>
                {
                    var tracker = context.Services.GetRequiredService<RevisionEventTracker>();
                    var node = new RevisionEventNode(tracker);
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        inputs: [CompositionPorts.Input<ApplicationSystemEvent>("Input", node.Input)],
                        events: node.Events,
                        errors: node.Errors));
                },
                inputs: [CompositionPorts.Metadata<ApplicationSystemEvent>("Input")]);

    private static void RegisterResources(ApplicationRuntimeServicesContext context)
    {
        var definition = (ResourceInstanceDefinition)context.Definition.Resources["Prefix"];
        var value = definition.Properties["Value"].GetString()!;
        var tracker = context.HostServices.GetRequiredService<ResourceTracker>();
        context.Services.AddSingleton(
            context.HostServices.GetRequiredService<RevisionEventTracker>());
        context.Services.AddFluxFlowResource<PrefixResource>(
            ApplicationAddress.Resource("Prefix"),
            _ => new PrefixResource(value, tracker));
    }

    private static ApplicationDefinition Definition(string prefix, bool includeThird = false)
        => ApplicationDefinitionJson.Deserialize(
            $$"""
            {
              "Resources": {
                "Prefix": {
                  "Type": "test.prefix-resource",
                  "Value": "{{prefix}}"
                }
              },
              "Workflows": {
                "Orders": {
                  "First": {
                    "Type": "test.prefix",
                    "Prefix": "Resources.Prefix",
                    "Output": "Second.Input"
                  },
                  "Second": {
                    "Type": "test.prefix",
                    "Prefix": "Resources.Prefix"
                  },
                  "RevisionEvents": {
                    "Type": "test.revision-events",
                    "Input": "System.Events.Output"
                  }{{(includeThird ? ",\n                  \"Third\": { \"Type\": \"test.prefix\", \"Prefix\": \"Resources.Prefix\" }" : string.Empty)}}
                }
              }
            }
            """);

    private sealed class PrefixResource(string value, ResourceTracker tracker) : IAsyncDisposable
    {
        public string Value { get; } = value;

        public ValueTask DisposeAsync()
        {
            tracker.MarkDisposed();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ResourceTracker
    {
        private int _disposed;

        public int Disposed => Volatile.Read(ref _disposed);

        public void MarkDisposed() => Interlocked.Increment(ref _disposed);
    }

    private sealed class DescriptorTracker
    {
        private int _disposed;

        public int Disposed => Volatile.Read(ref _disposed);

        public void MarkDisposed() => Interlocked.Increment(ref _disposed);
    }

    private sealed class RevisionEventTracker
    {
        private readonly object _gate = new();
        private readonly List<string> _phases = [];

        public IReadOnlyList<string> Phases
        {
            get
            {
                lock (_gate)
                    return _phases.ToArray();
            }
        }

        public void Add(string phase)
        {
            lock (_gate)
                _phases.Add(phase);
        }
    }

    private sealed class PrefixNode(string prefix) : FlowNode<string, string>
    {
        protected override Task ProcessAsync(FlowMessage<string> message)
        {
            Emit(message.With(prefix + message.Payload));
            return Task.CompletedTask;
        }
    }

    private sealed class TrackedNode(DescriptorTracker tracker) : FlowNode<string, string>
    {
        protected override Task ProcessAsync(FlowMessage<string> message)
            => Task.CompletedTask;

        protected override ValueTask OnDisposeAsync()
        {
            tracker.MarkDisposed();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RevisionEventNode(RevisionEventTracker tracker)
        : FlowNode<ApplicationSystemEvent, ApplicationSystemEvent>
    {
        protected override Task ProcessAsync(FlowMessage<ApplicationSystemEvent> message)
        {
            tracker.Add(message.Payload.Details.GetObject()["phase"].GetString());
            return Task.CompletedTask;
        }
    }

    private static async Task EventuallyAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
            await Task.Delay(10, timeout.Token);
    }
}
