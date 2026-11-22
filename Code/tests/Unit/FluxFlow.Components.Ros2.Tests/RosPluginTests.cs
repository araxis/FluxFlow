using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Ros2;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Plugins.Ros2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rcl;
using Shouldly;
using Xunit;
using RosString = FluxFlow.Plugins.Ros2.Messages.Std.String;

namespace FluxFlow.Components.Ros2.Tests;

public sealed partial class RosTopicTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Plugin_relays_standard_strings_and_restarts_without_old_deliveries(bool fluent)
    {
        var queues = new List<Channel<RosString>>();
        var published = Channel.CreateUnbounded<RosString>();
        var disposed = new ConcurrentQueue<string>();
        var node = Proxy<IRclNode>((method, _) =>
        {
            if (method.Name == "Dispose") { disposed.Enqueue("node"); return null; }
            if (method.Name == "CreatePublisher") return Proxy<IRclPublisher<RosString>>((call, args) =>
            {
                if (call.Name == "Dispose") { disposed.Enqueue("publisher"); return null; }
                if (call.Name == "PublishAsync") { published.Writer.TryWrite((RosString)args![0]!); return ValueTask.CompletedTask; }
                throw new NotSupportedException(call.Name);
            });
            if (method.Name == "CreateSubscription")
            {
                var queue = Channel.CreateUnbounded<RosString>();
                queues.Add(queue);
                return Proxy<IRclSubscription<RosString>>((call, args) =>
                {
                    if (call.Name == "Dispose") { disposed.Enqueue("subscriber"); return null; }
                    if (call.Name == "ReadAllAsync") return queue.Reader.ReadAllAsync((CancellationToken)args![0]!);
                    throw new NotSupportedException(call.Name);
                });
            }
            throw new NotSupportedException(method.Name);
        });
        var context = Proxy<IRclContext>((method, _) =>
        {
            if (method.Name == "CreateNode") return node;
            if (method.Name == "DisposeAsync") { disposed.Enqueue("context"); return ValueTask.CompletedTask; }
            throw new NotSupportedException(method.Name);
        });
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("""
            {"Resources":{"ros":{"Type":"ros.node","Name":"test"}},"Workflows":{"main":{
              "receive":{"Type":"ros.subscriber","Node":"ros","Topic":"/input","Output":"send.Input"},
              "send":{"Type":"ros.publisher","Node":"ros","Topic":"/output"}}}}
            """))).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IRclContext>(_ => context);
        if (fluent)
        {
            services.AddRosNodeResource("Resources.ros", new() { Name = "test" });
            new WorkflowBuilder(services, "main")
                .RosSubscriber("receive", new() { Node = "ros", Topic = "/input" })
                .RosPublisher("send", new() { Node = "ros", Topic = "/output" })
                .Link("receive.Output", "send.Input");
        }
        else
        {
            new RosPlugin().ConfigureWorkflow(services);
            services.AddFluxFlowApplication(configuration);
        }
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync().WaitAsync(Deadline);
        var workflow = provider.GetRequiredKeyedService<FlowWorkflow>("main");
        for (var run = 0; run < 2; run++)
        {
            if (run > 0) await workflow.StartAsync().WaitAsync(Deadline);
            var publisher = workflow.Services.GetRequiredKeyedService<IFlowNode>("main.send").ShouldBeOfType<RosPublisher<RosString>>();
            var capture = new BufferBlock<FlowMessage>();
            using var link = publisher.Output.LinkTo(capture);
            var text = "Unicode λ \n\"" + run;
            await queues[run].Writer.WriteAsync(new RosString { Data = text });
            (await published.Reader.ReadAsync().AsTask().WaitAsync(Deadline)).Data.ShouldBe(text);
            var result = await capture.ReceiveAsync(Deadline);
            result.IsError.ShouldBeFalse();
            result.Value.Json.EnumerateObject().Select(property => property.Name).ShouldBe(["Data"]);
            result.Value.Json.GetProperty("Data").GetString().ShouldBe(text);
            await workflow.StopAsync().WaitAsync(Deadline);
            disposed.Count(value => value == "subscriber").ShouldBe(run + 1);
            disposed.Count(value => value == "publisher").ShouldBe(run + 1);
            // A late native delivery stays on the old endpoint, never the restarted subscription.
            queues[run].Writer.TryWrite(new RosString { Data = "stale" }).ShouldBeTrue();
        }
        published.Reader.TryRead(out _).ShouldBeFalse();
        await app.StopAsync().WaitAsync(Deadline);
        await provider.DisposeAsync();
        disposed.TakeLast(2).ShouldBe(["node", "context"]);
    }

    [Fact]
    public void Plugin_preserves_custom_typed_registrations_and_does_not_duplicate_component_keys()
    {
        var services = new ServiceCollection().AddRosPublisher<TestMessage>().AddRosSubscriber<TestMessage>();
        var original = services.Where(item => item.ServiceType == typeof(ComponentRegistration)).ToArray();
        var plugin = new RosPlugin();
        plugin.ConfigureWorkflow(services);
        plugin.ConfigureWorkflow(services);
        services.Where(item => item.ServiceType == typeof(ComponentRegistration)).ShouldBe(original);
    }

    [Theory]
    [InlineData("{\"Data\":\"λ\\nhello\"}", false)]
    [InlineData("{\"data\":\"\"}", false)]
    [InlineData("{}", false)]
    [InlineData("{\"Data\":null}", true)]
    [InlineData("null", true)]
    [InlineData("[]", true)]
    [InlineData("{\"Other\":42}", true)]
    [InlineData("{\"Data\":42}", true)]
    [InlineData("\"hello\"", true)]
    public async Task Standard_string_input_uses_existing_strict_JSON_conversion(string json, bool invalid)
    {
        var sent = new List<RosString>();
        var native = Proxy<IRclPublisher<RosString>>((method, args) =>
        {
            if (method.Name == "Dispose") return null;
            sent.Add((RosString)args![0]!);
            return ValueTask.CompletedTask;
        });
        await using var publisher = new RosPublisher<RosString>(new() { Node = Proxy<IRclNode>((_, _) => native), Topic = "/test" });
        var capture = new BufferBlock<FlowMessage>();
        using var link = publisher.Output.LinkTo(capture);
        var input = FlowMessage.Create(new JsonFlowValue(JsonSerializer.Deserialize<JsonElement>(json)));
        await publisher.Input.SendAsync(input);
        var output = await capture.ReceiveAsync(Deadline);
        output.TraceId.ShouldBe(input.TraceId);
        output.IsError.ShouldBe(invalid);
        sent.Count.ShouldBe(invalid ? 0 : 1);
        if (!invalid) output.ShouldBeSameAs(input);
        else
        {
            var next = Message(new { Data = "after invalid input" });
            await publisher.Input.SendAsync(next);
            (await capture.ReceiveAsync(Deadline)).ShouldBeSameAs(next);
            sent.Single().Data.ShouldBe("after invalid input");
        }
    }

    private static void AssertOriginalFailure(Exception? actual, Exception original)
    {
        // .NET 11 Host.StopAsync also retains background-service failures in an aggregate.
        if (actual is AggregateException aggregate)
        {
            var errors = aggregate.Flatten().InnerExceptions;
            errors.ShouldNotBeEmpty();
            foreach (var error in errors) error.ShouldBeSameAs(original);
        }
        else actual.ShouldBeSameAs(original);
    }
}
