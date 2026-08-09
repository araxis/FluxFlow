using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;
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
    private static readonly ApplicationAddress FirstDiagnostics =
        ApplicationAddress.WorkflowPort("Orders", "First", "Diagnostics");
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
            FirstDiagnostics,
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
    public async Task Code_first_definition_executes_embedded_contracts_without_host_registration()
    {
        var auditValues = new HashSet<string>(StringComparer.Ordinal) { "priority" };
        var definition = CodeFirstRoutingDefinition(auditValues);
        var signalTracker = new SignalTracker();
        var services = new ServiceCollection();
        services.AddSingleton(signalTracker);
        services.AddFluxFlow(definition)
            .AddTestRuntimeAssembler(static _ => { });
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var access = provider.GetRequiredService<ApplicationRuntimeAssembler>();

        provider.GetRequiredService<ComponentCatalog>().Descriptors.ShouldBeEmpty();
        definition.ComponentDescriptors.Select(static descriptor => descriptor.Type)
            .ShouldBe(["test.identity-string", "test.signal"]);

        var started = await host.StartAsync();

        started.IsApplied.ShouldBeTrue(string.Join(
            Environment.NewLine,
            started.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Stage}: {diagnostic.Error.Code}: {diagnostic.Error.Message}: {diagnostic.Error.Details}")));
        host.CurrentDefinition.ShouldBeSameAs(definition);
        var ports = access.GetRequiredPorts();
        var sourceInput = ApplicationAddress.WorkflowPort("Main", "Source", "Input");
        var priorityOutput = ApplicationAddress.WorkflowPort("Main", "Priority", "Output");
        var standardOutput = ApplicationAddress.WorkflowPort("Main", "Standard", "Output");
        var auditOutput = ApplicationAddress.WorkflowPort("Audit", "Recorder", "Output");
        var priorityReceive = ports.ReceiveAsync<string>(priorityOutput, TimeSpan.FromSeconds(5));
        var auditReceive = ports.ReceiveAsync<string>(auditOutput, TimeSpan.FromSeconds(5));

        (await ports.SendAsync(sourceInput, FlowMessage.Create("priority")))
            .Status.ShouldBe(PortSendStatus.Accepted);

        (await priorityReceive).Message!.Value.ShouldBe("priority");
        (await auditReceive).Message!.Value.ShouldBe("priority");
        (await signalTracker.NextAsync()).ShouldBe("priority");

        var standardReceive = ports.ReceiveAsync<string>(standardOutput, TimeSpan.FromSeconds(5));
        (await ports.SendAsync(sourceInput, FlowMessage.Create("standard")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await standardReceive).Message!.Value.ShouldBe("standard");
        host.State.ShouldBe(ApplicationState.Running);

        await host.StopAsync();
    }

    [Fact]
    public async Task Effective_catalog_deduplicates_same_descriptor_and_runs_mixed_embedded_and_host_registered_components()
    {
        var embeddedActivations = 0;
        var hostActivations = 0;
        var embedded = CreateStringIdentityContract(
            "test.embedded",
            _ =>
            {
                embeddedActivations++;
                return new IdentityNode<string>();
            });
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var first = workflow.AddComponent("First", embedded);
        var second = workflow.AddComponent("Second", "test.host");
        first.Output.ConnectTo(second.Input<string>("Input"));
        var definition = application.Build();
        var services = new ServiceCollection();
        services.AddFluxFlow(definition)
            .AddComponent(embedded)
            .Advanced.AddDynamicComponent("test.host", component => component
                .UseFactory(_ =>
                {
                    hostActivations++;
                    return new IdentityNode<string>();
                })
                .HasInput("Input", static node => node.Input)
                .HasOutput("Output", static node => node.Output)
                .HasEvents("Events", static node => node.Events));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var access = provider.GetRequiredService<ApplicationRuntimeAssembler>();

        var started = await host.StartAsync();

        started.IsApplied.ShouldBeTrue(string.Join(
            Environment.NewLine,
            started.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Stage}: {diagnostic.Error.Code}: {diagnostic.Error.Message}: {diagnostic.Error.Details}")));
        embeddedActivations.ShouldBe(1);
        hostActivations.ShouldBe(1);
        provider.GetRequiredService<ComponentCatalog>().Descriptors
            .Single(descriptor => descriptor.Type == "test.embedded")
            .ShouldBeSameAs(embedded.Descriptor);
        var ports = access.GetRequiredPorts();
        var input = ApplicationAddress.WorkflowPort("Main", "First", "Input");
        var output = ApplicationAddress.WorkflowPort("Main", "Second", "Output");
        var receive = ports.ReceiveAsync<string>(output, TimeSpan.FromSeconds(5));
        (await ports.SendAsync(input, FlowMessage.Create("mixed")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await receive).Message!.Value.ShouldBe("mixed");

        await host.StopAsync();
    }

    [Fact]
    public async Task Effective_catalog_rejects_distinct_same_type_descriptors_before_activation_and_preserves_active_revision()
    {
        var activeActivations = 0;
        var rejectedActivations = 0;
        var activeContract = CreateStringIdentityContract(
            "test.identity",
            _ =>
            {
                activeActivations++;
                return new IdentityNode<string>();
            });
        var initialBuilder = new ApplicationDefinitionBuilder();
        initialBuilder.AddWorkflow("Main").AddComponent("Value", "test.identity");
        var initial = initialBuilder.Build();
        var rejectedContract = CreateStringIdentityContract(
            "test.identity",
            _ =>
            {
                rejectedActivations++;
                return new IdentityNode<string>();
            });
        var rejectedBuilder = new ApplicationDefinitionBuilder();
        rejectedBuilder.AddWorkflow("Main").AddComponent("Value", rejectedContract);
        var rejectedDefinition = rejectedBuilder.Build();
        var services = new ServiceCollection();
        services.AddFluxFlow(initial).AddComponent(activeContract);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var access = provider.GetRequiredService<ApplicationRuntimeAssembler>();
        (await host.StartAsync()).IsApplied.ShouldBeTrue();
        activeActivations.ShouldBe(1);
        var activePorts = access.GetRequiredPorts();
        var input = ApplicationAddress.WorkflowPort("Main", "Value", "Input");
        var output = ApplicationAddress.WorkflowPort("Main", "Value", "Output");

        var rejected = await host.ApplyAsync("conflicting-contract", rejectedDefinition);

        rejected.IsRejected.ShouldBeTrue();
        rejectedActivations.ShouldBe(0);
        var conflictMessage = rejected.Diagnostics.ShouldHaveSingleItem()
            .Error.Details!.Value.GetProperty("exceptionMessage").GetString()!;
        conflictMessage.ShouldContain("test.identity");
        conflictMessage.ShouldContain("conflicting descriptor registrations");
        host.CurrentDefinition.ShouldBeSameAs(initial);
        access.GetRequiredPorts().ShouldBeSameAs(activePorts);
        var receive = activePorts.ReceiveAsync<string>(output, TimeSpan.FromSeconds(5));
        (await activePorts.SendAsync(input, FlowMessage.Create("still-active")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await receive).Message!.Value.ShouldBe("still-active");
        activeActivations.ShouldBe(1);

        await host.StopAsync();
    }

    [Fact]
    public async Task Hot_reload_introduces_removes_and_replaces_embedded_contracts_without_rebuilding_services()
    {
        var alphaActivations = 0;
        var betaActivations = 0;
        var replacementActivations = 0;
        var alpha = CreateStringIdentityContract("test.alpha", _ =>
        {
            alphaActivations++;
            return new IdentityNode<string>();
        });
        var beta = CreateStringIdentityContract("test.beta", _ =>
        {
            betaActivations++;
            return new IdentityNode<string>();
        });
        var replacementBeta = CreateStringIdentityContract("test.beta", _ =>
        {
            replacementActivations++;
            return new IdentityNode<string>();
        });
        var initial = IdentityContractsDefinition(("Alpha", alpha));
        var expanded = IdentityContractsDefinition(("Alpha", alpha), ("Beta", beta));
        var removed = IdentityContractsDefinition(("Beta", beta));
        var replaced = IdentityContractsDefinition(("Beta", replacementBeta));
        var services = new ServiceCollection();
        services.AddFluxFlow(initial);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var access = provider.GetRequiredService<ApplicationRuntimeAssembler>();

        (await host.StartAsync()).IsApplied.ShouldBeTrue();
        alphaActivations.ShouldBe(1);
        (await host.ApplyAsync("expanded", expanded)).IsApplied.ShouldBeTrue();
        host.CurrentDefinition.ShouldBeSameAs(expanded);
        alphaActivations.ShouldBe(2);
        betaActivations.ShouldBe(1);
        (await host.ApplyAsync("removed", removed)).IsApplied.ShouldBeTrue();
        host.CurrentDefinition.ShouldBeSameAs(removed);
        betaActivations.ShouldBe(2);
        (await host.ApplyAsync("replaced", replaced)).IsApplied.ShouldBeTrue();
        host.CurrentDefinition.ShouldBeSameAs(replaced);
        replacementActivations.ShouldBe(1);
        betaActivations.ShouldBe(2);
        var ports = access.GetRequiredPorts();
        var input = ApplicationAddress.WorkflowPort("Main", "Beta", "Input");
        var output = ApplicationAddress.WorkflowPort("Main", "Beta", "Output");
        var receive = ports.ReceiveAsync<string>(output, TimeSpan.FromSeconds(5));
        (await ports.SendAsync(input, FlowMessage.Create("replacement")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await receive).Message!.Value.ShouldBe("replacement");

        await host.StopAsync();
    }

    [Fact]
    public async Task Failed_contract_replacement_retains_previous_factory_and_route()
    {
        var activeActivations = 0;
        var failedActivations = 0;
        var activeContract = CreateStringIdentityContract("test.identity", _ =>
        {
            activeActivations++;
            return new IdentityNode<string>();
        });
        var failingContract = CreateStringIdentityContract("test.identity", _ =>
        {
            failedActivations++;
            throw new InvalidOperationException("Replacement contract factory failed.");
        });
        var initial = IdentityContractsDefinition(("Value", activeContract));
        var replacement = IdentityContractsDefinition(("Value", failingContract));
        var services = new ServiceCollection();
        services.AddFluxFlow(initial);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var access = provider.GetRequiredService<ApplicationRuntimeAssembler>();
        (await host.StartAsync()).IsApplied.ShouldBeTrue();
        var activePorts = access.GetRequiredPorts();

        var failed = await host.ApplyAsync("failed-contract", replacement);

        failed.IsRejected.ShouldBeTrue();
        activeActivations.ShouldBe(1);
        failedActivations.ShouldBe(1);
        failed.Diagnostics.ShouldHaveSingleItem().Error.Details!.Value
            .GetProperty("exceptionMessage").GetString()!
            .ShouldContain("Replacement contract factory failed.");
        host.CurrentDefinition.ShouldBeSameAs(initial);
        access.GetRequiredPorts().ShouldBeSameAs(activePorts);
        var input = ApplicationAddress.WorkflowPort("Main", "Value", "Input");
        var output = ApplicationAddress.WorkflowPort("Main", "Value", "Output");
        var receive = activePorts.ReceiveAsync<string>(output, TimeSpan.FromSeconds(5));
        (await activePorts.SendAsync(input, FlowMessage.Create("retained")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        (await receive).Message!.Value.ShouldBe("retained");

        await host.StopAsync();
    }

    [Fact]
    public async Task Successful_contract_replacement_retires_captured_factory_closure()
    {
        await using var context = RunOnTerminatedThread(
            static () => CreateContractRetirementContext());

        ForceFullCollection();
        context.Closure.IsAlive.ShouldBeFalse();

        await context.Application.StopAsync();
    }

    [Fact]
    public async Task Successful_code_predicate_replacement_retires_captured_closure()
    {
        await using var context = RunOnTerminatedThread(
            static () => CreatePredicateRetirementContext());

        ForceFullCollection();
        context.Closure.IsAlive.ShouldBeFalse();

        await context.Application.StopAsync();
    }

    [Fact]
    public async Task Json_definition_still_requires_explicit_host_registration()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(IdentityDefinition("test.identity-legacy"))
            .AddTestRuntimeAssembler(AddTestComponents);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();

        var started = await host.StartAsync();

        host.CurrentDefinition.ShouldBeNull();
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
                services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.processing", component =>
                {
                    component.UseProcessing(CompositionProcessingCapabilities.ParallelRelaxedOrder);
                    component
                        .UseFactory(context =>
                        {
                            captured = context.BindConfiguration<ProcessingOptions>();
                            return new IdentityNode<string>();
                        })
                        .HasInput("Input", static node => node.Input)
                        .HasOutput("Output", static node => node.Output)
                        .HasEvents("Events", static node => node.Events);
                }));
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
                services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.blocking-source", component =>
                    component
                        .UseFactory(_ => source)
                        .HasOutput("Output", static node => node.Output)));
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

    [Theory]
    [InlineData(AdvancedPortMismatch.Missing)]
    [InlineData(AdvancedPortMismatch.Extra)]
    [InlineData(AdvancedPortMismatch.Renamed)]
    [InlineData(AdvancedPortMismatch.Mistyped)]
    [InlineData(AdvancedPortMismatch.WrongSignalKind)]
    public async Task Advanced_instance_factory_port_mismatch_is_rejected_and_disposed(
        AdvancedPortMismatch mismatch)
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
                services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.invalid", component =>
                {
                    var instance = component.UseInstanceFactory(_ => ValueTask.FromResult(
                        CreateMismatchedInstance(tracker, mismatch)));
                    switch (mismatch)
                    {
                        case AdvancedPortMismatch.Extra:
                            break;
                        case AdvancedPortMismatch.WrongSignalKind:
                            instance.HasSignalInput("Input");
                            break;
                        default:
                            instance.HasInput<string>("Input");
                            break;
                    }
                }));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();

        var result = await host.StartAsync();

        result.IsApplied.ShouldBeFalse();
        result.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        result.Diagnostics.ShouldContain(failure =>
            failure.Stage == ApplicationUpdateStage.ComponentPreparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                "Orders.Invalid",
                StringComparison.Ordinal));
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
                services.AddFluxFlowComponents().Advanced.AddDynamicComponent("test.partial", component =>
                    component.UseFactory(_ =>
                    {
                        if (Interlocked.Increment(ref factoryCalls) == 2)
                            throw new InvalidOperationException("Factory failed.");

                        return new TrackedNode(tracker);
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

        foreach (var value in new[] { "first", "second", "third" })
        {
            (await ports.SendAsync(input, FlowMessage.Create(value)))
                .Status.ShouldBe(PortSendStatus.Accepted);
        }

        var observed = await ports.ObserveAsync<string>(output, capacity: 4);
        observed.Status.ShouldBe(PortObserveStatus.Started);
        await using var observation = observed.Observation!;

        await host.StopAsync();

        var final = new[]
        {
            await observation.Messages.ReceiveAsync(TimeSpan.FromSeconds(5)),
            await observation.Messages.ReceiveAsync(TimeSpan.FromSeconds(5)),
            await observation.Messages.ReceiveAsync(TimeSpan.FromSeconds(5))
        };
        final.Select(static message => message.Value)
            .ShouldBe(["final:first", "final:second", "final:third"]);
    }

    [Fact]
    public async Task Eager_source_output_is_linked_before_source_start()
    {
        var tracker = new SourceOutputTracker();
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddFluxFlow(EagerSourceDefinition())
            .AddTestRuntimeAssembler(
                services => services.AddFluxFlowComponents().Advanced
                    .AddDynamicComponent("test.eager-source", component =>
                        component
                            .UseFactory(static _ => new EagerSource())
                            .HasOutput("Output", static source => source.Output))
                    .AddDynamicComponent("test.source-recorder", component =>
                        component
                            .UseFactory(static context => new SourceRecordingNode(
                                context.Services.GetRequiredService<SourceOutputTracker>()))
                            .HasInput("Input", static node => node.Input)),
                context => context.Services.AddSingleton(
                    context.HostServices.GetRequiredService<SourceOutputTracker>()));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();

        var started = await host.StartAsync();

        started.IsApplied.ShouldBeTrue();
        await EventuallyAsync(() => tracker.Values.Count == 3);
        tracker.Values.ShouldBe(["started:1", "started:2", "started:3"]);
        await host.StopAsync();
    }

    [Fact]
    public async Task Application_output_capacity_configures_stable_ports_without_overriding_component_capacity()
    {
        ProcessingOptions? captured = null;
        var services = new ServiceCollection();
        services.AddFluxFlow(CapacityDefinition(), options =>
            {
                options.InputCapacity = 5;
                options.OutputCapacity = 7;
            })
            .AddTestRuntimeAssembler(services =>
                services.AddFluxFlowComponents().Advanced.AddDynamicComponent(
                    "test.capacity",
                    component =>
                    {
                        component
                            .UseFactory(context =>
                            {
                                captured = context.BindConfiguration<ProcessingOptions>();
                                return new IdentityNode<string>();
                            })
                            .HasInput("Input", static node => node.Input)
                            .HasOutput("Output", static node => node.Output)
                            .HasEvents("Events", static node => node.Events);
                    }));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var assembler = provider.GetRequiredService<ApplicationRuntimeAssembler>();

        (await host.StartAsync()).IsApplied.ShouldBeTrue();

        var ports = assembler.GetRequiredPorts();
        var input = ports.Ports.Single(port =>
            port.Address == ApplicationAddress.WorkflowPort("Orders", "Value", "Input"));
        var output = ports.Ports.Single(port =>
            port.Address == ApplicationAddress.WorkflowPort("Orders", "Value", "Output"));
        input.Direction.ShouldBe(ApplicationPortDirection.Input);
        input.Capacity.ShouldBe(5);
        output.Direction.ShouldBe(ApplicationPortDirection.Output);
        output.Capacity.ShouldBe(7);
        captured.ShouldNotBeNull().BoundedCapacity.ShouldBe(37);
        host.CurrentDefinition.ShouldNotBeNull()
            .Workflows["Orders"].Components["Value"]
            .Properties["boundedCapacity"].GetInt32().ShouldBe(37);

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
        => services.AddFluxFlowComponents().Advanced
            .AddDynamicComponent("test.manual-source", component =>
                component
                    .UseFactory(static context =>
                    {
                        var source = new ManualSource();
                        context.Services.GetRequiredService<ManualSourceCatalog>()
                            .Add(context.ComponentName, source);
                        return source;
                    })
                    .HasOutput("Output", static source => source.Output))
            .AddDynamicComponent("test.source-recorder", component =>
                component
                    .UseFactory(static context => new SourceRecordingNode(
                        context.Services.GetRequiredService<SourceOutputTracker>()))
                    .HasInput("Input", static node => node.Input));

    private static ComponentInstance CreateMismatchedInstance(
        DescriptorTracker tracker,
        AdvancedPortMismatch mismatch)
    {
        var node = new TrackedNode(tracker);
        return mismatch switch
        {
            AdvancedPortMismatch.Missing => ComponentInstance.Create(node),
            AdvancedPortMismatch.Extra => ComponentInstance.Create(
                node,
                inputs: [ComponentPorts.Input<string>("Extra", node.Input)]),
            AdvancedPortMismatch.Renamed => ComponentInstance.Create(
                node,
                inputs: [ComponentPorts.Input<string>("Other", node.Input)]),
            AdvancedPortMismatch.Mistyped => ComponentInstance.Create(
                node,
                inputs: [ComponentPorts.Input<int>("Input", node.IntegerInput)]),
            AdvancedPortMismatch.WrongSignalKind => ComponentInstance.Create(
                node,
                inputs: [ComponentPorts.Input<string>("Input", node.Input)]),
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };
    }

    private static void AddTestComponents(IServiceCollection services)
        => services.AddFluxFlowComponents().Advanced
            .AddDynamicComponent("test.prefix", component =>
                component
                    .UseFactory(static context =>
                    {
                        var resource = context.GetRequiredResource<PrefixResource>("Prefix");
                        return new PrefixNode(resource.Value);
                    })
                    .HasInput("Input", static node => node.Input)
                    .HasOutput("Output", static node => node.Output)
                    .HasEvents("Diagnostics", static node => node.Events))
            .AddDynamicComponent("test.revision-events", component =>
                component
                    .UseFactory(static context => new RevisionEventNode(
                        context.Services.GetRequiredService<RevisionEventTracker>()))
                    .HasInput("Input", static node => node.Input)
                    .HasEvents("Events", static node => node.Events))
            .AddDynamicComponent("test.identity-string", component =>
                component
                    .UseFactory(static _ => new IdentityNode<string>())
                    .HasInput("Input", static node => node.Input)
                    .HasOutput("Output", static node => node.Output)
                    .HasEvents("Events", static node => node.Events))
            .AddDynamicComponent("test.identity-integer", component =>
                component
                    .UseFactory(static _ => new IdentityNode<int>())
                    .HasInput("Input", static node => node.Input)
                    .HasOutput("Output", static node => node.Output)
                    .HasEvents("Events", static node => node.Events))
            .AddDynamicComponent("test.final-output", component =>
                component
                    .UseFactory(static _ => new FinalOutputNode())
                    .HasInput("Input", static node => node.Input)
                    .HasOutput("Output", static node => node.Output)
                    .HasEvents("Events", static node => node.Events));

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

    private static ApplicationDefinition CapacityDefinition()
        => ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {},
              "Workflows": {
                "Orders": {
                  "Value": {
                    "Type": "test.capacity",
                    "boundedCapacity": 37
                  }
                }
              }
            }
            """);

    private static ApplicationDefinition CodeFirstRoutingDefinition(
        IReadOnlySet<string> auditValues)
    {
        var identity = CreateStringIdentityContract("test.identity-string");
        var signalContract = ComponentContract.Create(
            "test.signal",
            static component => component
                .UseFactory(static context => new SignalTargetNode(
                    context.Services.GetRequiredService<SignalTracker>()))
                .HasSignalInput("Signal", static node => node),
            static component => new SignalComponentHandle(component));
        var application = new ApplicationDefinitionBuilder();
        application
            .AddWorkflow("Main", out var main)
            .AddWorkflow("Audit", out var audit);
        var source = main.AddComponent("Source", identity);
        var priority = main.AddComponent("Priority", identity);
        var standard = main.AddComponent("Standard", identity);
        var recorder = audit.AddComponent("Recorder", identity);
        var signal = audit.AddComponent("Signal", signalContract);

        source.Output
            .ConnectTo(
                priority.Input,
                static value => value == "priority")
            .ConnectTo(
                standard.Input,
                static value => value == "standard")
            .ConnectTo(
                recorder.Input,
                auditValues.Contains)
            .ConnectTo(
                signal.Signal,
                static value => value == "priority");
        return application.Build();
    }

    private static ComponentContract<InputOutputComponentHandle<string, string>>
        CreateStringIdentityContract(string type)
        => CreateStringIdentityContract(type, static _ => new IdentityNode<string>());

    private static ComponentContract<InputOutputComponentHandle<string, string>>
        CreateStringIdentityContract(
            string type,
            Func<ComponentActivationContext, IdentityNode<string>> factory)
        => ComponentContract.Create(
            type,
            component => component
                .UseFactory(factory)
                .HasInput("Input", static node => node.Input)
                .HasOutput("Output", static node => node.Output)
                .HasEvents("Events", static node => node.Events),
            static component => new InputOutputComponentHandle<string, string>(
                component,
                "Input",
                "Output",
                "Events"));

    private static ApplicationDefinition IdentityContractsDefinition(
        params (string Name,
            ComponentContract<InputOutputComponentHandle<string, string>> Contract)[] components)
    {
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        foreach (var (name, contract) in components)
            workflow.AddComponent(name, contract);
        return application.Build();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ContractRevisionFixture CreateContractRevisionFixture()
    {
        var closure = new ContractFactoryClosure();
        var contract = CreateStringIdentityContract("test.identity", closure.CreateNode);
        return new ContractRevisionFixture(
            new MutableDefinitionSource(IdentityContractsDefinition(("Value", contract))),
            new WeakReference(closure));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ReplaceContractRevisionAsync(
        FluxFlowApplication host,
        MutableDefinitionSource source)
    {
        source.Definition = IdentityContractsDefinition(
            ("Value", CreateStringIdentityContract("test.identity")));
        var replacement = await host.ReloadAsync("contract-replacement");

        replacement.IsApplied.ShouldBeTrue(string.Join(
            Environment.NewLine,
            replacement.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Stage}: {diagnostic.Error.Code}: {diagnostic.Error.Message}")));
        replacement.PreviousRevision.ShouldNotBeNull();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PredicateRevisionFixture CreatePredicateRevisionFixture()
    {
        var closure = new PredicateClosure("allowed");
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var source = workflow.AddComponent("Source", "test.identity-string");
        var sink = workflow.AddComponent("Sink", "test.identity-string");
        source.Output<string>("Output").ConnectTo(
            sink.Input<string>("Input"),
            closure.Matches);
        return new PredicateRevisionFixture(
            new MutableDefinitionSource(application.Build()),
            new WeakReference(closure));
    }

    private static ApplicationDefinition CodeFirstUnlinkedDefinition()
    {
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        workflow.AddComponent("Source", "test.identity-string");
        workflow.AddComponent("Sink", "test.identity-string");
        return application.Build();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ReplacePredicateRevisionAsync(
        FluxFlowApplication host,
        MutableDefinitionSource source)
    {
        source.Definition = CodeFirstUnlinkedDefinition();
        var replacement = await host.ReloadAsync("replacement");

        replacement.IsApplied.ShouldBeTrue(string.Join(
            Environment.NewLine,
            replacement.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Stage}: {diagnostic.Error.Code}: {diagnostic.Error.Message}")));
        replacement.PreviousRevision.ShouldNotBeNull();
        host.CurrentDefinition.ShouldBeSameAs(source.Definition);
        host.LastUpdate!.PreviousRevision.ShouldBeNull();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetirementAssertionContext CreateContractRetirementContext()
    {
        var fixture = CreateContractRevisionFixture();
        var services = new ServiceCollection();
        services.AddFluxFlow(fixture.Source, options => options.StartWithHost = false);
        var provider = services.BuildServiceProvider();

        try
        {
            var application = provider.GetRequiredService<FluxFlowApplication>();
            application.StartAsync().GetAwaiter().GetResult().IsApplied.ShouldBeTrue();
            fixture.Closure.IsAlive.ShouldBeTrue();
            ReplaceContractRevisionAsync(application, fixture.Source).GetAwaiter().GetResult();
            return new RetirementAssertionContext(provider, application, fixture.Closure);
        }
        catch
        {
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetirementAssertionContext CreatePredicateRetirementContext()
    {
        var fixture = CreatePredicateRevisionFixture();
        var services = new ServiceCollection();
        services.AddFluxFlow(fixture.Source, options => options.StartWithHost = false)
            .AddTestRuntimeAssembler(AddTestComponents);
        var provider = services.BuildServiceProvider();

        try
        {
            var application = provider.GetRequiredService<FluxFlowApplication>();
            application.StartAsync().GetAwaiter().GetResult().IsApplied.ShouldBeTrue();
            fixture.Closure.IsAlive.ShouldBeTrue();
            ReplacePredicateRevisionAsync(application, fixture.Source).GetAwaiter().GetResult();
            return new RetirementAssertionContext(provider, application, fixture.Closure);
        }
        catch
        {
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    private static T RunOnTerminatedThread<T>(Func<T> operation)
        where T : class
    {
        T? result = null;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = operation();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        })
        {
            IsBackground = true
        };

        thread.Start();
        thread.Join();
        failure?.Throw();
        return result ?? throw new InvalidOperationException(
            "The retirement assertion thread completed without a result.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceFullCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

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
        protected override async Task ProcessAsync(FlowMessage<string> message)
        {
            await EmitAsync(message.With(prefix + message.Value), Stopping).ConfigureAwait(false);
            EmitEvent(new FlowEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = message.CorrelationId,
                Name = "prefix.processed"
            });
        }
    }

    private sealed class IdentityNode<T> : FlowNode<T, T>
    {
        protected override async Task ProcessAsync(FlowMessage<T> message)
            => await EmitAsync(message, Stopping).ConfigureAwait(false);
    }

    private sealed class FinalOutputNode : FlowNode<string, string>
    {
        private readonly List<FlowMessage<string>> _received = [];

        protected override Task ProcessAsync(FlowMessage<string> message)
        {
            _received.Add(message);
            return Task.CompletedTask;
        }

        protected override async ValueTask OnInputCompletedAsync()
        {
            foreach (var message in _received)
            {
                await EmitAsync(message.With($"final:{message.Value}"), Stopping)
                    .ConfigureAwait(false);
            }
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
            _output.Post(FlowMessage.Create("started:1"));
            _output.Post(FlowMessage.Create("started:2"));
            _output.Post(FlowMessage.Create("started:3"));
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
        public BufferBlock<FlowMessage<int>> IntegerInput { get; } = new();

        protected override Task ProcessAsync(FlowMessage<string> message)
            => Task.CompletedTask;

        protected override ValueTask OnDisposeAsync()
        {
            tracker.MarkDisposed();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SignalTracker
    {
        private readonly TaskCompletionSource<object?> _next =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Record(object? value) => _next.TrySetResult(value);

        public Task<object?> NextAsync()
            => _next.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed record PredicateRevisionFixture(
        MutableDefinitionSource Source,
        WeakReference Closure);

    private sealed record ContractRevisionFixture(
        MutableDefinitionSource Source,
        WeakReference Closure);

    private sealed class RetirementAssertionContext(
        ServiceProvider provider,
        FluxFlowApplication application,
        WeakReference closure) : IAsyncDisposable
    {
        public FluxFlowApplication Application { get; } = application;

        public WeakReference Closure { get; } = closure;

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    private sealed class PredicateClosure(string accepted)
    {
        public bool Matches(string value) => value == accepted;
    }

    private sealed class ContractFactoryClosure
    {
        public IdentityNode<string> CreateNode(ComponentActivationContext _)
            => new();
    }

    private sealed class MutableDefinitionSource(ApplicationDefinition definition)
        : IApplicationDefinitionSource
    {
        public ApplicationDefinition Definition { get; set; } = definition;

        public ValueTask<ApplicationDefinition> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Definition);
        }
    }

    private sealed class SignalComponentHandle : AuthoredComponentHandle
    {
        public SignalComponentHandle(ComponentHandle definition)
            : base(definition)
            => Signal = definition.SignalInput("Signal");

        public SignalInputPortHandle Signal { get; }
    }

    private sealed class SignalTargetNode(SignalTracker tracker) : IFlowNode, IFlowSignalTarget
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public ValueTask<bool> SendAsync<T>(
            FlowMessage<T> signal,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tracker.Record(signal.Value);
            return ValueTask.FromResult(true);
        }

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);

        public ValueTask DisposeAsync()
        {
            Complete();
            return ValueTask.CompletedTask;
        }
    }

    public enum AdvancedPortMismatch
    {
        Missing,
        Extra,
        Renamed,
        Mistyped,
        WrongSignalKind
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
