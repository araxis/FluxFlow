using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed partial class ApplicationPortRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fallback_routes_are_exclusive_for_connected_and_revision_outputs(bool revision)
    {
        var ordinaryAddress = ApplicationAddress.WorkflowPort("Main", "Ordinary", "Input");
        var fallbackAddress = ApplicationAddress.WorkflowPort("Main", "Fallback", "Input");
        await using var runtime = new ApplicationPortRuntimeBuilder().AddOutput(Output)
            .AddInput(ordinaryAddress).AddInput(fallbackAddress).Build();
        var ordinary = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 4 });
        var fallback = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 4 });
        var source = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 4 });
        await using var ordinaryAttachment = await runtime.AttachInputAsync(ordinaryAddress, ordinary);
        await using var fallbackAttachment = await runtime.AttachInputAsync(fallbackAddress, fallback);
        using var sourceAttachment = runtime.AttachOutput(Output, source);
        var links = CompileLinks("""
            [{"Port":"Fallback.Input","Fallback":true},
             {"Port":"Ordinary.Input","Condition":"positive(input)"}]
            """, ["Ordinary", "Fallback"], new CSharpFlowExpressionEngine(interpreter =>
                interpreter.SetFunction("positive", (FlowValue value) => value.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 0)));
        ApplicationPortRevisionLease? lease = null;
        var connections = new List<IDisposable>();
        try
        {
            if (revision)
            {
                await using var builder = runtime.CreateRevision("fallback");
                await using var prepared = builder.SetLinks(links).Build();
                lease = await prepared.ActivateAsync();
            }
            else
                foreach (var link in links) connections.Add(runtime.Connect(link));

            var messages = new[] { 1, -1, 2, 0 }.Select(value => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
            foreach (var message in messages)
            {
                (await source.SendAsync(message)).ShouldBeTrue();
                var target = message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 0 ? ordinary : fallback;
                (await target.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(message);
            }
            source.Complete();
            runtime.Complete();
            await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            ordinary.TryReceive(out _).ShouldBeFalse();
            fallback.TryReceive(out _).ShouldBeFalse();
        }
        finally
        {
            foreach (var connection in connections) connection.Dispose();
            if (lease is not null) await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task Fallback_changes_are_revision_changes_and_conflicts_do_not_replace_live_routes()
    {
        var firstAddress = ApplicationAddress.WorkflowPort("Main", "First", "Input");
        var secondAddress = ApplicationAddress.WorkflowPort("Main", "Second", "Input");
        await using var runtime = new ApplicationPortRuntimeBuilder().AddOutput(Output)
            .AddInput(firstAddress).AddInput(secondAddress).Build();
        var first = new BufferBlock<FlowMessage>();
        var second = new BufferBlock<FlowMessage>();
        var source = new BufferBlock<FlowMessage>();
        await using var firstAttachment = await runtime.AttachInputAsync(firstAddress, first);
        await using var secondAttachment = await runtime.AttachInputAsync(secondAddress, second);
        using var sourceAttachment = runtime.AttachOutput(Output, source);
        var firstFallback = CompileLinks("""{"Port":"First.Input","Fallback":true}""", ["First"]);
        var firstOrdinary = CompileLinks("\"First.Input\"", ["First"]);
        var secondFallback = CompileLinks("""{"Port":"Second.Input","Fallback":true}""", ["Second"]);
        var current = runtime.PrepareRevisionRouting(firstOrdinary);
        var changed = runtime.PrepareRevisionRouting(firstFallback);
        current.GetChangedSources(changed).ShouldBe([Output]);
        current.GetChangedTargets(changed).ShouldBe([firstAddress]);
        Should.Throw<InvalidOperationException>(() => runtime.PrepareRevisionRouting([.. firstFallback, .. secondFallback]))
            .Message.ShouldContain("fallback");
        using var connection = runtime.Connect(firstFallback[0]);
        Should.Throw<InvalidOperationException>(() => runtime.Connect(secondFallback[0])).Message.ShouldContain("fallback");
        var candidateSource = new BufferBlock<FlowMessage>();
        var trackedOutput = new FallbackCandidateOutput(runtime.PrepareRevisionOutput(Output, candidateSource));
        var startupCalls = 0;
        await using (var prepared = new ApplicationPortRevision(runtime, "conflict",
            new Dictionary<ApplicationAddress, object?>(), [trackedOutput],
            runtime.PrepareRevisionRouting(secondFallback), routingConfigured: true))
        {
            (await Should.ThrowAsync<InvalidOperationException>(() => prepared.ActivateAsync(beforeCommit: _ =>
                { startupCalls++; return Task.CompletedTask; }).AsTask()))
                .Message.ShouldContain("fallback");
            startupCalls.ShouldBe(0);
            trackedOutput.ActivationCount.ShouldBe(0);
            trackedOutput.DisposalCount.ShouldBe(1);
        }
        candidateSource.Complete();
        await candidateSource.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.CurrentRevision.ShouldBeNull();
        var retained = Message("retained");
        (await source.SendAsync(retained)).ShouldBeTrue();
        (await first.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(retained);
        connection.Dispose();
        await using var replacementBuilder = runtime.CreateRevision("replacement");
        await using var replacement = replacementBuilder.SetLinks(secondFallback).Build();
        await using var lease = await replacement.ActivateAsync();
        runtime.CurrentRevision.ShouldNotBeNull().RevisionId.ShouldBe("replacement");
        Should.Throw<InvalidOperationException>(() => runtime.Connect(firstFallback[0])).Message.ShouldContain("fallback");
        var next = Message("next");
        (await source.SendAsync(next)).ShouldBeTrue();
        (await second.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(next);
        source.Complete();
        runtime.Complete();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        first.TryReceive(out _).ShouldBeFalse();
        second.TryReceive(out _).ShouldBeFalse();
    }

    private sealed class FallbackCandidateOutput(IPreparedApplicationOutput inner) : IPreparedApplicationOutput
    {
        public Task DispatchCompletion => inner.DispatchCompletion;
        internal int ActivationCount { get; private set; }
        internal int DisposalCount { get; private set; }
        public ApplicationAddress Address => inner.Address;
        public void ThrowIfFaulted() => inner.ThrowIfFaulted();
        public void Activate() { ActivationCount++; inner.Activate(); }
        public ValueTask DrainAsync(CancellationToken cancellationToken) => inner.DrainAsync(cancellationToken);
        public void Dispose() { DisposalCount++; inner.Dispose(); }
    }
}
