using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
using FluxFlow.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Http.AspNetCore.Tests;

public sealed class HttpTriggerIngressTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Trigger_shutdown_settles_all_requests_in_its_only_ingress_queue(bool fault)
    {
        await using var trigger = new HttpTriggerNode(new RequestReplyOptions { Capacity = 3 });
        var requests = new List<(DefaultHttpContext Http, HttpRequestContext Request)>();
        for (var i = 0; i < 3; i++)
        {
            var http = new DefaultHttpContext();
            http.Request.Method = "GET";
            http.Request.Path = "/queued";
            var request = await HttpRequestContext.CreateAsync(http);
            (await trigger.Incoming.SendAsync(request)).ShouldBeTrue();
            requests.Add((http, request));
        }
        var failure = new InvalidOperationException("trigger stopped before startup");
        if (fault) trigger.Fault(failure); else trigger.Complete();
        var completionError = await Record.ExceptionAsync(() => trigger.Completion.WaitAsync(Deadline));
        if (fault) completionError.ShouldBeSameAs(failure); else completionError.ShouldBeNull();
        foreach (var (http, request) in requests)
        {
            request.Completed.IsCompletedSuccessfully.ShouldBeTrue();
            // Already admitted callers are failed; 503 belongs to rejected admission.
            http.Response.StatusCode.ShouldBe(500);
        }
        trigger.Incoming.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        trigger.ProductionCompletion.IsCompleted.ShouldBeTrue();
        (await trigger.Incoming.SendAsync(requests[0].Request)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hosted_stop_owns_caller_notification_even_when_its_wait_is_canceled(bool cancelStop)
    {
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new ActionBlock<FlowMessage>(_ => published.TrySetResult());
        var services = new ServiceCollection().AddFluxFlowHttpTrigger("requests", trigger =>
            trigger.Output.LinkTo(consumer, new DataflowLinkOptions { PropagateCompletion = true }));
        await using var provider = services.BuildServiceProvider();
        var lifetime = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await lifetime.StartAsync(CancellationToken.None);
        var trigger = provider.GetRequiredKeyedService<HttpTriggerNode>("requests");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new BlockingFailureContext(release.Task);
        (await trigger.Incoming.SendAsync(request)).ShouldBeTrue();
        await published.Task.WaitAsync(Deadline);
        using var cancellation = new CancellationTokenSource();
        var stopping = lifetime.StopAsync(cancellation.Token);
        try
        {
            await request.FailureEntered.Task.WaitAsync(Deadline);
            stopping.IsCompleted.ShouldBeFalse();
            trigger.Completion.IsCompleted.ShouldBeFalse();
            (await trigger.Incoming.SendAsync(new BlockingFailureContext(Task.CompletedTask))).ShouldBeFalse();
            if (cancelStop)
            {
                // Establish the drained-output scenario before canceling the host wait.
                await trigger.ProductionCompletion.WaitAsync(Deadline);
                await trigger.Responses.Completion.WaitAsync(Deadline);
                await consumer.Completion.WaitAsync(Deadline);
                cancellation.Cancel();
                var error = (await Record.ExceptionAsync(() => stopping.WaitAsync(Deadline))).ShouldNotBeNull();
                error.ShouldBeAssignableTo<OperationCanceledException>();
                ((OperationCanceledException)error).CancellationToken.ShouldBe(cancellation.Token);
                trigger.Completion.IsCompleted.ShouldBeFalse();
                request.FailureExited.ShouldBeFalse();
            }
        }
        finally { release.TrySetResult(); }
        if (!cancelStop) await stopping.WaitAsync(Deadline);
        var completionError = await Record.ExceptionAsync(() => trigger.Completion.WaitAsync(Deadline));
        var consumerError = await Record.ExceptionAsync(() => consumer.Completion.WaitAsync(Deadline));
        // The native output already drained before cancellation; it stays completed.
        // Canceling the host's wait does not abandon the still-owned callback.
        completionError.ShouldBeNull();
        consumerError.ShouldBeNull();
        request.Error.ShouldBeOfType<OperationCanceledException>();
        request.FailCalls.ShouldBe(1);
        request.ReplyCalls.ShouldBe(0);
        request.AckCalls.ShouldBe(0);
        request.FailureExited.ShouldBeTrue();
        trigger.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private sealed class BlockingFailureContext(Task release) : IRequestContext<HttpTriggerRequest, HttpTriggerReply>
    {
        public HttpTriggerRequest Request { get; } = new() { Method = "GET", Path = "/request" };
        public CorrelationId? CorrelationId => null;
        public TaskCompletionSource FailureEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Error { get; private set; }
        public int FailCalls { get; private set; }
        public int ReplyCalls { get; private set; }
        public int AckCalls { get; private set; }
        public bool FailureExited { get; private set; }
        public async Task FailAsync(Exception error, CancellationToken cancellationToken = default)
        {
            Error = error;
            FailCalls++;
            FailureEntered.TrySetResult();
            await release;
            FailureExited = true;
        }
        public Task ReplyAsync(HttpTriggerReply response, CancellationToken cancellationToken = default)
        { ReplyCalls++; return Task.CompletedTask; }
        public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
        { AckCalls++; return Task.CompletedTask; }
    }
}
