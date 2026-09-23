using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class NativeControlRegistrationTests
{
    [Fact]
    public async Task Open_and_close_are_ordinary_native_inputs_and_need_no_terminal_control_helper()
    {
        var services = new ServiceCollection();
        services.AddComponentFactory("main.gate", _ => new NativeGate())
            .AddInputPort<NativeGate>("main.gate", "Input", node => node.Input)
            .AddInputPort<NativeGate>("main.gate", "Open", node => node.Open)
            .AddInputPort<NativeGate>("main.gate", "Close", node => node.Close)
            .AddOutputPort<NativeGate>("main.gate", "Output", node => node.Output);
        var received = OutputProbe.Register(services, "main.gate.Output");
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        var gate = (NativeGate)provider.GetRequiredKeyedService<IFlowNode>("main.gate");
        ((IFlowNode)gate).TerminalInputs.ShouldBeEmpty();
        await appExecution.StartAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42)));
        (await appExecution.GetTestInput("main.gate.Open").SendAsync(message, deadline.Token)).ShouldBeTrue();
        await gate.Opened.Task.WaitAsync(deadline.Token);
        (await appExecution.GetTestInput("main.gate.Input").SendAsync(message)).ShouldBeTrue();
        var reply = await received.Messages.ReceiveAsync(deadline.Token);
        reply.Value.Json.GetInt32().ShouldBe(42);

        (await appExecution.GetTestInput("main.gate.Close").SendAsync(message, deadline.Token)).ShouldBeTrue();
        await gate.Closed.Task.WaitAsync(deadline.Token);
        (await gate.Input.SendAsync(message, deadline.Token)).ShouldBeTrue();
        await gate.Dropped.Task.WaitAsync(deadline.Token);
        gate.Forwarded.ShouldBe(1);
        await appExecution.StopAsync(deadline.Token);
        gate.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    // Test fixture only: all three inputs are ordinary Dataflow blocks.
    private sealed class NativeGate : IFlowNode
    {
        private bool _open;
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Dropped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Forwarded { get; private set; }
        public ActionBlock<FlowMessage> Input { get; }
        public ActionBlock<FlowMessage> Open { get; }
        public ActionBlock<FlowMessage> Close { get; }
        public BufferBlock<FlowMessage> Output { get; } = new(new DataflowBlockOptions { BoundedCapacity = 1 });
        public Task Completion { get; }

        public NativeGate()
        {
            Open = new(_ => { Volatile.Write(ref _open, true); Opened.TrySetResult(); });
            Close = new(_ => { Volatile.Write(ref _open, false); Closed.TrySetResult(); });
            Input = new(async message =>
            {
                if (!Volatile.Read(ref _open)) { Dropped.TrySetResult(); return; }
                if (!await Output.SendAsync(message)) throw new InvalidOperationException("Output rejected.");
                Forwarded++;
            }, new ExecutionDataflowBlockOptions { BoundedCapacity = 1 });
            Completion = DrainAsync();
        }

        private async Task DrainAsync()
        {
            try
            {
                await Task.WhenAll(Input.Completion, Open.Completion, Close.Completion);
                Output.Complete();
                await Output.Completion;
            }
            catch (Exception error) { ((IDataflowBlock)Output).Fault(error); throw; }
        }
        public void Complete() { Input.Complete(); Open.Complete(); Close.Complete(); }
        public void Fault(Exception error)
        {
            foreach (IDataflowBlock block in new IDataflowBlock[] { Input, Open, Close, Output }) block.Fault(error);
        }
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }
}
