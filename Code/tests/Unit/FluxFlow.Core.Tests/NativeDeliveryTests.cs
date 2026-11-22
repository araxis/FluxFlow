using FluxFlow.Application.Addressing;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Execution;
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
    public async Task Conditional_and_unconditional_links_deliver_independently()
    {
        var source = new PassThrough();
        var ordinary = new Sink();
        var all = new Sink();
        var(application, provider) = await BuildDeliveryAsync(Bindings(source, ordinary, all), predicate: message => message.Value.Json.GetInt32() > 0);
        await using var providerLifetime = provider;
        var applicationExecution = application;
        var messages = new[]
        {
            1,
            -1,
            2,
            0
        }.Select(value => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        await using (applicationExecution)
        {
            await applicationExecution.StartAsync();
            foreach (var message in messages)
                (await applicationExecution.GetTestInput("main.source.Input").SendAsync(message)).ShouldBeTrue();
            await applicationExecution.StopAsync().WaitAsync(Deadline);
            ordinary.Messages.ToArray().ShouldBe([messages[0], messages[2]]);
            all.Messages.ToArray().ShouldBe(messages);
            ordinary.Messages.First().ShouldBeSameAs(messages[0]);
            all.Messages.First().ShouldBeSameAs(messages[0]);
            source.Calls.ShouldBe(4);
            applicationExecution.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }

        await provider.DisposeAsync();
        source.Disposals.ShouldBe(1);
        ordinary.Disposals.ShouldBe(1);
        all.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Broadcast_delivery_does_not_wait_for_full_consumer()
    {
        var source = new PassThrough();
        const int capacity = 4;
        var target = new Sink(capacity, hold: true);
        var healthy = new Sink();
        var fillers = Enumerable.Range(0, capacity).Select(i => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(-i - 1, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        foreach (var filler in fillers)
            target.Input.Post(filler).ShouldBeTrue();
        var bindings = Bindings(source, target, healthy);
        var(application, provider) = await BuildDeliveryAsync(bindings);
        await using var providerLifetime = provider;
        var applicationExecution = application;
        try
        {
            await applicationExecution.StartAsync();
            var messages = new[]
            {
                FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(11, new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
                FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(12, new JsonSerializerOptions(JsonSerializerDefaults.Web))))
            };
            foreach (var message in messages)
            {
                (await applicationExecution.GetTestInput("main.source.Input").SendAsync(message)).ShouldBeTrue();
                (await healthy.Received.Reader.ReadAsync().AsTask().WaitAsync(Deadline)).ShouldBeSameAs(message);
            }

            source.Complete();
            await source.Completion.WaitAsync(Deadline);
            target.Input.Complete();
            target.Release.TrySetResult();
            await applicationExecution.StopAsync().WaitAsync(Deadline);
            await applicationExecution.Completion.WaitAsync(Deadline);
            target.Messages.ToArray().ShouldBe(fillers);
            healthy.Messages.ToArray().ShouldBe(messages);
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

    private static async Task<(FlowApplication Application, ServiceProvider Provider)> BuildDeliveryAsync(ServiceCollection bindings, Predicate<FlowMessage>? predicate = null)
    {
        bindings.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.target.Input"), predicate ?? (message => message.Value.Json.GetInt32() > 0));
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.healthy.Input"));
        });
        var provider = bindings.BuildServiceProvider();
        try
        {
            return (await provider.BuildApplicationAsync(), provider);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    private static ServiceCollection Bindings(PassThrough source, Sink target, Sink? healthy = null)
    {
        var bindings = new ServiceCollection();
        bindings.AddComponentFactory(ComponentAddress.Parse("main.source"), _ => source).AddInputPort<PassThrough>(ComponentAddress.Parse("main.source"), "Input", node => node.Input).AddOutputPort<PassThrough>(ComponentAddress.Parse("main.source"), "Output", node => node.Output).AddEventPort<PassThrough>(ComponentAddress.Parse("main.source"), node => node.Events);
        bindings.AddComponentFactory(ComponentAddress.Parse("main.target"), _ => target).AddInputPort<Sink>(ComponentAddress.Parse("main.target"), "Input", node => node.Input);
        if (healthy is not null)
            bindings.AddComponentFactory(ComponentAddress.Parse("main.healthy"), _ => healthy).AddInputPort<Sink>(ComponentAddress.Parse("main.healthy"), "Input", node => node.Input);
        return bindings;
    }

    private sealed class PassThrough : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public PassThrough()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        public int Calls { get; private set; }
        public int Disposals { get; private set; }

        private async Task ProcessAsync(FlowMessage message)
        {
            Calls++;
            await EmitAsync(message);
        }

        protected override ValueTask OnDisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Sink : IFlowNode
    {
        public Sink(int capacity = 8, bool hold = false)
        {
            if (!hold)
                Release.SetResult();
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
        public async ValueTask DisposeAsync()
        {
            Disposals++;
            Release.TrySetResult();
            Complete();
            await Completion;
        }
    }
}
