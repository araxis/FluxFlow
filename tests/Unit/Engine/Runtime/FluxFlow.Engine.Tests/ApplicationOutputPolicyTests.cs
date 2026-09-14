using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed partial class ApplicationOutputDispatchTests
{
    [Fact]
    public async Task Automatic_replies_remain_active_across_business_route_revisions()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var routing = new ApplicationRevisionRouting();
        await using var exchanges = new FlowRequestReply();
        var output = new ApplicationOutputPort(Address, 2, static _ => { }, static _ => { }, routing, exchanges: exchanges);
        output.EnableRequestReplies();
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        var laterDeliveries = new List<FlowMessage>();
        SetRoutes(routing, [new DelegateRoute(0, async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        })]);
        try
        {
            var first = exchanges.RequestAsync(Address.Value, Messages(1)[0], async (message, token) =>
            {
                (await source.Target.SendAsync(message, token)).ShouldBeTrue();
            }, Deadline);
            await entered.Task.WaitAsync(Deadline);
            await first.WaitAsync(Deadline);
            exchanges.PendingCount.ShouldBe(0);
            await using (await output.BeginRevisionAsync(CancellationToken.None).AsTask().WaitAsync(Deadline))
                SetRoutes(routing, [new DelegateRoute(1, (message, _) =>
                {
                    laterDeliveries.Add(message);
                    return ValueTask.CompletedTask;
                })]);
            release.SetResult();
            await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            laterDeliveries.ShouldBeEmpty();
            var second = await exchanges.RequestAsync(Address.Value, Messages(1)[0], async (message, token) =>
            {
                (await source.Target.SendAsync(message, token)).ShouldBeTrue();
            }, Deadline);
            await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            laterDeliveries.ShouldHaveSingleItem().ShouldBeSameAs(second);
            exchanges.PendingCount.ShouldBe(0);
            output.Complete();
            await output.Completion.WaitAsync(Deadline);
        }
        finally { release.TrySetResult(); output.Abort(); }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unmatched_output_requires_explicit_handling(bool discard, bool conditional)
    {
        var capture = new ControlledCapture();
        capture.Release.SetResult();
        var routing = new ApplicationRevisionRouting();
        var offers = 0;
        if (conditional) SetRoutes(routing, [new DelegateRoute(0,
            (_, _) => { offers++; return ValueTask.CompletedTask; }, matches: _ => false)]);
        var output = new ApplicationOutputPort(Address, 2, static _ => { }, static _ => { },
            routing, capture, discardUnmatched: discard);
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        using var receive = output.RegisterReceive();
        await using var observation = output.Observe<FlowValue>(2).Observation.ShouldNotBeNull();
        var message = Messages(1)[0];
        try
        {
            (await source.Target.SendAsync(message)).ShouldBeTrue();
            if (discard)
            {
                await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
                (await receive.Task.WaitAsync(Deadline)).Message.ShouldBeSameAs(message);
                (await observation.Messages.ReceiveAsync().WaitAsync(Deadline)).Value.ShouldBeSameAs(message.Value);
                output.Completion.IsCompleted.ShouldBeFalse();
                output.Complete();
                await output.Completion.WaitAsync(Deadline);
            }
            else
            {
                var failure = await Should.ThrowAsync<InvalidOperationException>(() => output.Completion.WaitAsync(Deadline));
                failure.Message.ShouldContain(Address.Value);
                failure.Message.ShouldContain("No route accepted");
                (await receive.Task.WaitAsync(Deadline)).Status.ShouldBe(PortReceiveStatus.Completed);
                (await Should.ThrowAsync<InvalidOperationException>(() => observation.Completion.WaitAsync(Deadline)))
                    .ShouldBeSameAs(failure);
                (await source.Target.SendAsync(message)).ShouldBeFalse();
            }
            capture.Messages.ShouldHaveSingleItem().ShouldBeSameAs(message);
            offers.ShouldBe(0);
        }
        finally { output.Abort(); }
    }
}

public sealed partial class ApplicationPortRuntimeTests
{
    [Fact]
    public async Task Output_policy_revisions_change_only_at_activation()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().AddOutput(Output).AddInput(Input).Build();
        var source = new BufferBlock<FlowMessage>();
        using var outputAttachment = runtime.AttachOutput(Output, source);
        var processed = 0;
        var processor = new ActionBlock<FlowMessage>(async message =>
        {
            Interlocked.Increment(ref processed);
            (await source.SendAsync(message)).ShouldBeTrue();
        }, new ExecutionDataflowBlockOptions { BoundedCapacity = 2 });
        await using var inputAttachment = await runtime.AttachInputAsync(Input, processor);
        var addresses = new[] { Output };
        var initial = runtime.PrepareRevisionRouting([], addresses);
        addresses[0] = Input;
        initial.DiscardedOutputs.ShouldBe([Output]);
        var next = runtime.PrepareRevisionRouting([]);
        initial.GetChangedSources(next).ShouldBe([Output]);
        initial.GetChangedTargets(next).ShouldBeEmpty();
        await using var firstBuilder = runtime.CreateRevision("discard");
        await using var first = firstBuilder.SetLinks([], discardedOutputs: [Output]).Build();
        await using var firstLease = await first.ActivateAsync();
        await using var nextBuilder = runtime.CreateRevision("reply");
        await using var prepared = nextBuilder.SetLinks([]).Build();
        var request = FlowMessage.Create("reply");
        (await runtime.SendAndReceiveAsync<string, string>(
            Input, Output, request, TimeSpan.FromSeconds(5))).Status.ShouldBe(PortRequestStatus.Received);
        processed.ShouldBe(1);
        runtime.CurrentRevision.ShouldNotBeNull().RevisionId.ShouldBe("discard");
        await using var nextLease = await prepared.ActivateAsync();
        var reply = await runtime.SendAndReceiveAsync<string, string>(Input, Output, request, TimeSpan.FromSeconds(5));
        reply.Status.ShouldBe(PortRequestStatus.Received);
        reply.Response.ShouldNotBeNull().Value.ShouldBe("reply");
        reply.Response.TraceId.ShouldBe(request.TraceId);
        reply.Response.CorrelationId.ShouldNotBeNull();
        processed.ShouldBe(2);
        await using var lastBuilder = runtime.CreateRevision("terminal");
        await using var last = lastBuilder.SetLinks([], discardedOutputs: [Output]).Build();
        await using var lastLease = await last.ActivateAsync();
        (await runtime.SendAndReceiveAsync<string, string>(
            Input, Output, request, TimeSpan.FromSeconds(5))).Status.ShouldBe(PortRequestStatus.Received);
        processed.ShouldBe(3);
        processor.Complete();
        await processor.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        source.Complete();
        runtime.Complete();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Automatic_replies_settle_requests_and_route_late_messages_to_fallback(bool revision)
    {
        var fallbackAddress = ApplicationAddress.WorkflowPort("Main", "Fallback", "Input");
        await using var runtime = new ApplicationPortRuntimeBuilder().AddOutput(Output)
            .AddInput(Input).AddInput(fallbackAddress).Build();
        var fallback = new BufferBlock<FlowMessage>();
        await using var fallbackAttachment = await runtime.AttachInputAsync(fallbackAddress, fallback);
        var source = new BufferBlock<FlowMessage>();
        using var sourceAttachment = runtime.AttachOutput(Output, source);
        var links = CompileLinks("""{"Port":"Fallback.Input","Fallback":true}""", ["Fallback"]);
        using var connection = revision ? null : runtime.Connect(links[0]);
        await using var builder = runtime.CreateRevision("reply");
        await using var prepared = builder.SetLinks(revision ? links : []).Build();
        await using var lease = await prepared.ActivateAsync();
        var responses = new List<FlowMessage>();
        var processor = new ActionBlock<FlowMessage>(async message =>
        {
            responses.Add(message);
            (await source.SendAsync(message)).ShouldBeTrue();
        }, new ExecutionDataflowBlockOptions { BoundedCapacity = 4 });
        await using var inputAttachment = await runtime.AttachInputAsync(Input, processor);
        var request = FlowMessage.Create("same trace");
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => runtime.SendAndReceiveAsync<string, string>(
            Input, Output, request, TimeSpan.FromSeconds(5))));
        results.ShouldAllBe(result => result.Status == PortRequestStatus.Received && result.Response!.Value == "same trace");
        results.Select(result => result.Response!.CorrelationId).Distinct().Count().ShouldBe(3);
        results.ShouldAllBe(result => result.Response!.TraceId == request.TraceId);
        (await source.SendAsync(responses[0])).ShouldBeTrue();
        (await source.SendAsync(Message("unsolicited"))).ShouldBeTrue();
        (await fallback.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(responses[0]);
        (await fallback.ReceiveAsync(TimeSpan.FromSeconds(5))).Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("unsolicited");
        processor.Complete();
        await processor.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        source.Complete();
        runtime.Complete();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        fallback.TryReceive(out _).ShouldBeFalse();
        responses.Count.ShouldBe(3);
    }
}
