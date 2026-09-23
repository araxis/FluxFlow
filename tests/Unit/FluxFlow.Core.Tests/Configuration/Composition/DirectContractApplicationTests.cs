using FluxFlow.Core.Addressing;
using FluxFlow.Core.Application;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class DirectContractApplicationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Configuration_strings_reach_factories_without_placeholder_expansion()
    {
        var prefix = "${Prefix}";
        var label = "${Label}";
        var services = new ServiceCollection();
        services.AddKeyedSingleton("${Prefix}", "configured:");
        RegisterProbe(services, "Main.n", host => new ProbeNode(message => message.WithValue(
            (FlowValue)(host.GetRequiredFlowResource<string>(prefix, "Main") + label))));
        var received = OutputProbe.Register(services, "Main.n.Output");
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        await app.GetTestInput("Main.n.Input").SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        (await received.Messages.ReceiveAsync(Timeout)).Value.Json.GetString().ShouldBe("configured:${Label}");
    }

    [Fact]
    public async Task DI_build_preserves_resource_contracts_host_ownership_and_native_identity()
    {
        var log = new ConcurrentQueue<string>();
        var registrar = new ResourceRegistrar(log);
        var services = new ServiceCollection();
        services.AddSingleton(_ => new HostResource(log));
        RegisterResources(services, registrar);
        ProbeNode? node = null;
        HostResource? host = null;
        RegisterProbe(services, "Main.Processor", dependencies =>
        {
            host = dependencies.GetRequiredService<HostResource>();
            var resource = dependencies.GetRequiredFlowResource<OwnedResource>("store", "Main");
            return node = new ProbeNode(message => message.WithValue((FlowValue)(resource.Prefix + message.Value.Json.GetString())),
                () => log.Enqueue("node"));
        });
        registrar.Created.ShouldBe(0);
        var received = OutputProbe.Register(services, "Main.Processor.Output");
        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var app = await root.BuildApplicationAsync();
        await using (app)
        {
            app.GetComponent("Main.Processor").ShouldBeSameAs(node);
            app.GetTestInput("Main.Processor.Input").ShouldBeSameAs(node!.Input);
            app.GetOutput("Main.Processor.Output").Block.ShouldBeSameAs(node.Output);
            host.ShouldBeSameAs(root.GetRequiredService<HostResource>());
            await app.StartAsync();
            await app.GetTestInput("Main.Processor.Input").SendAsync(FlowMessage.Create((FlowValue)"value"));
            (await received.Messages.ReceiveAsync(Timeout)).Value.Json.GetString().ShouldBe("prefix:value");
            await app.StopAsync().WaitAsync(Timeout);
            log.ShouldBeEmpty();
        }
        await app.DisposeAsync();
        log.ShouldBeEmpty();
        registrar.Created.ShouldBe(1);
        registrar.Disposed.ShouldBe(0);
        host!.Disposed.ShouldBeFalse();
        await root.DisposeAsync();
        log.ToArray().ShouldBe(["node", "resource", "host"]);
        registrar.Disposed.ShouldBe(1);
        host.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Independent_host_providers_own_independent_resources()
    {
        var registrar = new ResourceRegistrar(new());
        var seen = new List<OwnedResource>();
        var services = new ServiceCollection();
        RegisterResources(services, registrar);
        RegisterProbe(services, "main.node", provider =>
        {
            seen.Add(provider.GetRequiredFlowResource<OwnedResource>("store"));
            return new ProbeNode();
        });
        await using var provider = services.BuildServiceProvider();
        await using var secondProvider = services.BuildServiceProvider();
        await using var first = await provider.BuildApplicationAsync();
        await using var second = await secondProvider.BuildApplicationAsync();
        seen[0].ShouldNotBeSameAs(seen[1]);
        await first.DisposeAsync();
        registrar.Disposed.ShouldBe(0);
        await provider.DisposeAsync();
        registrar.Disposed.ShouldBe(1);
        await second.DisposeAsync();
        registrar.Disposed.ShouldBe(1);
        await secondProvider.DisposeAsync();
        registrar.Disposed.ShouldBe(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sources_preserve_start_production_drain_and_renamed_controls(bool start)
    {
        var source = new ControlledSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new ProbeNode(async message => { entered.TrySetResult(); await release.Task; return message; });
        var services = new ServiceCollection();
        services.AddComponentFactory("Main.Source", _ => source)
            .AddInputPort<ControlledSource>("Main.Source", "Terminal", node => node.Control)
            .AddOutputPort<ControlledSource>("Main.Source", "Output", node => node.Output);
        RegisterProbe(services, "Main.Worker", _ => worker);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("Main.Source.Output", "Main.Worker.Input");
            ports.Link("Main.Worker.Output", "Main.Source.Terminal");
        });
        await using var provider = services.BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        app.GetComponent("Main.Source").ShouldBeSameAs(source);
        source.TerminalInputs.ShouldContain(provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("Main.Source.Terminal"));
        if (start)
        {
            await app.StartAsync();
            await entered.Task.WaitAsync(Timeout);
            var stopping = app.StopAsync();
            await source.ProductionCompletion.WaitAsync(Timeout);
            stopping.IsCompleted.ShouldBeFalse();
            source.Control.Completion.IsCompleted.ShouldBeFalse();
            release.TrySetResult();
            await stopping.WaitAsync(Timeout);
        }
        await app.DisposeAsync().AsTask().WaitAsync(Timeout);
        source.Starts.ShouldBe(start ? 1 : 0);
        source.Controls.ShouldBe(start ? 1 : 0);
        source.Disposals.ShouldBe(0);
        await provider.DisposeAsync();
        source.Disposals.ShouldBe(1);
        worker.Processed.ShouldBe(start ? 1 : 0);
    }

    [Fact]
    public async Task Native_input_failure_cannot_hide_behind_component_additional_completion()
    {
        var extra = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var node = new ProbeNode { AdditionalCompletion = extra.Task };
        var peerFault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = new SinkNode(_ => { }, error => peerFault.TrySetResult(error));
        var services = new ServiceCollection();
        RegisterProbe(services, "Main.Source", _ => node);
        services.AddComponentFactory("Main.Sink", _ => peer).AddInputPort<SinkNode>("Main.Sink", "Input", value => value.Input);
        services.AddSingleton<LinkFactory>(ports => ports.Link("Main.Source.Output", "Main.Sink.Input"));
        await using var provider = services.BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var failure = new InvalidOperationException("native failure");
        node.Fault(failure);
        (await peerFault.Task.WaitAsync(Timeout)).ShouldBeSameAs(failure);
        app.Completion.IsCompleted.ShouldBeFalse();
        extra.TrySetResult();
        (await Should.ThrowAsync<Exception>(() => app.Completion.WaitAsync(Timeout))).ShouldBeSameAs(failure);
        AllErrors(await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask().WaitAsync(Timeout))).ShouldContain(failure);
        peer.Disposals.ShouldBe(0);
        (await Should.ThrowAsync<Exception>(() => provider.DisposeAsync().AsTask())).ShouldBeSameAs(failure);
        peer.Disposals.ShouldBe(1);
        node.Disposals.ShouldBe(0);
    }

    [Theory]
    [InlineData("throw", false)]
    [InlineData("null", false)]
    [InlineData("ports", false)]
    [InlineData("cancel", false)]
    [InlineData("throw", true)]
    [InlineData("configuration", false)]
    [InlineData("resource", false)]
    public async Task Failed_build_stops_components_and_leaves_disposal_to_DI(string failureMode, bool cleanupFails)
    {
        var log = new ConcurrentQueue<string>();
        var original = new InvalidOperationException("activation failure");
        var nodeError = new InvalidOperationException("node cleanup failure");
        var resourceError = new InvalidOperationException("resource cleanup failure");
        var registrar = new ResourceRegistrar(log, cleanupFails ? resourceError : null);
        using var cancellation = new CancellationTokenSource();
        var services = new ServiceCollection();
        RegisterResources(services, registrar);
        RegisterProbe(services, "Main.First", dependencies =>
        {
            dependencies.GetRequiredFlowResource<OwnedResource>("store");
            return new ProbeNode(dispose: () => { log.Enqueue("first"); if (cleanupFails) throw nodeError; });
        });
        RegisterProbe(services, "Main.Second", dependencies =>
        {
            if (failureMode == "configuration") JsonSerializer.Deserialize<RequiredProbeOptions>("{}");
            if (failureMode == "resource") dependencies.GetRequiredFlowResource<OwnedResource>("Missing");
            if (failureMode == "throw") throw original;
            if (failureMode == "null") return null!;
            if (failureMode == "cancel") cancellation.Cancel();
            return new ProbeNode(dispose: () => log.Enqueue("second"));
        });
        if (failureMode == "ports") services.AddInputPort<ProbeNode>("Main.Second", "Invalid", _ => null!);
        await using var provider = services.BuildServiceProvider();
        var failure = await Should.ThrowAsync<Exception>(() => provider.BuildApplicationAsync(cancellationToken: cancellation.Token));
        registrar.Disposed.ShouldBe(0);
        log.ShouldBeEmpty();
        if (failureMode == "throw") AllErrors(failure).ShouldContain(original);
        if (failureMode == "configuration") failure.ToString().ShouldContain("Limit");
        if (failureMode == "resource") failure.ToString().ShouldContain("Missing");
        if (failureMode == "null") failure.Message.ShouldContain("returned no component");
        if (failureMode == "ports") failure.Message.ShouldContain("selector returned no block");
        if (failureMode == "cancel") failure.ShouldBeAssignableTo<OperationCanceledException>()!.CancellationToken.ShouldBe(cancellation.Token);
        if (cleanupFails)
        {
            AllErrors(failure).ShouldNotContain(nodeError);
            (await Should.ThrowAsync<InvalidOperationException>(() => provider.DisposeAsync().AsTask())).ShouldBeSameAs(nodeError);
            log.ToArray().ShouldBe(["first"]);
            registrar.Disposed.ShouldBe(0);
        }
        else
        {
            await provider.DisposeAsync();
            registrar.Disposed.ShouldBe(1);
            log.ToArray().ShouldBe(failureMode is "ports" or "cancel" ? ["second", "first", "resource"] : ["first", "resource"]);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Native_nodes_cannot_be_shared_between_providers(bool resolveBeforeBuild)
    {
        var node = new ProbeNode();
        var services = new ServiceCollection();
        RegisterProbe(services, "Main.Node", _ => node);
        TestSink.Register(services, "Main.Node.Output");
        await using var provider = services.BuildServiceProvider();
        if (resolveBeforeBuild) provider.GetRequiredKeyedService<IFlowNode>("Main.Node").ShouldBeSameAs(node);
        var owner = await provider.BuildApplicationAsync();
        await using (owner)
        {
            await using var rejectedProvider = services.BuildServiceProvider();
            (await Should.ThrowAsync<ArgumentException>(() => rejectedProvider.BuildApplicationAsync())).Message.ShouldContain("cannot be registered twice");
            await using var other = new ServiceCollection().AddComponentFactory("Other.Node", _ => node).BuildServiceProvider();
            Should.Throw<ArgumentException>(() => other.GetRequiredKeyedService<IFlowNode>("Other.Node"));
            owner.GetComponent("Main.Node").ShouldBeSameAs(node);
            node.Disposals.ShouldBe(0);
            await owner.StartAsync();
            await node.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(7))));
            await owner.StopAsync().WaitAsync(Timeout);
            node.Processed.ShouldBe(1);
        }
        node.Disposals.ShouldBe(0);
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Named_event_outputs_preserve_headers_parameters_and_isolated_rejections()
    {
        var events = new BufferBlock<FlowMessage>();
        var received = new ConcurrentQueue<FlowMessage>();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejected = new ConcurrentQueue<(string Source, string Target, FlowMessage Message, Exception Error)>();
        var engine = new RecordingEngine();
        var unused = new RecordingEngine();
        var node = new ProbeNode(message => { events.Post(message).ShouldBeTrue(); return message; });
        var sink = new SinkNode(message => { received.Enqueue(message); if (received.Count == 2) delivered.TrySetResult(); });
        var services = new ServiceCollection();
        RegisterProbe(services, "Main.Source", _ => node);
        services.AddEventPort<ProbeNode>("Main.Source", "Audit", _ => events);
        services.AddComponentFactory("Main.Sink", _ => sink).AddInputPort<SinkNode>("Main.Sink", "Input", value => value.Input);
        services.AddSingleton<IFlowExpressionEngine>(unused);
        services.AddKeyedSingleton<IFlowExpressionEngine>("chosen", engine);
        services.AddSingleton<LinkFactory>(ports =>
        {
            var compiled = ports.GetRequiredKeyedService<IFlowExpressionEngine>("chosen")
                .Compile<bool>("above threshold", new Dictionary<string, Type> { ["threshold"] = typeof(int), ["input"] = typeof(FlowValue), ["message"] = typeof(FlowMessage) });
            ports.Link("Main.Source.Audit", "Main.Sink.Input", message =>
            {
                var variables = new Dictionary<string, object?>(FlowMapContext.ForMessage(message).Variables)
                    { ["threshold"] = 5 };
                return compiled(new FlowMapContext { Variables = variables });
            }, FluxFlow.Core.Execution.FlowDeliveryPolicy.IsolateRejection,
                (message, error) => rejected.Enqueue(("Main.Source.Audit", "Main.Sink.Input", message, error)));
        });
        TestSink.Register(services, "Main.Source.Output");
        TestSink.Register(services, "Main.Source.Audit");
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var messages = Enumerable.Range(5, 4).Select(value => FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(value)))).ToArray();
        foreach (var message in messages) await app.GetTestInput("Main.Source.Input").SendAsync(message);
        await delivered.Task.WaitAsync(Timeout);
        await app.StopAsync().WaitAsync(Timeout);
        engine.Compilations.ShouldBe(1);
        engine.Evaluations.ShouldBe(4);
        unused.Compilations.ShouldBe(0);
        var accepted = received.ToArray();
        accepted.Select(message => message.Value.Json.GetInt32()).ShouldBe([6, 8]);
        accepted[0].Value.ShouldBeSameAs(messages[1].Value);
        accepted[0].MessageId.ShouldBe(messages[1].MessageId);
        accepted[0].Headers[FlowEventHeaders.Source].Json.GetString().ShouldBe("Main.Source");
        var refusal = rejected.ShouldHaveSingleItem();
        refusal.Source.ShouldBe("Main.Source.Audit");
        refusal.Target.ShouldBe("Main.Sink.Input");
        refusal.Message.Value.ShouldBeSameAs(messages[2].Value);
        refusal.Error.ShouldBeSameAs(engine.Failure);
        events.Complete();
        await events.Completion.WaitAsync(Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_start_failure_is_retained_and_DI_stops_at_first_disposal_failure(bool cleanupFails)
    {
        var original = new InvalidOperationException("start failed");
        var cleanup = new InvalidOperationException("resource cleanup failed");
        var registrar = new ResourceRegistrar(new(), cleanupFails ? cleanup : null);
        var source = new ControlledSource { StartFailure = original };
        var services = new ServiceCollection();
        RegisterResources(services, registrar);
        services.AddComponentFactory("Main.Source", dependencies => { dependencies.GetRequiredFlowResource<OwnedResource>("store"); return source; })
            .AddInputPort<ControlledSource>("Main.Source", "Terminal", value => value.Control)
            .AddOutputPort<ControlledSource>("Main.Source", "Output", value => value.Output);
        TestSink.Register(services, "Main.Source.Output");
        await using var provider = services.BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        var failure = await Should.ThrowAsync<Exception>(() => app.StartAsync().WaitAsync(Timeout));
        AllErrors(failure).ShouldContain(original);
        AllErrors(failure).ShouldNotContain(cleanup);
        source.Disposals.ShouldBe(0);
        registrar.Disposed.ShouldBe(0);
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask());
        (await Should.ThrowAsync<Exception>(() => provider.DisposeAsync().AsTask())).ShouldBeSameAs(original);
        source.Disposals.ShouldBe(1);
        registrar.Disposed.ShouldBe(0);
    }

    [Fact]
    public async Task Pre_cancel_prevents_resource_and_component_construction_after_registration()
    {
        var calls = 0;
        var registrar = new ResourceRegistrar(new());
        var services = new ServiceCollection();
        RegisterResources(services, registrar);
        RegisterProbe(services, "main.node", _ => { calls++; return new ProbeNode(); });
        await using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        (await Should.ThrowAsync<OperationCanceledException>(() => provider.BuildApplicationAsync(cancellationToken: cancellation.Token)))
            .CancellationToken.ShouldBe(cancellation.Token);
        calls.ShouldBe(0);
        registrar.Registrations.ShouldBe(1);
        registrar.Created.ShouldBe(0);
    }

    private static void RegisterProbe(IServiceCollection services, string address, Func<IServiceProvider, ProbeNode> create)
        => services.AddComponentFactory(address, provider => create(provider))
            .AddInputPort<ProbeNode>(address, "Input", node => node.Input)
            .AddOutputPort<ProbeNode>(address, "Output", node => node.Output);

    private static void RegisterResources(IServiceCollection services, ResourceRegistrar registrar)
    {
        services.AddApplicationResources(resources => registrar.Register(resources,
            ApplicationAddress.Resource("store"), JsonSerializer.SerializeToElement(new { Type = "test.resource" })));
    }


    private sealed class RequiredProbeOptions
    {
        public required int Limit { get; init; }
    }

    private static IEnumerable<Exception> AllErrors(Exception error)
        => new[] { error }.Concat(error is AggregateException aggregate ? aggregate.InnerExceptions.SelectMany(AllErrors)
            : error.InnerException is { } inner ? AllErrors(inner) : []);


    private sealed class RecordingEngine : IFlowExpressionEngine
    {
        public string Name => "recording";
        public int Compilations;
        public int Evaluations;
        public readonly Exception Failure = new InvalidOperationException("condition failed");
        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => throw new InvalidOperationException("The compiled condition must be reused.");
        public Func<FlowMapContext, T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> types)
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
                var value = message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (value == 7) throw Failure;
                return (T)(object)(value > Convert.ToInt32(context.Variables["threshold"]));
            }).Evaluate;
        }
        private sealed class Compiled<T>(Func<FlowMapContext, T> evaluate)
        {
            public T Evaluate(FlowMapContext context) => evaluate(context);
        }
    }

    private sealed class ResourceRegistrar(ConcurrentQueue<string> log, Exception? failure = null)
    {
        public int Registrations;
        public int Created;
        public int Disposed;
        public void Register(IServiceCollection services, ApplicationAddress address, JsonElement resource)
        {
            Registrations++;
            resource.GetProperty("Type").GetString().ShouldBe("test.resource");
            services.AddKeyedSingleton<OwnedResource>("store", (_, _) =>
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

    private sealed class ProbeNode : IFlowNode
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
        }
        public ITargetBlock<FlowMessage> Input => _block;
        public ISourceBlock<FlowMessage> Output => _block;
        public Task AdditionalCompletion { get; init; } = Task.CompletedTask;
        public Task Completion => Task.WhenAll(_block.Completion, AdditionalCompletion);
        public void Complete() => _block.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)_block).Fault(exception);
        public async ValueTask DisposeAsync()
        {
            Disposals++;
            Complete();
            try { await Completion; } finally { _dispose?.Invoke(); }
        }
    }

    private sealed class SinkNode : IFlowNode
    {
        private readonly ActionBlock<FlowMessage> _block;
        private readonly Action<Exception>? _fault;
        public int Disposals;
        public SinkNode(Action<FlowMessage> receive, Action<Exception>? fault = null)
        {
            _fault = fault;
            _block = new(receive, new ExecutionDataflowBlockOptions { BoundedCapacity = 8 });
        }
        public ITargetBlock<FlowMessage> Input => _block;
        public Task Completion => _block.Completion;
        public void Complete() => _block.Complete();
        public void Fault(Exception exception) { _fault?.Invoke(exception); ((IDataflowBlock)_block).Fault(exception); }
        public async ValueTask DisposeAsync() { Disposals++; Complete(); await Completion; }
    }

    private sealed class ControlledSource : IFlowSource
    {
        public readonly BufferBlock<FlowMessage> Output = new(new DataflowBlockOptions { BoundedCapacity = 8 });
        public readonly ActionBlock<FlowMessage> Control;
        public int Starts;
        public Exception? StartFailure;
        public int Controls;
        public int Disposals;
        public ControlledSource()
        {
            Control = new(context => Controls++, new ExecutionDataflowBlockOptions { BoundedCapacity = 8 });
            Completion = Task.WhenAll(Output.Completion, Control.Completion);
        }
        public IEnumerable<ITargetBlock<FlowMessage>> TerminalInputs => [Control];
        public Task Completion { get; }
        public Task ProductionCompletion => Output.Completion;
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            Starts++;
            if (StartFailure is not null) throw StartFailure;
            (await Output.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken)).ShouldBeTrue();
        }
        public void StopProduction() => Output.Complete();
        public void Complete() { Output.Complete(); Control.Complete(); }
        public void Fault(Exception error) { ((IDataflowBlock)Output).Fault(error); ((IDataflowBlock)Control).Fault(error); }
        public async ValueTask DisposeAsync() { Disposals++; Complete(); await Completion; }
    }
}
