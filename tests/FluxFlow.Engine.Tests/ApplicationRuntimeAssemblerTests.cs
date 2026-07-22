using System.Threading.Tasks.Dataflow;
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
    private static readonly ApplicationAddress FirstEvents =
        ApplicationAddress.WorkflowPort("Orders", "First", "Events");
    private static readonly ApplicationAddress ThirdInput =
        ApplicationAddress.WorkflowPort("Orders", "Third", "Input");
    private static readonly ApplicationAddress ThirdOutput =
        ApplicationAddress.WorkflowPort("Orders", "Third", "Output");

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
            port.Address.Kind == ApplicationAddressKind.WorkflowPort).ShouldBe(8);
        ports.CurrentRevision!.RevisionId.ShouldBe("initial");

        var firstReceive = ports.ReceiveAsync<string>(Output, TimeSpan.FromSeconds(5));
        var eventReceive = ports.ReceiveAsync<CompositionComponentEvent>(
            FirstEvents,
            TimeSpan.FromSeconds(5));
        (await ports.SendAsync(Input, FlowMessage.Create("value")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        var first = await firstReceive;
        first.Status.ShouldBe(PortReceiveStatus.Received);
        first.Message!.Payload.ShouldBe("one:one:value");
        var componentEvent = await eventReceive;
        componentEvent.Status.ShouldBe(PortReceiveStatus.Received);
        componentEvent.Message!.Payload.ComponentAddress.ShouldBe("Orders.First");
        componentEvent.Message.Payload.Name.ShouldBe("prefix.processed");
        componentEvent.Message.TraceId.IsEmpty.ShouldBeFalse();

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
    public async Task Revision_that_changes_the_direct_port_surface_replaces_the_current_generation()
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
        (await host.StartApplicationAsync()).Succeeded.ShouldBeTrue();
        var ports = access.GetRequiredPorts();

        var expanded = await host.ApplyAsync("expanded", Definition("two:", includeThird: true));

        expanded.Status.ShouldBe(ApplicationRevisionUpdateStatus.Activated);
        host.Current!.RevisionId.ShouldBe("expanded");
        var expandedPorts = access.GetRequiredPorts();
        expandedPorts.ShouldNotBeSameAs(ports);
        expandedPorts.CurrentRevision!.RevisionId.ShouldBe("expanded");
        await ports.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        (await ports.SendAsync(Input, FlowMessage.Create("retired")))
            .Status.ShouldBe(PortSendStatus.Completed);

        var receive = expandedPorts.ReceiveAsync<string>(Output, TimeSpan.FromSeconds(5));
        (await expandedPorts.SendAsync(Input, FlowMessage.Create("active")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        var received = await receive;
        received.Message!.Payload.ShouldBe("two:two:active");

        var thirdReceive = expandedPorts.ReceiveAsync<string>(ThirdOutput, TimeSpan.FromSeconds(5));
        (await expandedPorts.SendAsync(ThirdInput, FlowMessage.Create("direct")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await thirdReceive).Message!.Payload.ShouldBe("two:direct");

        var contracted = await host.ApplyAsync("contracted", Definition("three:"));

        contracted.Status.ShouldBe(ApplicationRevisionUpdateStatus.Activated);
        var contractedPorts = access.GetRequiredPorts();
        contractedPorts.ShouldNotBeSameAs(expandedPorts);
        await expandedPorts.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Should.Throw<KeyNotFoundException>(() =>
            contractedPorts.SendAsync(ThirdInput, FlowMessage.Create("removed")));
        var contractedReceive = contractedPorts.ReceiveAsync<string>(Output, TimeSpan.FromSeconds(5));
        (await contractedPorts.SendAsync(Input, FlowMessage.Create("current")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await contractedReceive).Message!.Payload.ShouldBe("three:three:current");

        await host.StopApplicationAsync();
        resources.Disposed.ShouldBe(3);
    }

    [Fact]
    public async Task Rejected_surface_change_leaves_the_current_generation_active()
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

        var rejected = await host.ApplyAsync("invalid", InvalidExpandedDefinition());

        rejected.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        rejected.Failures.ShouldHaveSingleItem().Stage
            .ShouldBe(ApplicationRevisionFailureStage.Preparation);
        host.Current!.RevisionId.ShouldBe("initial");
        access.GetRequiredPorts().ShouldBeSameAs(ports);
        ports.CurrentRevision!.RevisionId.ShouldBe("initial");
        var receive = ports.ReceiveAsync<string>(Output, TimeSpan.FromSeconds(5));
        (await ports.SendAsync(Input, FlowMessage.Create("still-active")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await receive).Message!.Payload.ShouldBe("one:one:still-active");

        await host.StopApplicationAsync();
    }

    [Fact]
    public async Task Revision_that_changes_a_port_payload_type_replaces_the_generation()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowApplication(IdentityDefinition("test.identity-string"))
            .UseRuntimeAssembler(runtime => runtime.RegisterNodes(RegisterNodes));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IApplicationRevisionHost>();
        var access = provider.GetRequiredService<IApplicationRuntimeAccess>();
        (await host.StartApplicationAsync()).Succeeded.ShouldBeTrue();
        var stringPorts = access.GetRequiredPorts();
        var input = ApplicationAddress.WorkflowPort("Orders", "Value", "Input");
        var output = ApplicationAddress.WorkflowPort("Orders", "Value", "Output");

        var stringReceive = stringPorts.ReceiveAsync<string>(output, TimeSpan.FromSeconds(5));
        (await stringPorts.SendAsync(input, FlowMessage.Create("one")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await stringReceive).Message!.Payload.ShouldBe("one");

        var revised = await host.ApplyAsync(
            "integer",
            IdentityDefinition("test.identity-integer"));

        revised.Status.ShouldBe(ApplicationRevisionUpdateStatus.Activated);
        var integerPorts = access.GetRequiredPorts();
        integerPorts.ShouldNotBeSameAs(stringPorts);
        await stringPorts.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var integerReceive = integerPorts.ReceiveAsync<int>(output, TimeSpan.FromSeconds(5));
        (await integerPorts.SendAsync(input, FlowMessage.Create(42)))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await integerReceive).Message!.Payload.ShouldBe(42);

        await host.StopApplicationAsync();
    }

    [Fact]
    public async Task Legacy_component_type_is_normalized_and_executes_through_the_runtime_assembler()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowApplication(IdentityDefinition("test.identity-legacy"))
            .UseRuntimeAssembler(runtime => runtime.RegisterNodes(registry =>
            {
                RegisterNodes(registry);
                registry.RegisterAlias("test.identity-legacy", "test.identity-string");
            }));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IApplicationRevisionHost>();

        var started = await host.StartApplicationAsync();

        started.Succeeded.ShouldBeTrue();
        started.Update!.NormalizationDiagnostics.ShouldHaveSingleItem()
            .CanonicalType.ShouldBe("test.identity-string");
        host.CurrentDefinition!.Workflows["Orders"].Components["Value"].Type
            .ShouldBe("test.identity-string");
        var ports = provider.GetRequiredService<IApplicationRuntimeAccess>().GetRequiredPorts();
        var input = ApplicationAddress.WorkflowPort("Orders", "Value", "Input");
        var output = ApplicationAddress.WorkflowPort("Orders", "Value", "Output");
        var receive = ports.ReceiveAsync<string>(output, TimeSpan.FromSeconds(5));
        (await ports.SendAsync(input, FlowMessage.Create("canonical")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await receive).Message!.Payload.ShouldBe("canonical");

        await host.StopApplicationAsync();
    }

    [Fact]
    public async Task Nested_processing_profile_resources_are_mapped_into_component_options()
    {
        ProcessingOptions? captured = null;
        var services = new ServiceCollection();
        services.AddFluxFlowApplication(ProcessingDefinition())
            .UseRuntimeAssembler(runtime => runtime.RegisterNodes(registry =>
                registry.Register(
                    "test.processing",
                    context =>
                    {
                        captured = context.BindConfiguration<ProcessingOptions>();
                        var node = new IdentityNode<string>();
                        return ValueTask.FromResult(ComposedNode.Create(
                            node,
                            inputs: [CompositionPorts.Input<string>("Input", node.Input)],
                            outputs: [CompositionPorts.Output<string>("Output", node.Output)]));
                    },
                    inputs: [CompositionPorts.Metadata<string>("Input")],
                    outputs: [CompositionPorts.Metadata<string>("Output")],
                    processingCapabilities:
                        CompositionProcessingCapabilities.ParallelRelaxedOrder)));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IApplicationRevisionHost>();

        var started = await host.StartApplicationAsync();

        started.Succeeded.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured.BoundedCapacity.ShouldBe(512);
        captured.MaxDegreeOfParallelism.ShouldBe(4);
        captured.EnsureOrdered.ShouldBeFalse();
        await host.StopApplicationAsync();
    }

    [Fact]
    public async Task Disposed_assembler_rejects_prepared_generation_adoption_and_cleans_the_candidate()
    {
        var source = new BlockingStartSource();
        var services = new ServiceCollection();
        services.AddFluxFlowApplication(BlockingSourceDefinition())
            .UseRuntimeAssembler(runtime => runtime.RegisterNodes(registry =>
                registry.Register(
                    "test.blocking-source",
                    _ => ValueTask.FromResult(ComposedNode.Create(
                        source,
                        outputs: [CompositionPorts.Output<string>("Output", source.Output)])),
                    outputs: [CompositionPorts.Metadata<string>("Output")])));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IApplicationRevisionHost>();
        var assembler = provider.GetRequiredService<ApplicationRuntimeAssembler>();
        var access = provider.GetRequiredService<IApplicationRuntimeAccess>();
        var start = host.StartApplicationAsync().AsTask();
        await source.StartEntered.WaitAsync(TimeSpan.FromSeconds(5));

        await assembler.DisposeAsync();
        source.ReleaseStart();
        var result = await start;

        result.Succeeded.ShouldBeFalse();
        result.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        result.Update.Failures.ShouldContain(static failure =>
            failure.Stage == ApplicationRevisionFailureStage.Activation);
        access.Ports.ShouldBeNull();
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

    [Fact]
    public async Task Later_factory_failure_disposes_components_created_earlier_in_preparation()
    {
        var tracker = new DescriptorTracker();
        var factoryCalls = 0;
        var services = new ServiceCollection();
        services.AddFluxFlowApplication(ApplicationDefinitionJson.Deserialize(
                """
                {
                  "Resources": {},
                  "Workflows": {
                    "Orders": {
                      "First": { "Type": "test.partial" },
                      "Second": { "Type": "test.partial" }
                    }
                  }
                }
                """))
            .UseRuntimeAssembler(runtime => runtime.RegisterNodes(registry =>
                registry.Register(
                    "test.partial",
                    _ =>
                    {
                        if (Interlocked.Increment(ref factoryCalls) == 2)
                            throw new InvalidOperationException("Factory failed.");

                        return ValueTask.FromResult(ComposedNode.Create(new TrackedNode(tracker)));
                    })));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IApplicationRevisionHost>();

        var result = await host.StartApplicationAsync();

        result.Succeeded.ShouldBeFalse();
        result.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        factoryCalls.ShouldBe(2);
        tracker.Disposed.ShouldBe(1);
        provider.GetRequiredService<IApplicationRuntimeAccess>().Ports.ShouldBeNull();
    }

    [Fact]
    public async Task Stop_drains_final_component_output_before_retiring_the_revision()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowApplication(IdentityDefinition("test.final-output"))
            .UseRuntimeAssembler(runtime => runtime.RegisterNodes(RegisterNodes));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IApplicationRevisionHost>();
        (await host.StartApplicationAsync()).Succeeded.ShouldBeTrue();
        var ports = provider.GetRequiredService<IApplicationRuntimeAccess>().GetRequiredPorts();
        var input = ApplicationAddress.WorkflowPort("Orders", "Value", "Input");
        var output = ApplicationAddress.WorkflowPort("Orders", "Value", "Output");

        (await ports.SendAsync(input, FlowMessage.Create("held")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        var finalReceive = ports.ReceiveAsync<string>(output, TimeSpan.FromSeconds(5));

        await host.StopApplicationAsync();

        var final = await finalReceive;
        final.Status.ShouldBe(PortReceiveStatus.Received);
        final.Message.ShouldNotBeNull().Payload.ShouldBe("final:held");
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
                inputs: [CompositionPorts.Metadata<ApplicationSystemEvent>("Input")])
            .Register(
                "test.identity-string",
                static _ =>
                {
                    var node = new IdentityNode<string>();
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
                "test.identity-integer",
                static _ =>
                {
                    var node = new IdentityNode<int>();
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        inputs: [CompositionPorts.Input<int>("Input", node.Input)],
                        outputs: [CompositionPorts.Output<int>("Output", node.Output)],
                        events: node.Events,
                        errors: node.Errors));
                },
                inputs: [CompositionPorts.Metadata<int>("Input")],
                outputs: [CompositionPorts.Metadata<int>("Output")])
            .Register(
                "test.final-output",
                static _ =>
                {
                    var node = new FinalOutputNode();
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        inputs: [CompositionPorts.Input<string>("Input", node.Input)],
                        outputs: [CompositionPorts.Output<string>("Output", node.Output)]));
                },
                inputs: [CompositionPorts.Metadata<string>("Input")],
                outputs: [CompositionPorts.Metadata<string>("Output")]);

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

    private static ApplicationDefinition InvalidExpandedDefinition()
        => ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {
                "Prefix": {
                  "Type": "test.prefix-resource",
                  "Value": "invalid:"
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
                  "Third": {
                    "Type": "test.unknown"
                  }
                }
              }
            }
            """);

    private static ApplicationDefinition ProcessingDefinition()
        => ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {
                "Processing": {
                  "Fast": {
                    "Type": "processing.profile",
                    "Mode": "Parallel",
                    "Order": "Relaxed",
                    "Buffer": "Large"
                  }
                }
              },
              "Workflows": {
                "Orders": {
                  "Worker": {
                    "Type": "test.processing",
                    "Processing": "Resources.Processing.Fast"
                  }
                }
              }
            }
            """);

    private static ApplicationDefinition IdentityDefinition(string type)
        => ApplicationDefinitionJson.Deserialize(
            $$"""
            {
              "Resources": {},
              "Workflows": {
                "Orders": {
                  "Value": {
                    "Type": "{{type}}"
                  }
                }
              }
            }
            """);

    private static ApplicationDefinition BlockingSourceDefinition()
        => ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {},
              "Workflows": {
                "Orders": {
                  "Wait": {
                    "Type": "test.blocking-source"
                  }
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
            EmitEvent(new FlowEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = message.CorrelationId,
                Name = "prefix.processed"
            });
            return Task.CompletedTask;
        }
    }

    private sealed class IdentityNode<T> : FlowNode<T, T>
    {
        protected override Task ProcessAsync(FlowMessage<T> message)
        {
            Emit(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FinalOutputNode : FlowNode<string, string>
    {
        private FlowMessage<string>? _last;

        protected override Task ProcessAsync(FlowMessage<string> message)
        {
            _last = message;
            return Task.CompletedTask;
        }

        protected override ValueTask OnInputCompletedAsync()
        {
            if (_last is not null)
                Emit(_last.With($"final:{_last.Payload}"));
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ProcessingOptions
    {
        public int BoundedCapacity { get; init; }

        public int MaxDegreeOfParallelism { get; init; }

        public bool EnsureOrdered { get; init; }
    }

    private sealed class BlockingStartSource : IFlowSource
    {
        private readonly BroadcastBlock<FlowMessage<string>> _output = new(static message => message);
        private readonly TaskCompletionSource _startEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseStart =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ISourceBlock<FlowMessage<string>> Output => _output;

        public Task StartEntered => _startEntered.Task;

        public Task Completion => _output.Completion;

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _startEntered.TrySetResult();
            await _releaseStart.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseStart() => _releaseStart.TrySetResult();

        public void Complete() => _output.Complete();

        public void Fault(Exception exception) => ((IDataflowBlock)_output).Fault(exception);

        public async ValueTask DisposeAsync()
        {
            Complete();
            await Completion;
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
