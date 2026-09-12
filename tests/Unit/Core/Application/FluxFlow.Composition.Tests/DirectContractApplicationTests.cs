using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Authoring;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Json.Model;
using FluxFlow.Json;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class DirectContractApplicationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Contract_graph_executes_with_owned_resources_borrowed_host_and_native_ports(bool embedded)
    {
        var log = new ConcurrentQueue<string>();
        var registrar = new ResourceRegistrar(log);
        var constructions = 0;
        ProbeNode? node = null;
        HostResource? host = null;
        var descriptor = new ComponentDescriptor("test.process", async context =>
        {
            await Task.Yield();
            constructions++;
            host = context.Services.GetRequiredService<HostResource>();
            var resource = context.GetRequiredResource<OwnedResource>("Store");
            var options = context.BindConfiguration<ProbeOptions>();
            node = new ProbeNode(message => message.With(FlowValue.From(resource.Prefix + options.Label + message.Value!.Deserialize<string>())),
                () => log.Enqueue("node"));
            return Instance(node, () => { log.Enqueue("activation"); return ValueTask.CompletedTask; });
        }, inputs: [new("Input", typeof(FlowValue))], outputs: [new("Output", typeof(FlowValue))],
            options: [new("Label", typeof(string), true)],
            resources: [new("Store", typeof(OwnedResource), true), new("Optional", typeof(OwnedResource))]);
        var definition = Definition(("Processor", new ComponentDefinition(descriptor.Type,
            Properties(("Store", "store"), ("Label", "configured:"), ("Optional", null!)))));
        var services = new ServiceCollection();
        services.AddSingleton(_ => new HostResource(log));
        services.AddSingleton<IApplicationResourceRegistrar>(registrar);
        if (!embedded) services.AddSingleton(new ComponentCatalog([descriptor]));
        await using var provider = services.BuildServiceProvider();
        var application = embedded
            ? await new ApplicationSetup(definition, componentDescriptors: [descriptor],
                resourceContracts: [ApplicationResourceContract.Create("test.resource", registrar, resource => new ResourceView(resource))])
                .BuildApplicationAsync(provider)
            : await ApplicationDefinitionJson.Deserialize(ApplicationDefinitionJson.Serialize(definition))
                .BuildApplicationAsync(provider);
        await using (application)
        {
            constructions.ShouldBe(1);
            registrar.Registrations.ShouldBe(1);
            registrar.Created.ShouldBe(1);
            log.ShouldBeEmpty();
            host.ShouldBeSameAs(provider.GetRequiredService<HostResource>());
            application.GetInput("Main.Processor.Input").Block.ShouldBeSameAs(node!.Input);
            application.GetOutput("Main.Processor.Output").Block.ShouldBeSameAs(node.Output);
            application.RepliesFrom(application.GetOutput("Main.Processor.Output"));
            await application.StartAsync();
            var reply = await application.RequestAsync(application.GetInput("Main.Processor.Input"),
                application.GetOutput("Main.Processor.Output"), FlowMessage.Create(FlowValue.From("value")), Timeout);
            reply.Value!.Deserialize<string>().ShouldBe("prefix:configured:value");
            application.PendingRequestCount.ShouldBe(0);
            await application.StopAsync().WaitAsync(Timeout);
            log.ShouldBeEmpty();
        }
        await application.DisposeAsync();
        log.ToArray().ShouldBe(["node", "activation", "resource"]);
        registrar.Disposed.ShouldBe(1);
        host!.Disposed.ShouldBeFalse();
        await provider.DisposeAsync();
        host.Disposed.ShouldBeTrue();
        log.Last().ShouldBe("host");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Typed_predicates_use_canonical_messages_without_an_expression_engine(bool useConstructorBindings)
    {
        var receivedLow = new ConcurrentQueue<FlowMessage>();
        var receivedHigh = new ConcurrentQueue<FlowMessage>();
        var sourceNode = new ProbeNode();
        var lowNode = new SinkNode(receivedLow.Enqueue);
        var highNode = new SinkNode(receivedHigh.Enqueue);
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("Main");
        var source = workflow.AddComponent("Source", "test.source");
        var low = workflow.AddComponent("Low", "test.low");
        var high = workflow.AddComponent("High", "test.high");
        source.Output<int>("Output").LinkTo(low.Input<int>("Input"), value => value <= 5)
            .LinkTo(high.Input<int>("Input"), value => value > 5);
        var services = new ServiceCollection();
        services.AddSingleton(new ComponentCatalog([
            Descriptor("test.source", _ => ValueTask.FromResult(Instance(sourceNode))),
            SinkDescriptor("test.low", lowNode), SinkDescriptor("test.high", highNode)]));
        await using var provider = services.BuildServiceProvider();
        var bindings = new ComponentBindings();
        bindings.Add("test.source", _ => sourceNode);
        bindings.Add("test.low", _ => lowNode);
        bindings.Add("test.high", _ => highNode);
        await using var application = useConstructorBindings
            ? await builder.BuildApplicationAsync(bindings)
            : await builder.BuildApplicationAsync(provider);
        await application.StartAsync();
        var first = FlowMessage.Create(FlowValue.From(5));
        var second = FlowMessage.Create(FlowValue.From(6));
        (await application.SendAsync(application.GetInput("Main.Source.Input"), first)).ShouldBeTrue();
        (await application.SendAsync(application.GetInput("Main.Source.Input"), second)).ShouldBeTrue();
        await application.StopAsync().WaitAsync(Timeout);
        receivedLow.ShouldHaveSingleItem().ShouldBeSameAs(first);
        receivedHigh.ShouldHaveSingleItem().ShouldBeSameAs(second);
        sourceNode.Processed.ShouldBe(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Source_views_preserve_start_production_drain_and_renamed_controls(bool start)
    {
        var source = new ControlledSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downstream = new ProbeNode(async message =>
        {
            entered.TrySetResult();
            await release.Task;
            return message;
        });
        var sourceDescriptor = new ComponentDescriptor("test.source", _ => ValueTask.FromResult(
            new ComponentInstance(source, [ComponentPorts.SignalInput("Terminal", source.Control)],
                [ComponentPorts.Output("Output", source.Output)])),
            inputs: [new("Terminal", typeof(object), ComponentPortLinkCardinality.Multiple, ComponentPortKind.Signal)],
            outputs: [new("Output", typeof(FlowValue))]);
        var definition = Definition(
            ("Source", new ComponentDefinition("test.source", Properties(("Output", "Worker.Input")))),
            ("Worker", new ComponentDefinition("test.worker", Properties(("Output", "Source.Terminal")))));
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var application = await new ApplicationSetup(definition, componentDescriptors:
            [sourceDescriptor, Descriptor("test.worker", _ => ValueTask.FromResult(Instance(downstream)))])
            .BuildApplicationAsync(provider);
        application.GetInput("Main.Source.Terminal").IsControl.ShouldBeTrue();
        application.GetInput("Main.Source.Terminal").Block.ShouldBeSameAs(source.Control);
        application.GetInput("Main.Source.Terminal").Owner.Instance.ShouldBeAssignableTo<IFlowSource>();
        if (start)
        {
            await application.StartAsync();
            await entered.Task.WaitAsync(Timeout);
            var stopping = application.StopAsync();
            await source.ProductionCompletion.WaitAsync(Timeout);
            stopping.IsCompleted.ShouldBeFalse();
            source.Control.Completion.IsCompleted.ShouldBeFalse();
            release.TrySetResult();
            await stopping.WaitAsync(Timeout);
        }
        await application.DisposeAsync().AsTask().WaitAsync(Timeout);
        source.Starts.ShouldBe(start ? 1 : 0);
        source.Controls.ShouldBe(start ? 1 : 0);
        source.Disposals.ShouldBe(1);
        source.Control.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        source.ProductionCompletion.IsCompletedSuccessfully.ShouldBeTrue();
        downstream.Processed.ShouldBe(start ? 1 : 0);
    }

    [Fact]
    public async Task Native_node_failure_cannot_hide_behind_additional_completion()
    {
        var node = new ProbeNode();
        var peerFault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = new SinkNode(_ => { }, fault: error => peerFault.TrySetResult(error));
        var extra = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var descriptor = Descriptor("test.source", _ => ValueTask.FromResult(new ComponentInstance(node,
            [ComponentPorts.Input("Input", node.Input)], [ComponentPorts.Output("Output", node.Output)], completion: extra.Task)));
        var definition = Definition(("Source", new ComponentDefinition("test.source", Properties(("Output", "Sink.Input")))),
            ("Sink", new ComponentDefinition("test.sink")));
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var app = await new ApplicationSetup(definition, componentDescriptors: [descriptor, SinkDescriptor("test.sink", peer)])
            .BuildApplicationAsync(provider);
        await app.StartAsync();
        var failure = new InvalidOperationException("native failure");
        node.Fault(failure);
        (await peerFault.Task.WaitAsync(Timeout)).ShouldBeSameAs(failure);
        app.Completion.IsCompleted.ShouldBeFalse();
        extra.TrySetResult();
        var observed = await Should.ThrowAsync<Exception>(async () => await app.Completion.WaitAsync(Timeout));
        observed.ShouldBeSameAs(failure);
        var shutdown = await Should.ThrowAsync<AggregateException>(async () => await app.DisposeAsync().AsTask().WaitAsync(Timeout));
        shutdown.Flatten().InnerExceptions.ShouldContain(failure);
        peer.Disposals.ShouldBe(1);
    }

    [Theory]
    [InlineData("unknown", "unknown type")]
    [InlineData("port", "Missing")]
    [InlineData("physical", "FlowValue")]
    [InlineData("physical-input", "FlowValue")]
    [InlineData("option", "Limit")]
    [InlineData("option-null", "Limit")]
    [InlineData("resource", "Store")]
    [InlineData("key", "non-empty key")]
    [InlineData("key-whitespace", "non-empty key")]
    [InlineData("isolated", "rejection handler")]
    public async Task Invalid_metadata_is_rejected_before_registration_or_activation(string invalidity, string expected)
    {
        var calls = 0;
        var registrar = new ResourceRegistrar(new());
        var descriptor = new ComponentDescriptor("test.node", _ =>
        {
            calls++;
            return ValueTask.FromResult(Instance(new ProbeNode()));
        }, inputs: [new("Input", invalidity == "physical-input" ? typeof(int) : typeof(FlowValue))],
            outputs: [new("Output", invalidity == "physical" ? typeof(int) : typeof(FlowValue))],
            options: invalidity is "option" or "option-null" ? [new("Limit", typeof(int), true)] : [],
            resources: invalidity is "resource" or "key" or "key-whitespace" ? [new("Store", typeof(OwnedResource), true)] : []);
        var properties = invalidity == "port" ? Properties(("Output", "Node.Missing"))
            : invalidity == "key" ? Properties(("Store", 42))
            : invalidity == "key-whitespace" ? Properties(("Store", "  "))
            : invalidity == "option-null" ? Properties(("Limit", null!))
            : invalidity == "isolated" ? Properties(("Output", new { Port = "Sink.Input", Delivery = "IsolateRejection" }))
            : Properties();
        var definition = Definition(("Node", new ComponentDefinition(invalidity == "unknown" ? "missing" : "test.node", properties)),
            ("Sink", new ComponentDefinition("test.node", Properties(("Store", "store"), ("Limit", 8)))));
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationResourceRegistrar>(registrar);
        await using var provider = services.BuildServiceProvider();
        var failure = await Should.ThrowAsync<Exception>(() => new ApplicationSetup(definition, componentDescriptors: [descriptor])
            .BuildApplicationAsync(provider));
        failure.ToString().ShouldContain(expected);
        calls.ShouldBe(0);
        registrar.Registrations.ShouldBe(0);
        registrar.Created.ShouldBe(0);
    }

    [Theory]
    [InlineData("throw", false)]
    [InlineData("null", false)]
    [InlineData("ports", false)]
    [InlineData("cancel", false)]
    [InlineData("throw", true)]
    public async Task Failed_activation_disposes_components_before_resources_and_retains_failures(string failureMode, bool cleanupFails)
    {
        var log = new ConcurrentQueue<string>();
        var activationError = new InvalidOperationException("activation failure");
        var nodeError = new InvalidOperationException("node cleanup failure");
        var resourceError = new InvalidOperationException("resource cleanup failure");
        var registrar = new ResourceRegistrar(log, cleanupFails ? resourceError : null);
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var descriptor = Descriptor("test.node", async context =>
        {
            calls++;
            if (calls == 1)
            {
                context.Services.GetRequiredKeyedService<OwnedResource>("store");
                return Instance(new ProbeNode(dispose: () =>
                {
                    log.Enqueue("first");
                    if (cleanupFails) throw nodeError;
                }));
            }
            await Task.Yield();
            if (failureMode == "throw") throw activationError;
            if (failureMode == "null") return null!;
            var node = new ProbeNode(dispose: () => log.Enqueue("second"));
            if (failureMode == "ports") return new ComponentInstance(node);
            cancellation.Cancel();
            return Instance(node);
        });
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationResourceRegistrar>(registrar);
        await using var provider = services.BuildServiceProvider();
        var definition = Definition(("First", new ComponentDefinition(descriptor.Type)), ("Second", new ComponentDefinition(descriptor.Type)));
        var failure = await Should.ThrowAsync<Exception>(() => new ApplicationSetup(definition, componentDescriptors: [descriptor])
            .BuildApplicationAsync(provider, cancellationToken: cancellation.Token));
        failure.ShouldNotBeOfType<TimeoutException>();
        calls.ShouldBe(2);
        registrar.Disposed.ShouldBe(1);
        log.ToArray().ShouldBe(failureMode is "ports" or "cancel" ? ["second", "first", "resource"] : ["first", "resource"]);
        if (failureMode == "throw") AllErrors(failure).ShouldContain(activationError);
        if (failureMode == "null") failure.Message.ShouldContain("returned null");
        if (failureMode == "ports") failure.Message.ShouldContain("ports");
        if (failureMode == "cancel") failure.ShouldBeAssignableTo<OperationCanceledException>()!.CancellationToken.ShouldBe(cancellation.Token);
        if (cleanupFails)
        {
            AllErrors(failure).ShouldContain(nodeError);
            AllErrors(failure).ShouldContain(resourceError);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Owned_native_nodes_and_views_cannot_be_shared_between_graphs(bool directFirst)
    {
        var node = new ProbeNode();
        var instance = Instance(node);
        var descriptor = Descriptor("test.node", _ => ValueTask.FromResult(instance));
        var setup = new ApplicationSetup(Definition(("Node", new ComponentDefinition(descriptor.Type))), componentDescriptors: [descriptor]);
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var owner = directFirst ? new FlowApplication() : await setup.BuildApplicationAsync(provider);
        await using (owner)
        {
            if (directFirst) owner.AddWorkflow("Main").Add("Node", node);
            var failure = await Should.ThrowAsync<InvalidOperationException>(() => setup.BuildApplicationAsync(provider));
            failure.Message.ShouldContain("cannot be added twice");
            await using var other = new FlowApplication();
            Should.Throw<ArgumentException>(() => other.AddWorkflow("Other").Add("Node", node));
            var view = owner.GetInput("Main.Node.Input").Owner.Instance;
            Should.Throw<ArgumentException>(() => other.AddWorkflow("View").Add("Node", view));
            node.Disposals.ShouldBe(0);
            node.Completion.IsCompleted.ShouldBeFalse();
            owner.GetOutput("Main.Node.Output").DiscardUnmatched();
            await owner.StartAsync();
            (await owner.SendAsync(owner.GetInput("Main.Node.Input"), FlowMessage.Create(FlowValue.From(7)))).ShouldBeTrue();
            await owner.StopAsync().WaitAsync(Timeout);
            node.Processed.ShouldBe(1);
        }
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Named_engines_and_named_event_outputs_preserve_parameters_and_isolated_rejections()
    {
        var events = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 8 });
        var received = new ConcurrentQueue<FlowMessage>();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejected = new ConcurrentQueue<(string Source, string Target, FlowMessage Message, Exception Error)>();
        var engine = new RecordingEngine();
        var unused = new RecordingEngine();
        var node = new ProbeNode(message => { events.Post(message).ShouldBeTrue(); return message; });
        var sink = new SinkNode(message => { received.Enqueue(message); if (received.Count == 2) delivered.TrySetResult(); });
        var source = new ComponentDescriptor("test.events", _ => ValueTask.FromResult(new ComponentInstance(node,
            [ComponentPorts.Input("Input", node.Input)], [ComponentPorts.Output("Output", node.Output)],
            addressableEvents: [new("Audit", events)])), inputs: [new("Input", typeof(FlowValue))],
            outputs: [new("Output", typeof(FlowValue)), new("Audit", typeof(FlowValue))]);
        var document = Definition(
            ("Source", new ComponentDefinition(source.Type, Properties(("Audit", new
            {
                Port = "Sink.Input", Condition = "above threshold", Parameters = new { threshold = 5 }, Delivery = "IsolateRejection"
            })))), ("Sink", new ComponentDefinition("test.sink")));
        document = new ApplicationDefinition(document.Resources, document.Workflows, expressionEngine: "chosen");
        var services = new ServiceCollection();
        services.AddSingleton<IFlowExpressionEngine>(unused);
        services.AddKeyedSingleton<IFlowExpressionEngine>("chosen", engine);
        await using var provider = services.BuildServiceProvider();
        await using var app = await new ApplicationSetup(document, componentDescriptors: [source, SinkDescriptor("test.sink", sink)])
            .BuildApplicationAsync(provider, (from, to, message, error) => rejected.Enqueue((from, to, message, error)));
        app.GetOutput("Main.Source.Output").DiscardUnmatched();
        app.GetOutput("Main.Source.Audit").DiscardUnmatched();
        await app.StartAsync();
        var messages = Enumerable.Range(5, 4).Select(value => FlowMessage.Create(FlowValue.From(value))).ToArray();
        foreach (var message in messages)
            (await app.SendAsync(app.GetInput("Main.Source.Input"), message)).ShouldBeTrue();
        await delivered.Task.WaitAsync(Timeout);
        await app.StopAsync().WaitAsync(Timeout);
        engine.Compilations.ShouldBe(1);
        engine.Evaluations.ShouldBe(4);
        unused.Compilations.ShouldBe(0);
        unused.Evaluations.ShouldBe(0);
        var accepted = received.ToArray();
        accepted.Select(message => message.Value!.Deserialize<int>()).ShouldBe([6, 8]);
        accepted[0].Value.ShouldBeSameAs(messages[1].Value);
        accepted[1].Value.ShouldBeSameAs(messages[3].Value);
        accepted[0].MessageId.ShouldBe(messages[1].MessageId);
        accepted[0].Headers[FlowEventHeaders.Source].ShouldBe("Main.Source");
        var refusal = rejected.ShouldHaveSingleItem();
        refusal.Source.ShouldBe("Main.Source.Audit");
        refusal.Target.ShouldBe("Main.Sink.Input");
        refusal.Message.Value.ShouldBeSameAs(messages[2].Value);
        refusal.Error.ShouldBeSameAs(engine.Failure);
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        events.Complete();
        await events.Completion.WaitAsync(Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_start_failure_disposes_the_graph_and_resources_once_with_all_failures(bool cleanupFails)
    {
        var log = new ConcurrentQueue<string>();
        var startFailure = new InvalidOperationException("start failed");
        var cleanupFailure = new InvalidOperationException("resource cleanup failed");
        var registrar = new ResourceRegistrar(log, cleanupFails ? cleanupFailure : null);
        var source = new ControlledSource { StartFailure = startFailure };
        var descriptor = new ComponentDescriptor("test.source", context =>
        {
            context.Services.GetRequiredKeyedService<OwnedResource>("store");
            return ValueTask.FromResult(new ComponentInstance(source,
                [ComponentPorts.SignalInput("Terminal", source.Control)], [ComponentPorts.Output("Output", source.Output)]));
        }, inputs: [new("Terminal", typeof(object), ComponentPortLinkCardinality.Multiple, ComponentPortKind.Signal)],
            outputs: [new("Output", typeof(FlowValue))]);
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationResourceRegistrar>(registrar);
        await using var provider = services.BuildServiceProvider();
        var app = await new ApplicationSetup(Definition(("Source", new ComponentDefinition(descriptor.Type))),
            componentDescriptors: [descriptor]).BuildApplicationAsync(provider);
        app.GetOutput("Main.Source.Output").DiscardUnmatched();
        var failure = await Should.ThrowAsync<Exception>(() => app.StartAsync().WaitAsync(Timeout));
        AllErrors(failure).ShouldContain(startFailure);
        if (cleanupFails) AllErrors(failure).ShouldContain(cleanupFailure);
        source.Starts.ShouldBe(1);
        source.Disposals.ShouldBe(1);
        source.Control.Completion.IsFaulted.ShouldBeTrue();
        registrar.Created.ShouldBe(1);
        registrar.Disposed.ShouldBe(1);
        await Should.ThrowAsync<AggregateException>(async () => await app.DisposeAsync());
        source.Disposals.ShouldBe(1);
        registrar.Disposed.ShouldBe(1);
    }

    [Fact]
    public async Task Build_guards_and_pre_cancel_do_not_register_or_activate()
    {
        var calls = 0;
        var registrar = new ResourceRegistrar(new());
        var descriptor = Descriptor("test.node", _ => { calls++; return ValueTask.FromResult(Instance(new ProbeNode())); });
        var setup = new ApplicationSetup(Definition(("Node", new ComponentDefinition(descriptor.Type))), componentDescriptors: [descriptor]);
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationResourceRegistrar>(registrar);
        await using var provider = services.BuildServiceProvider();
        await Should.ThrowAsync<ArgumentNullException>(() => setup.BuildApplicationAsync((IServiceProvider)null!));
        await Should.ThrowAsync<ArgumentNullException>(() => ((ApplicationSetup)null!).BuildApplicationAsync(provider));
        Should.Throw<ArgumentNullException>(() => ((ApplicationDefinitionBuilder)null!).BuildApplicationAsync(provider));
        Should.Throw<ArgumentNullException>(() => ((ApplicationDefinition)null!).BuildApplicationAsync(provider));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var registrations = new ServiceCollection();
        var context = new ApplicationResourceRegistrationContext(setup.Definition, 1, "application", provider, registrations);
        Should.Throw<ArgumentNullException>(() => ((ApplicationSetup)null!).RegisterResources(context));
        Should.Throw<ArgumentNullException>(() => setup.RegisterResources(null!));
        Should.Throw<ArgumentException>(() => new ApplicationSetup(new ApplicationDefinition()).RegisterResources(context));
        Should.Throw<OperationCanceledException>(() => setup.RegisterResources(context, cancellationToken: cancellation.Token));
        registrations.ShouldBeEmpty();
        var failure = await Should.ThrowAsync<OperationCanceledException>(() => setup.BuildApplicationAsync(provider,
            cancellationToken: cancellation.Token));
        failure.CancellationToken.ShouldBe(cancellation.Token);
        calls.ShouldBe(0);
        registrar.Registrations.ShouldBe(0);
        registrar.Created.ShouldBe(0);
        await using var emptyProvider = new ServiceCollection().BuildServiceProvider();
        var empty = await new ApplicationDefinitionBuilder().BuildApplicationAsync(emptyProvider);
        await empty.StartAsync();
        await empty.Completion.WaitAsync(Timeout);
        await empty.DisposeAsync();
    }

    [Fact]
    public void Canonical_predicate_materialization_handles_null_errors_and_invalid_values()
    {
        var calls = 0;
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("Main");
        var source = workflow.AddComponent("Source", "test.node");
        var sink = workflow.AddComponent("Sink", "test.node");
        source.Output<string?>("Output").LinkTo(sink.Input<string?>("Input"), value => { calls++; return value is null; });
        var descriptor = Descriptor("test.node", _ => throw new InvalidOperationException("Compilation must not activate."));
        var result = new Links.ApplicationLinkCompiler(new([descriptor])).Compile(builder.Build());
        result.Diagnostics.ShouldBeEmpty();
        var link = result.Links.ShouldHaveSingleItem();
        link.MessageType.ShouldBe(typeof(string));
        link.TryMatch(Context(FlowMessage.Create(FlowValue.Null)), out var failure).ShouldBeTrue();
        failure.ShouldBeNull();
        calls.ShouldBe(1);
        link.TryMatch(Context(FlowMessage.Create(FlowValue.From("text"))), out failure).ShouldBeFalse();
        failure.ShouldBeNull();
        link.TryMatch(Context(FlowMessage.CreateError(new FlowError("test", "failed", "testing"))), out failure).ShouldBeFalse();
        failure.ShouldBeNull();
        calls.ShouldBe(2);
        link.TryMatch(Context(FlowMessage.Create(FlowValue.From(new { Value = 3 }))), out failure).ShouldBeFalse();
        failure.ShouldBeOfType<JsonException>();
        calls.ShouldBe(2);
        link.TryMatch(Context(FlowMessage.Create(FlowValue.Null)), out failure).ShouldBeTrue();
        failure.ShouldBeNull();
        calls.ShouldBe(3);

        static FlowMapContext Context(FlowMessage message) => new() { Variables = new Dictionary<string, object?> { ["message"] = message } };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_factory_results_are_rejected_without_disposing_the_first_activation(bool parallel)
    {
        var node = new ProbeNode();
        var instance = Instance(node);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var descriptor = Descriptor("test.node", async _ =>
        {
            if (Interlocked.Increment(ref calls) == 2) entered.TrySetResult();
            if (parallel) await release.Task;
            return instance;
        });
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var first = CaptureAsync();
        var second = CaptureAsync();
        await entered.Task.WaitAsync(Timeout);
        release.TrySetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(Timeout);
        results.Where(result => result.Instance is not null).ShouldHaveSingleItem().Instance.ShouldBeSameAs(instance);
        results.Where(result => result.Error is not null).ShouldHaveSingleItem().Error
            .ShouldBeOfType<InvalidOperationException>().Message.ShouldContain("already attached");
        calls.ShouldBe(2);
        node.Disposals.ShouldBe(0);
        node.Completion.IsCompleted.ShouldBeFalse();
        instance.Inputs["Input"].Target.ShouldBeSameAs(node.Input);
        await instance.DisposeAsync().AsTask().WaitAsync(Timeout);
        node.Disposals.ShouldBe(1);

        async Task<(ComponentInstance? Instance, Exception? Error)> CaptureAsync()
        {
            try { return (await descriptor.Factory(new ComponentActivationContext(provider, "Main", "Node", new ComponentDefinition(descriptor.Type))), null); }
            catch (Exception error) { return (null, error); }
        }
    }

    private sealed record ProbeOptions(string Label);

    private static ComponentDescriptor Descriptor(string type, ComponentFactory factory) => new(type, factory,
        inputs: [new("Input", typeof(FlowValue))], outputs: [new("Output", typeof(FlowValue))]);

    private static ComponentDescriptor SinkDescriptor(string type, SinkNode node) => new(type,
        _ => ValueTask.FromResult(new ComponentInstance(node, [ComponentPorts.Input("Input", node.Input)])),
        inputs: [new("Input", typeof(FlowValue))]);

    private static ComponentInstance Instance(ProbeNode node, Func<ValueTask>? dispose = null) => new(node,
        [ComponentPorts.Input("Input", node.Input)], [ComponentPorts.Output("Output", node.Output)], disposeAsync: dispose);

    private static Dictionary<string, JsonElement> Properties(params (string Name, object Value)[] values)
        => values.ToDictionary(pair => pair.Name, pair => JsonSerializer.SerializeToElement(pair.Value));

    private static ApplicationDefinition Definition(params (string Name, ComponentDefinition Component)[] components)
        => new(resources: new Dictionary<string, ResourceDefinition> { ["store"] = new ResourceInstanceDefinition("test.resource") },
            workflows: new Dictionary<string, WorkflowDefinition> { ["Main"] = new(components.ToDictionary(pair => pair.Name, pair => pair.Component)) });

    private static IEnumerable<Exception> AllErrors(Exception error)
        => new[] { error }.Concat(error is AggregateException aggregate ? aggregate.InnerExceptions.SelectMany(AllErrors)
            : error.InnerException is { } inner ? AllErrors(inner) : []);

    private sealed class ResourceView(ResourceHandle handle) : AuthoredResourceHandle(handle);

    private sealed class RecordingEngine : IFlowExpressionEngine
    {
        public string Name => "recording";
        public int Compilations;
        public int Evaluations;
        public readonly Exception Failure = new InvalidOperationException("condition failed");
        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => throw new InvalidOperationException("The compiled condition must be reused.");
        public IFlowCompiledExpression<T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> types)
        {
            Compilations++;
            expression.ShouldBe("above threshold");
            types["input"].ShouldBe(typeof(FlowValue));
            types["message"].ShouldBe(typeof(FlowMessage));
            types.ContainsKey("threshold").ShouldBeTrue();
            return new Compiled<T>(context =>
            {
                Evaluations++;
                var message = context.Variables["message"].ShouldBeOfType<FlowMessage>();
                context.Variables["input"].ShouldBeSameAs(message.Value);
                context.Variables["payload"].ShouldBeSameAs(message.Value);
                var value = message.Value!.Deserialize<int>();
                if (value == 7) throw Failure;
                return (T)(object)(value > Convert.ToInt32(context.Variables["threshold"]));
            });
        }
        private sealed class Compiled<T>(Func<FlowMapContext, T> evaluate) : IFlowCompiledExpression<T>
        {
            public T Evaluate(FlowMapContext context) => evaluate(context);
        }
    }

    private sealed class ResourceRegistrar(ConcurrentQueue<string> log, Exception? failure = null) : IApplicationResourceRegistrar
    {
        public int Registrations;
        public int Created;
        public int Disposed;
        public void Register(ApplicationResourceRegistrationContext context)
        {
            Registrations++;
            context.Definition.Resources["store"].ShouldBeOfType<ResourceInstanceDefinition>().Type.ShouldBe("test.resource");
            context.Services.AddKeyedSingleton<OwnedResource>("store", (_, _) =>
            {
                Created++;
                return new OwnedResource(() => { Disposed++; log.Enqueue("resource"); if (failure is not null) throw failure; });
            });
        }
    }

    private sealed class OwnedResource(Action dispose) : IAsyncDisposable
    {
        public string Prefix => "prefix:";
        public ValueTask DisposeAsync() { dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class HostResource(ConcurrentQueue<string> log) : IAsyncDisposable
    {
        public bool Disposed;
        public ValueTask DisposeAsync() { Disposed = true; log.Enqueue("host"); return ValueTask.CompletedTask; }
    }

    private sealed class ProbeNode : IFlowComponent
    {
        private readonly TransformBlock<FlowMessage, FlowMessage> _block;
        private readonly Action? _dispose;
        public int Processed;
        public int Disposals;
        public ProbeNode(Func<FlowMessage, FlowMessage>? transform = null, Action? dispose = null)
            : this(message => Task.FromResult(transform?.Invoke(message) ?? message), dispose) { }
        public ProbeNode(Func<FlowMessage, Task<FlowMessage>> transform, Action? dispose = null)
        {
            _dispose = dispose;
            _block = new(async message => { Processed++; return await transform(message); },
                new ExecutionDataflowBlockOptions { BoundedCapacity = 8 });
            Ports = new(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _block },
                outputs: new Dictionary<string, ISourceBlock<FlowMessage>> { ["Output"] = _block });
        }
        public ITargetBlock<FlowMessage> Input => _block;
        public ISourceBlock<FlowMessage> Output => _block;
        public FlowPorts Ports { get; }
        public Task Completion => _block.Completion;
        public void Complete() => _block.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)_block).Fault(exception);
        public async ValueTask DisposeAsync()
        {
            Disposals++;
            Complete();
            try { await Completion; } finally { _dispose?.Invoke(); }
        }
    }

    private sealed class SinkNode : IFlowComponent
    {
        private readonly ActionBlock<FlowMessage> _block;
        private readonly Action<Exception>? _fault;
        public int Disposals;
        public SinkNode(Action<FlowMessage> receive, Action<Exception>? fault = null)
        {
            _fault = fault;
            _block = new(receive, new ExecutionDataflowBlockOptions { BoundedCapacity = 8 });
            Ports = new(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _block });
        }
        public ITargetBlock<FlowMessage> Input => _block;
        public FlowPorts Ports { get; }
        public Task Completion => _block.Completion;
        public void Complete() => _block.Complete();
        public void Fault(Exception exception) { _fault?.Invoke(exception); ((IDataflowBlock)_block).Fault(exception); }
        public async ValueTask DisposeAsync() { Disposals++; Complete(); await Completion; }
    }

    private sealed class ControlledSource : IFlowComponent, IFlowSource
    {
        public readonly BufferBlock<FlowMessage> Output = new(new DataflowBlockOptions { BoundedCapacity = 8 });
        public readonly ActionBlock<FlowMessage> Control;
        public int Starts;
        public Exception? StartFailure;
        public int Controls;
        public int Disposals;
        public ControlledSource()
        {
            Control = new(_ => Controls++, new ExecutionDataflowBlockOptions { BoundedCapacity = 8 });
            Ports = new(outputs: new Dictionary<string, ISourceBlock<FlowMessage>> { ["Output"] = Output },
                controls: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Reply"] = Control });
            Completion = Task.WhenAll(Output.Completion, Control.Completion);
        }
        public FlowPorts Ports { get; }
        public Task Completion { get; }
        public Task ProductionCompletion => Output.Completion;
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            Starts++;
            if (StartFailure is not null) throw StartFailure;
            (await Output.SendAsync(FlowMessage.Create(FlowValue.From(1)), cancellationToken)).ShouldBeTrue();
        }
        public void StopProduction() => Output.Complete();
        public void Complete() { Output.Complete(); Control.Complete(); }
        public void Fault(Exception error) { ((IDataflowBlock)Output).Fault(error); ((IDataflowBlock)Control).Fault(error); }
        public async ValueTask DisposeAsync() { Disposals++; Complete(); await Completion; }
    }
}
