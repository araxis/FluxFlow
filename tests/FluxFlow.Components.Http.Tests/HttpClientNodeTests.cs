using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Nodes;
using FluxFlow.Components.Http.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Http.Tests;

public sealed class HttpClientNodeTests
{
    [Fact]
    public void Canonical_contracts_copy_headers_and_serialize_error_discriminator()
    {
        var requestHeaders = new Dictionary<string, string>
        {
            ["X-Request"] = "original"
        };
        var request = new HttpClientRequest { Headers = requestHeaders };
        requestHeaders["X-Request"] = "changed";
        request.Headers["X-Request"].ShouldBe("original");

        string[] responseValues = ["first"];
        var response = new HttpResponseResult(
            DateTimeOffset.UnixEpoch,
            "GET",
            "https://example.test/",
            503,
            "Unavailable",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["Retry-After"] = responseValues
            },
            FlowContent.FromBytes(Array.Empty<byte>()),
            elapsedMilliseconds: 1,
            success: false,
            bodyTruncated: false);
        responseValues[0] = "changed";
        response.Headers["Retry-After"].ShouldBe(["first"]);

        HttpClientResult result = new HttpClientFailureResult(
            DateTimeOffset.UnixEpoch,
            "GET",
            "https://example.test/",
            elapsedMilliseconds: 1,
            new FluxFlow.Data.FlowError(
                HttpErrorCodeNames.NonSuccessStatus,
                "Unavailable.",
                "HTTP"),
            response);
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize<HttpClientResult>(result));
        var root = document.RootElement;

        root.GetProperty("Kind").GetString().ShouldBe(HttpResultKinds.Error);
        root.GetProperty("IsError").GetBoolean().ShouldBeTrue();
        root.GetProperty("Error").GetProperty("Code").GetString()
            .ShouldBe(HttpErrorCodeNames.NonSuccessStatus);
        root.GetProperty("Response").GetProperty("StatusCode").GetInt32()
            .ShouldBe(503);
    }

    [Fact]
    public async Task Request_and_response_preserve_exact_content_and_message_lineage()
    {
        byte[] requestBytes = [0x00, 0x7F, 0xFF];
        byte[] responseBytes = [0xE9];
        var handler = new RecordingHandler((_, _) => Respond(
            HttpStatusCode.OK,
            responseBytes,
            "text/plain; charset=\"iso-8859-1\""));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var output = Sink(node.Output);
        var request = FlowMessage.Create(
            new HttpClientRequest
            {
                Method = "POST",
                Url = "https://example.test/items",
                Headers = new Dictionary<string, string>
                {
                    ["X-Request"] = "A-100"
                },
                Body = FlowContent.FromBytes(
                    requestBytes,
                    "application/octet-stream")
            },
            new CorrelationId("http-correlation"),
            new TraceId("http-trace"));

        (await node.Input.SendAsync(request)).ShouldBeTrue();

        var message = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var result = message.Payload.ShouldBeOfType<HttpResponseResult>();
        message.CorrelationId.ShouldBe(request.CorrelationId);
        message.TraceId.ShouldBe(request.TraceId);
        message.CausationId.ShouldBe(request.MessageId);
        message.MessageId.ShouldNotBe(request.MessageId);
        handler.LastBody.ShouldBe(requestBytes);
        handler.LastRequestHeader.ShouldBe("A-100");
        handler.LastContentType.ShouldBe("application/octet-stream");
        result.StatusCode.ShouldBe(200);
        result.Success.ShouldBeTrue();
        result.IsError.ShouldBeFalse();
        result.Body.OriginalBytes.AsSpan().ToArray().ShouldBe(responseBytes);
        result.Body.ContentType.ShouldBe("text/plain; charset=\"iso-8859-1\"");
        result.Body.Encoding.ShouldBe("iso-8859-1");
    }

    [Fact]
    public async Task Output_fans_out_every_result_to_every_consumer()
    {
        var calls = 0;
        var handler = new RecordingHandler((_, _) => Respond(
            HttpStatusCode.OK,
            Encoding.UTF8.GetBytes($"r{Interlocked.Increment(ref calls)}"),
            "text/plain"));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var first = Sink(node.Output);
        var second = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/1"
        }));
        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/2"
        }));

        DecodeText((await first.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>().Body).ShouldBe("r1");
        DecodeText((await first.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>().Body).ShouldBe("r2");
        DecodeText((await second.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>().Body).ShouldBe("r1");
        DecodeText((await second.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>().Body).ShouldBe("r2");
    }

    [Fact]
    public async Task Relative_url_resolves_against_client_base_address()
    {
        var handler = new RecordingHandler((_, _) => Respond(
            HttpStatusCode.OK,
            [],
            null));
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        await using var node = new HttpClientNode(client);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "v1/status"
        }));

        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>().StatusCode.ShouldBe(200);
        handler.LastRequestUri.ShouldBe("https://api.example.test/v1/status");
    }

    [Fact]
    public async Task Non_success_response_is_a_normal_response_result_by_default()
    {
        var handler = new RecordingHandler((_, _) => Respond(
            HttpStatusCode.UnprocessableEntity,
            Encoding.UTF8.GetBytes("invalid"),
            "application/problem+json"));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/items"
        }));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>();
        result.StatusCode.ShouldBe(422);
        result.Success.ShouldBeFalse();
        result.IsError.ShouldBeFalse();
        result.Body.ContentType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Configured_non_success_response_is_an_error_result_and_later_input_continues()
    {
        var calls = 0;
        var handler = new RecordingHandler((_, _) => Interlocked.Increment(ref calls) == 1
            ? Respond(HttpStatusCode.ServiceUnavailable, Encoding.UTF8.GetBytes("later"), "text/plain")
            : Respond(HttpStatusCode.OK, Encoding.UTF8.GetBytes("ready"), "text/plain"));
        await using var node = new HttpClientNode(
            new HttpClient(handler),
            new HttpClientNodeOptions { TreatNonSuccessStatusAsError = true });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/status"
        }));
        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/status"
        }));

        var failure = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpClientFailureResult>();
        failure.IsError.ShouldBeTrue();
        failure.Error!.Code.ShouldBe(HttpErrorCodeNames.NonSuccessStatus);
        failure.Response.ShouldNotBeNull().StatusCode.ShouldBe(503);
        failure.Response.Body.OriginalBytes.AsSpan().ToArray()
            .ShouldBe(Encoding.UTF8.GetBytes("later"));

        var success = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>();
        success.StatusCode.ShouldBe(200);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Value_only_request_body_returns_error_result_and_later_input_continues()
    {
        var handler = new RecordingHandler((_, _) => Respond(
            HttpStatusCode.OK,
            [],
            null));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/value",
            Body = FlowContent.FromValue(FlowValue.From("serialize upstream"))
        }));
        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/bytes",
            Body = FlowContent.FromBytes(Array.Empty<byte>(), "application/octet-stream")
        }));

        var failure = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpClientFailureResult>();
        failure.Error!.Code.ShouldBe(HttpErrorCodeNames.InvalidContent);
        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>();
        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_method_returns_error_result_and_later_input_continues()
    {
        var handler = new RecordingHandler((_, _) => Respond(
            HttpStatusCode.OK,
            [],
            null));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Method = "NOT VALID",
            Url = "https://example.test/invalid"
        }));
        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/valid"
        }));

        var failure = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpClientFailureResult>();
        failure.Error!.Code.ShouldBe(HttpErrorCodeNames.InvalidMethod);
        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>();
        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Network_failure_is_a_normal_error_result()
    {
        var handler = new RecordingHandler((_, _) =>
            throw new HttpRequestException("connection refused"));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var output = Sink(node.Output);
        var events = Sink(node.Events);
        var request = FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/status"
        });

        await node.Input.SendAsync(request);

        var failure = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpClientFailureResult>();
        failure.Error!.Code.ShouldBe(HttpErrorCodeNames.Network);
        failure.Error.IsTransient.ShouldBeTrue();
        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        @event.Name.ShouldBe(HttpClientNode.RequestFailed);
        @event.CorrelationId.ShouldBe(request.CorrelationId);
    }

    [Fact]
    public async Task Request_timeout_is_a_normal_transient_error_result()
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await using var node = new HttpClientNode(
            new HttpClient(handler),
            new HttpClientNodeOptions { DefaultTimeoutMilliseconds = 20 });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/timeout"
        }));

        var failure = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpClientFailureResult>();
        failure.Error!.Code.ShouldBe(HttpErrorCodeNames.Timeout);
        failure.Error.IsTransient.ShouldBeTrue();
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_url_without_base_address_is_a_normal_error_result()
    {
        var handler = new RecordingHandler((_, _) => Respond(
            HttpStatusCode.OK,
            [],
            null));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest()));

        var failure = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpClientFailureResult>();
        failure.Error!.Code.ShouldBe(HttpErrorCodeNames.InvalidUrl);
        handler.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Response_body_is_bounded_and_keeps_content_metadata()
    {
        var bytes = Enumerable.Repeat((byte)0x41, 32).ToArray();
        var handler = new RecordingHandler((_, _) => Respond(
            HttpStatusCode.OK,
            bytes,
            "application/octet-stream"));
        await using var node = new HttpClientNode(
            new HttpClient(handler),
            new HttpClientNodeOptions { MaxResponseBodyBytes = 8 });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/content"
        }));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>();
        result.BodyTruncated.ShouldBeTrue();
        result.Body.OriginalBytes.Length.ShouldBe(8);
        result.Body.ContentType.ShouldBe("application/octet-stream");
    }

    [Theory]
    [InlineData("iso-8859-1")]
    [InlineData("\"iso-8859-1\"")]
    public async Task Response_body_preserves_and_honors_declared_charset(string charset)
    {
        byte[] bytes = [0xE9];
        var handler = new RecordingHandler((_, _) => Respond(
            HttpStatusCode.OK,
            bytes,
            $"text/plain; charset={charset}"));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/encoded"
        }));

        var body = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>().Body;
        body.OriginalBytes.AsSpan().ToArray().ShouldBe(bytes);
        body.Encoding.ShouldBe("iso-8859-1");
        DecodeText(body).ShouldBe("\u00e9");
    }

    [Fact]
    public async Task Invalid_response_charset_falls_back_to_utf8_when_decoded()
    {
        const string expected = "fallback \u2713";
        var handler = new RecordingHandler((_, _) => Respond(
            HttpStatusCode.OK,
            Encoding.UTF8.GetBytes(expected),
            "text/plain; charset=not-a-real-charset"));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/fallback"
        }));

        var body = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>().Body;
        body.Encoding.ShouldBe("not-a-real-charset");
        DecodeText(body).ShouldBe(expected);
    }

    [Fact]
    public async Task Structured_json_response_decodes_with_the_default_content_catalog()
    {
        const string json = "{\"message\":\"hello\"}";
        var handler = new RecordingHandler((_, _) => Respond(
            HttpStatusCode.OK,
            Encoding.UTF8.GetBytes(json),
            "application/problem+json"));
        await using var node = new HttpClientNode(new HttpClient(handler));
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/problem"
        }));

        var body = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.ShouldBeOfType<HttpResponseResult>().Body;
        body.ReadAsFlowValue(FlowContentCodecCatalog.CreateDefault())
            .GetObject()["message"].GetString().ShouldBe("hello");
    }

    [Fact]
    public async Task Success_emits_a_correlated_completion_event()
    {
        var handler = new RecordingHandler((_, _) => Respond(
            HttpStatusCode.OK,
            [],
            null));
        await using var node = new HttpClientNode(new HttpClient(handler));
        Sink(node.Output);
        var events = Sink(node.Events);
        var request = FlowMessage.Create(new HttpClientRequest
        {
            Url = "https://example.test/status"
        });

        await node.Input.SendAsync(request);

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        @event.Name.ShouldBe(HttpClientNode.RequestCompleted);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(request.CorrelationId);
    }

    [Fact]
    public void Constructor_requires_http_client()
        => Should.Throw<ArgumentNullException>(() => new HttpClientNode(null!));

    [Theory]
    [InlineData("boundedCapacity")]
    [InlineData("maxResponseBodyBytes")]
    [InlineData("maxDegreeOfParallelism")]
    [InlineData("defaultTimeoutMilliseconds")]
    public void Constructor_rejects_invalid_options(string option)
    {
        var options = option switch
        {
            "boundedCapacity" => new HttpClientNodeOptions { BoundedCapacity = 0 },
            "maxResponseBodyBytes" => new HttpClientNodeOptions { MaxResponseBodyBytes = 0 },
            "maxDegreeOfParallelism" => new HttpClientNodeOptions { MaxDegreeOfParallelism = 0 },
            _ => new HttpClientNodeOptions { DefaultTimeoutMilliseconds = 0 }
        };
        using var client = new HttpClient(new RecordingHandler((_, _) => Respond(
            HttpStatusCode.OK,
            [],
            null)));

        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new HttpClientNode(client, options));

        exception.Message.ShouldContain(option switch
        {
            "boundedCapacity" => "BoundedCapacity",
            "maxResponseBodyBytes" => "MaxResponseBodyBytes",
            "maxDegreeOfParallelism" => "MaxDegreeOfParallelism",
            _ => "DefaultTimeoutMilliseconds"
        });
    }

    private static string DecodeText(FlowContent content)
        => content.ReadAsFlowValue(FlowContentCodecCatalog.CreateDefault()).GetString();

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private static Task<HttpResponseMessage> Respond(
        HttpStatusCode status,
        byte[] body,
        string? contentType)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(body)
        };
        if (contentType is not null)
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        return Task.FromResult(response);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public byte[]? LastBody { get; private set; }

        public string? LastRequestHeader { get; private set; }

        public string? LastContentType { get; private set; }

        public string? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            LastRequestHeader = request.Headers.TryGetValues("X-Request", out var values)
                ? values.Single()
                : null;
            LastContentType = request.Content?.Headers.ContentType?.MediaType;
            LastRequestUri = request.RequestUri?.ToString();
            var response = await handler(request, cancellationToken);
            response.RequestMessage ??= request;
            return response;
        }
    }
}
