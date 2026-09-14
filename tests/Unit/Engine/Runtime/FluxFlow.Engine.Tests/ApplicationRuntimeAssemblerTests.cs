using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;
using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine.Internal.Revisions;
using FluxFlow.Json.Model;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Engine.Signals;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using ApplicationResourceRegistrationContext = FluxFlow.Composition.ApplicationResourceRegistrationContext;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationRuntimeAssemblerTests
{
    [Fact]
    public async Task System_conditions_use_application_engine_even_when_target_workflow_overrides_it()
    {
        const string json = """
            {"ExpressionEngine":"events","Resources":{},"Workflows":{"Main":{
              "ExpressionEngine":"unused",
              "Recorder":{"Type":"test.revision-events","Input":{"Port":"System.Events.Output","Condition":"accept"}}
            }}}
            """;
        var services = new ServiceCollection();
        services.AddSingleton<RevisionEventTracker>();
        services.AddKeyedSingleton<IFlowExpressionEngine>("events", new CSharpFlowExpressionEngine(
            interpreter => interpreter.SetVariable("accept", true)));
        services.AddFluxFlowUpdates(ApplicationDefinitionJson.Deserialize(json)).AddTestRuntimeAssembler(AddTestComponents,
            context => context.Services.AddSingleton(context.HostServices.GetRequiredService<RevisionEventTracker>()));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        (await host.StartAsync()).IsApplied.ShouldBeTrue();
        var tracker = provider.GetRequiredService<RevisionEventTracker>();
        await EventuallyAsync(() => tracker.Phases.Count >= 3);
        tracker.Phases.Take(3).ShouldBe(["Proposed", "Accepted", "Activated"]);

        var rejected = await host.ApplyAsync("invalid-system-engine", ApplicationDefinitionJson.Deserialize(json.Replace("\"events\"", "\"missing\"")));
        rejected.IsRejected.ShouldBeTrue();
        host.Current!.RevisionId.ShouldBe("initial");
        await host.StopAsync();
    }

    [Fact]
    public async Task Hosted_json_conditions_resolve_named_engines_and_preserve_running_revision_on_rejection()
    {
        const string json = """
            {"ExpressionEngine":"loose","Resources":{},"Workflows":{"Orders":{
              "First":{"Type":"test.identity-integer","DiscardUnmatched":["Output"],"Output":{"Port":"Second.Input",
                "Condition":"input.Json.GetInt32() > minimum + offset","Parameters":{"minimum":10}}},
              "Second":{"Type":"test.identity-integer","DiscardUnmatched":["Output"]}
            }}}
            """;
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>("loose", new CSharpFlowExpressionEngine(
            interpreter => interpreter.SetVariable("offset", 0)));
        services.AddKeyedSingleton<IFlowExpressionEngine>("strict", new CSharpFlowExpressionEngine(
            interpreter => interpreter.SetVariable("offset", 10)));
        services.AddFluxFlowUpdates(ApplicationDefinitionJson.Deserialize(json)).AddTestRuntimeAssembler(AddTestComponents);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var runtime = provider.GetRequiredService<ApplicationRuntimeAssembler>();
        (await host.StartAsync()).IsApplied.ShouldBeTrue();

        // A rejected value precedes the accepted one: the first observed output must be 11.
        var ports = runtime.GetRequiredPorts();
        var first = ports.ReceiveAsync<int>(Output, TimeSpan.FromSeconds(5));
        (await ports.SendAsync(Input, FlowMessage.Create(10))).Status.ShouldBe(PortSendStatus.Accepted);
        (await ports.SendAsync(Input, FlowMessage.Create(11))).Status.ShouldBe(PortSendStatus.Accepted);
        (await first).Message!.Value.ShouldBe(11);

        var updated = await host.ApplyAsync("strict-revision", ApplicationDefinitionJson.Deserialize(json.Replace("loose", "strict")));
        updated.IsApplied.ShouldBeTrue();
        var second = runtime.GetRequiredPorts().ReceiveAsync<int>(Output, TimeSpan.FromSeconds(5));
        (await runtime.GetRequiredPorts().SendAsync(Input, FlowMessage.Create(11))).Status.ShouldBe(PortSendStatus.Accepted);
        (await runtime.GetRequiredPorts().SendAsync(Input, FlowMessage.Create(21))).Status.ShouldBe(PortSendStatus.Accepted);
        (await second).Message!.Value.ShouldBe(21);

        var changedParameters = json.Replace("loose", "strict").Replace("\"minimum\":10", "\"minimum\":20");
        (await host.ApplyAsync("parameters-revision", ApplicationDefinitionJson.Deserialize(changedParameters))).IsApplied.ShouldBeTrue();
        var parameterOutput = runtime.GetRequiredPorts().ReceiveAsync<int>(Output, TimeSpan.FromSeconds(5));
        (await runtime.GetRequiredPorts().SendAsync(Input, FlowMessage.Create(21))).Status.ShouldBe(PortSendStatus.Accepted);
        (await runtime.GetRequiredPorts().SendAsync(Input, FlowMessage.Create(31))).Status.ShouldBe(PortSendStatus.Accepted);
        (await parameterOutput).Message!.Value.ShouldBe(31);

        var rejected = await host.ApplyAsync("missing-revision", ApplicationDefinitionJson.Deserialize(json.Replace("loose", "missing")));
        rejected.IsRejected.ShouldBeTrue();
        host.Current!.RevisionId.ShouldBe("parameters-revision");
        var third = runtime.GetRequiredPorts().ReceiveAsync<int>(Output, TimeSpan.FromSeconds(5));
        (await runtime.GetRequiredPorts().SendAsync(Input, FlowMessage.Create(32))).Status.ShouldBe(PortSendStatus.Accepted);
        (await third).Message!.Value.ShouldBe(32);
        await host.StopAsync();
    }

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
        services.AddFluxFlowUpdates(Definition("one:"))
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
        var eventReceive = ports.ReceiveAsync<global::FluxFlow.Data.FlowValue>(
            FirstDiagnostics,
            TimeSpan.FromSeconds(5));
        (await ports.SendAsync(Input, FlowMessage.Create("value")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        var first = await firstReceive;
        first.Status.ShouldBe(PortReceiveStatus.Received);
        first.Message!.Value.ShouldBe("one:one:value");
        var componentEvent = await eventReceive;
        componentEvent.Status.ShouldBe(PortReceiveStatus.Received);
        componentEvent.Message!.Headers[FlowEventHeaders.Source].ShouldBe("Orders.First");
        componentEvent.Message.ToFlowEvent().Name.ShouldBe("prefix.processed");
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
        services.AddFluxFlowUpdates(Definition("one:"))
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
        services.AddFluxFlowUpdates(Definition("one:"))
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
    public async Task Code_first_definition_executes_embedded_contracts_without_host_registration()
    {
        var auditValues = new HashSet<string>(StringComparer.Ordinal) { "priority" };
        var definition = CodeFirstRoutingDefinition(auditValues);
        var signalTracker = new SignalTracker();
        var services = new ServiceCollection();
        services.AddSingleton(signalTracker);
        services.AddFluxFlowUpdates(definition)
            .AddTestRuntimeAssembler(static _ => { });
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var access = provider.GetRequiredService<ApplicationRuntimeAssembler>();

        FluxFlowRegistrationExtensions.CreateFactoryMap(provider).ShouldBeEmpty();
        definition.ComponentFactories.Keys.Order(StringComparer.Ordinal)
            .ShouldBe(["test.identity-string", "test.signal"]);

        var started = await host.StartAsync();

        started.IsApplied.ShouldBeTrue(string.Join(
            Environment.NewLine,
            started.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Stage}: {diagnostic.Error.Code}: {diagnostic.Error.Message}: {diagnostic.Error.Details}")));
        host.CurrentSetup.ShouldBeSameAs(definition);
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
        (await signalTracker.NextAsync()).ShouldBeAssignableTo<FlowValue>().ShouldNotBeNull().Json
            .Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("priority");

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
                return new IdentityNode();
            });
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var first = workflow.AddComponent("First", embedded.Key, embedded.Value, static _ => { }, static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, "Input", "Output", "Events"));
        var second = workflow.AddComponent("Second", "test.host");
        first.Output.LinkTo(second.Input<FlowValue>("Input"));
        second.Output<FlowValue>("Output").DiscardUnmatched();
        var definition = application.Build();
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(definition)
            .AddComponent(embedded.Key, embedded.Value)
            .AddComponent("test.host", context =>
        {
            hostActivations++;
            var createdNode = new IdentityNode();
            context.HasInput("Input", createdNode.Input);
            context.HasOutput("Output", createdNode.Output);
            context.HasEvents("Events", createdNode.Events);
            return createdNode;
        });
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
        FluxFlowRegistrationExtensions.CreateFactoryMap(provider).Keys.ShouldContain(embedded.Key);
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
                return new IdentityNode();
            });
        var initialBuilder = new ApplicationDefinitionBuilder();
        initialBuilder.AddWorkflow("Main").AddComponent("Value", "test.identity")
            .Output<FlowValue>("Output").DiscardUnmatched();
        var initial = initialBuilder.Build();
        var rejectedContract = CreateStringIdentityContract(
            "test.identity",
            _ =>
            {
                rejectedActivations++;
                return new IdentityNode();
            });
        var rejectedBuilder = new ApplicationDefinitionBuilder();
        rejectedBuilder.AddWorkflow("Main").AddComponent("Value", rejectedContract.Key, rejectedContract.Value, static _ => { }, static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, "Input", "Output", "Events"));
        var rejectedDefinition = rejectedBuilder.Build();
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(initial).AddComponent(activeContract.Key, activeContract.Value);
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
        conflictMessage.ShouldContain("conflicting registrations");
        host.CurrentSetup.ShouldBeSameAs(initial);
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
            return new IdentityNode();
        });
        var beta = CreateStringIdentityContract("test.beta", _ =>
        {
            betaActivations++;
            return new IdentityNode();
        });
        var replacementBeta = CreateStringIdentityContract("test.beta", _ =>
        {
            replacementActivations++;
            return new IdentityNode();
        });
        var initial = IdentityContractsDefinition(("Alpha", alpha));
        var expanded = IdentityContractsDefinition(("Alpha", alpha), ("Beta", beta));
        var removed = IdentityContractsDefinition(("Beta", beta));
        var replaced = IdentityContractsDefinition(("Beta", replacementBeta));
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(initial);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        var access = provider.GetRequiredService<ApplicationRuntimeAssembler>();

        (await host.StartAsync()).IsApplied.ShouldBeTrue();
        alphaActivations.ShouldBe(1);
        (await host.ApplyAsync("expanded", expanded)).IsApplied.ShouldBeTrue();
        host.CurrentSetup.ShouldBeSameAs(expanded);
        alphaActivations.ShouldBe(2);
        betaActivations.ShouldBe(1);
        (await host.ApplyAsync("removed", removed)).IsApplied.ShouldBeTrue();
        host.CurrentSetup.ShouldBeSameAs(removed);
        betaActivations.ShouldBe(2);
        (await host.ApplyAsync("replaced", replaced)).IsApplied.ShouldBeTrue();
        host.CurrentSetup.ShouldBeSameAs(replaced);
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
            return new IdentityNode();
        });
        var failingContract = CreateStringIdentityContract("test.identity", _ =>
        {
            failedActivations++;
            throw new InvalidOperationException("Replacement contract factory failed.");
        });
        var initial = IdentityContractsDefinition(("Value", activeContract));
        var replacement = IdentityContractsDefinition(("Value", failingContract));
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(initial);
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
        host.CurrentSetup.ShouldBeSameAs(initial);
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

        await CollectRetiredClosureAsync(context.Closure);
        context.Closure.IsAlive.ShouldBeFalse();

        await context.Application.StopAsync();
    }

    [Fact]
    public async Task Successful_code_predicate_replacement_retires_captured_closure()
    {
        await using var context = RunOnTerminatedThread(
            static () => CreatePredicateRetirementContext());

        await CollectRetiredClosureAsync(context.Closure);
        context.Closure.IsAlive.ShouldBeFalse();

        await context.Application.StopAsync();
    }

    [Fact]
    public async Task Json_definition_still_requires_explicit_host_registration()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(IdentityDefinition("test.identity-legacy"))
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
        services.AddFluxFlowUpdates(ProcessingDefinition())
            .AddTestRuntimeAssembler(services =>
                services.AddComponent("test.processing", context =>
        {
            context.BindConfiguration<System.Text.Json.JsonElement>(processingCapabilities: CompositionProcessingCapabilities.ParallelRelaxedOrder);
            captured = context.BindConfiguration<ProcessingOptions>();
            var createdNode = new IdentityNode();
            context.HasInput("Input", createdNode.Input);
            context.HasOutput("Output", createdNode.Output);
            context.HasEvents("Events", createdNode.Events);
            return createdNode;
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
        services.AddFluxFlowUpdates(BlockingSourceDefinition())
            .AddTestRuntimeAssembler(services =>
                services.AddComponent("test.blocking-source", context =>
        {
            var createdNode = source;
            context.HasOutput("Output", createdNode.Output);
            return createdNode;
        }));
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
    public async Task Later_factory_failure_disposes_components_created_earlier_in_preparation()
    {
        var tracker = new DescriptorTracker();
        var factoryCalls = 0;
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(ApplicationDefinitionJson.Deserialize(
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
                services.AddComponent("test.partial", context =>
        {
            if (Interlocked.Increment(ref factoryCalls) == 2)
                throw new InvalidOperationException("Factory failed.");

            var createdNode = new TrackedNode(tracker);

            return createdNode;
        }));
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
        services.AddFluxFlowUpdates(IdentityDefinition("test.final-output"))
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_drains_linked_final_outputs_for_documents_and_code(bool codeAuthored)
    {
        ApplicationSetup setup;
        if (codeAuthored)
        {
            var builder = new ApplicationDefinitionBuilder();
            var workflow = builder.AddWorkflow("Orders");
            var source = workflow.AddComponent("Source", "test.final-output");
            var target = workflow.AddComponent("Target", "test.final-output");
            source.Output<FlowValue>("Output").LinkTo(target.Input<FlowValue>("Input"));
            target.Output<FlowValue>("Output").DiscardUnmatched();
            setup = builder.Build();
        }
        else
        {
            setup = new ApplicationSetup(ApplicationDefinitionJson.Deserialize(
                """
                {"Resources":{},"Workflows":{"Orders":{
                  "Source":{"Type":"test.final-output"},
                  "Target":{"Type":"test.final-output","Input":"Source.Output","DiscardUnmatched":["Output"]}
                }}}
                """));
        }
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(setup).AddTestRuntimeAssembler(AddTestComponents);
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        (await host.StartAsync()).IsApplied.ShouldBeTrue();
        var input = ApplicationAddress.WorkflowPort("Orders", "Source", "Input");
        var output = ApplicationAddress.WorkflowPort("Orders", "Target", "Output");
        var observed = await host.Ports.ObserveAsync<string>(output, capacity: 3);
        observed.Status.ShouldBe(PortObserveStatus.Started);
        await using var observation = observed.Observation!;
        foreach (var value in new[] { "one", "two", "three" })
            (await host.Ports.SendAsync(input, FlowMessage.Create(value))).Status.ShouldBe(PortSendStatus.Accepted);

        await host.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        var final = new[]
        {
            await observation.Messages.ReceiveAsync(TimeSpan.FromSeconds(5)),
            await observation.Messages.ReceiveAsync(TimeSpan.FromSeconds(5)),
            await observation.Messages.ReceiveAsync(TimeSpan.FromSeconds(5))
        };
        final.Select(message => message.Value).ShouldBe(["final:final:one", "final:final:two", "final:final:three"]);
        host.State.ShouldBe(ApplicationState.Stopped);
    }

    [Fact]
    public async Task Eager_source_output_is_linked_before_source_start()
    {
        var tracker = new SourceOutputTracker();
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddFluxFlowUpdates(EagerSourceDefinition())
            .AddTestRuntimeAssembler(
                services => services.AddComponent("test.eager-source", context =>
        {
            var createdNode = new EagerSource();
            context.HasOutput("Output", createdNode.Output);
            return createdNode;
        })
                    .AddComponent("test.source-recorder", context =>
        {
            var createdNode = new SourceRecordingNode(
                                    context.Services.GetRequiredService<SourceOutputTracker>());
            context.HasInput("Input", createdNode.Input);
            return createdNode;
        }),
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
        services.AddFluxFlowUpdates(CapacityDefinition(), options =>
            {
                options.InputCapacity = 5;
                options.OutputCapacity = 7;
            })
            .AddTestRuntimeAssembler(services =>
                services.AddComponent("test.capacity", context =>
        {
            captured = context.BindConfiguration<ProcessingOptions>();
            var createdNode = new IdentityNode();
            context.HasInput("Input", createdNode.Input);
            context.HasOutput("Output", createdNode.Output);
            context.HasEvents("Events", createdNode.Events);
            return createdNode;
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
        services.AddFluxFlowUpdates(FanInDefinition())
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
        services.AddFluxFlowUpdates(FanInDefinition())
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
        => services.AddComponent("test.manual-source", context =>
        {
            var source = new ManualSource();
            context.Services.GetRequiredService<ManualSourceCatalog>()
                .Add(context.ComponentName, source);
            var createdNode = source;
            context.HasOutput("Output", createdNode.Output);
            return createdNode;
        })
            .AddComponent("test.source-recorder", context =>
        {
            var createdNode = new SourceRecordingNode(
                            context.Services.GetRequiredService<SourceOutputTracker>());
            context.HasInput("Input", createdNode.Input);
            return createdNode;
        });


    private static void AddTestComponents(IServiceCollection services)
    {
        services.AddComponent("test.prefix", context =>
        {
            var resource = context.GetRequiredResource<PrefixResource>("Prefix");
            var createdNode = new PrefixNode(resource.Value);
            context.HasInput("Input", createdNode.Input);
            context.HasOutput("Output", createdNode.Output);
            context.HasEvents("Diagnostics", createdNode.Events);
            return createdNode;
        })
            .AddComponent("test.revision-events", context =>
        {
            var createdNode = new RevisionEventNode(
                            context.Services.GetRequiredService<RevisionEventTracker>());
            context.HasInput("Input", createdNode.Input);
            context.HasEvents("Events", createdNode.Events);
            return createdNode;
        })
            .AddComponent("test.identity-string", context =>
        {
            var createdNode = new IdentityNode();
            context.HasInput("Input", createdNode.Input);
            context.HasOutput("Output", createdNode.Output);
            context.HasEvents("Events", createdNode.Events);
            return createdNode;
        })
            .AddComponent("test.identity-integer", context =>
        {
            var createdNode = new IdentityNode();
            context.HasInput("Input", createdNode.Input);
            context.HasOutput("Output", createdNode.Output);
            context.HasEvents("Events", createdNode.Events);
            return createdNode;
        })
            .AddComponent("test.final-output", context =>
        {
            var createdNode = new FinalOutputNode();
            context.HasInput("Input", createdNode.Input);
            context.HasOutput("Output", createdNode.Output);
            context.HasEvents("Events", createdNode.Events);
            return createdNode;
        });

    }

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

    private static ApplicationSetup Definition(string prefix, bool includeThird = false)
        => new ApplicationSetup(ApplicationDefinitionJson.Deserialize(
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
                    "DiscardUnmatched": ["Diagnostics"],
                    "Output": "Second.Input"
                  },
                  "Second": {
                    "Type": "test.prefix",
                    "DiscardUnmatched": ["Output", "Diagnostics"],
                    "Prefix": "Resources.Prefix"
                  },
                  "RevisionEvents": {
                    "Type": "test.revision-events",
                    "Input": "System.Events.Output"
                  }{{(includeThird ? ",\n                  \"Third\": { \"Type\": \"test.prefix\", \"Prefix\": \"Resources.Prefix\", \"DiscardUnmatched\": [\"Output\", \"Diagnostics\"] }" : string.Empty)}}
                }
              }
            }
            """));

    private static ApplicationSetup InvalidExpandedDefinition()
        => new ApplicationSetup(ApplicationDefinitionJson.Deserialize(
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
                    "DiscardUnmatched": ["Diagnostics"],
                    "Output": "Second.Input"
                  },
                  "Second": {
                    "Type": "test.prefix",
                    "DiscardUnmatched": ["Output", "Diagnostics"],
                    "Prefix": "Resources.Prefix"
                  },
                  "Third": {
                    "Type": "test.unknown"
                  }
                }
              }
            }
            """));

    private static ApplicationSetup ProcessingDefinition()
        => new ApplicationSetup(ApplicationDefinitionJson.Deserialize(
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
            """));

    private static ApplicationSetup CapacityDefinition()
        => new ApplicationSetup(ApplicationDefinitionJson.Deserialize(
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
            """));

    private static ApplicationSetup CodeFirstRoutingDefinition(
        IReadOnlySet<string> auditValues)
    {
        var identity = CreateStringIdentityContract("test.identity-string");
        Func<ComponentActivationContext, ValueTask<IFlowNode>> signalFactory = context =>
        {
            var createdNode = new SignalTargetNode(
                    context.Services.GetRequiredService<SignalTracker>());
            context.HasInput("Signal", createdNode.Signal, signal: true);
            return ValueTask.FromResult<IFlowNode>(createdNode);
        };
        var application = new ApplicationDefinitionBuilder();
        application
            .AddWorkflow("Main", out var main)
            .AddWorkflow("Audit", out var audit);
        var source = main.AddComponent("Source", identity.Key, identity.Value, static _ => { },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, "Input", "Output", "Events"));
        var priority = main.AddComponent("Priority", identity.Key, identity.Value, static _ => { },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, "Input", "Output", "Events"));
        var standard = main.AddComponent("Standard", identity.Key, identity.Value, static _ => { },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, "Input", "Output", "Events"));
        var recorder = audit.AddComponent("Recorder", identity.Key, identity.Value, static _ => { },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, "Input", "Output", "Events"));
        var signal = audit.AddComponent("Signal", "test.signal", signalFactory, static _ => { }, static component => new SignalComponentHandle(component));
        priority.Output.DiscardUnmatched();
        standard.Output.DiscardUnmatched();
        recorder.Output.DiscardUnmatched();

        source.Output
            .LinkTo(
                priority.Input,
                static value => value.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) == "priority")
            .LinkTo(
                standard.Input,
                static value => value.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) == "standard")
            .LinkTo(
                recorder.Input,
                value => auditValues.Contains(value.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!))
            .LinkTo(
                signal.Signal,
                static value => value.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) == "priority");
        return application.Build();
    }

    private static KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>
        CreateStringIdentityContract(string type)
        => CreateStringIdentityContract(type, static _ => new IdentityNode());

    private static KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>
        CreateStringIdentityContract(
            string type,
            Func<ComponentActivationContext, IdentityNode> factory)
        => new(type, context =>
        {
            var createdNode = factory(context);
            context.HasInput("Input", createdNode.Input);
            context.HasOutput("Output", createdNode.Output);
            context.HasEvents("Events", createdNode.Events);
            return ValueTask.FromResult<IFlowNode>(createdNode);
        });

    private static ApplicationSetup IdentityContractsDefinition(
        params (string Name,
            KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>> Contract)[] components)
    {
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        foreach (var (name, contract) in components)
            workflow.AddComponent(name, contract.Key, contract.Value, static _ => { },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, "Input", "Output", "Events")).Output.DiscardUnmatched();
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
        source.Output<FlowValue>("Output").LinkTo(
            sink.Input<FlowValue>("Input"),
            value => closure.Matches(value.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
        return new PredicateRevisionFixture(
            new MutableDefinitionSource(application.Build()),
            new WeakReference(closure));
    }

    private static ApplicationSetup CodeFirstUnlinkedDefinition()
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
        host.CurrentSetup.ShouldBeSameAs(source.Definition);
        host.LastUpdate!.PreviousRevision.ShouldBeNull();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetirementAssertionContext CreateContractRetirementContext()
    {
        var fixture = CreateContractRevisionFixture();
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(fixture.Source, options => options.StartWithHost = false);
        var provider = services.BuildServiceProvider();

        try
        {
            var application = provider.GetRequiredService<FluxFlowApplication>();
            application.StartAsync().GetAwaiter().GetResult().IsApplied.ShouldBeTrue();
            ForceFullCollection();
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
        services.AddFluxFlowUpdates(fixture.Source, options => options.StartWithHost = false)
            .AddTestRuntimeAssembler(AddTestComponents);
        var provider = services.BuildServiceProvider();

        try
        {
            var application = provider.GetRequiredService<FluxFlowApplication>();
            application.StartAsync().GetAwaiter().GetResult().IsApplied.ShouldBeTrue();
            ForceFullCollection();
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

    private static async Task CollectRetiredClosureAsync(WeakReference closure)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            ForceFullCollection();
            if (!closure.IsAlive)
                return;

            // Completed async operations can still have stack roots while unwinding.
            await Task.Delay(10);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceFullCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static ApplicationSetup IdentityDefinition(string type)
        => new ApplicationSetup(ApplicationDefinitionJson.Deserialize(
            $$"""
            {
              "Resources": {},
              "Workflows": {
                "Orders": {
                  "Value": {
                    "Type": "{{type}}",
                    "DiscardUnmatched": ["Output"]
                  }
                }
              }
            }
            """));

    private static ApplicationSetup BlockingSourceDefinition()
        => new ApplicationSetup(ApplicationDefinitionJson.Deserialize(
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
            """));

    private static ApplicationSetup EagerSourceDefinition()
        => new ApplicationSetup(ApplicationDefinitionJson.Deserialize(
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
            """));

    private static ApplicationSetup FanInDefinition()
        => new ApplicationSetup(ApplicationDefinitionJson.Deserialize(
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
            """));

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

    private sealed class PrefixNode(string prefix) : FlowNode
    {
        protected override async Task ProcessAsync(FlowMessage message)
        {
            await EmitAsync(message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(prefix + message.Value!.Json.Deserialize<string>(), new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Stopping).ConfigureAwait(false);
            EmitEvent(new FlowEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = message.CorrelationId,
                Name = "prefix.processed"
            });
        }
    }

    private sealed class IdentityNode : FlowNode
    {
        protected override async Task ProcessAsync(FlowMessage message)
            => await EmitAsync(message, Stopping).ConfigureAwait(false);
    }

    private sealed class FinalOutputNode : FlowNode
    {
        private readonly List<FlowMessage> _received = [];

        protected override Task ProcessAsync(FlowMessage message)
        {
            _received.Add(message);
            return Task.CompletedTask;
        }

        protected override async ValueTask OnInputCompletedAsync()
        {
            foreach (var message in _received)
            {
                await EmitAsync(message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement($"final:{message.Value!.Json.Deserialize<string>()}", new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Stopping)
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
        private readonly BroadcastBlock<FlowMessage> _output = new(static message => message);
        private readonly TaskCompletionSource _startEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseStart =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ISourceBlock<FlowMessage> Output => _output;

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
        private readonly BroadcastBlock<FlowMessage> _output =
            new(static message => message);

        public ISourceBlock<FlowMessage> Output => _output;

        public Task Completion => _output.Completion;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _output.Post(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("started:1", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
            _output.Post(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("started:2", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
            _output.Post(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("started:3", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
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
        private readonly BufferBlock<FlowMessage> _output = new();

        public ISourceBlock<FlowMessage> Output => _output;

        public Task Completion => _output.Completion;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public bool Emit(string value) => _output.Post(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));

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

    private sealed class SourceRecordingNode(SourceOutputTracker tracker) : FlowNode
    {
        protected override Task ProcessAsync(FlowMessage message)
        {
            tracker.Add(message.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
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

    private sealed class TrackedNode(DescriptorTracker tracker) : FlowNode
    {
        protected override Task ProcessAsync(FlowMessage message)
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
        public IdentityNode CreateNode(ComponentActivationContext _)
            => new();
    }

    private sealed class MutableDefinitionSource(ApplicationSetup definition)
        : IApplicationDefinitionSource
    {
        public ApplicationSetup Definition { get; set; } = definition;

        public ValueTask<ApplicationSetup> LoadAsync(
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

    private sealed class SignalTargetNode(SignalTracker tracker) : IFlowNode
    {
        public ITargetBlock<FlowMessage> Signal { get; } =
            new ActionBlock<FlowMessage>(message => tracker.Record(message.Value));
        public Task Completion => Signal.Completion;
        public void Complete() => Signal.Complete();
        public void Fault(Exception exception) => Signal.Fault(exception);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }

    private sealed class RevisionEventNode(RevisionEventTracker tracker)
        : FlowNode
    {
        protected override Task ProcessAsync(FlowMessage message)
        {
            tracker.Add(message.ToFlowEvent().Details.Json
                .GetProperty("phase").GetString()!);
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
