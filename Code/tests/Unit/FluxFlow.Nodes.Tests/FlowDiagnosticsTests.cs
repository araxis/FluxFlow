using FluxFlow.Core.Nodes;
using System.Text.Json;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;
[Collection(ProcessingTraceCollection.Name)]
public sealed class FlowDiagnosticsTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Original_exception_is_observable_without_changing_flow(bool transform, bool tracing, bool throwingObserver)
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(activityListener);
        var observations = new List<FlowProcessingException>();
        using var diagnostics = new Subscription(value =>
        {
            observations.Add(value);
            if (throwingObserver)
                throw new InvalidOperationException("observer failed");
        });
        var original = new InvalidOperationException("processing failed", new ArgumentException("inner"));
        await using IFlowNode node = transform ? new TransformProbe(original) : new ActionProbe(original);
        var output = new BufferBlock<FlowMessage>();
        var input = node is ActionProbe action ? action.Input : ((TransformProbe)node).Input;
        using var link = ((FlowNode)node).Output.LinkTo(output);
        var bad = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        var good = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var upstream = FlowMessage.Create(new FlowError("upstream", "upstream", "processing"));
        await input.SendAsync(bad);
        await input.SendAsync(good);
        await input.SendAsync(upstream);
        var failure = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        var success = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(upstream);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        failure.Error!.Code.ShouldBe("node.processing_failed");
        failure.CausationId.ShouldBe(bad.MessageId);
        success.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(2);
        var observed = observations.ShouldHaveSingleItem();
        observed.Exception.ShouldBeSameAs(original);
        observed.Exception.InnerException.ShouldBeSameAs(original.InnerException);
        observed.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.ComponentType.ShouldBe(node.GetType().FullName);
        observed.MessageId.ShouldBe(bad.MessageId);
        observed.CorrelationId.ShouldBe(bad.CorrelationId);
        observed.TraceId.ShouldBe(bad.TraceId);
        if (tracing)
        {
            observed.Activity.ShouldNotBeNull().Status.ShouldBe(ActivityStatusCode.Error);
            observed.Activity.IsStopped.ShouldBeTrue();
        }
        else
            observed.Activity.ShouldBeNull();
    }

    private sealed class ActionProbe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public ActionProbe(Exception error)
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
            this.error = error;
            Lifetime.Observe();
        }

        private readonly Exception error;
        private async Task ProcessAsync(FlowMessage message)
        {
            if (message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) == 1)
                throw error;
            await EmitAsync(message.WithValue(message.Value), Stopping);
        }
    }

    private sealed class TransformProbe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public TransformProbe(Exception error)
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            this.error = error;
            Lifetime.Observe();
        }

        private readonly Exception error;
        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        {
            if (message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) == 1)
                throw error;
            return Task.FromResult<IEnumerable<FlowMessage>>([message.WithValue(message.Value)]);
        }
    }

    private sealed class Subscription : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly Action<FlowProcessingException> _observe;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _discovery;
        public Subscription(Action<FlowProcessingException> observe)
        {
            _observe = observe;
            _discovery = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == FlowDiagnostics.ListenerName)
                _subscriptions.Add(listener.Subscribe(this, name => name == FlowDiagnostics.ProcessingExceptionEvent));
        }

        public void OnNext(KeyValuePair<string, object?> value) => _observe((FlowProcessingException)value.Value!);
        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        public void Dispose()
        {
            _discovery.Dispose();
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
        }
    }
}
