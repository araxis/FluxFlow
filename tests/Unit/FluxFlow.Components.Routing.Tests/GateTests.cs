using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class GateTests
{
    [Fact]
    public void Gate_does_not_inherit_application_registration()
        => typeof(Gate).GetMethods().ShouldNotContain(method => method.Name == "RegisterPorts");

    private static FlowMessage Message() => FlowMessage.Create(JsonFlowValue.Null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gate_state_applies_to_error_envelopes_too(bool open)
    {
        await using var gate = new Gate(open);
        var output = new BufferBlock<FlowMessage>();
        using var link = gate.Output.LinkTo(output);
        var error = FlowMessage.Create(new FlowError("test", "failure", "test"));
        await gate.Input.SendAsync(error);
        gate.Complete();
        await gate.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        if (open) (await output.ReceiveAsync()).ShouldBeSameAs(error);
        output.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Native_cancellation_closes_all_gate_inputs()
    {
        using var cancellation = new CancellationTokenSource();
        await using var gate = new Gate(options: new ExecutionDataflowBlockOptions { CancellationToken = cancellation.Token });
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => gate.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        foreach (var input in new[] { gate.Input, gate.Open, gate.Close })
            (await input.SendAsync(Message())).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initial_state_controls_exact_message_forwarding(bool open)
    {
        await using var gate = new Gate(initiallyOpen: open);
        var output = new BufferBlock<FlowMessage>();
        using var link = gate.Output.LinkTo(output);
        var message = Message();
        (await gate.Input.SendAsync(message)).ShouldBeTrue();
        gate.Complete();
        await gate.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        if (open) (await output.ReceiveAsync()).ShouldBeSameAs(message);
        output.Count.ShouldBe(0);
        gate.Open.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        gate.Close.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        (await gate.Open.SendAsync(Message())).ShouldBeFalse();
    }

    [Fact]
    public async Task Addressed_commands_change_state_and_registration_uses_native_ports()
    {
        var services = new ServiceCollection();
        services.AddComponentFactory("main.gate", _ => new Gate())
            .AddInputPort<Gate>("main.gate", "Input", node => node.Input)
            .AddInputPort<Gate>("main.gate", "Enable", node => node.Open)
            .AddInputPort<Gate>("main.gate", "Disable", node => node.Close)
            .AddOutputPort<Gate>("main.gate", "Output", node => node.Output)
            .AddEventPort<Gate>("main.gate", node => node.Events);
        var received = OutputProbe.Register(services, "main.gate.Output");
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        var gate = (Gate)provider.GetRequiredKeyedService<IFlowNode>("main.gate");
        provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.gate.Enable").ShouldBeSameAs(gate.Open);
        provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.gate.Disable").ShouldBeSameAs(gate.Close);
        ((IFlowNode)gate).TerminalInputs.ShouldBeEmpty();
        await appExecution.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        (await appExecution.GetTestInput("main.gate.Enable").SendAsync(Message(), timeout.Token)).ShouldBeTrue();
        await WaitState(gate, true, timeout.Token);
        var message = Message();
        (await appExecution.GetTestInput("main.gate.Input").SendAsync(message)).ShouldBeTrue();
        var reply = await received.Messages.ReceiveAsync(timeout.Token);
        reply.Value.ShouldBeSameAs(message.Value);
        (await appExecution.GetTestInput("main.gate.Disable").SendAsync(Message(), timeout.Token)).ShouldBeTrue();
        await WaitState(gate, false, timeout.Token);
        (await appExecution.GetTestInput("main.gate.Enable").SendAsync(Message(), timeout.Token)).ShouldBeTrue();
        await WaitState(gate, true, timeout.Token);
        await appExecution.StopAsync(timeout.Token);
        gate.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Fault_closes_all_inputs_and_completion_reports_original_exception()
    {
        await using var gate = new Gate();
        var failure = new InvalidOperationException("stop");
        gate.Fault(failure);
        (await Should.ThrowAsync<InvalidOperationException>(() => gate.Completion.WaitAsync(TimeSpan.FromSeconds(5))))
            .ShouldBeSameAs(failure);
        foreach (var input in new[] { gate.Input, gate.Open, gate.Close })
            (await input.SendAsync(Message())).ShouldBeFalse();
    }

    private static async Task WaitState(Gate gate, bool open, CancellationToken token)
    {
        while (gate.IsOpen != open) await Task.Delay(1, token);
    }
}
