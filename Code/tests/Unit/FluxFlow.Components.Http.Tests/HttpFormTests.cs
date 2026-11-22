using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;
using NativeClient = System.Net.Http.HttpClient;

namespace FluxFlow.Components.Http.Tests;

public sealed partial class HttpTests
{
    [Theory]
    [InlineData(false, false, 0, 200, 1, false)]
    [InlineData(true, false, 0, 200, 1, false)]
    [InlineData(true, true, 2, 200, 3, false)]
    [InlineData(false, true, 2, 503, 3, false)]
    [InlineData(false, false, 2, 503, 1, false)]
    [InlineData(true, true, 1, 200, 2, true)]
    public async Task Multipart_uploads_preserve_fields_files_and_retry_bodies(
        bool jsonInput, bool retryUnsafe, int retries, int finalStatus, int expectedAttempts, bool disconnectFirst)
    {
        var first = Path.GetTempFileName();
        var second = Path.GetTempFileName();
        try
        {
            var bytes = Enumerable.Range(0, 100_000).Select(index => (byte)(index % 256)).ToArray();
            await File.WriteAllBytesAsync(first, bytes);
            var bodies = new ConcurrentQueue<byte[]>();
            await using var server = await Server.Start(async context =>
            {
                context.Request.EnableBuffering();
                using var raw = new MemoryStream();
                await context.Request.Body.CopyToAsync(raw);
                bodies.Enqueue(raw.ToArray());
                context.Request.Body.Position = 0;
                var form = await context.Request.ReadFormAsync();
                form["deviceId"].ToString().ShouldBe("robot-01");
                form["description"].ToString().ShouldBe("caf\u00e9 & camera");
                form.ContainsKey("empty").ShouldBeTrue();
                form["empty"].ToString().ShouldBe("");
                form.Files.Count.ShouldBe(2);
                form.Files[0].Name.ShouldBe("files");
                form.Files[0].FileName.ShouldBe("capture.bin");
                form.Files[0].ContentType.ShouldBe("application/x-capture");
                using var uploaded = new MemoryStream();
                await form.Files[0].CopyToAsync(uploaded);
                uploaded.ToArray().ShouldBe(bytes);
                form.Files[1].Name.ShouldBe("files");
                form.Files[1].FileName.ShouldBe(Path.GetFileName(second));
                form.Files[1].ContentType.ShouldBe("application/octet-stream");
                form.Files[1].Length.ShouldBe(0);
                if (disconnectFirst && bodies.Count == 1) context.Abort();
                else context.Response.StatusCode = bodies.Count < expectedAttempts ? 503 : finalStatus;
            });
            await using var provider = ResourceProvider(new
            {
                Type = "http.client", BaseAddress = server.Address,
                Retry = new { MaxRetryAttempts = retries, Delay = "00:00:00.001", UseJitter = false, RetryUnsafeMethods = retryUnsafe }
            });
            await using var component = new HttpClientComponent(new() { Client = provider.GetRequiredFlowResource<NativeClient>("api") });
            var output = Capture(component.Output);
            var request = new HttpSendRequest
            {
                Method = "POST", RequestUrl = "/upload",
                Form = new HttpForm
                {
                    Fields = new Dictionary<string, string> { ["deviceId"] = "robot-01", ["description"] = "caf\u00e9 & camera", ["empty"] = "" },
                    Files = new[]
                    {
                        new HttpFormFile { Name = "files", FilePath = first, FileName = "capture.bin", ContentType = "application/x-capture" },
                        new HttpFormFile { Name = "files", FilePath = second }
                    }
                }
            };
            var message = FlowMessage.Create(jsonInput ? new JsonFlowValue(request.Json) : request, correlationId: CorrelationId.New());
            await component.Input.SendAsync(message);
            var result = await output.ReceiveAsync(Deadline);
            result.IsError.ShouldBeFalse(result.Error?.Message);
            result.Value.ShouldBeOfType<HttpResponse>().StatusCode.ShouldBe(finalStatus);
            result.TraceId.ShouldBe(message.TraceId);
            result.CorrelationId.ShouldBe(message.CorrelationId);
            result.CausationId.ShouldBe(message.MessageId);
            bodies.Count.ShouldBe(expectedAttempts);
            foreach (var body in bodies) body.ShouldBe(bodies.First());
            using var firstReleased = File.Open(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var secondReleased = File.Open(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(first); File.Delete(second); }
    }

    [Fact]
    public async Task Multipart_forms_allow_fields_without_files()
    {
        const string formJson = """{"fields":{"value":"one","empty":""}}""";
        await using var server = await Server.Start(async context =>
        {
            context.Request.ContentType!.ShouldStartWith("multipart/form-data; boundary=");
            var form = await context.Request.ReadFormAsync();
            form.Files.ShouldBeEmpty();
            form["value"].ToString().ShouldBe("one");
            form.ContainsKey("empty").ShouldBeTrue();
            form["empty"].ToString().ShouldBe("");
        });
        using var client = new NativeClient { BaseAddress = server.Address };
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        using var document = JsonDocument.Parse($$"""{"requestUrl":"/","method":"POST","form":{{formJson}}}""");
        await component.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(document.RootElement)));
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeFalse(result.Error?.Message);
        result.Value.ShouldBeOfType<HttpResponse>().StatusCode.ShouldBe(200);
    }

    [Theory]
    [InlineData("\"body\":null", "Specify Body or Form")]
    [InlineData("\"body\":\"text\"", "Specify Body or Form")]
    [InlineData("\"contentType\":\"multipart/form-data\"", "Form sets its own multipart Content-Type")]
    [InlineData("\"headers\":{\"content-type\":\"multipart/form-data\"}", "Form sets its own multipart Content-Type")]
    [InlineData("\"encoding\":\"Base64\"", "Encoding applies to Body")]
    public async Task Multipart_body_options_reject_conflicts_with_a_valid_form(string extra, string expectedError)
    {
        using var handler = new Handler((_, _) => throw new Exception("Should not send."));
        using var client = new NativeClient(handler);
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        using var document = JsonDocument.Parse($$$$"""{"requestUrl":"https://example.test/",{{{{extra}}}},"form":{"fields":{"name":"value"}}}""");
        await component.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(document.RootElement)));
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeTrue();
        result.Error!.Message.ShouldContain(expectedError);
        handler.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"fields":{"":"value"}}""")]
    [InlineData("""{"fields":{"name":null}}""")]
    [InlineData("""{"files":[null]}""")]
    [InlineData("""{"files":[{"name":"file"}]}""")]
    [InlineData("""{"files":[{"name":"","filePath":"unused"}]}""")]
    [InlineData("""{"files":[{"name":"file","filePath":""}]}""")]
    [InlineData("""{"files":[{"name":"file","filePath":"unused","fileName":""}]}""")]
    public async Task Invalid_multipart_requests_fail_before_network_work(string formJson)
    {
        using var handler = new Handler((_, _) => throw new Exception("Should not send."));
        using var client = new NativeClient(handler);
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        using var document = JsonDocument.Parse($$"""{"requestUrl":"https://example.test/","form":{{formJson}}}""");
        await component.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(document.RootElement)));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        handler.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("part-name")]
    [InlineData("content-type")]
    [InlineData("header")]
    [InlineData("transport")]
    [InlineData("timeout")]
    public async Task Multipart_files_are_closed_after_failure_and_next_input_can_succeed(string failure)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "payload");
            using var handler = new Handler(async (request, token) =>
            {
                if (request.Content is not null)
                {
                    if (failure == "transport") throw new HttpRequestException("Connection failed.");
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var client = new NativeClient(handler);
            await using var component = new HttpClientComponent(new() { Client = client });
            var output = Capture(component.Output);
            var files = new List<HttpFormFile> { new() { Name = "first", FilePath = path } };
            if (failure == "missing") files.Add(new() { Name = "second", FilePath = path + ".missing" });
            if (failure == "part-name") files.Add(new() { Name = "\"invalid", FilePath = path });
            if (failure == "content-type") files.Add(new() { Name = "second", FilePath = path, ContentType = "invalid" });
            var request = new HttpSendRequest
            {
                RequestUrl = "https://example.test/", Method = "POST", Form = new HttpForm { Files = files },
                Headers = failure == "header" ? new Dictionary<string, string> { ["Invalid Header"] = "value" } : new Dictionary<string, string>(),
                Timeout = TimeSpan.FromMilliseconds(200)
            };
            await component.Input.SendAsync(FlowMessage.Create(request));
            (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
            handler.Calls.ShouldBe(failure is "transport" or "timeout" ? 1 : 0);
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            await component.Input.SendAsync(FlowMessage.Create(new HttpSendRequest { RequestUrl = "https://example.test/" }));
            (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Multipart_timeout_during_retry_delay_releases_files()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "payload");
            var calls = 0;
            await using var server = await Server.Start(async context =>
            {
                var form = await context.Request.ReadFormAsync();
                form.Files.Single().Length.ShouldBe(7);
                Interlocked.Increment(ref calls);
                context.Response.StatusCode = 503;
            });
            await using var provider = ResourceProvider(new
            {
                Type = "http.client", BaseAddress = server.Address, Timeout = "00:00:10",
                Retry = new { MaxRetryAttempts = 3, Delay = "00:00:05", UseJitter = false, RetryUnsafeMethods = true }
            });
            await using var component = new HttpClientComponent(new() { Client = provider.GetRequiredFlowResource<NativeClient>("api") });
            var output = Capture(component.Output);
            await component.Input.SendAsync(FlowMessage.Create(new HttpSendRequest
            {
                Method = "POST", RequestUrl = "/", Timeout = TimeSpan.FromMilliseconds(500),
                Form = new HttpForm { Files = new[] { new HttpFormFile { Name = "file", FilePath = path } } }
            }));
            (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
            Volatile.Read(ref calls).ShouldBe(1);
            using var released = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Multipart_component_fault_cancels_upload_and_releases_files()
    {
        var path = Path.GetTempFileName();
        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new Handler(async (_, token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var client = new NativeClient(handler);
            await using var component = new HttpClientComponent(new() { Client = client });
            await component.Input.SendAsync(FlowMessage.Create(new HttpSendRequest
            {
                RequestUrl = "https://example.test/", Method = "POST",
                Form = new HttpForm { Files = new[] { new HttpFormFile { Name = "file", FilePath = path } } }
            }));
            await started.Task.WaitAsync(Deadline);
            component.Fault(new InvalidOperationException("stop"));
            await Should.ThrowAsync<Exception>(() => component.Completion.WaitAsync(Deadline));
            await component.DisposeAsync();
            handler.Disposed.ShouldBeFalse();
            using var released = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Http_json_form_snapshots_collections_and_preserves_file_references()
    {
        var fields = new Dictionary<string, string> { ["device"] = "robot-01" };
        var files = new List<HttpFormFile> { new() { Name = "image", FilePath = "not-opened.jpg" } };
        var request = new HttpSendRequest
        {
            RequestUrl = "/upload", Form = new HttpForm { Fields = fields, Files = files }
        };
        fields["device"] = "mutated";
        files.Clear();
        var form = request.Json.GetProperty("Form");
        form.GetProperty("Fields").GetProperty("device").GetString().ShouldBe("robot-01");
        form.GetProperty("Files").GetArrayLength().ShouldBe(1);
        var file = form.GetProperty("Files")[0];
        file.GetProperty("Name").GetString().ShouldBe("image");
        file.GetProperty("FilePath").GetString().ShouldBe("not-opened.jpg");
        file.TryGetProperty("FileName", out _).ShouldBeFalse();
        file.TryGetProperty("ContentType", out _).ShouldBeFalse();
    }
}
