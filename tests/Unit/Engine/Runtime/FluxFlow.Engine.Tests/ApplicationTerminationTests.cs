using FluxFlow.Composition;
using System.Runtime.ExceptionServices;
using FluxFlow.Composition.Definition;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationTerminationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(1, false)]
    [InlineData(32, false)]
    [InlineData(32, true)]
    public async Task Stop_preserves_component_failure_when_input_drain_also_fails(int requests, bool delayObservation)
    {
        var node = new HeldNode(delayObservation);
        var builder = new ApplicationDefinitionBuilder();
        var component = builder.AddWorkflow("main").AddComponent("held", "test.held", context => { context.HasInput(node.Input); context.HasOutput(node.Output); context.HasEvents(node.Events); return ValueTask.FromResult<IFlowNode>(node); }, static _ => { }, static handle => new InputOutputComponentHandle<FlowValue, FlowValue>(handle, "Input", "Output", "Events"));
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(builder.Build(), options =>
        {
            options.StartWithHost = false;
            options.InputCapacity = 4;
            options.OutputCapacity = 4;
        });
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        try
        {
            (await host.StartAsync()).IsApplied.ShouldBeTrue();
            var ports = host.Ports;
            var pending = Enumerable.Range(0, requests).Select(index => ports.SendAndReceiveAsync<int, int>(
                "main.held.Input", "main.held.Output", FlowMessage.Create(index), Deadline)).ToArray();
            await node.Entered.Task.WaitAsync(Deadline);
            pending.ShouldAllBe(task => !task.IsCompleted);
            var original = new InvalidOperationException("original component failure");
            node.Fault(original);
            if (delayObservation)
            {
                await Should.ThrowAsync<InvalidOperationException>(() => node.Completion.WaitAsync(Deadline));
                using var deadline = new CancellationTokenSource(Deadline);
                while (ports.Status.Ports.Single(port => port.Address.Value == "main.held.Input").ActiveAttachments != 0)
                    await Task.Delay(1, deadline.Token);
            }
            var stopping = host.StopAsync().AsTask();
            node.ObserveFailure.TrySetResult();
            var replies = await Task.WhenAll(pending).WaitAsync(Deadline);
            replies.ShouldAllBe(reply => reply.Status == PortRequestStatus.OutputCompleted || reply.Status == PortRequestStatus.InputCompleted);
            replies.ShouldAllBe(reply => reply.Response == null);
            var failure = await Should.ThrowAsync<AggregateException>(() => stopping.WaitAsync(Deadline));
            failure.Flatten().InnerExceptions.ShouldContain(original);
            failure.Flatten().InnerExceptions.Count(error => ReferenceEquals(error, original)).ShouldBe(1);
            if (delayObservation)
                failure.Flatten().InnerExceptions.ShouldContain(error => error.Message.Contains("cannot drain without an active target"));
            node.Disposals.ShouldBe(1);
            node.Completion.IsFaulted.ShouldBeTrue();
            await provider.DisposeAsync();
            node.Disposals.ShouldBe(1);
        }
        finally
        {
            node.ObserveFailure.TrySetResult();
            if (!node.Completion.IsCompleted) node.Fault(new OperationCanceledException("Test cleanup."));
        }
    }

    private sealed class HeldNode : FlowNode, IFlowNode
    {
        private readonly Task _observedCompletion;
        public HeldNode(bool delayObservation) : base(new() { InputCapacity = 4, OutputCapacity = 4 })
        {
            _observedCompletion = ObserveAsync();
            if (!delayObservation) ObserveFailure.TrySetResult();
        }
        Task IFlowNode.Completion => _observedCompletion;
        public TaskCompletionSource ObserveFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Disposals;
        protected override async Task ProcessAsync(FlowMessage message)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, Stopping);
        }
        protected override ValueTask OnDisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
        private async Task ObserveAsync()
        {
            try { await Completion; }
            catch (Exception error)
            {
                await ObserveFailure.Task;
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }
    }
}
