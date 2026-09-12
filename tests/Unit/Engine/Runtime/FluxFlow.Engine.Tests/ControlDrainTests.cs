using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ControlDrainTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Revision_candidate_fallback_drain_keeps_controls_open_until_work_finishes()
    {
        var source = new ControlledSource();
        var worker = new HeldWorker();
        using var inputLink = source.Output.LinkTo(worker.Input, new DataflowLinkOptions { PropagateCompletion = true });
        using var controlLink = worker.Output.LinkTo(source.Ack, new DataflowLinkOptions());
        await using var ports = new ApplicationPortRuntimeBuilder().Build();
        await using var builder = ports.CreateRevision("controls");
        await using var candidate = new ApplicationRuntimeRevisionCandidate(
            [new(source), new(worker)], builder.Build(), [], null, () => ValueTask.CompletedTask, null);
        Task stopping;
        try
        {
            await candidate.ActivateAsync();
            await worker.Entered.Task.WaitAsync(Deadline);
            stopping = candidate.DrainAsync().AsTask();
            await AssertDrainAsync(source, worker, stopping);
        }
        finally { worker.Release.TrySetResult(); source.Release.TrySetResult(); }
        await stopping.WaitAsync(Deadline);
        source.Accepted.ShouldBeSameAs(source.Message);
        candidate.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        source.DisposeCalls.ShouldBe(0);
        await candidate.DisposeAsync();
        source.DisposeCalls.ShouldBe(1);
        worker.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Instance_first_application_drains_native_controls_after_business_work()
    {
        var source = new ControlledSource();
        var worker = new HeldWorker();
        await using var runtime = new FlowApplication();
        var workflow = runtime.AddWorkflow("main");
        var producer = workflow.Add("source", source);
        var consumer = workflow.Add("worker", worker);
        producer.Output.LinkTo(consumer.Input);
        consumer.Output.LinkTo(producer.Inputs["Ack"]);
        Task stopping;
        try
        {
            await runtime.StartAsync();
            await worker.Entered.Task.WaitAsync(Deadline);
            stopping = runtime.StopAsync();
            await AssertDrainAsync(source, worker, stopping);
        }
        finally { worker.Release.TrySetResult(); source.Release.TrySetResult(); }
        await stopping.WaitAsync(Deadline);
        source.Accepted.ShouldBeSameAs(source.Message);
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        worker.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Ack", null, false)]
    [InlineData("Terminal", null, false)]
    [InlineData("Ack", "Events", false)]
    [InlineData("Terminal", "Activity", false)]
    [InlineData("Ack", "Events", true)]
    [InlineData("Terminal", "Activity", true)]
    public async Task Update_host_stop_drains_controls_by_native_identity(string controlName, string? eventName, bool linkEvents)
    {
        var source = new ControlledSource();
        var worker = new HeldWorker();
        var recorder = new EventRecorder();
        var eventLink = linkEvents ? $",\"{eventName}\":\"Recorder.Input\""
            : eventName is not null ? $",\"DiscardUnmatched\":[\"{eventName}\"]" : "";
        var definition = ApplicationDefinitionJson.Deserialize(
            $$$$"""
            {"Resources":{},"Workflows":{"Main":{
              "Source":{"Type":"test.control-source","Ack":"Worker.Output"{{{{eventLink}}}}},
              "Worker":{"Type":"test.held-worker","Input":"Source.Output"},
              "Recorder":{"Type":"test.event-recorder"}
            }}}
            """.Replace("\"Ack\":", $"\"{controlName}\":", StringComparison.Ordinal));
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(definition).AddTestRuntimeAssembler(registration =>
        {
            var components = registration.AddFluxFlowComponents().Advanced;
            components.AddDynamicComponent("test.control-source", component =>
            {
                var ports = component.UseFactory(_ => source)
                    .HasOutput("Output", node => node.Output)
                    .HasSignalInput(controlName, node => node.Ack);
                if (eventName is not null) ports.HasEvents(eventName, node => node.Events);
            });
            components.AddDynamicComponent("test.held-worker", component => component
                    .UseFactory(_ => worker)
                    .HasInput("Input", node => node.Input)
                    .HasOutput("Output", node => node.Output));
            components.AddDynamicComponent("test.event-recorder", component => component
                .UseFactory(_ => recorder).HasInput("Input", node => node.Input));
        });
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<FluxFlowApplication>();
        using var stopDeadline = new CancellationTokenSource(Deadline);
        using var abort = stopDeadline.Token.Register(() =>
        {
            var timeout = new TimeoutException("Controlled shutdown did not drain before the test deadline.");
            source.Fault(timeout);
            worker.Fault(timeout);
        });
        Task stopping;
        try
        {
            (await host.StartAsync()).IsApplied.ShouldBeTrue();
            await worker.Entered.Task.WaitAsync(Deadline);
            stopping = host.StopAsync(stopDeadline.Token).AsTask();
            await AssertDrainAsync(source, worker, stopping);
        }
        finally { worker.Release.TrySetResult(); source.Release.TrySetResult(); }
        await stopping.WaitAsync(Deadline);
        source.Accepted.ShouldBeSameAs(source.Message);
        source.DisposeCalls.ShouldBe(1);
        worker.DisposeCalls.ShouldBe(1);
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        source.Events.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        recorder.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        recorder.DisposeCalls.ShouldBe(1);
        if (linkEvents)
        {
            recorder.Messages.ShouldHaveSingleItem();
            recorder.Messages[0].MessageId.ShouldBe(source.ControlEvent.MessageId);
            recorder.Messages[0].Value.ShouldBeSameAs(source.ControlEvent.Value);
        }
        else recorder.Messages.ShouldBeEmpty();
        host.State.ShouldBe(ApplicationState.Stopped);
    }

    private static async Task AssertDrainAsync(ControlledSource source, HeldWorker worker, Task stopping)
    {
        await source.ProductionCompletion.WaitAsync(Deadline);
        source.Ack.Completion.IsCompleted.ShouldBeFalse();
        source.Accepted.ShouldBeNull();
        stopping.IsCompleted.ShouldBeFalse();
        worker.Release.TrySetResult();
        await source.Entered.Task.WaitAsync(Deadline);
        source.Completion.IsCompleted.ShouldBeFalse();
        stopping.IsCompleted.ShouldBeFalse();
        source.DisposeCalls.ShouldBe(0);
    }

    private sealed class ControlledSource : FlowSource
    {
        public ControlledSource() => Ack = AddControlInput("Ack", async (message, token) =>
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
            Accepted = message;
            EmitEvent(ControlEvent);
        }, 1);
        public ITargetBlock<FlowMessage> Ack { get; }
        public FlowMessage Message { get; } = FlowMessage.Create(FlowValue.From("accepted"));
        public FlowMessage ControlEvent { get; } = FlowMessage.Create(FlowValue.From("control-finished"));
        public FlowMessage? Accepted { get; private set; }
        public TaskCompletionSource Entered { get; } = Gate();
        public TaskCompletionSource Release { get; } = Gate();
        public int DisposeCalls { get; private set; }
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            await EmitAsync(Message, cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        protected override ValueTask OnDisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }

    private sealed class EventRecorder : FlowNode
    {
        public List<FlowMessage> Messages { get; } = [];
        public int DisposeCalls { get; private set; }
        protected override Task ProcessAsync(FlowMessage message) { Messages.Add(message); return Task.CompletedTask; }
        protected override ValueTask OnDisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }

    private sealed class HeldWorker : FlowNode
    {
        public TaskCompletionSource Entered { get; } = Gate();
        public TaskCompletionSource Release { get; } = Gate();
        public int DisposeCalls { get; private set; }
        protected override async Task ProcessAsync(FlowMessage message)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(Stopping);
            await EmitAsync(message, Stopping);
        }
        protected override ValueTask OnDisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }
}
