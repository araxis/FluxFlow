using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class TypedCodeFirstApplicationAuthoringTests
{
    [Fact]
    public void Complete_contracts_add_configured_and_configuration_free_components_with_inferred_handles_and_owned_descriptors()
    {
        var runtimeConfigurations = 0;
        var activations = 0;
        var optionsCreated = 0;
        var optionsApplied = 0;
        var handlesCreated = 0;
        var routerContract = ComponentContract.Create<RouterOptions, RouterHandle>(
            " test.router ",
            component =>
            {
                runtimeConfigurations++;
                ConfigureRuntime(component, _ =>
                {
                    activations++;
                    return new AuthoringNode();
                });
            },
            () =>
            {
                optionsCreated++;
                return new RouterOptions();
            },
            (options, definition) =>
            {
                optionsApplied++;
                definition.Set("Minimum", options.Minimum);
                definition.Set("Label", options.Label);
            },
            component =>
            {
                handlesCreated++;
                return new RouterHandle(component);
            });
        var sinkContract = ComponentContract.Create(
            "test.sink",
            component =>
            {
                runtimeConfigurations++;
                ConfigureRuntime(component, _ =>
                {
                    activations++;
                    return new AuthoringNode();
                });
            },
            static component => new InputComponentHandle<Order>(component, "Orders", "Events"));
        runtimeConfigurations.ShouldBe(2);
        activations.ShouldBe(0);
        var application = new ApplicationDefinitionBuilder();
        application.AddWorkflow("Main", out var workflow);

        workflow
            .AddComponent(
                "Router",
                routerContract,
                options =>
                {
                    options.Minimum = 17;
                    options.Label = "priority";
                },
                out var router)
            .AddComponent("Sink", sinkContract, out var sink)
            .ShouldBeSameAs(workflow);

        router.ShouldBeOfType<RouterHandle>();
        sink.ShouldBeOfType<InputComponentHandle<Order>>();
        router.Type.ShouldBe("test.router");
        router.Address.Value.ShouldBe("Main.Router");
        router.Input.Address.Value.ShouldBe("Main.Router.Orders");
        router.Approved.Address.Value.ShouldBe("Main.Router.Approved");
        router.Rejected.Address.Value.ShouldBe("Main.Router.Rejected");
        router.Refresh.Address.Value.ShouldBe("Main.Router.Refresh");
        router.Events.Address.Value.ShouldBe("Main.Router.Events");
        sink.Input.Address.Value.ShouldBe("Main.Sink.Orders");
        sink.Events.Address.Value.ShouldBe("Main.Sink.Events");
        optionsCreated.ShouldBe(1);
        optionsApplied.ShouldBe(1);
        handlesCreated.ShouldBe(1);
        activations.ShouldBe(0);

        var definition = application.Build();
        var routerDefinition = definition.Workflows["Main"].Components["Router"];
        routerDefinition.Type.ShouldBe("test.router");
        routerDefinition.Properties.Keys.ShouldBe(["Label", "Minimum"], ignoreOrder: true);
        routerDefinition.Properties["Minimum"].GetInt32().ShouldBe(17);
        routerDefinition.Properties["Label"].GetString().ShouldBe("priority");
        definition.Workflows["Main"].Components["Sink"].Type.ShouldBe("test.sink");
        definition.Links.ShouldBeEmpty();
        definition.ComponentDescriptors.Select(static descriptor => descriptor.Type)
            .ShouldBe(["test.router", "test.sink"]);
        definition.ComponentDescriptors[0].ShouldBeSameAs(routerContract.Descriptor);
        definition.ComponentDescriptors[1].ShouldBeSameAs(sinkContract.Descriptor);
        routerContract.Type.ShouldBe("test.router");
        routerContract.Descriptor.ProcessingCapabilities.ShouldBe(
            CompositionProcessingCapabilities.ParallelRelaxedOrder);
        routerContract.Descriptor.Inputs.Keys.ShouldBe(
            ["Orders", "Refresh", "Signal"],
            ignoreOrder: true);
        routerContract.Descriptor.Inputs["Refresh"].Kind.ShouldBe(ComponentPortKind.Signal);
        routerContract.Descriptor.Outputs.Keys.ShouldBe(
            ["Approved", "Events", "Orders", "Rejected"],
            ignoreOrder: true);
        routerContract.Descriptor.Outputs["Events"].MessageType.ShouldBe(typeof(ComponentEvent));
        routerContract.Descriptor.Options.Keys.ShouldBe(["minimum"]);
        routerContract.Descriptor.Options["minimum"].IsRequired.ShouldBeTrue();
        routerContract.Descriptor.Resources.Keys.ShouldBe(["clock"]);
        routerContract.Descriptor.Resources["clock"].IsRequired.ShouldBeTrue();
        ((ICollection<ComponentDescriptor>)definition.ComponentDescriptors).IsReadOnly.ShouldBeTrue();
        activations.ShouldBe(0);
    }

    [Fact]
    public void Typed_contract_failures_are_atomic_and_allow_same_name_retry_without_descriptor_leak()
    {
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var distinctive = new DistinctiveException("handle creation failed");
        var failHandle = true;
        var unstable = ComponentContract.Create(
            "test.unstable",
            ConfigureRuntime,
            component =>
            {
                if (failHandle)
                    throw distinctive;
                return new OutputComponentHandle<int>(component, "Value", "Events");
            });

        var caught = Should.Throw<DistinctiveException>(() =>
            workflow.AddComponent("Retry", unstable));
        caught.ShouldBeSameAs(distinctive);

        failHandle = false;
        var recovered = workflow.AddComponent("Retry", unstable);
        recovered.Address.Value.ShouldBe("Main.Retry");

        var applyFailure = new DistinctiveException("option application failed");
        var brokenOptions = ComponentContract.Create<RouterOptions, RouterHandle>(
            "test.options",
            ConfigureRuntime,
            static () => new RouterOptions(),
            (_, _) => throw applyFailure,
            static component => new RouterHandle(component));
        Should.Throw<DistinctiveException>(() =>
            workflow.AddComponent("Options", brokenOptions, static _ => { }))
            .ShouldBeSameAs(applyFailure);
        workflow.AddComponent(
            "Options",
            ComponentContract.Create(
                "test.recovered",
                ConfigureRuntime,
                static component => new RouterHandle(component)));

        var nullContract = Should.Throw<ArgumentNullException>(() =>
            workflow.AddComponent(
                "NullContract",
                (ComponentContract<RouterHandle>)null!));
        nullContract.ParamName.ShouldBe("component");
        var nullConfigure = Should.Throw<ArgumentNullException>(() =>
            workflow.AddComponent(
                "NullConfigure",
                ComponentContract.Create<RouterOptions, RouterHandle>(
                    "test.configured",
                    ConfigureRuntime,
                    static () => new RouterOptions(),
                    static (_, _) => { },
                    static component => new RouterHandle(component)),
                (Action<RouterOptions>)null!));
        nullConfigure.ParamName.ShouldBe("configure");

        var definition = application.Build();
        definition.Workflows["Main"].Components.Keys.ShouldBe(
            ["Options", "Retry"],
            ignoreOrder: true);
        definition.Workflows["Main"].Components.ContainsKey("NullContract").ShouldBeFalse();
        definition.Workflows["Main"].Components.ContainsKey("NullConfigure").ShouldBeFalse();
        definition.ComponentDescriptors.Select(static descriptor => descriptor.Type)
            .ShouldBe(["test.recovered", "test.unstable"]);
        var descriptorTypes = definition.ComponentDescriptors
            .Select(static descriptor => descriptor.Type)
            .ToArray();
        descriptorTypes.ShouldNotContain("test.options");
        descriptorTypes.ShouldNotContain("test.configured");
    }

    [Fact]
    public void Contract_factories_capture_complete_descriptors_without_activation_and_validate_every_delegate()
    {
        var invalidType = Should.Throw<ArgumentException>(() =>
            ComponentContract.Create(
                " ",
                ConfigureRuntime,
                static component => new RouterHandle(component)));
        invalidType.ParamName.ShouldBe("type");

        var runtimeConfiguration = Should.Throw<ArgumentNullException>(() =>
            ComponentContract.Create(
                "test.simple",
                null!,
                static component => new RouterHandle(component)));
        runtimeConfiguration.ParamName.ShouldBe("configureRuntime");

        var simpleHandle = Should.Throw<ArgumentNullException>(() =>
            ComponentContract.Create<RouterHandle>(
                "test.simple",
                ConfigureRuntime,
                null!));
        simpleHandle.ParamName.ShouldBe("createHandle");

        var optionsFactory = Should.Throw<ArgumentNullException>(() =>
            ComponentContract.Create<RouterOptions, RouterHandle>(
                "test.configured",
                ConfigureRuntime,
                null!,
                static (_, _) => { },
                static component => new RouterHandle(component)));
        optionsFactory.ParamName.ShouldBe("createOptions");

        var apply = Should.Throw<ArgumentNullException>(() =>
            ComponentContract.Create<RouterOptions, RouterHandle>(
                "test.configured",
                ConfigureRuntime,
                static () => new RouterOptions(),
                null!,
                static component => new RouterHandle(component)));
        apply.ParamName.ShouldBe("apply");

        var configuredHandle = Should.Throw<ArgumentNullException>(() =>
            ComponentContract.Create<RouterOptions, RouterHandle>(
                "test.configured",
                ConfigureRuntime,
                static () => new RouterOptions(),
                static (_, _) => { },
                null!));
        configuredHandle.ParamName.ShouldBe("createHandle");

        var configurationFailure = new DistinctiveException("runtime configuration failed");
        Should.Throw<DistinctiveException>(() =>
                ComponentContract.Create(
                    "test.failed-runtime",
                    _ => throw configurationFailure,
                    static component => new RouterHandle(component)))
            .ShouldBeSameAs(configurationFailure);

        var missingFactory = Should.Throw<InvalidOperationException>(() =>
            ComponentContract.Create(
                "test.missing-factory",
                static component => component.AddOption<bool>("enabled"),
                static component => new RouterHandle(component)));
        missingFactory.Message.ShouldContain("test.missing-factory");
        missingFactory.Message.ShouldContain(nameof(RuntimeComponentRegistrationBuilder.UseFactory));
    }

    [Fact]
    public void Repeated_contract_use_deduplicates_sorted_definition_descriptors_and_conflicting_contracts_fail_atomically()
    {
        var zulu = ComponentContract.Create(
            "test.zulu",
            ConfigureRuntime,
            static component => new RouterHandle(component));
        var alpha = ComponentContract.Create(
            "test.alpha",
            ConfigureRuntime,
            static component => new RouterHandle(component));
        var conflictingZulu = ComponentContract.Create(
            "test.zulu",
            ConfigureRuntime,
            static component => new RouterHandle(component));
        conflictingZulu.Descriptor.ShouldNotBeSameAs(zulu.Descriptor);
        var application = new ApplicationDefinitionBuilder();
        application
            .AddWorkflow("Main", out var main)
            .AddWorkflow("Secondary", out var secondary);

        main.AddComponent("ZuluOne", zulu);
        main.AddComponent("Alpha", alpha);
        secondary.AddComponent("ZuluTwo", zulu);
        var conflict = Should.Throw<InvalidOperationException>(() =>
            secondary.AddComponent("Conflict", conflictingZulu));

        conflict.Message.ShouldContain("test.zulu");
        conflict.Message.ShouldContain("conflicting contracts");
        var definition = application.Build();
        definition.ComponentDescriptors.Select(static descriptor => descriptor.Type)
            .ShouldBe(["test.alpha", "test.zulu"]);
        definition.ComponentDescriptors[0].ShouldBeSameAs(alpha.Descriptor);
        definition.ComponentDescriptors[1].ShouldBeSameAs(zulu.Descriptor);
        definition.Workflows["Main"].Components.Keys.ShouldBe(
            ["Alpha", "ZuluOne"],
            ignoreOrder: true);
        definition.Workflows["Secondary"].Components.Keys.ShouldBe(["ZuluTwo"]);
        definition.Workflows["Secondary"].Components.ContainsKey("Conflict").ShouldBeFalse();
    }

    [Fact]
    public void Direct_connect_returns_same_output_and_records_ordered_local_and_cross_workflow_fanout()
    {
        var application = new ApplicationDefinitionBuilder();
        application
            .AddWorkflow("Main", out var main)
            .AddWorkflow("Audit", out var audit);
        var sourceContract = ComponentContract.Create(
            "test.source",
            ConfigureRuntime,
            static component => new OutputComponentHandle<Order>(component, "Orders", "Events"));
        var sinkContract = ComponentContract.Create(
            "test.sink",
            ConfigureRuntime,
            static component => new InputComponentHandle<Order>(component, "Orders", "Events"));
        var auditContract = ComponentContract.Create(
            "test.audit",
            ConfigureRuntime,
            static component => new SignalComponentHandle(component));
        var source = main.AddComponent(
            "Source",
            sourceContract);
        var priority = main.AddComponent(
            "Priority",
            sinkContract);
        var standard = main.AddComponent(
            "Standard",
            sinkContract);
        var auditSink = audit.AddComponent(
            "Recorder",
            auditContract);

        var returned = source.Output
            .ConnectTo(priority.Input)
            .ConnectTo(standard.Input, "input.Priority == false")
            .ConnectTo(auditSink.Signal, static order => order.Priority);

        returned.ShouldBeSameAs(source.Output);
        var definition = application.Build();
        definition.Links.Count.ShouldBe(3);
        definition.Links.Select(static link => link.Source.Value)
            .ShouldAllBe(static source => source == "Main.Source.Orders");
        definition.Links.Select(static link => link.Target.Value).ShouldBe(
        [
            "Main.Priority.Orders",
            "Main.Standard.Orders",
            "Audit.Recorder.Signal"
        ]);
        definition.Links[0].IsConditional.ShouldBeFalse();
        definition.Links[1].ConditionExpression.ShouldBe("input.Priority == false");
        definition.Links[2].ConditionExpression.ShouldBeNull();
        definition.Links[2].IsConditional.ShouldBeTrue();
        definition.Links.ShouldAllBe(static link =>
            link.MessageType == typeof(Order) &&
            link.DeclarationSide == ApplicationLinkDeclarationSide.Output);
    }

    [Fact]
    public void Connection_entry_points_share_scope_owner_duplicate_and_cardinality_rules()
    {
        var application = new ApplicationDefinitionBuilder();
        application
            .AddWorkflow("Main", out var main)
            .AddWorkflow("Audit", out var audit);
        var source = main.AddComponent("Source", "test.source");
        var local = main.AddComponent("Local", "test.sink");
        var remote = audit.AddComponent("Remote", "test.sink");
        var sourceOutput = source.Output<int>(
            "Value",
            ComponentPortLinkCardinality.Single);
        var localInput = local.Input<int>("Value");
        var remoteInput = remote.Input<int>("Value");

        var wrongScope = Should.Throw<InvalidOperationException>(() =>
            main.Connect(sourceOutput, remoteInput));
        wrongScope.Message.ShouldContain("Audit");
        application.Connect(sourceOutput, remoteInput).ShouldBeSameAs(application);

        var cardinality = Should.Throw<InvalidOperationException>(() =>
            sourceOutput.ConnectTo(localInput));
        cardinality.Message.ShouldContain("Main.Source.Value");
        cardinality.Message.ShouldContain("only one connection");

        var foreignApplication = new ApplicationDefinitionBuilder();
        var foreign = foreignApplication.AddWorkflow("Foreign");
        var foreignInput = foreign.AddComponent("Sink", "test.sink").Input<int>("Value");
        var ownership = Should.Throw<InvalidOperationException>(() =>
            application.Connect(sourceOutput, foreignInput));
        ownership.Message.ShouldContain("different application definition builder");

        var definition = application.Build();
        definition.Links.ShouldHaveSingleItem();
        definition.Links[0].Target.ShouldBe(remoteInput.Address);
        Should.Throw<InvalidOperationException>(() =>
            application.Connect(sourceOutput, remoteInput));
    }

    [Fact]
    public void Null_conditions_fail_before_connection_mutation_and_preserve_parameter_names()
    {
        var application = new ApplicationDefinitionBuilder();
        var workflow = application.AddWorkflow("Main");
        var source = workflow.AddComponent("Source", "test.source");
        var sink = workflow.AddComponent("Sink", "test.sink");
        var output = source.Output<string?>("Value");
        var input = sink.Input<string?>("Value");

        var nullString = Should.Throw<ArgumentException>(() =>
            output.ConnectTo(input, (string)null!));
        nullString.ParamName.ShouldBe("condition");
        var blankString = Should.Throw<ArgumentException>(() =>
            workflow.Connect(output, input, "  "));
        blankString.ParamName.ShouldBe("condition");
        var nullPredicate = Should.Throw<ArgumentNullException>(() =>
            application.Connect(output, input, (Func<string?, bool>)null!));
        nullPredicate.ParamName.ShouldBe("when");

        output.ConnectTo(input).ShouldBeSameAs(output);
        var definition = application.Build();
        definition.Links.ShouldHaveSingleItem();
        definition.Links[0].IsConditional.ShouldBeFalse();
    }

    private sealed class RouterHandle : AuthoredComponentHandle
    {
        public RouterHandle(ComponentHandle definition)
            : base(definition)
        {
            Input = definition.Input<Order>("Orders");
            Approved = definition.Output<Order>("Approved");
            Rejected = definition.Output<Order>("Rejected");
            Refresh = definition.SignalInput("Refresh");
            Events = definition.Output<ComponentEvent>("Events");
        }

        public InputPortHandle<Order> Input { get; }

        public OutputPortHandle<Order> Approved { get; }

        public OutputPortHandle<Order> Rejected { get; }

        public SignalInputPortHandle Refresh { get; }

        public OutputPortHandle<ComponentEvent> Events { get; }
    }

    private sealed class SignalComponentHandle : AuthoredComponentHandle
    {
        public SignalComponentHandle(ComponentHandle definition)
            : base(definition)
        {
            Signal = definition.SignalInput("Signal");
            Events = definition.Output<ComponentEvent>("Events");
        }

        public SignalInputPortHandle Signal { get; }

        public OutputPortHandle<ComponentEvent> Events { get; }
    }

    private sealed class RouterOptions
    {
        public int Minimum { get; set; }

        public string? Label { get; set; }
    }

    private sealed record Order(bool Priority = false);

    private static void ConfigureRuntime(RuntimeComponentRegistrationBuilder component)
        => ConfigureRuntime(component, static _ => new AuthoringNode());

    private static void ConfigureRuntime(
        RuntimeComponentRegistrationBuilder component,
        Func<ComponentActivationContext, AuthoringNode> factory)
    {
        component.UseProcessing(CompositionProcessingCapabilities.ParallelRelaxedOrder);
        component
            .UseFactory(factory)
            .HasInput("Orders", static node => node.OrdersInput)
            .HasSignalInput("Refresh", static node => node.Refresh)
            .HasSignalInput("Signal", static node => node.Signal)
            .HasOutput("Approved", static node => node.Approved)
            .HasOutput("Rejected", static node => node.Rejected)
            .HasOutput("Orders", static node => node.OrdersOutput)
            .HasEvents("Events", static node => node.Events);
        component.AddOption<int>("minimum", isRequired: true);
        component.AddResource<TimeProvider>("clock", isRequired: true);
    }

    private sealed class AuthoringNode : IFlowNode
    {
        public BufferBlock<FlowMessage<Order>> OrdersInput { get; } = new();

        public RecordingSignalTarget Refresh { get; } = new();

        public RecordingSignalTarget Signal { get; } = new();

        public BufferBlock<FlowMessage<Order>> Approved { get; } = new();

        public BufferBlock<FlowMessage<Order>> Rejected { get; } = new();

        public BufferBlock<FlowMessage<Order>> OrdersOutput { get; } = new();

        public BufferBlock<FlowEvent> Events { get; } = new();

        public Task Completion => Task.CompletedTask;

        public void Complete()
        {
            OrdersInput.Complete();
            Refresh.Complete();
            Signal.Complete();
            Approved.Complete();
            Rejected.Complete();
            OrdersOutput.Complete();
            Events.Complete();
        }

        public void Fault(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            ((IDataflowBlock)OrdersInput).Fault(exception);
            ((IDataflowBlock)Approved).Fault(exception);
            ((IDataflowBlock)Rejected).Fault(exception);
            ((IDataflowBlock)OrdersOutput).Fault(exception);
            ((IDataflowBlock)Events).Fault(exception);
        }

        public ValueTask DisposeAsync()
        {
            Complete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSignalTarget : IFlowSignalTarget
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public ValueTask<bool> SendAsync<T>(
            FlowMessage<T> signal,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public void Complete() => _completion.TrySetResult();
    }

    private sealed class DistinctiveException(string message) : Exception(message);
}
