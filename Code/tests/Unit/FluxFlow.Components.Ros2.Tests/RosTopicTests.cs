using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Ros2;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rcl;
using Rcl.Qos;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Ros2.Tests;

public sealed partial class RosTopicTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static FlowMessage Message(object value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));

    [Fact]
    public async Task Publisher_converts_fields_preserves_envelope_and_bypasses_errors()
    {
        var sent = new List<TestMessage>();
        var disposed = 0;
        var publisher = Proxy<IRclPublisher<TestMessage>>((method, args) =>
        {
            if (method.Name == "PublishAsync") { sent.Add((TestMessage)args![0]!); return ValueTask.CompletedTask; }
            if (method.Name == "Dispose") { disposed++; return null; }
            throw new NotSupportedException(method.Name);
        });
        var nodeDisposed = false;
        var node = Proxy<IRclNode>((method, _) =>
        {
            if (method.Name == "CreatePublisher") return publisher;
            if (method.Name == "Dispose") { nodeDisposed = true; return null; }
            throw new NotSupportedException(method.Name);
        });
        await using var component = new RosPublisher<TestMessage>(new() { Node = node, Topic = "/test" });
        var capture = new BufferBlock<FlowMessage>();
        using var link = component.Output.LinkTo(capture);
        var original = Message(new { Data = "hello", SequenceNumber = 7 });
        var error = FlowMessage.Create(new FlowError("upstream", "failed", "test"));
        await component.Input.SendAsync(original);
        await component.Input.SendAsync(error);
        (await capture.ReceiveAsync(Deadline)).ShouldBeSameAs(original);
        (await capture.ReceiveAsync(Deadline)).ShouldBeSameAs(error);
        sent.Count.ShouldBe(1);
        sent[0].Data.ShouldBe("hello");
        sent[0].SequenceNumber.ShouldBe(7);
        await component.DisposeAsync();
        await component.DisposeAsync();
        disposed.ShouldBe(1);
        nodeDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task Publisher_reports_conversion_and_transport_failure_then_recovers()
    {
        var calls = 0;
        var native = Proxy<IRclPublisher<TestMessage>>((method, _) =>
        {
            if (method.Name == "Dispose") return null;
            if (method.Name == "PublishAsync")
                return ++calls == 1 ? ValueTask.FromException(new IOException("transport")) : ValueTask.CompletedTask;
            throw new NotSupportedException(method.Name);
        });
        var node = Proxy<IRclNode>((_, _) => native);
        await using var component = new RosPublisher<TestMessage>(new() { Node = node, Topic = "/test" });
        var capture = new BufferBlock<FlowMessage>();
        using var link = component.Output.LinkTo(capture);
        var invalid = Message(new { misspelled = true });
        await component.Input.SendAsync(invalid);
        var converted = await capture.ReceiveAsync(Deadline);
        converted.IsError.ShouldBeTrue();
        converted.TraceId.ShouldBe(invalid.TraceId);
        calls.ShouldBe(0);
        await component.Input.SendAsync(Message(new { data = "fails" }));
        (await capture.ReceiveAsync(Deadline)).Error!.Message.ShouldContain("transport");
        await component.Input.SendAsync(Message(new { data = "works" }));
        (await capture.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Disposal_waits_for_inflight_native_publish()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;
        var native = Proxy<IRclPublisher<TestMessage>>((method, _) =>
        {
            if (method.Name == "Dispose") { disposed = true; return null; }
            entered.TrySetResult();
            return new ValueTask(finish.Task);
        });
        var node = Proxy<IRclNode>((_, _) => native);
        var component = new RosPublisher<TestMessage>(new() { Node = node, Topic = "/test" });
        await component.Input.SendAsync(Message(new { data = "hello" }));
        await entered.Task.WaitAsync(Deadline);
        var disposal = component.DisposeAsync().AsTask();
        try
        {
            disposal.IsCompleted.ShouldBeFalse();
            disposed.ShouldBeFalse();
        }
        finally { finish.TrySetResult(); }
        await disposal.WaitAsync(Deadline);
        disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Subscriber_starts_lazily_snapshots_messages_and_disposes_on_stop()
    {
        var queue = Channel.CreateUnbounded<TestMessage>();
        var created = 0;
        var disposed = 0;
        var native = Subscription(queue, () => disposed++);
        var node = Proxy<IRclNode>((_, _) => { created++; return native; });
        await using var component = new RosSubscriber<TestMessage>(new() { Node = node, Topic = "/test" });
        var capture = new BufferBlock<FlowMessage>();
        using var link = component.Output.LinkTo(capture);
        created.ShouldBe(0);
        await component.StartAsync();
        await component.StartAsync();
        var sample = new TestMessage { Data = "before", SequenceNumber = 1 };
        await queue.Writer.WriteAsync(sample);
        var first = await capture.ReceiveAsync(Deadline);
        sample.Data = "after";
        first.Value.Json.EnumerateObject().Select(property => property.Name).ShouldBe(["Data", "SequenceNumber"]);
        first.Value.Json.GetProperty("Data").GetString().ShouldBe("before");
        first.Value.Json.GetProperty("SequenceNumber").GetInt32().ShouldBe(1);
        await queue.Writer.WriteAsync(sample);
        var second = await capture.ReceiveAsync(Deadline);
        second.TraceId.ShouldNotBe(first.TraceId);
        second.MessageId.ShouldNotBe(first.MessageId);
        component.StopProduction();
        await component.Completion.WaitAsync(Deadline);
        created.ShouldBe(1);
        disposed.ShouldBe(1);
    }

    [Fact]
    public async Task Subscriber_conversion_failure_is_output_and_native_failure_faults_completion()
    {
        var queue = Channel.CreateUnbounded<TestMessage>();
        var disposed = 0;
        var node = Proxy<IRclNode>((_, _) => Subscription(queue, () => disposed++));
        await using var component = new RosSubscriber<TestMessage>(new()
        {
            Node = node, Topic = "/test", ToValue = value => value.Data == "bad"
                ? throw new FormatException("invalid sample") : (FlowValue)value.Data
        });
        var capture = new BufferBlock<FlowMessage>();
        using var link = component.Output.LinkTo(capture);
        await component.StartAsync();
        await queue.Writer.WriteAsync(new() { Data = "bad" });
        (await capture.ReceiveAsync(Deadline)).Error!.Code.ShouldBe("ros.conversion_failed");
        await queue.Writer.WriteAsync(new() { Data = "good" });
        (await capture.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("good");
        var failure = new IOException("lost subscription");
        queue.Writer.TryComplete(failure);
        (await Record.ExceptionAsync(() => component.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        disposed.ShouldBe(1);
    }

    [Fact]
    public async Task Disposing_unstarted_subscriber_does_not_create_native_endpoint()
    {
        var node = Proxy<IRclNode>((_, _) => throw new InvalidOperationException("must not resolve"));
        await using var component = new RosSubscriber<TestMessage>(new() { Node = node, Topic = "/test" });
        await component.DisposeAsync().AsTask().WaitAsync(Deadline);
        await component.StartAsync();
        component.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Configuration_defers_native_construction_snapshots_settings_and_links_both_directions()
    {
        using var config = Config("ros.subscriber");
        config["Workflows:main:node:Output"] = "send.Input";
        config["Workflows:main:send:Type"] = "ros.publisher";
        config["Workflows:main:send:Node"] = "ros";
        config["Workflows:main:send:Topic"] = "/outgoing";
        config["Workflows:main:send:Input"] = "node.Output";
        config["Workflows:main:node:Qos:History"] = "KeepLast";
        config["Workflows:main:node:Qos:Depth"] = "5";
        config["Workflows:main:node:Qos:Reliability"] = "BestEffort";
        var log = new List<string>();
        var published = new TaskCompletionSource<TestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = Channel.CreateUnbounded<TestMessage>();
        var nativePublisher = Proxy<IRclPublisher<TestMessage>>((method, args) =>
        {
            if (method.Name == "Dispose") { log.Add("publisher disposed"); return null; }
            published.TrySetResult((TestMessage)args![0]!);
            return ValueTask.CompletedTask;
        });
        var node = Proxy<IRclNode>((method, args) =>
        {
            if (method.Name == "Dispose") { log.Add("node disposed"); return null; }
            if (method.Name == "CreatePublisher")
            {
                args![0].ShouldBe("/outgoing");
                log.Add("publisher created");
                return nativePublisher;
            }
            var options = (SubscriptionOptions)args![1]!;
            options.Qos.Depth.ShouldBe(5);
            options.Qos.Reliability.ShouldBe(ReliabilityPolicy.BestEffort);
            log.Add("subscriber created");
            return Subscription(queue, () => log.Add("subscriber disposed"));
        });
        var services = new ServiceCollection().AddRosPublisher<TestMessage>().AddRosSubscriber<TestMessage>();
        services.AddKeyedSingleton<ResourceRegistration>("test.node", (registrations, address, _) =>
            registrations.AddFluxFlowResource<IRclNode>(address, _ => { log.Add("node created"); return node; }));
        services.AddFluxFlowApplication(config);
        log.ShouldBeEmpty();
        services.Count(d => d.ServiceType == typeof(LinkFactory) && !d.IsKeyedService).ShouldBe(1);
        config["Workflows:main:send:Topic"] = "/changed";
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        log.ShouldBe(new[] { "node created", "publisher created" });
        provider.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Events").ShouldNotBeNull();
        provider.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.send.Events").ShouldNotBeNull();
        await app.StartAsync();
        await queue.Writer.WriteAsync(new() { Data = "forwarded", SequenceNumber = 8 });
        (await published.Task.WaitAsync(Deadline)).Data.ShouldBe("forwarded");
        await app.StopAsync().WaitAsync(Deadline);
        log.ShouldContain("subscriber disposed");
        log.ShouldNotContain("node disposed");
        await provider.DisposeAsync();
        log[^2].ShouldBe("publisher disposed");
        log[^1].ShouldBe("node disposed");
    }

    [Fact]
    public async Task Shared_node_survives_workflow_restart_while_other_workflow_keeps_delivering()
    {
        var log = new ConcurrentQueue<string>();
        var incoming = new Dictionary<string, Channel<TestMessage>>
        {
            ["/main/in"] = Channel.CreateUnbounded<TestMessage>(),
            ["/other/in"] = Channel.CreateUnbounded<TestMessage>()
        };
        var published = Channel.CreateUnbounded<(string Topic, TestMessage Message)>();
        var node = Proxy<IRclNode>((method, args) =>
        {
            if (method.Name == "Dispose") { log.Enqueue("node disposed"); return null; }
            var topic = (string)args![0]!;
            if (method.Name == "CreateSubscription")
            {
                log.Enqueue(topic + " created");
                return Subscription(incoming[topic], () => log.Enqueue(topic + " disposed"));
            }
            if (method.Name == "CreatePublisher")
            {
                log.Enqueue(topic + " created");
                return Proxy<IRclPublisher<TestMessage>>((operation, values) =>
                {
                    if (operation.Name == "Dispose") { log.Enqueue(topic + " disposed"); return null; }
                    if (operation.Name == "PublishAsync")
                        return published.Writer.WriteAsync((topic, (TestMessage)values![0]!));
                    throw new NotSupportedException(operation.Name);
                });
            }
            throw new NotSupportedException(method.Name);
        });
        var context = Proxy<IRclContext>((method, _) =>
        {
            if (method.Name == "CreateNode") { log.Enqueue("node created"); return node; }
            if (method.Name == "DisposeAsync") { log.Enqueue("context disposed"); return ValueTask.CompletedTask; }
            throw new NotSupportedException(method.Name);
        });
        var settings = new Dictionary<string, string?>
        {
            ["Resources:ros:Type"] = "ros.node",
            ["Resources:ros:Name"] = "shared"
        };
        foreach (var workflow in new[] { "main", "other" })
        {
            settings[$"Workflows:{workflow}:receive:Type"] = "ros.subscriber";
            settings[$"Workflows:{workflow}:receive:Node"] = "ros";
            settings[$"Workflows:{workflow}:receive:Topic"] = $"/{workflow}/in";
            settings[$"Workflows:{workflow}:receive:Output"] = "send.Input";
            settings[$"Workflows:{workflow}:send:Type"] = "ros.publisher";
            settings[$"Workflows:{workflow}:send:Node"] = "ros";
            settings[$"Workflows:{workflow}:send:Topic"] = $"/{workflow}/out";
        }
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IRclContext>(_ => context);
        services.AddRosNode().AddRosPublisher<TestMessage>().AddRosSubscriber<TestMessage>()
            .AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync().WaitAsync(Deadline);
        var main = provider.GetRequiredKeyedService<FlowWorkflow>("main");
        var other = provider.GetRequiredKeyedService<FlowWorkflow>("other");
        var originalMain = main.Services.GetRequiredKeyedService<IFlowNode>("main.receive");
        var originalOther = other.Services.GetRequiredKeyedService<IFlowNode>("other.receive");
        await CheckDelivery("main", "before restart");
        await CheckDelivery("other", "before stop");

        await main.StopAsync().WaitAsync(Deadline);
        originalMain.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        CheckEndpoints("main", created: 1, disposed: 1);
        CheckEndpoints("other", created: 1, disposed: 0);
        log.ShouldNotContain("node disposed");
        log.ShouldNotContain("context disposed");
        await CheckDelivery("other", "while main stopped");

        await main.StartAsync().WaitAsync(Deadline);
        main.Services.GetRequiredKeyedService<IFlowNode>("main.receive").ShouldNotBeSameAs(originalMain);
        other.Services.GetRequiredKeyedService<IFlowNode>("other.receive").ShouldBeSameAs(originalOther);
        main.Services.GetRequiredFlowResource<IRclNode>("ros", "main").ShouldBeSameAs(node);
        other.Services.GetRequiredFlowResource<IRclNode>("ros", "other").ShouldBeSameAs(node);
        await CheckDelivery("main", "after restart");
        await CheckDelivery("other", "after main restart");
        CheckEndpoints("main", created: 2, disposed: 1);
        CheckEndpoints("other", created: 1, disposed: 0);

        await app.StopAsync().WaitAsync(Deadline);
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        CheckEndpoints("main", created: 2, disposed: 2);
        CheckEndpoints("other", created: 1, disposed: 1);
        log.Count(entry => entry == "node created").ShouldBe(1);
        log.ShouldNotContain("node disposed");
        log.ShouldNotContain("context disposed");
        published.Reader.TryRead(out _).ShouldBeFalse();
        await provider.DisposeAsync().AsTask().WaitAsync(Deadline);
        log.Count(entry => entry == "node disposed").ShouldBe(1);
        log.Count(entry => entry == "context disposed").ShouldBe(1);
        log.TakeLast(2).ShouldBe(new[] { "node disposed", "context disposed" });
        CheckEndpoints("main", created: 2, disposed: 2);
        CheckEndpoints("other", created: 1, disposed: 1);

        async Task CheckDelivery(string workflow, string text)
        {
            await incoming[$"/{workflow}/in"].Writer.WriteAsync(new() { Data = text });
            var result = await published.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
            result.Topic.ShouldBe($"/{workflow}/out");
            result.Message.Data.ShouldBe(text);
        }

        void CheckEndpoints(string workflow, int created, int disposed)
        {
            foreach (var port in new[] { "in", "out" })
            {
                log.Count(entry => entry == $"/{workflow}/{port} created").ShouldBe(created);
                log.Count(entry => entry == $"/{workflow}/{port} disposed").ShouldBe(disposed);
            }
        }
    }

    [Theory]
    [InlineData("ros.publisher", "Topic", "")]
    [InlineData("ros.publisher", "Node", "")]
    [InlineData("ros.publisher", "OutputCapacity", "0")]
    [InlineData("ros.subscriber", "QueueSize", "0")]
    [InlineData("ros.subscriber", "FullMode", "Wait")]
    [InlineData("ros.subscriber", "FullMode", "999")]
    [InlineData("ros.subscriber", "OutputCapacity", "-1")]
    public void Invalid_configuration_is_atomic(string type, string property, string value)
    {
        using var config = Config(type);
        config["Workflows:main:node:" + property] = value;
        var services = new ServiceCollection().AddRosPublisher<TestMessage>().AddRosSubscriber<TestMessage>();
        services.AddKeyedSingleton<ResourceRegistration>("test.node", (_, _, _) => { });
        var before = services.ToArray();
        var error = Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(config));
        error.ToString().ShouldContain("main.node");
        services.ToArray().ShouldBe(before);
    }

    [Fact]
    public async Task Native_node_resources_reuse_host_context_and_dispose_in_dependency_order()
    {
        var log = new List<string>();
        var node = Proxy<IRclNode>((method, _) => { log.Add(method.Name); return null; });
        var context = Proxy<IRclContext>((method, _) =>
        {
            log.Add("context." + method.Name);
            return method.Name == "CreateNode" ? node : ValueTask.CompletedTask;
        });
        var services = new ServiceCollection();
        services.AddSingleton<IRclContext>(_ => context);
        services.AddRosNode();
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Resources:ros:Type"] = "ros.node", ["Resources:ros:Name"] = "test", ["Workflows:main"] = null }).Build();
        services.AddFluxFlowApplication(config);
        log.ShouldBeEmpty();
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredFlowResource<IRclNode>("ros").ShouldBeSameAs(node);
        provider.GetRequiredFlowResource<IRclNode>("ros").ShouldBeSameAs(node);
        log.ShouldBe(new[] { "context.CreateNode" });
        await provider.DisposeAsync();
        log.ShouldBe(new[] { "context.CreateNode", "Dispose", "context.DisposeAsync" });
    }

    [Theory]
    [InlineData("ros.publisher")]
    [InlineData("ros.subscriber")]
    public async Task Native_endpoint_creation_failure_preserves_exception_and_DI_ownership(string type)
    {
        var failure = new IOException("Native endpoint creation failed.");
        var log = new ConcurrentQueue<string>();
        var publisher = Proxy<IRclPublisher<TestMessage>>((method, _) =>
        {
            if (method.Name == "Dispose") { log.Enqueue("publisher disposed"); return null; }
            throw new NotSupportedException(method.Name);
        });
        var node = Proxy<IRclNode>((method, args) =>
        {
            if (method.Name == "Dispose") { log.Enqueue("node disposed"); return null; }
            if (method.Name == "CreatePublisher" && (string)args![0]! == "/prepared")
            {
                log.Enqueue("publisher created");
                return publisher;
            }
            if (method.Name is "CreatePublisher" or "CreateSubscription")
            {
                log.Enqueue(method.Name + " failed");
                throw failure;
            }
            throw new NotSupportedException(method.Name);
        });
        var context = Proxy<IRclContext>((method, _) =>
        {
            if (method.Name == "CreateNode") { log.Enqueue("node created"); return node; }
            if (method.Name == "DisposeAsync") { log.Enqueue("context disposed"); return ValueTask.CompletedTask; }
            throw new NotSupportedException(method.Name);
        });
        using var config = Config(type);
        config["Resources:ros:Type"] = "ros.node";
        config["Resources:ros:Name"] = "shared";
        config["Workflows:main:prepared:Type"] = "ros.publisher";
        config["Workflows:main:prepared:Node"] = "ros";
        config["Workflows:main:prepared:Topic"] = "/prepared";
        var services = new ServiceCollection();
        services.AddSingleton<IRclContext>(_ => context);
        services.AddRosNode().AddRosPublisher<TestMessage>().AddRosSubscriber<TestMessage>()
            .AddFluxFlowApplication(config);
        var provider = services.BuildServiceProvider();
        Exception? disposalError = null;
        try
        {
            // Construct a sibling first so cleanup is exercised independently of section ordering.
            var scope = provider.GetWorkflowServices("main");
            var prepared = scope.GetRequiredKeyedService<IFlowNode>("main.prepared");
            if (type == "ros.publisher")
            {
                (await Record.ExceptionAsync(() => provider.BuildApplicationAsync().WaitAsync(Deadline)))
                    .ShouldBeSameAs(failure);
                prepared.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            }
            else
            {
                var app = await provider.BuildApplicationAsync().WaitAsync(Deadline);
                var subscriber = scope.GetRequiredKeyedService<IFlowNode>("main.node");
                // Source failures are authoritative on Completion, even when Start has already returned.
                var startError = await Record.ExceptionAsync(() => app.StartAsync().WaitAsync(Deadline));
                if (startError is not null) startError.ShouldBeSameAs(failure);
                (await Record.ExceptionAsync(() => subscriber.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
                (await Record.ExceptionAsync(() => app.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
                (await Record.ExceptionAsync(() => app.StopAsync().WaitAsync(Deadline))).ShouldBeSameAs(failure);
            }
            log.ToArray().ShouldBe(new[]
            {
                "node created", "publisher created",
                type == "ros.publisher" ? "CreatePublisher failed" : "CreateSubscription failed",
                "publisher disposed"
            });
        }
        finally
        {
            disposalError = await Record.ExceptionAsync(() => provider.DisposeAsync().AsTask().WaitAsync(Deadline));
        }
        if (type == "ros.publisher") disposalError.ShouldBeNull();
        else
        {
            var errors = disposalError.ShouldBeOfType<AggregateException>().Flatten().InnerExceptions;
            errors.ShouldNotBeEmpty();
            foreach (var error in errors) error.ShouldBeSameAs(failure);
        }
        log.Count(entry => entry == "publisher disposed").ShouldBe(1);
        log.Count(entry => entry == "node disposed").ShouldBe(1);
        log.Count(entry => entry == "context disposed").ShouldBe(1);
        log.TakeLast(2).ShouldBe(new[] { "node disposed", "context disposed" });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generic_host_starts_topics_and_cleans_up_after_shutdown_or_subscription_failure(bool failSubscription)
    {
        var queue = Channel.CreateUnbounded<TestMessage>();
        var entered = new TaskCompletionSource<TestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new ConcurrentQueue<string>();
        var failure = new IOException("Lost native subscription.");
        var publisher = Proxy<IRclPublisher<TestMessage>>((method, args) =>
        {
            if (method.Name == "Dispose") { log.Enqueue("publisher disposed"); return null; }
            if (method.Name == "PublishAsync")
            {
                entered.TrySetResult((TestMessage)args![0]!);
                return new ValueTask(release.Task);
            }
            throw new NotSupportedException(method.Name);
        });
        var node = Proxy<IRclNode>((method, _) =>
        {
            if (method.Name == "Dispose") { log.Enqueue("node disposed"); return null; }
            if (method.Name == "CreatePublisher") { log.Enqueue("publisher created"); return publisher; }
            if (method.Name == "CreateSubscription")
            {
                log.Enqueue("subscriber created");
                return Subscription(queue, () => log.Enqueue("subscriber disposed"));
            }
            throw new NotSupportedException(method.Name);
        });
        var context = Proxy<IRclContext>((method, _) =>
        {
            if (method.Name == "CreateNode") { log.Enqueue("node created"); return node; }
            if (method.Name == "DisposeAsync") { log.Enqueue("context disposed"); return ValueTask.CompletedTask; }
            throw new NotSupportedException(method.Name);
        });
        using var config = Config("ros.subscriber");
        config["Resources:ros:Type"] = "ros.node";
        config["Resources:ros:Name"] = "hosted";
        config["Workflows:main:node:Output"] = "send.Input";
        config["Workflows:main:send:Type"] = "ros.publisher";
        config["Workflows:main:send:Node"] = "ros";
        config["Workflows:main:send:Topic"] = "/outgoing";
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IRclContext>(_ => context);
        builder.Services.AddRosNode().AddRosPublisher<TestMessage>().AddRosSubscriber<TestMessage>()
            .AddFluxFlowApplication(config).AddFluxFlow();
        var host = builder.Build();
        Exception? stopError = null, disposalError = null;
        try
        {
            log.ShouldBeEmpty();
            var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = host.Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping.Register(() => stopping.TrySetResult());
            await host.StartAsync().WaitAsync(Deadline);
            var app = host.Services.GetRequiredService<FlowApplication>();
            await queue.Writer.WriteAsync(new() { Data = "hosted", SequenceNumber = 3 });
            var sent = await entered.Task.WaitAsync(Deadline);
            sent.Data.ShouldBe("hosted");
            sent.SequenceNumber.ShouldBe(3);
            log.ToArray().ShouldBe(new[] { "node created", "publisher created", "subscriber created" });

            if (failSubscription)
            {
                queue.Writer.TryComplete(failure).ShouldBeTrue();
                release.TrySetResult();
                (await Record.ExceptionAsync(() => app.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
                // Observe the default BackgroundService failure policy before explicitly stopping the host.
                await stopping.Task.WaitAsync(Deadline);
                AssertOriginalFailure(await Record.ExceptionAsync(() => host.StopAsync().WaitAsync(Deadline)), failure);
            }
            else
            {
                var stopped = host.StopAsync();
                stopped.IsCompleted.ShouldBeFalse();
                log.ShouldNotContain("publisher disposed");
                log.ShouldNotContain("node disposed");
                log.ShouldNotContain("context disposed");
                release.TrySetResult();
                await stopped.WaitAsync(Deadline);
                await app.Completion.WaitAsync(Deadline);
            }
            log.Count(entry => entry == "subscriber disposed").ShouldBe(1);
            log.Count(entry => entry == "publisher disposed").ShouldBe(1);
            log.ShouldNotContain("node disposed");
            log.ShouldNotContain("context disposed");
        }
        finally
        {
            release.TrySetResult();
            stopError = await Record.ExceptionAsync(() => host.StopAsync().WaitAsync(Deadline));
            disposalError = await Record.ExceptionAsync(() => ((IAsyncDisposable)host).DisposeAsync().AsTask().WaitAsync(Deadline));
        }
        if (failSubscription)
        {
            AssertOriginalFailure(stopError, failure);
            var errors = disposalError.ShouldBeOfType<AggregateException>().Flatten().InnerExceptions;
            errors.ShouldNotBeEmpty();
            foreach (var error in errors) error.ShouldBeSameAs(failure);
        }
        else
        {
            stopError.ShouldBeNull();
            disposalError.ShouldBeNull();
        }
        log.Count(entry => entry == "subscriber disposed").ShouldBe(1);
        log.Count(entry => entry == "publisher disposed").ShouldBe(1);
        log.Count(entry => entry == "node disposed").ShouldBe(1);
        log.Count(entry => entry == "context disposed").ShouldBe(1);
        log.TakeLast(2).ShouldBe(new[] { "node disposed", "context disposed" });
    }

    private static ConfigurationRoot Config(string type) => (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Resources:ros:Type"] = "test.node",
            ["Workflows:main:node:Type"] = type,
            ["Workflows:main:node:Node"] = "ros",
            ["Workflows:main:node:Topic"] = "/test"
        }).Build();

    private static IRclSubscription<TestMessage> Subscription(Channel<TestMessage> queue, Action dispose)
        => Proxy<IRclSubscription<TestMessage>>((method, args) =>
        {
            if (method.Name == "Dispose") { dispose(); return null; }
            if (method.Name == "ReadAllAsync") return queue.Reader.ReadAllAsync((CancellationToken)args![0]!);
            throw new NotSupportedException(method.Name);
        });

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
    {
        if (typeof(T) == typeof(IRclNode)) return (T)(object)new TestNode(call);
        var result = DispatchProxy.Create<T, NativeProxy>();
        ((NativeProxy)(object)result).Call = call;
        return result;
    }

    public class NativeProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args);
    }
}
