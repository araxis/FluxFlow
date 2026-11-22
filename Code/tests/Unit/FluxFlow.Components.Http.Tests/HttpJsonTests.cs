using System.Net;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;
using NativeClient = System.Net.Http.HttpClient;

namespace FluxFlow.Components.Http.Tests;

public sealed partial class HttpTests
{
    [Fact]
    public void Http_reply_preserves_declared_names_and_arbitrary_body_keys()
    {
        using var document = JsonDocument.Parse("""{"MixedCase":1,"lower":2,"snake_case":3}""");
        var reply = new HttpReply(201, new JsonFlowValue(document.RootElement),
            new Dictionary<string, string> { ["X-Mixed-Case"] = "value" }, "application/json");

        reply.Json.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["StatusCode", "Body", "Headers", "ContentType"]);
        reply.Json.GetProperty("Body").GetRawText().ShouldBe(document.RootElement.GetRawText());
        reply.Json.GetProperty("Headers").GetProperty("X-Mixed-Case").GetString().ShouldBe("value");
        var roundTrip = HttpReply.Read(new JsonFlowValue(reply.Json));
        roundTrip.StatusCode.ShouldBe(201);
        roundTrip.Body!.Json.GetRawText().ShouldBe(document.RootElement.GetRawText());
    }

    [Fact]
    public void Http_json_request_preserves_defaults_and_omits_absent_values()
    {
        var request = new HttpSendRequest { RequestUrl = "https://example.test/" };

        request.Json.GetRawText().ShouldBe(
            """{"Method":"GET","RequestUrl":"https://example.test/","Headers":{},"Encoding":"Text","ResponseEncoding":"Text"}""");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Http_json_request_preserves_body_shape_and_optional_values(bool explicitNull)
    {
        var headers = new Dictionary<string, string> { ["X-Custom"] = "original" };
        var request = new HttpSendRequest
        {
            RequestUrl = "/items", Method = "POST", Headers = headers,
            Body = explicitNull ? JsonFlowValue.Null : new CustomHttpBody(),
            ContentType = "application/json", Encoding = HttpBodyEncoding.Json,
            ResponseEncoding = HttpBodyEncoding.Base64, Timeout = TimeSpan.FromSeconds(3)
        };
        headers["X-Custom"] = "mutated";

        var json = request.Json;
        json.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["Method", "RequestUrl", "Headers", "Body", "ContentType", "Encoding", "ResponseEncoding", "Timeout"]);
        json.GetProperty("Method").GetString().ShouldBe("POST");
        json.GetProperty("RequestUrl").GetString().ShouldBe("/items");
        json.GetProperty("Headers").GetProperty("X-Custom").GetString().ShouldBe("original");
        json.GetProperty("Body").GetRawText().ShouldBe(explicitNull ? "null" : """{"items":[1,true,"two"]}""");
        json.GetProperty("ContentType").GetString().ShouldBe("application/json");
        json.GetProperty("Encoding").GetString().ShouldBe("Json");
        json.GetProperty("ResponseEncoding").GetString().ShouldBe("Base64");
        json.GetProperty("Timeout").GetString().ShouldBe("00:00:03");
    }

    [Theory]
    [InlineData(HttpBodyEncoding.Text, "response")]
    [InlineData(HttpBodyEncoding.Base64, "cmVzcG9uc2U=")]
    public async Task Http_json_response_preserves_headers_body_and_lineage(HttpBodyEncoding encoding, string body)
    {
        using var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                ReasonPhrase = "Accepted", Content = new StringContent("response")
            };
            response.Headers.Add("X-Result", new[] { "one", "two" });
            response.TrailingHeaders.Add("X-Trailer", "done");
            return Task.FromResult(response);
        });
        using var client = new NativeClient(handler);
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        var message = FlowMessage.Create(new HttpSendRequest
        {
            RequestUrl = "https://example.test/", ResponseEncoding = encoding
        }, correlationId: CorrelationId.New());

        await component.Input.SendAsync(message);
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeFalse(result.Error?.Message);
        var json = result.Value.ShouldBeOfType<HttpResponse>().Json;
        json.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["StatusCode", "ReasonPhrase", "Headers", "Body", "Encoding"]);
        json.GetProperty("StatusCode").GetInt32().ShouldBe(202);
        json.GetProperty("ReasonPhrase").GetString().ShouldBe("Accepted");
        json.GetProperty("Headers").GetProperty("X-Result").EnumerateArray()
            .Select(value => value.GetString()).ShouldBe(["one", "two"]);
        json.GetProperty("Headers").GetProperty("X-Trailer")[0].GetString().ShouldBe("done");
        json.GetProperty("Headers").GetProperty("Content-Type")[0].GetString().ShouldBe("text/plain; charset=utf-8");
        json.GetProperty("Body").GetString().ShouldBe(body);
        json.GetProperty("Encoding").GetString().ShouldBe(encoding.ToString());
        result.TraceId.ShouldBe(message.TraceId);
        result.CorrelationId.ShouldBe(message.CorrelationId);
        result.CausationId.ShouldBe(message.MessageId);
    }

    private sealed class CustomHttpBody : FlowValue
    {
        protected override JsonElement CreateJson()
        {
            using var document = JsonDocument.Parse("""{"items":[1,true,"two"]}""");
            return document.RootElement.Clone();
        }
    }
}
