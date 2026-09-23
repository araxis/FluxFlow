using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class NativeDeliveryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Fallback_delivery_is_exclusive_with_native_predicates()
    {
        var source = new PassThrough();
        var ordinary = new Sink();
        var fallback = new Sink();
        var (application, provider) = await BuildDeliveryAsync(Bindings(source, ordinary, fallback),
            FlowDeliveryPolicy.Reliable, fallback: true,
            predicate: message => message.Value.Json.GetInt32() > 0);
        await using var providerLifetime = provider;
        var applicationExecution = application;
        var messages = new[] { 1, -1, 2, 0 }.Select(value => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        await using (applicationExecution)
        {
            await applicationExecution.StartAsync();
            foreach (var message in messages)
                (await applicationExecution.GetTestInput("main.source.Input").SendAsync(message)).ShouldBeTrue();
            await applicationExecution.StopAsync().WaitAsync(Deadline);
            ordinary.Messages.ToArray().ShouldBe([messages[0], messages[2]]);
            fallback.Messages.ToArray().ShouldBe([messages[1], messages[3]]);
            ordinary.Messages.First().ShouldBeSameAs(messages[0]);
            fallback.Messages.First().ShouldBeSameAs(messages[1]);
            source.Calls.ShouldBe(4);
            applicationExecution.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        await provider.DisposeAsync();
        source.Disposals.ShouldBe(1);
        ordinary.Disposals.ShouldBe(1);
        fallback.Disposals.ShouldBe(1);
    }

    [Theory]
    [InlineData(FlowDeliveryPolicy.Reliable)]
    [InlineData(FlowDeliveryPolicy.RejectWhenFull)]
    [InlineData(FlowDeliveryPolicy.IsolateRejection)]
    [InlineData(FlowDeliveryPolicy.Reliable, true)]
    [InlineData(FlowDeliveryPolicy.RejectWhenFull, true)]
    [InlineData(FlowDeliveryPolicy.IsolateRejection, true)]
    public async Task Delivery_preserves_condition_failures_and_handler_isolation(FlowDeliveryPolicy delivery, bool fallback = false)
    {
        var failure = new InvalidOperationException("condition sentinel");
        var source = new PassThrough();
        var target = new Sink();
        var healthy = new Sink();
        var rejected = Channel.CreateUnbounded<(string Source, string Target, FlowMessage Message, Exception Error)>();
        Action<string, string, FlowMessage, Exception> report = (s, t, m, e) =>
        {
            rejected.Writer.TryWrite((s, t, m, e));
            throw new InvalidOperationException("handler sentinel");
        };
        var bindings = Bindings(source, target, healthy);
        var (application, provider) = await BuildDeliveryAsync(bindings, delivery, report,
            fallback, _ => throw failure);
        await using var providerLifetime = provider;
        var applicationExecution = application;
        try
        {
            await applicationExecution.StartAsync();
            var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(17, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            (await applicationExecution.GetTestInput("main.source.Input").SendAsync(message)).ShouldBeTrue();
            if (delivery == FlowDeliveryPolicy.IsolateRejection)
            {
                var rejection = await rejected.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
                rejection.Source.ShouldBe("main.source.Output");
                rejection.Target.ShouldBe("main.target.Input");
                rejection.Message.ShouldBeSameAs(message);
                rejection.Error.ShouldBeSameAs(failure);
                (await healthy.Received.Reader.ReadAsync().AsTask().WaitAsync(Deadline)).ShouldBeSameAs(message);
                await applicationExecution.StopAsync().WaitAsync(Deadline);
                applicationExecution.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                healthy.Messages.ShouldHaveSingleItem().ShouldBeSameAs(message);
            }
            else
            {
                (await Should.ThrowAsync<InvalidOperationException>(() => applicationExecution.Completion.WaitAsync(Deadline)))
                    .ShouldBeSameAs(failure);
                healthy.Messages.ShouldBeEmpty("Failed condition selection must precede sibling delivery.");
            }
            rejected.Reader.TryRead(out _).ShouldBeFalse();
            target.Messages.ShouldBeEmpty();
            source.Calls.ShouldBe(1);
        }
        finally
        {
            if (delivery == FlowDeliveryPolicy.IsolateRejection)
            {
                await applicationExecution.DisposeAsync();
                await provider.DisposeAsync();
            }
            else
            {
                var disposal = await Should.ThrowAsync<AggregateException>(() => applicationExecution.DisposeAsync().AsTask());
                disposal.Flatten().InnerExceptions.ShouldAllBe(error => ReferenceEquals(error, failure));
                (await Should.ThrowAsync<Exception>(() => provider.DisposeAsync().AsTask())).ShouldBeSameAs(failure);
            }
        }
        if (delivery == FlowDeliveryPolicy.IsolateRejection)
        {
            source.Disposals.ShouldBe(1);
            target.Disposals.ShouldBe(1);
            healthy.Disposals.ShouldBe(1);
        }
        else (source.Disposals + target.Disposals + healthy.Disposals).ShouldBe(1);
    }

    [Theory]
    [InlineData(FlowDeliveryPolicy.Reliable, 4)]
    [InlineData(FlowDeliveryPolicy.IsolateRejection, 2)]
    public async Task Native_delivery_honor_capacity_and_isolation(FlowDeliveryPolicy delivery, int capacity)
    {
        var source = new PassThrough();
        var target = new Sink(capacity, hold: true);
        var healthy = new Sink();
        var fillers = Enumerable.Range(0, capacity).Select(i => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(-i - 1, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        foreach (var filler in fillers) target.Input.Post(filler).ShouldBeTrue();
        var rejected = Channel.CreateUnbounded<(string Source, string Target, FlowMessage Message, Exception Error)>();
        Action<string, string, FlowMessage, Exception> report = (s, t, m, e) => rejected.Writer.TryWrite((s, t, m, e));
        var bindings = Bindings(source, target, healthy);
        var (application, provider) = await BuildDeliveryAsync(bindings, delivery, report);
        await using var providerLifetime = provider;
        var applicationExecution = application;
        try
        {
            await applicationExecution.StartAsync();
            var messages = new[] { FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(11, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(12, new JsonSerializerOptions(JsonSerializerDefaults.Web)))) };
            foreach (var message in messages)
            {
                (await applicationExecution.GetTestInput("main.source.Input").SendAsync(message)).ShouldBeTrue();
                (await healthy.Received.Reader.ReadAsync().AsTask().WaitAsync(Deadline)).ShouldBeSameAs(message);
                if (delivery == FlowDeliveryPolicy.IsolateRejection)
                {
                    var failure = await rejected.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
                    failure.Source.ShouldBe("main.source.Output");
                    failure.Target.ShouldBe("main.target.Input");
                    failure.Message.ShouldBeSameAs(message);
                    failure.Error.ShouldBeOfType<InvalidOperationException>();
                    failure.Error.Message.ShouldContain("rejected", Case.Insensitive);
                }
                else target.Release.TrySetResult();
            }
            target.Release.TrySetResult();
            await applicationExecution.StopAsync().WaitAsync(Deadline);
            await applicationExecution.Completion.WaitAsync(Deadline);
            target.Messages.ToArray().ShouldBe(delivery == FlowDeliveryPolicy.Reliable ? [.. fillers, .. messages] : fillers);
            healthy.Messages.ToArray().ShouldBe(messages);
            rejected.Reader.TryRead(out _).ShouldBeFalse();
            source.Calls.ShouldBe(2);
        }
        finally
        {
            target.Release.TrySetResult();
            await applicationExecution.DisposeAsync();
            await provider.DisposeAsync();
        }
        source.Disposals.ShouldBe(1);
        target.Disposals.ShouldBe(1);
        healthy.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Isolated_delivery_requires_handler_before_any_processing()
    {
        var source = new PassThrough();
        var target = new Sink();
        var healthy = new Sink();
        var bindings = Bindings(source, target, healthy);
        var error = await Should.ThrowAsync<ArgumentNullException>(() =>
            BuildDeliveryAsync(bindings, FlowDeliveryPolicy.IsolateRejection));
        error.ParamName.ShouldBe("onRejected");
        source.Calls.ShouldBe(0);
        source.Disposals.ShouldBe(1);
        target.Disposals.ShouldBe(1);
        healthy.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Immediate_native_delivery_faults_on_full_input_without_waiting()
    {
        var source = new PassThrough();
        var target = new Sink(capacity: 3, hold: true);
        var healthy = new Sink();
        foreach (var value in new[] { 1, 2, 3 }) target.Input.Post(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ShouldBeTrue();
        var bindings = Bindings(source, target, healthy);
        var reports = 0;
        Action<string, string, FlowMessage, Exception> report = (_, _, _, _) => Interlocked.Increment(ref reports);
        var (application, provider) = await BuildDeliveryAsync(bindings, FlowDeliveryPolicy.RejectWhenFull, report);
        await using var providerLifetime = provider;
        var applicationExecution = application;
        Exception? failure = null;
        try
        {
            await applicationExecution.StartAsync();
            var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(99, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            (await applicationExecution.GetTestInput("main.source.Input").SendAsync(message)).ShouldBeTrue();
            failure = await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Deadline));
            failure.Message.ShouldContain("rejected", Case.Insensitive);
            target.Release.Task.IsCompleted.ShouldBeFalse();
            target.Messages.ShouldNotContain(message);
            reports.ShouldBe(0);
            source.Calls.ShouldBe(1);
            target.Release.TrySetResult();
            (await Should.ThrowAsync<InvalidOperationException>(() => applicationExecution.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
        }
        finally
        {
            target.Release.TrySetResult();
            var disposal = await Should.ThrowAsync<AggregateException>(() => applicationExecution.DisposeAsync().AsTask());
            disposal.Flatten().InnerExceptions.ShouldAllBe(error => ReferenceEquals(error, failure));
            (await Should.ThrowAsync<Exception>(() => provider.DisposeAsync().AsTask())).ShouldBeSameAs(failure);
        }
        // Native DI stops on the first disposal exception; Application has already drained.
        (source.Disposals + target.Disposals + healthy.Disposals).ShouldBe(1);
    }

    private static async Task<(FlowApplication Application, ServiceProvider Provider)> BuildDeliveryAsync(
        ServiceCollection bindings, FlowDeliveryPolicy delivery,
        Action<string, string, FlowMessage, Exception>? report = null, bool fallback = false,
        Predicate<FlowMessage>? predicate = null)
    {
        bindings.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.target.Input", predicate ?? (message => message.Value.Json.GetInt32() > 0),
                delivery, delivery != FlowDeliveryPolicy.IsolateRejection || report is null ? null :
                    (message, error) => report("main.source.Output", "main.target.Input", message, error));
            if (fallback) ports.LinkFallback("main.source.Output", "main.healthy.Input");
            else ports.Link("main.source.Output", "main.healthy.Input");
        });
        var provider = bindings.BuildServiceProvider();
        try { return (await provider.BuildApplicationAsync(), provider); }
        catch { await provider.DisposeAsync(); throw; }
    }

    private static ServiceCollection Bindings(PassThrough source, Sink target, Sink? healthy = null)
    {
        var bindings = new ServiceCollection();
        bindings.AddComponentFactory("main.source", _ => source)
            .AddInputPort<PassThrough>("main.source", "Input", node => node.Input)
            .AddOutputPort<PassThrough>("main.source", "Output", node => node.Output)
            .AddEventPort<PassThrough>("main.source", node => node.Events);
        bindings.AddComponentFactory("main.target", _ => target)
            .AddInputPort<Sink>("main.target", "Input", node => node.Input);
        if (healthy is not null) bindings.AddComponentFactory("main.healthy", _ => healthy)
            .AddInputPort<Sink>("main.healthy", "Input", node => node.Input);
        return bindings;
    }

    private sealed class PassThrough : FlowNode
    {
        public int Calls { get; private set; }
        public int Disposals { get; private set; }
        protected override async Task ProcessAsync(FlowMessage message) { Calls++; await EmitAsync(message); }
        protected override ValueTask OnDisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Sink : IFlowNode
    {
        public Sink(int capacity = 8, bool hold = false)
        {
            if (!hold) Release.SetResult();
            Input = new ActionBlock<FlowMessage>(async message =>
            {
                Messages.Enqueue(message);
                Received.Writer.TryWrite(message);
                await Release.Task;
            }, new ExecutionDataflowBlockOptions { BoundedCapacity = capacity });
        }
        public ActionBlock<FlowMessage> Input { get; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Channel<FlowMessage> Received { get; } = Channel.CreateUnbounded<FlowMessage>();
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public int Disposals { get; private set; }
        public Task Completion => Input.Completion;
        public void Complete() => Input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)Input).Fault(error);
        public async ValueTask DisposeAsync() { Disposals++; Release.TrySetResult(); Complete(); await Completion; }
    }
}
