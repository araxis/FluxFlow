using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine.Internal.Revisions;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using ApplicationResourceRegistrationContext = FluxFlow.Composition.ApplicationResourceRegistrationContext;

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
        services.AddFluxFlow(Definition("one:"))
            .AddTestRuntimeAssembler(AddTestComponents, RegisterResources);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var access = provider.GetRequiredService<ApplicationRuntimeAssembler>();
        var resources = provider.GetRequiredService<ResourceTracker>();
        var revisionEvents = provider.GetRequiredService<RevisionEventTracker>();
        access.Ports.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => access.GetRequiredPorts());

        var started = await host.StartAsync();

        started.IsApplied.ShouldBeTrue();
        started.ActiveRevision.ShouldBe(host.Current);
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
        var eventReceive = ports.ReceiveAsync<ComponentEvent>(
            FirstEvents,
            TimeSpan.FromSeconds(5));
        (await ports.SendAsync(Input, FlowMessage.Create("value")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        var first = await firstReceive;
        first.Status.ShouldBe(PortReceiveStatus.Received);
        first.Message!.Value.ShouldBe("one:one:value");
        var componentEvent = await eventReceive;
        componentEvent.Status.ShouldBe(PortReceiveStatus.Received);
        componentEvent.Message!.Value.ComponentAddress.ShouldBe("Orders.First");
        componentEvent.Message.Value.Name.ShouldBe("prefix.processed");
        componentEvent.Message.TraceId.IsEmpty.ShouldBeFalse();

        var revised = await host.ApplyAsync("revision-2", Definition("two:"));

        revised.IsApplied.ShouldBeTrue();
        resources.Disposed.ShouldBe(1);
        access.GetRequiredPorts().ShouldBeSameAs(ports);
        ports.CurrentRevision!.RevisionId.ShouldBe("revision-2");
        var secondReceive = ports.ReceiveAsync<string>(Output, TimeSpan.FromSeconds(5));
        (await ports.SendAsync(Input, FlowMessage.Create("next")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        var second = await secondReceive;
        second.Status.ShouldBe(PortReceiveStatus.Received);
        second.Message!.Value.ShouldBe("two:two:next");

        await host.StopAsync();
        resources.Disposed.ShouldBe(2);
    }

    [Fact]
    public async Task Revision_that_changes_the_direct_port_surface_replaces_the_current_generation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ResourceTracker>();
        services.AddSingleton<RevisionEventTracker>();
        services.AddFluxFlow(Definition("one:"))
            .AddTestRuntimeAssembler(AddTestComponents, RegisterResources);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var access = provider.GetRequiredService<ApplicationRuntimeAssembler>();
        var resources = provider.GetRequiredService<ResourceTracker>();
        (await host.StartAsync()).IsApplied.ShouldBeTrue();
        var ports = access.GetRequiredPorts();

        var expanded = await host.ApplyAsync("expanded", Definition("two:", includeThird: true));

        expanded.Status.ShouldBe(ApplicationUpdateStatus.Applied);
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
        received.Message!.Value.ShouldBe("two:two:active");

        var thirdReceive = expandedPorts.ReceiveAsync<string>(ThirdOutput, TimeSpan.FromSeconds(5));
        (await expandedPorts.SendAsync(ThirdInput, FlowMessage.Create("direct")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await thirdReceive).Message!.Value.ShouldBe("two:direct");

        var contracted = await host.ApplyAsync("contracted", Definition("three:"));

        contracted.Status.ShouldBe(ApplicationUpdateStatus.Applied);
        var contractedPorts = access.GetRequiredPorts();
        contractedPorts.ShouldNotBeSameAs(expandedPorts);
        await expandedPorts.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Should.Throw<KeyNotFoundException>(() =>
            contractedPorts.SendAsync(ThirdInput, FlowMessage.Create("removed")));
        var contractedReceive = contractedPorts.ReceiveAsync<string>(Output, TimeSpan.FromSeconds(5));
        (await contractedPorts.SendAsync(Input, FlowMessage.Create("current")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await contractedReceive).Message!.Value.ShouldBe("three:three:current");

        await host.StopAsync();
        resources.Disposed.ShouldBe(3);
    }

    [Fact]
    public async Task Rejected_surface_change_leaves_the_current_generation_active()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ResourceTracker>();
        services.AddSingleton<RevisionEventTracker>();
        services.AddFluxFlow(Definition("one:"))
            .AddTestRuntimeAssembler(AddTestComponents, RegisterResources);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var access = provider.GetRequiredService<ApplicationRuntimeAssembler>();
        (await host.StartAsync()).IsApplied.ShouldBeTrue();
        var ports = access.GetRequiredPorts();

        var rejected = await host.ApplyAsync("invalid", InvalidExpandedDefinition());

        rejected.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        rejected.Diagnostics.ShouldHaveSingleItem().Stage
            .ShouldBe(ApplicationUpdateStage.ComponentPreparation);
        host.Current!.RevisionId.ShouldBe("initial");
        access.GetRequiredPorts().ShouldBeSameAs(ports);
        ports.CurrentRevision!.RevisionId.ShouldBe("initial");
        var receive = ports.ReceiveAsync<string>(Output, TimeSpan.FromSeconds(5));
        (await ports.SendAsync(Input, FlowMessage.Create("still-active")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await receive).Message!.Value.ShouldBe("one:one:still-active");

        await host.StopAsync();
    }

    [Fact]
    public async Task Revision_that_changes_a_port_payload_type_replaces_the_generation()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(IdentityDefinition("test.identity-string"))
            .AddTestRuntimeAssembler(AddTestComponents);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var access = provider.GetRequiredService<ApplicationRuntimeAssembler>();
        (await host.StartAsync()).IsApplied.ShouldBeTrue();
        var stringPorts = access.GetRequiredPorts();
        var input = ApplicationAddress.WorkflowPort("Orders", "Value", "Input");
        var output = ApplicationAddress.WorkflowPort("Orders", "Value", "Output");

        var stringReceive = stringPorts.ReceiveAsync<string>(output, TimeSpan.FromSeconds(5));
        (await stringPorts.SendAsync(input, FlowMessage.Create("one")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await stringReceive).Message!.Value.ShouldBe("one");

        var revised = await host.ApplyAsync(
            "integer",
            IdentityDefinition("test.identity-integer"));

        revised.Status.ShouldBe(ApplicationUpdateStatus.Applied);
        var integerPorts = access.GetRequiredPorts();
        integerPorts.ShouldNotBeSameAs(stringPorts);
        await stringPorts.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var integerReceive = integerPorts.ReceiveAsync<int>(output, TimeSpan.FromSeconds(5));
        (await integerPorts.SendAsync(input, FlowMessage.Create(42)))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await integerReceive).Message!.Value.ShouldBe(42);

        await host.StopAsync();
    }

    [Fact]
    public async Task Obsolete_component_type_is_rejected_by_the_runtime_assembler()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(IdentityDefinition("test.identity-legacy"))
            .AddTestRuntimeAssembler(AddTestComponents);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();

        var started = await host.StartAsync();

        started.IsRejected.ShouldBeTrue();
        var diagnostic = started.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Stage.ShouldBe(ApplicationUpdateStage.ComponentPreparation);
        diagnostic.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!
            .ShouldContain("unknown type 'test.identity-legacy'");
        host.CurrentDefinition.ShouldBeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task Nested_processing_profile_resources_are_mapped_into_component_options()
    {
        ProcessingOptions? captured = null;
        var services = new ServiceCollection();
        services.AddFluxFlow(ProcessingDefinition())
            .AddTestRuntimeAssembler(services =>
                services.AddFluxFlowComponent(new ComponentDescriptor(
                    "test.processing",
                    context =>
                    {
                        captured = context.BindConfiguration<ProcessingOptions>();
                        var node = new IdentityNode<string>();
                        return ValueTask.FromResult(ComponentInstance.Create(
                            node,
                            inputs: [ComponentPorts.Input<string>("Input", node.Input)],
                            outputs: [ComponentPorts.Output<string>("Output", node.Output)]));
                    },
                    inputs: [ComponentPorts.Metadata<string>("Input")],
                    outputs: [ComponentPorts.Metadata<string>("Output")],
                    processingCapabilities:
                        CompositionProcessingCapabilities.ParallelRelaxedOrder)));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();

        var started = await host.StartAsync();

        started.IsApplied.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured.BoundedCapacity.ShouldBe(512);
        captured.MaxDegreeOfParallelism.ShouldBe(4);
        captured.EnsureOrdered.ShouldBeFalse();
        await host.StopAsync();
    }

    [Fact]
    public async Task Disposed_assembler_rejects_prepared_generation_adoption_and_cleans_the_candidate()
    {
        var source = new BlockingStartSource();
        var services = new ServiceCollection();
        services.AddFluxFlow(BlockingSourceDefinition())
            .AddTestRuntimeAssembler(services =>
                services.AddFluxFlowComponent(new ComponentDescriptor(
                    "test.blocking-source",
                    _ => ValueTask.FromResult(ComponentInstance.Create(
                        source,
                        outputs: [ComponentPorts.Output<string>("Output", source.Output)])),
                    outputs: [ComponentPorts.Metadata<string>("Output")])));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var assembler = provider.GetRequiredService<ApplicationRuntimeAssembler>();
        var access = provider.GetRequiredService<ApplicationRuntimeAssembler>();
        var start = host.StartAsync().AsTask();
        await source.StartEntered.WaitAsync(TimeSpan.FromSeconds(5));

        await assembler.DisposeAsync();
        source.ReleaseStart();
        var result = await start;

        result.IsApplied.ShouldBeFalse();
        result.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        result.Diagnostics.ShouldContain(static failure =>
            failure.Stage == ApplicationUpdateStage.Activation);
        access.Ports.ShouldBeNull();
    }

    [Fact]
    public async Task Descriptor_that_does_not_match_registration_is_disposed_on_rejection()
    {
        var tracker = new DescriptorTracker();
        var services = new ServiceCollection();
        services.AddFluxFlow(ApplicationDefinitionJson.Deserialize(
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
            .AddTestRuntimeAssembler(services =>
                services.AddFluxFlowComponent(new ComponentDescriptor(
                    "test.invalid",
                    _ =>
                    {
                        var node = new TrackedNode(tracker);
                        return ValueTask.FromResult(ComponentInstance.Create(node));
                    },
                    inputs: [ComponentPorts.Metadata<string>("Input")])));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();

        var result = await host.StartAsync();

        result.IsApplied.ShouldBeFalse();
        result.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        tracker.Disposed.ShouldBe(1);
        provider.GetRequiredService<ApplicationRuntimeAssembler>().Ports.ShouldBeNull();
    }

    [Fact]
    public async Task Later_factory_failure_disposes_components_created_earlier_in_preparation()
    {
        var tracker = new DescriptorTracker();
        var factoryCalls = 0;
        var services = new ServiceCollection();
        services.AddFluxFlow(ApplicationDefinitionJson.Deserialize(
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
            .AddTestRuntimeAssembler(services =>
                services.AddFluxFlowComponent(new ComponentDescriptor(
                    "test.partial",
                    _ =>
                    {
                        if (Interlocked.Increment(ref factoryCalls) == 2)
                            throw new InvalidOperationException("Factory failed.");

                        return ValueTask.FromResult(ComponentInstance.Create(new TrackedNode(tracker)));
                    })));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();

        var result = await host.StartAsync();

        result.IsApplied.ShouldBeFalse();
        result.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        factoryCalls.ShouldBe(2);
        tracker.Disposed.ShouldBe(1);
        provider.GetRequiredService<ApplicationRuntimeAssembler>().Ports.ShouldBeNull();
    }

    [Fact]
    public async Task Stop_drains_final_component_output_before_retiring_the_revision()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(IdentityDefinition("test.final-output"))
            .AddTestRuntimeAssembler(AddTestComponents);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        (await host.StartAsync()).IsApplied.ShouldBeTrue();
        var ports = provider.GetRequiredService<ApplicationRuntimeAssembler>().GetRequiredPorts();
        var input = ApplicationAddress.WorkflowPort("Orders", "Value", "Input");
        var output = ApplicationAddress.WorkflowPort("Orders", "Value", "Output");

        (await ports.SendAsync(input, FlowMessage.Create("held")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        var finalReceive = ports.ReceiveAsync<string>(output, TimeSpan.FromSeconds(5));

        await host.StopAsync();

        var final = await finalReceive;
        final.Status.ShouldBe(PortReceiveStatus.Received);
        final.Message.ShouldNotBeNull().Value.ShouldBe("final:held");
    }

    [Fact]
    public async Task Eager_source_output_is_linked_before_source_start()
    {
        var tracker = new SourceOutputTracker();
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddFluxFlow(EagerSourceDefinition())
            .AddTestRuntimeAssembler(
                services => services
                    .AddFluxFlowComponent(new ComponentDescriptor(
                        "test.eager-source",
                        static _ =>
                        {
                            var source = new EagerSource();
                            return ValueTask.FromResult(ComponentInstance.Create(
                                source,
                                outputs:
                                [
                                    ComponentPorts.Output<string>("Output", source.Output)
                                ]));
                        },
                        outputs: [ComponentPorts.Metadata<string>("Output")]))
                    .AddFluxFlowComponent(new ComponentDescriptor(
                        "test.source-recorder",
                        static context =>
                        {
                            var node = new SourceRecordingNode(
                                context.Services.GetRequiredService<SourceOutputTracker>());
                            return ValueTask.FromResult(ComponentInstance.Create(
                                node,
                                inputs:
                                [
                                    ComponentPorts.Input<string>("Input", node.Input)
                                ]));
                        },
                        inputs: [ComponentPorts.Metadata<string>("Input")])),
                context => context.Services.AddSingleton(
                    context.HostServices.GetRequiredService<SourceOutputTracker>()));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();

        var started = await host.StartAsync();

        started.IsApplied.ShouldBeTrue();
        await EventuallyAsync(() => tracker.Values.Count == 1);
        tracker.Values.ShouldBe(["started"]);
        await host.StopAsync();
    }

    [Fact]
    public async Task Fan_in_target_remains_active_until_the_revision_stops()
    {
        var sources = new ManualSourceCatalog();
        var tracker = new SourceOutputTracker();
        var services = new ServiceCollection();
        services.AddSingleton(sources);
        services.AddSingleton(tracker);
        services.AddFluxFlow(FanInDefinition())
            .AddTestRuntimeAssembler(
                AddFanInTestComponents,
                context =>
                {
                    context.Services.AddSingleton(
                        context.HostServices.GetRequiredService<ManualSourceCatalog>());
                    context.Services.AddSingleton(
                        context.HostServices.GetRequiredService<SourceOutputTracker>());
                });
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        (await host.StartAsync()).IsApplied.ShouldBeTrue();

        sources["First"].Emit("one").ShouldBeTrue();
        sources["First"].Complete();
        await sources["First"].Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await EventuallyAsync(() => tracker.Values.Count == 1);

        sources["Second"].Emit("two").ShouldBeTrue();
        sources["Second"].Complete();
        await sources["Second"].Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await EventuallyAsync(() => tracker.Values.Count == 2);

        tracker.Values.ShouldBe(["one", "two"]);
        await host.StopAsync();
    }

    [Fact]
    public async Task Fan_in_source_fault_is_reported_once_during_revision_drain()
    {
        var sources = new ManualSourceCatalog();
        var tracker = new SourceOutputTracker();
        var services = new ServiceCollection();
        services.AddSingleton(sources);
        services.AddSingleton(tracker);
        services.AddFluxFlow(FanInDefinition())
            .AddTestRuntimeAssembler(
                AddFanInTestComponents,
                context =>
                {
                    context.Services.AddSingleton(
                        context.HostServices.GetRequiredService<ManualSourceCatalog>());
                    context.Services.AddSingleton(
                        context.HostServices.GetRequiredService<SourceOutputTracker>());
                });
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        (await host.StartAsync()).IsApplied.ShouldBeTrue();

        sources["First"].Fault(new InvalidOperationException("source failed"));
        sources["Second"].Complete();

        var exception = await Should.ThrowAsync<AggregateException>(async () =>
            await host.StopAsync());

        var sourceFailures = exception.Flatten().InnerExceptions
            .Where(failure => failure.Message == "source failed")
            .ToArray();
        sourceFailures.Length.ShouldBe(1);
    }

    private static void AddFanInTestComponents(IServiceCollection services)
        => services
            .AddFluxFlowComponent(new ComponentDescriptor(
                "test.manual-source",
                static context =>
                {
                    var source = new ManualSource();
                    context.Services.GetRequiredService<ManualSourceCatalog>()
                        .Add(context.ComponentName, source);
                    return ValueTask.FromResult(ComponentInstance.Create(
                        source,
                        outputs: [ComponentPorts.Output<string>("Output", source.Output)]));
                },
                outputs: [ComponentPorts.Metadata<string>("Output")]))
            .AddFluxFlowComponent(new ComponentDescriptor(
                "test.source-recorder",
                static context =>
                {
                    var node = new SourceRecordingNode(
                        context.Services.GetRequiredService<SourceOutputTracker>());
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        inputs: [ComponentPorts.Input<string>("Input", node.Input)]));
                },
                inputs: [ComponentPorts.Metadata<string>("Input")]));

    private static void AddTestComponents(IServiceCollection services)
        => services
            .AddFluxFlowComponent(new ComponentDescriptor(
                "test.prefix",
                static context =>
                {
                    var resource = context.GetRequiredResource<PrefixResource>("Prefix");
                    var node = new PrefixNode(resource.Value);
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        inputs: [ComponentPorts.Input<string>("Input", node.Input)],
                        outputs: [ComponentPorts.Output<string>("Output", node.Output)],
                        events: node.Events));
                },
                inputs: [ComponentPorts.Metadata<string>("Input")],
                outputs: [ComponentPorts.Metadata<string>("Output")]))
            .AddFluxFlowComponent(new ComponentDescriptor(
                "test.revision-events",
                static context =>
                {
                    var tracker = context.Services.GetRequiredService<RevisionEventTracker>();
                    var node = new RevisionEventNode(tracker);
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        inputs: [ComponentPorts.Input<ApplicationSystemEvent>("Input", node.Input)],
                        events: node.Events));
                },
                inputs: [ComponentPorts.Metadata<ApplicationSystemEvent>("Input")]))
            .AddFluxFlowComponent(new ComponentDescriptor(
                "test.identity-string",
                static _ =>
                {
                    var node = new IdentityNode<string>();
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        inputs: [ComponentPorts.Input<string>("Input", node.Input)],
                        outputs: [ComponentPorts.Output<string>("Output", node.Output)],
                        events: node.Events));
                },
                inputs: [ComponentPorts.Metadata<string>("Input")],
                outputs: [ComponentPorts.Metadata<string>("Output")]))
            .AddFluxFlowComponent(new ComponentDescriptor(
                "test.identity-integer",
                static _ =>
                {
                    var node = new IdentityNode<int>();
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        inputs: [ComponentPorts.Input<int>("Input", node.Input)],
                        outputs: [ComponentPorts.Output<int>("Output", node.Output)],
                        events: node.Events));
                },
                inputs: [ComponentPorts.Metadata<int>("Input")],
                outputs: [ComponentPorts.Metadata<int>("Output")]))
            .AddFluxFlowComponent(new ComponentDescriptor(
                "test.final-output",
                static _ =>
                {
                    var node = new FinalOutputNode();
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        inputs: [ComponentPorts.Input<string>("Input", node.Input)],
                        outputs: [ComponentPorts.Output<string>("Output", node.Output)]));
                },
                inputs: [ComponentPorts.Metadata<string>("Input")],
                outputs: [ComponentPorts.Metadata<string>("Output")]));

    private static void RegisterResources(ApplicationResourceRegistrationContext context)
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

    private static ApplicationDefinition EagerSourceDefinition()
        => ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {},
              "Workflows": {
                "Orders": {
                  "Emit": {
                    "Type": "test.eager-source",
                    "Output": "Record.Input"
                  },
                  "Record": {
                    "Type": "test.source-recorder"
                  }
                }
              }
            }
            """);

    private static ApplicationDefinition FanInDefinition()
        => ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {},
              "Workflows": {
                "Orders": {
                  "First": {
                    "Type": "test.manual-source",
                    "Output": "Sink.Input"
                  },
                  "Second": {
                    "Type": "test.manual-source",
                    "Output": "Sink.Input"
                  },
                  "Sink": {
                    "Type": "test.source-recorder"
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
            Emit(message.With(prefix + message.Value));
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
                Emit(_last.With($"final:{_last.Value}"));
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

    private sealed class EagerSource : IFlowSource
    {
        private readonly BroadcastBlock<FlowMessage<string>> _output =
            new(static message => message);

        public ISourceBlock<FlowMessage<string>> Output => _output;

        public Task Completion => _output.Completion;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _output.Post(FlowMessage.Create("started"));
            _output.Complete();
            return Task.CompletedTask;
        }

        public void Complete() => _output.Complete();

        public void Fault(Exception exception) => ((IDataflowBlock)_output).Fault(exception);

        public async ValueTask DisposeAsync()
        {
            Complete();
            await Completion.ConfigureAwait(false);
        }
    }

    private sealed class ManualSource : IFlowSource
    {
        private readonly BufferBlock<FlowMessage<string>> _output = new();

        public ISourceBlock<FlowMessage<string>> Output => _output;

        public Task Completion => _output.Completion;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public bool Emit(string value) => _output.Post(FlowMessage.Create(value));

        public void Complete() => _output.Complete();

        public void Fault(Exception exception) => ((IDataflowBlock)_output).Fault(exception);

        public async ValueTask DisposeAsync()
        {
            _output.Complete();
            try
            {
                await _output.Completion.ConfigureAwait(false);
            }
            catch
            {
                // Revision drain is the observable component-fault path.
            }
        }
    }

    private sealed class ManualSourceCatalog
    {
        private readonly Dictionary<string, ManualSource> _sources = new(StringComparer.Ordinal);

        public ManualSource this[string componentName] => _sources[componentName];

        public void Add(string componentName, ManualSource source)
            => _sources.Add(componentName, source);
    }

    private sealed class SourceRecordingNode(SourceOutputTracker tracker) : FlowNode<string, string>
    {
        protected override Task ProcessAsync(FlowMessage<string> message)
        {
            tracker.Add(message.Value);
            return Task.CompletedTask;
        }
    }

    private sealed class SourceOutputTracker
    {
        private readonly object _gate = new();
        private readonly List<string> _values = [];

        public IReadOnlyList<string> Values
        {
            get
            {
                lock (_gate)
                    return _values.ToArray();
            }
        }

        public void Add(string value)
        {
            lock (_gate)
                _values.Add(value);
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
            tracker.Add(message.Value.Details!.Value.GetProperty("phase").GetString()!);
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
