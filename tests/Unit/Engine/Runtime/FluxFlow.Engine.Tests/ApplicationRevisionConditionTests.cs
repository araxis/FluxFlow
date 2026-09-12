using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Links;
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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Executable_condition_identity_controls_revision_gates(bool conditional, bool reuse)
    {
        var first = conditional ? RevisionCondition(true, false) : CompileLinks("\"Sink.Input\"", ["Sink"]).Single();
        var second = reuse ? first : conditional ? RevisionCondition(false, false) : CompileLinks("\"Sink.Input\"", ["Sink"]).Single();
        first.ConditionExpression.ShouldBe(second.ConditionExpression);
        await using var runtime = new ApplicationPortRuntimeBuilder().AddOutput(Output).AddInput(first.Target).Build();
        var before = runtime.PrepareRevisionRouting([first]);
        var after = runtime.PrepareRevisionRouting([second]);

        before.GetChangedSources(after).ShouldBe(conditional && !reuse ? new[] { Output } : Array.Empty<ApplicationAddress>());
        before.GetChangedTargets(after).ShouldBe(conditional && !reuse ? new[] { first.Target } : Array.Empty<ApplicationAddress>());
        after.GetChangedSources(before).ShouldBe(before.GetChangedSources(after));
        before.Routes[Output].ShouldHaveSingleItem().Target.ShouldBe(first.Target);
        after.Routes[Output].ShouldHaveSingleItem().Target.ShouldBe(first.Target);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Condition_only_revision_waits_for_capture_before_switching_bindings(bool parameters, bool cancel)
    {
        var original = RevisionCondition(true, parameters);
        var replacement = RevisionCondition(false, parameters);
        original.ConditionExpression.ShouldBe(replacement.ConditionExpression);
        var capture = new RevisionConditionCapture();
        await using var runtime = new ApplicationPortRuntimeBuilder(capture)
            .AddOutput(Output, capacity: 2, discardUnmatched: true).AddInput(original.Target, capacity: 2).Build();
        var source = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 2 });
        var target = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 2 });
        using var sourceAttachment = runtime.AttachOutput(Output, source);
        await using var targetAttachment = await runtime.AttachInputAsync(original.Target, target);
        await using var originalBuilder = runtime.CreateRevision("original-condition");
        await using var originalRevision = originalBuilder.SetLinks([original]).Build();
        await using var originalLease = await originalRevision.ActivateAsync();
        await using var replacementBuilder = runtime.CreateRevision("replacement-condition");
        await using var replacementRevision = replacementBuilder.SetLinks([replacement]).Build();
        using var cancellation = new CancellationTokenSource();
        ApplicationPortRevisionLease? replacementLease = null;
        Task<ApplicationPortRevisionLease>? activation = null;
        var first = Message("before-condition-change");
        try
        {
            (await source.SendAsync(first)).ShouldBeTrue();
            (await capture.Entered.Task.WaitAsync(DeliveryDeadline)).ShouldBeSameAs(first);
            activation = replacementRevision.ActivateAsync(cancellation.Token).AsTask();
            activation.IsCompleted.ShouldBeFalse();
            runtime.CurrentRevision.ShouldNotBeNull().RevisionId.ShouldBe("original-condition");
            target.Count.ShouldBe(0);

            if (cancel)
            {
                cancellation.Cancel();
                var error = await Should.ThrowAsync<OperationCanceledException>(() => activation.WaitAsync(DeliveryDeadline));
                error.CancellationToken.ShouldBe(cancellation.Token);
                runtime.CurrentRevision.ShouldNotBeNull().RevisionId.ShouldBe("original-condition");
            }
            capture.Release.TrySetResult();
            if (!cancel) replacementLease = await activation.WaitAsync(DeliveryDeadline);
            (await target.ReceiveAsync(DeliveryDeadline)).ShouldBeSameAs(first);

            var second = Message("after-condition-change");
            var dispatched = runtime.ReceiveAsync<FlowValue>(Output, DeliveryDeadline);
            (await source.SendAsync(second)).ShouldBeTrue();
            var observed = (await dispatched).Message.ShouldNotBeNull();
            observed.MessageId.ShouldBe(second.MessageId);
            observed.Value.ShouldBeSameAs(second.Value);
            if (cancel) (await target.ReceiveAsync(DeliveryDeadline)).ShouldBeSameAs(second);
            else target.Count.ShouldBe(0);
            runtime.CurrentRevision.ShouldNotBeNull().RevisionId.ShouldBe(cancel ? "original-condition" : "replacement-condition");
            runtime.Complete();
            await runtime.Completion.WaitAsync(DeliveryDeadline);
            runtime.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally
        {
            capture.Release.TrySetResult();
            cancellation.Cancel();
            if (activation is not null && replacementLease is null)
            {
                try { replacementLease = await activation.WaitAsync(DeliveryDeadline); }
                catch (OperationCanceledException) { }
            }
            if (replacementLease is not null) await replacementLease.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Different_condition_instances_do_not_bypass_duplicate_link_validation(bool parameters)
    {
        var first = RevisionCondition(true, parameters);
        var second = RevisionCondition(true, parameters);
        second.ShouldNotBeSameAs(first);
        await using var runtime = new ApplicationPortRuntimeBuilder().AddOutput(Output).AddInput(first.Target).Build();

        Should.Throw<InvalidOperationException>(() => runtime.PrepareRevisionRouting([first, second]))
            .Message.ShouldContain("duplicate link");
        runtime.PrepareRevisionRouting([first]).Routes[Output].ShouldHaveSingleItem();
    }

    private static CompiledApplicationLink RevisionCondition(bool accept, bool parameters)
    {
        var declaration = parameters
            ? "{\"Port\":\"Sink.Input\",\"Condition\":\"accept\",\"Parameters\":{\"accept\":" + (accept ? "true" : "false") + "}}"
            : "{\"Port\":\"Sink.Input\",\"Condition\":\"accept\"}";
        var engine = parameters ? new CSharpFlowExpressionEngine()
            : new CSharpFlowExpressionEngine(interpreter => interpreter.SetVariable("accept", accept));
        return CompileLinks(declaration, ["Sink"], engine).Single();
    }

    private sealed class RevisionConditionCapture : IApplicationOutputCapture, IApplicationOutputCaptureResolver
    {
        internal TaskCompletionSource<FlowMessage> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IApplicationOutputCapture? Resolve(ApplicationAddress address) => address == Output ? this : null;
        public async ValueTask CaptureAsync(FlowMessage message, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(message);
            await Release.Task.WaitAsync(cancellationToken);
        }
    }
}
