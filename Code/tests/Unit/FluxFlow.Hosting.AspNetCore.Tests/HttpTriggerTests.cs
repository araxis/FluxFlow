using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Components.Http;
using FluxFlow.Components.Mapping;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Components;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Hosting.AspNetCore.Tests;

public sealed class HttpTriggerTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Form_fields_preserve_repeated_empty_and_unicode_values(bool reply)
    {
        await using var server = await Server.Start(reply ? "Reply" : "Acknowledge",
            process: reply ? message => new HttpReply(200, message.Value) : null);
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        using var content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
        {
            new("tag", "one"), new("tag", "two"), new("empty", ""), new("title", "café + &")
        });
        var raw = await content.ReadAsStringAsync();
        using var response = await server.Client.PostAsync("/api/items/1", content);
        response.StatusCode.ShouldBe(reply ? HttpStatusCode.OK : HttpStatusCode.Accepted);
        var data = (await output.Messages.ReceiveAsync(Deadline)).Value.Json;
        var form = data.GetProperty("Form");
        form.GetProperty("tag").EnumerateArray().Select(value => value.GetString()).ShouldBe(["one", "two"]);
        form.GetProperty("empty")[0].GetString().ShouldBe("");
        form.GetProperty("title")[0].GetString().ShouldBe("café + &");
        data.GetProperty("Files").GetArrayLength().ShouldBe(0);
        data.GetProperty("Body").GetString().ShouldBe(raw);
        data.GetProperty("Encoding").GetString().ShouldBe("Text");
        if (reply)
        {
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            result.RootElement.GetProperty("Form").GetRawText().ShouldBe(form.GetRawText());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multipart_files_are_owned_snapshots_including_repeated_names_and_empty_files(bool reply)
    {
        // Exceeds the native buffering threshold so the temporary-file path is exercised too.
        var bytes = Enumerable.Range(0, 70_000).Select(index => (byte)index).ToArray();
        JsonElement data;
        await using (var server = await Server.Start(reply ? "Reply" : "Acknowledge", maxBody: 128 * 1024,
            process: reply ? message => new HttpReply(200, message.Value) : null))
        {
            using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("first"), "tag");
            content.Add(new StringContent("second"), "tag");
            var binary = new ByteArrayContent(bytes);
            binary.Headers.ContentType = new("application/octet-stream");
            content.Add(binary, "upload", "reading.bin");
            content.Add(new ByteArrayContent([]), "upload", "empty.bin");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/1") { Content = content };
            if (reply) request.Headers.TransferEncodingChunked = true;
            using var response = await server.Client.SendAsync(request);
            response.StatusCode.ShouldBe(reply ? HttpStatusCode.OK : HttpStatusCode.Accepted);
            data = (await output.Messages.ReceiveAsync(Deadline)).Value.Json;
            if (reply)
            {
                using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                result.RootElement.GetProperty("Files")[0].GetProperty("Content").GetBytesFromBase64().ShouldBe(bytes);
            }
        }
        data.GetProperty("Form").GetProperty("tag").EnumerateArray()
            .Select(value => value.GetString()).ShouldBe(["first", "second"]);
        var files = data.GetProperty("Files");
        files.GetArrayLength().ShouldBe(2);
        files[0].GetProperty("Name").GetString().ShouldBe("upload");
        files[0].GetProperty("FileName").GetString().ShouldBe("reading.bin");
        files[0].GetProperty("ContentType").GetString().ShouldBe("application/octet-stream");
        files[0].GetProperty("Length").GetInt64().ShouldBe(bytes.Length);
        files[0].GetProperty("Content").GetBytesFromBase64().ShouldBe(bytes);
        files[1].GetProperty("Name").GetString().ShouldBe("upload");
        files[1].GetProperty("FileName").GetString().ShouldBe("empty.bin");
        files[1].GetProperty("Length").GetInt64().ShouldBe(0);
        files[1].GetProperty("Content").GetBytesFromBase64().ShouldBeEmpty();
        data.GetProperty("Encoding").GetString().ShouldBe("Base64");
        data.GetProperty("Body").GetBytesFromBase64().Length.ShouldBeGreaterThan(bytes.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multipart_total_body_limit_rejects_known_and_chunked_uploads(bool chunked)
    {
        await using var server = await Server.Start(maxBody: 512);
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[600]), "upload", "large.bin");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/1") { Content = content };
        if (chunked) request.Headers.TransferEncodingChunked = true;
        using var response = await server.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        output.Messages.TryReceive(out _).ShouldBeFalse();
        server.Trigger.PendingCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("multipart/form-data", "missing boundary")]
    [InlineData("multipart/form-data; boundary=test", "--test\r\nContent-Disposition: form-data; name=\"field\"\r\n\r\ntruncated")]
    public async Task Malformed_forms_return_400_without_publication(string contentType, string body)
    {
        await using var server = await Server.Start();
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        using var content = new StringContent(body);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        using var response = await server.Client.PostAsync("/api/items/1", content);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        output.Messages.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Form_parsing_respects_the_hosts_native_form_options()
    {
        await using var server = await Server.Start(formOptions: options => options.ValueCountLimit = 1);
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["one"] = "1", ["two"] = "2" });
        using var response = await server.Client.PostAsync("/api/items/1", content);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        output.Messages.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Acknowledgement_publishes_owned_request_and_events_using_native_routing()
    {
        await using var server = await Server.Start();
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        using var events = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Events"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/42?q=one&q=two")
        { Content = new StringContent("{\"value\":7}", Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Test", ["one", "two"]);
        using var response = await server.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.GetValues("X-Host").Single().ShouldBe("native-filter");
        var message = await output.Messages.ReceiveAsync(Deadline);
        var data = message.Value.Json;
        data.TryGetProperty("Form", out _).ShouldBeFalse();
        data.TryGetProperty("Files", out _).ShouldBeFalse();
        data.GetProperty("Path").GetString().ShouldBe("/api/items/42");
        data.GetProperty("RouteValues").GetProperty("id").GetString().ShouldBe("42");
        data.GetProperty("Query").GetProperty("q").EnumerateArray().Select(item => item.GetString()).ShouldBe(["one", "two"]);
        data.GetProperty("Body").GetProperty("value").GetInt32().ShouldBe(7);
        data.GetProperty("Encoding").GetString().ShouldBe("Json");
        (await events.Messages.ReceiveAsync(Deadline)).Value.Json.GetProperty("Name").GetString().ShouldBe("http.request_received");
        using var wrongMethod = await server.Client.GetAsync("/api/items/42");
        wrongMethod.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        using var wrongRoute = await server.Client.PostAsync("/api/items/not-an-int", null);
        wrongRoute.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await server.App.StopAsync().WaitAsync(Deadline);
        data.GetProperty("Body").GetProperty("value").GetInt32().ShouldBe(7);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_requests_receive_their_own_reply_and_duplicate_replies_are_ignored(bool drain)
    {
        await using var server = await Server.Start("Reply", shutdownMode: drain ? "Drain" : "Cancel");
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        var first = server.Client.PostAsync("/api/items/1", null);
        var one = await output.Messages.ReceiveAsync(Deadline);
        var second = server.Client.PostAsync("/api/items/2", null);
        var two = await output.Messages.ReceiveAsync(Deadline);
        one.CorrelationId.ShouldNotBe(two.CorrelationId);
        server.Trigger.PendingCount.ShouldBe(2);
        var workflow = server.App.Services.GetRequiredKeyedService<FlowWorkflow>("web");
        var stopping = drain ? workflow.StopAsync() : null;
        if (stopping is not null)
        {
            stopping.IsCompleted.ShouldBeFalse();
            using var refused = await server.Client.PostAsync("/api/items/3", null);
            refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            output.Messages.TryReceive(out _).ShouldBeFalse();
            server.Trigger.PendingCount.ShouldBe(2);
        }
        var reply = two.WithValue(new HttpReply(201, "second", new Dictionary<string, string> { ["X-Reply"] = "two" }));
        (await server.Trigger.Response.SendAsync(reply)).ShouldBeTrue();
        using var secondResponse = await second.WaitAsync(Deadline);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await secondResponse.Content.ReadAsStringAsync()).ShouldBe("second");
        secondResponse.Headers.GetValues("X-Reply").Single().ShouldBe("two");
        server.Trigger.PendingCount.ShouldBe(1);
        if (stopping is not null) stopping.IsCompleted.ShouldBeFalse();
        await server.Trigger.Response.SendAsync(reply);
        first.IsCompleted.ShouldBeFalse();
        await server.Trigger.Response.SendAsync(one.WithValue(Json("{\"body\":{\"answer\":42}}")));
        using var firstResponse = await first.WaitAsync(Deadline);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await firstResponse.Content.ReadAsStringAsync()).ShouldBe("{\"answer\":42}");
        if (stopping is not null)
        {
            await stopping.WaitAsync(Deadline);
            workflow.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            server.Trigger.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        server.Trigger.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Timeout_releases_waiter_and_late_reply_does_not_complete_next_request()
    {
        await using var server = await Server.Start("Reply", "00:00:00.2");
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        var pending = server.Client.PostAsync("/api/items/1", null);
        var old = await output.Messages.ReceiveAsync(Deadline);
        using var timedOut = await pending.WaitAsync(Deadline);
        timedOut.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        server.Trigger.PendingCount.ShouldBe(0);
        var next = server.Client.PostAsync("/api/items/2", null);
        var current = await output.Messages.ReceiveAsync(Deadline);
        await server.Trigger.Response.SendAsync(old.WithValue(new HttpReply(200, "late")));
        await server.Trigger.Response.SendAsync(current.WithValue(new HttpReply(200, "current")));
        using var response = await next.WaitAsync(Deadline);
        (await response.Content.ReadAsStringAsync()).ShouldBe("current");
    }

    [Fact]
    public async Task Client_disconnect_and_host_shutdown_release_pending_requests()
    {
        await using var server = await Server.Start("Reply");
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        using var canceled = new CancellationTokenSource();
        var pending = server.Client.PostAsync("/api/items/1", null, canceled.Token);
        await output.Messages.ReceiveAsync(Deadline);
        canceled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await pending);
        using var deadline = new CancellationTokenSource(Deadline);
        while (server.Trigger.PendingCount != 0) await Task.Delay(10, deadline.Token);
        var stopping = server.Client.PostAsync("/api/items/2", null);
        await output.Messages.ReceiveAsync(Deadline);
        await server.App.StopAsync().WaitAsync(Deadline);
        using var response = await stopping.WaitAsync(Deadline);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        server.Trigger.PendingCount.ShouldBe(0);
        server.Trigger.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, "Cancel")]
    [InlineData(true, "Cancel")]
    [InlineData(false, "Drain")]
    [InlineData(true, "Drain")]
    public async Task Workflow_restart_after_timeout_or_cancellation_does_not_reuse_pending_correlations(bool cancel, string shutdownMode)
    {
        await using var server = await Server.Start("Reply", cancel ? "00:00:30" : "00:00:01", maxPending: 1, shutdownMode: shutdownMode);
        var workflow = server.App.Services.GetRequiredKeyedService<FlowWorkflow>("web");
        var original = server.Trigger;
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        using var cancellation = new CancellationTokenSource();
        var pending = server.Client.PostAsync("/api/items/1", null, cancellation.Token);
        var old = await output.Messages.ReceiveAsync(Deadline);
        original.PendingCount.ShouldBe(1);
        // Drain must also terminate when an accepted request expires or its client leaves.
        var stopping = shutdownMode == "Drain" ? workflow.StopAsync() : null;
        if (stopping is not null) stopping.IsCompleted.ShouldBeFalse();
        if (cancel)
        {
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(async () => await pending.WaitAsync(Deadline));
            using var deadline = new CancellationTokenSource(Deadline);
            while (original.PendingCount != 0) await Task.Delay(10, deadline.Token);
        }
        else
        {
            using var timedOut = await pending.WaitAsync(Deadline);
            timedOut.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        }
        original.PendingCount.ShouldBe(0);
        await (stopping ?? workflow.StopAsync()).WaitAsync(Deadline);
        original.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        using var unavailable = await server.Client.PostAsync("/api/items/2", null);
        unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        output.Messages.TryReceive(out _).ShouldBeFalse();

        await workflow.StartAsync().WaitAsync(Deadline);
        var restarted = (HttpTrigger)workflow.Services.GetRequiredKeyedService<IFlowNode>("web.incoming");
        restarted.ShouldNotBeSameAs(original);
        using var currentOutput = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        var next = server.Client.PostAsync("/api/items/3", null);
        var current = await currentOutput.Messages.ReceiveAsync(Deadline);
        current.CorrelationId.ShouldNotBe(old.CorrelationId);
        restarted.PendingCount.ShouldBe(1);
        // Even a late reply delivered to the new run cannot satisfy its fresh request.
        (await restarted.Response.SendAsync(old.WithValue(new HttpReply(200, "late")))).ShouldBeTrue();
        (await restarted.Response.SendAsync(current.WithValue(new HttpReply(200, "current")))).ShouldBeTrue();
        using var response = await next.WaitAsync(Deadline);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("current");
        restarted.PendingCount.ShouldBe(0);
        await workflow.StopAsync().WaitAsync(Deadline);
        restarted.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        original.PendingCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("Cancel")]
    [InlineData("Drain")]
    public async Task Workflow_stop_applies_configured_shutdown_mode_and_restart_recovers(string shutdownMode)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await Server.Start("Reply", shutdownMode: shutdownMode, processAsync: async message =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(Deadline);
            return new HttpReply(200, message.Value.Json.GetProperty("Path").GetString()!);
        });
        var workflow = server.App.Services.GetRequiredKeyedService<FlowWorkflow>("web");
        var original = server.Trigger;
        try
        {
            var pending = server.Client.PostAsync("/api/items/1", null);
            await entered.Task.WaitAsync(Deadline);
            var stopping = workflow.StopAsync();
            stopping.IsCompleted.ShouldBeFalse(); // Downstream work is still held in the mapper.
            if (shutdownMode == "Drain")
            {
                pending.IsCompleted.ShouldBeFalse();
                original.PendingCount.ShouldBe(1);
            }
            else
            {
                (await pending.WaitAsync(Deadline)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
                original.PendingCount.ShouldBe(0);
            }
            using var refused = await server.Client.PostAsync("/api/items/2", null);
            refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            release.TrySetResult();
            using var response = await pending.WaitAsync(Deadline);
            if (shutdownMode == "Drain")
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                (await response.Content.ReadAsStringAsync()).ShouldBe("/api/items/1");
            }
            await stopping.WaitAsync(Deadline);
            original.PendingCount.ShouldBe(0);
            original.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            using var stopped = await server.Client.PostAsync("/api/items/3", null);
            stopped.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

            await workflow.StartAsync().WaitAsync(Deadline);
            var restarted = (HttpTrigger)workflow.Services.GetRequiredKeyedService<IFlowNode>("web.incoming");
            restarted.ShouldNotBeSameAs(original);
            using var recovered = await server.Client.PostAsync("/api/items/4", null);
            recovered.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await recovered.Content.ReadAsStringAsync()).ShouldBe("/api/items/4");
            restarted.PendingCount.ShouldBe(0);
            await workflow.StopAsync().WaitAsync(Deadline);
            restarted.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Drain_stop_deadline_cancels_pending_request_and_rejects_restart()
    {
        var server = await Server.Start("Reply", shutdownMode: "Drain");
        Exception? disposalFailure;
        try
        {
            var workflow = server.App.Services.GetRequiredKeyedService<FlowWorkflow>("web");
            using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
            var pending = server.Client.PostAsync("/api/items/1", null);
            await output.Messages.ReceiveAsync(Deadline);
            server.Trigger.PendingCount.ShouldBe(1);

            using var shutdown = new CancellationTokenSource();
            var stopping = workflow.StopAsync(shutdown.Token);
            stopping.IsCompleted.ShouldBeFalse();
            pending.IsCompleted.ShouldBeFalse();
            // Signal deadline expiry explicitly, without depending on timer scheduling.
            shutdown.Cancel();
            var cancellation = await Should.ThrowAsync<OperationCanceledException>(() => stopping.WaitAsync(Deadline));
            cancellation.CancellationToken.ShouldBe(shutdown.Token);
            using var response = await pending.WaitAsync(Deadline);
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            server.Trigger.PendingCount.ShouldBe(0);
            await Should.ThrowAsync<OperationCanceledException>(() => server.Trigger.Completion.WaitAsync(Deadline));
            var completion = workflow.Completion;
            await Should.ThrowAsync<OperationCanceledException>(() => completion.WaitAsync(Deadline));
            await Should.ThrowAsync<InvalidOperationException>(() => workflow.StartAsync().WaitAsync(Deadline));
            workflow.Completion.ShouldBeSameAs(completion);
            server.Trigger.PendingCount.ShouldBe(0);
        }
        finally
        {
            // Always clean up without replacing an earlier assertion failure.
            disposalFailure = await Record.ExceptionAsync(() => server.DisposeAsync().AsTask().WaitAsync(Deadline));
        }
        // DI disposal preserves the failure of the canceled workflow run.
        var failure = disposalFailure.ShouldBeOfType<AggregateException>();
        failure.Flatten().InnerExceptions.ShouldAllBe(error => error is OperationCanceledException);
    }

    [Theory]
    [InlineData("Reply")]
    [InlineData("Acknowledge")]
    public async Task Host_drain_does_not_publish_an_upload_that_has_not_reached_the_workflow(string mode)
    {
        await using var server = await Server.Start(mode, shutdownMode: "Drain",
            process: mode == "Reply" ? _ => new HttpReply(200, "processed") : null);
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        using var deadline = new CancellationTokenSource(Deadline);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Client.BaseAddress!.Port, deadline.Token);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await stream.WriteAsync("POST /api/items/1 HTTP/1.1\r\nHost: localhost\r\nContent-Length: 1\r\nExpect: 100-continue\r\n\r\n"u8.ToArray(), deadline.Token);
        // Kestrel sends Continue when the endpoint starts reading the body.
        (await reader.ReadLineAsync(deadline.Token)).ShouldBe("HTTP/1.1 100 Continue");
        (await reader.ReadLineAsync(deadline.Token)).ShouldBe("");
        server.Trigger.PendingCount.ShouldBe(0);
        var stopping = server.App.StopAsync(deadline.Token);
        server.App.Lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeTrue();
        await stream.WriteAsync("x"u8.ToArray(), deadline.Token);
        (await reader.ReadLineAsync(deadline.Token)).ShouldBe("HTTP/1.1 503 Service Unavailable");
        await stopping.WaitAsync(deadline.Token);
        output.Messages.TryReceive(out _).ShouldBeFalse();
        server.Trigger.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Host_shutdown_allows_an_accepted_drain_request_to_reply()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await Server.Start("Reply", shutdownMode: "Drain", processAsync: async _ =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(Deadline);
            return new HttpReply(200, "finished");
        });
        try
        {
            var pending = server.Client.PostAsync("/api/items/1", null);
            await entered.Task.WaitAsync(Deadline);
            var stopping = server.App.StopAsync();
            server.App.Lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeTrue();
            stopping.IsCompleted.ShouldBeFalse();
            pending.IsCompleted.ShouldBeFalse();
            release.TrySetResult();
            using var response = await pending.WaitAsync(Deadline);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).ShouldBe("finished");
            await stopping.WaitAsync(Deadline);
            server.Trigger.PendingCount.ShouldBe(0);
            server.Trigger.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Capacity_exhaustion_returns_503_without_publishing_and_recovers()
    {
        await using var server = await Server.Start("Reply", maxPending: 1);
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        var pending = server.Client.PostAsync("/api/items/1", null);
        var request = await output.Messages.ReceiveAsync(Deadline);
        using var refused = await server.Client.PostAsync("/api/items/2", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        output.Messages.TryReceive(out _).ShouldBeFalse();
        server.Trigger.PendingCount.ShouldBe(1);
        await server.Trigger.Response.SendAsync(request.WithValue(new HttpReply()));
        using var completed = await pending.WaitAsync(Deadline);
        completed.StatusCode.ShouldBe(HttpStatusCode.OK);
        server.Trigger.PendingCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configured_links_round_trip_replies_and_errors_without_a_parallel_registration_path(bool fail)
    {
        var calls = 0;
        await using var server = await Server.Start("Reply", process: message =>
        {
            Interlocked.Increment(ref calls);
            if (fail) throw new InvalidOperationException("private failure");
            return new HttpReply(201, message.Value);
        });
        using var response = await server.Client.PostAsync("/api/items/42", new StringContent("hello"));
        response.StatusCode.ShouldBe(fail ? HttpStatusCode.InternalServerError : HttpStatusCode.Created);
        var text = await response.Content.ReadAsStringAsync();
        if (fail) text.ShouldNotContain("private failure");
        else
        {
            using var data = JsonDocument.Parse(text);
            data.RootElement.GetProperty("Body").GetString().ShouldBe("hello");
        }
        await server.App.StopAsync().WaitAsync(Deadline);
        Volatile.Read(ref calls).ShouldBe(1); // Both directions declare the same links.
        server.Trigger.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Chunked_bodies_obey_the_limit_and_binary_data_is_preserved()
    {
        await using var server = await Server.Start(maxBody: 8);
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/1")
        { Content = new StreamContent(new MemoryStream(new byte[9])) };
        request.Headers.TransferEncodingChunked = true;
        using var rejected = await server.Client.SendAsync(request);
        rejected.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        output.Messages.TryReceive(out _).ShouldBeFalse();
        byte[] bytes = [255, 0, 1];
        using var accepted = await server.Client.PostAsync("/api/items/2", new ByteArrayContent(bytes));
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var data = (await output.Messages.ReceiveAsync(Deadline)).Value.Json;
        data.GetProperty("Encoding").GetString().ShouldBe("Base64");
        Convert.FromBase64String(data.GetProperty("Body").GetString()!).ShouldBe(bytes);
    }

    [Theory]
    [InlineData(204)]
    [InlineData(304)]
    public async Task No_body_statuses_do_not_write_workflow_payload(int status)
    {
        await using var server = await Server.Start("Reply", process: _ => new HttpReply(status, "unused"));
        using var response = await server.Client.PostAsync("/api/items/1", null);
        ((int)response.StatusCode).ShouldBe(status);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(304)]
    public async Task Explicit_content_type_is_preserved_without_a_body(int status)
    {
        await using var server = await Server.Start("Reply", process: _ => new HttpReply(status, contentType: "application/problem+json"));
        using var response = await server.Client.PostAsync("/api/items/1", null);
        ((int)response.StatusCode).ShouldBe(status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canceled_or_stopped_ingress_does_not_publish(bool reply)
    {
        await using var trigger = new HttpTrigger();
        var output = new BufferBlock<FlowMessage>();
        using var link = trigger.Output.LinkTo(output);
        await trigger.StartAsync();
        var message = FlowMessage.Create((FlowValue)"request");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => Send(canceled.Token));
        trigger.StopProduction();
        await Should.ThrowAsync<OperationCanceledException>(() => Send(CancellationToken.None));
        await trigger.Completion.WaitAsync(Deadline);
        trigger.PendingCount.ShouldBe(0);
        output.Count.ShouldBe(0);

        async Task Send(CancellationToken token)
        {
            if (reply) await trigger.RequestAsync(message, Deadline, token);
            else await trigger.PublishAsync(message, token);
        }
    }

    [Theory]
    [InlineData(HttpTriggerShutdownMode.Cancel)]
    [InlineData(HttpTriggerShutdownMode.Drain)]
    public async Task Component_fault_releases_waiters(HttpTriggerShutdownMode shutdownMode)
    {
        await using var trigger = new HttpTrigger(shutdownMode: shutdownMode);
        await trigger.StartAsync();
        var output = new BufferBlock<FlowMessage>();
        using var link = trigger.Output.LinkTo(output);
        var pending = trigger.RequestAsync(FlowMessage.Create((FlowValue)"request"), Deadline);
        await output.ReceiveAsync(Deadline);
        if (shutdownMode == HttpTriggerShutdownMode.Drain)
        {
            trigger.StopProduction();
            pending.IsCompleted.ShouldBeFalse();
        }
        trigger.Fault(new InvalidOperationException("fault"));
        await Should.ThrowAsync<OperationCanceledException>(async () => await pending.WaitAsync(Deadline));
        await Should.ThrowAsync<Exception>(async () => await trigger.Completion.WaitAsync(Deadline));
        trigger.PendingCount.ShouldBe(0);
    }

    [Fact]
    public void Reply_json_uses_source_generated_metadata_and_preserves_defaults()
    {
        var defaults = HttpReply.Read(Json("{}"));
        defaults.StatusCode.ShouldBe(200);
        defaults.Headers.ShouldBeEmpty();
        var headers = new Dictionary<string, string> { ["X-Test"] = "original" };
        var reply = new HttpReply(201, Json("{\"value\":3}"), headers, "application/json");
        headers["X-Test"] = "changed";
        var restored = HttpReply.Read(new JsonFlowValue(reply.Json));
        restored.Headers["X-Test"].ShouldBe("original");
        restored.Body!.Json.GetProperty("value").GetInt32().ShouldBe(3);
        restored.StatusCode.ShouldBe(201);
    }

    [Theory]
    [InlineData("{", "application/json", 400)]
    [InlineData("123456789", "text/plain", 413)]
    public async Task Invalid_and_oversized_bodies_are_rejected_without_publication(string body, string contentType, int status)
    {
        await using var server = await Server.Start(maxBody: 8);
        using var output = server.App.Services.ObserveOutput(PortAddress.Parse("web.incoming.Output"));
        using var response = await server.Client.PostAsync("/api/items/1", new StringContent(body, Encoding.UTF8, contentType));
        ((int)response.StatusCode).ShouldBe(status);
        output.Messages.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public void Invalid_configuration_is_transactional()
    {
        var services = new ServiceCollection().AddHttpTriggers();
        var before = services.ToArray();
        using var config = Configuration("Reply", "00:00:00");
        Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(config));
        services.ToArray().ShouldBe(before);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("Unknown")]
    public void Invalid_shutdown_mode_rejects_registration_atomically(string mode)
    {
        var services = new ServiceCollection().AddHttpTriggers();
        var before = services.ToArray();
        using var config = Configuration("Reply", "00:00:30");
        config["Workflows:web:incoming:ShutdownMode"] = mode;
        Should.Throw<Exception>(() => services.AddFluxFlowApplication(config));
        services.ToArray().ShouldBe(before);
    }

    [Fact]
    public async Task Component_works_without_a_web_host_and_correlates_requests_with_the_same_trace()
    {
        await using var trigger = new HttpTrigger();
        var output = new BufferBlock<FlowMessage>();
        using var link = trigger.Output.LinkTo(output);
        await trigger.StartAsync();
        var input = FlowMessage.Create((FlowValue)"request");
        var first = trigger.RequestAsync(input, Deadline);
        var one = await output.ReceiveAsync(Deadline);
        var second = trigger.RequestAsync(input, Deadline);
        var two = await output.ReceiveAsync(Deadline);
        one.TraceId.ShouldBe(two.TraceId);
        one.CorrelationId.ShouldNotBe(two.CorrelationId);
        await trigger.Response.SendAsync(two.WithValue((FlowValue)"second"));
        await trigger.Response.SendAsync(one.WithValue((FlowValue)"first"));
        (await first).Value.Json.GetString().ShouldBe("first");
        (await second).Value.Json.GetString().ShouldBe("second");
        trigger.Complete();
        await trigger.Completion.WaitAsync(Deadline);
    }

    private static JsonFlowValue Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(document.RootElement);
    }

    private static ConfigurationRoot Configuration(string mode, string timeout, int maxBody = 1024, int maxPending = 1024, bool pipeline = false)
    {
        var links = pipeline ? ", \"Output\": \"reply.Input\", \"Response\": \"reply.Output\"" : "";
        var reply = pipeline ? ", \"reply\": { \"Type\": \"test.reply\", \"Input\": \"incoming.Output\", \"Output\": \"incoming.Response\" }" : "";
        return (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes($$"""
        { "Workflows": { "web": { "incoming": {
          "Type": "http.trigger", "Route": "/items/{id:int}", "Methods": ["POST"],
          "ResponseMode": "{{mode}}", "ReplyTimeout": "{{timeout}}", "MaxRequestBodyBytes": {{maxBody}},
          "MaxPendingRequests": {{maxPending}} {{links}}
        } {{reply}} } } }
        """))).Build();
    }

    private sealed class Server(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public WebApplication App => app;
        public HttpClient Client => client;
        public HttpTrigger Trigger { get; } = (HttpTrigger)app.Services.GetWorkflowServices("web").GetRequiredKeyedService<IFlowNode>("web.incoming");
        public static async Task<Server> Start(string mode = "Acknowledge", string timeout = "00:00:30", int maxBody = 1024,
            int maxPending = 1024, Func<FlowMessage, FlowValue>? process = null, Action<FormOptions>? formOptions = null,
            string shutdownMode = "Cancel", Func<FlowMessage, ValueTask<FlowValue>>? processAsync = null)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            if (formOptions is not null) builder.Services.Configure(formOptions);
            using var config = Configuration(mode, timeout, maxBody, maxPending, process is not null || processAsync is not null);
            config["Workflows:web:incoming:ShutdownMode"] = shutdownMode;
            if (process is not null || processAsync is not null)
                builder.Services.AddKeyedSingleton<ComponentRegistration>("test.reply", (services, section) =>
                {
                    var address = section.GetComponentAddress();
                    var settings = section.ReadRequiredConfiguration<ReplyConfiguration>();
                    services.AddComponentFactory(address, _ => new Mapper(new()
                        { MapAsync = (message, _) => processAsync?.Invoke(message) ?? ValueTask.FromResult(process!(message)) }))
                        .AddInputPort<Mapper>(address, "Input", node => node.Input)
                        .AddOutputPort<Mapper>(address, "Output", node => node.Output)
                        .AddEventPort<Mapper>(address, "Events", node => node.Events);
                    services.AddLinks(address, settings);
                });
            builder.Services.AddHttpTriggers().AddFluxFlowApplication(config).AddFluxFlow();
            var app = builder.Build();
            app.MapGroup("/api").MapFluxFlowHttpTriggers().AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers["X-Host"] = "native-filter";
                return await next(context);
            });
            try { await app.StartAsync(); }
            catch { await app.DisposeAsync(); throw; }
            return new(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = Deadline });
        }
        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            try { await app.StopAsync().WaitAsync(Deadline); }
            finally { await app.DisposeAsync(); }
        }
    }

    private sealed record ReplyConfiguration : ComponentConfiguration;
}
