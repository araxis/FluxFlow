using System.Text.Json;
using FluxFlow.Components.Http.Nodes;
using System.Text;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Http.Tests;

public sealed class HttpRequestJsonTests
{
    [Fact]
    public void Typed_request_keeps_identity_and_generated_json_round_trips()
    {
        FluxFlow.Components.Http.Contracts.HttpClientRequest request = new() { Url = "https://example.test/", Method = "POST", Body = FlowContent.FromBytes(new byte[] { 1, 2, 3 }) };
        HttpRequestJson.Read(request).ShouldBeSameAs(request);
        FlowValue portable = new JsonFlowValue(request.Json);
        var restored = HttpRequestJson.Read(portable);
        restored.ShouldNotBeSameAs(request);
        restored.Json.GetRawText().ShouldBe(request.Json.GetRawText());
    }

    [Theory]
    [InlineData("\"سلام\\nHTTP\"", "سلام\nHTTP", "text/plain")]
    [InlineData("{ \"orderId\" : 42 }", "{\"orderId\":42}", "application/json")]
    [InlineData("[1, true, null]", "[1,true,null]", "application/json")]
    [InlineData("true", "true", "application/json")]
    [InlineData("42", "42", "application/json")]
    public void Body_conversion_preserves_text_and_compact_json(string body, string expected, string contentType)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            "{\"METHOD\":\"POST\",\"URL\":\"https://example.test\",\"BODY\":" + body +
            ",\"headers\":{\"X-Tenant\":\"north\"},\"timeout\":\"00:00:03\"}");
        var result = HttpRequestJson.Read((FlowValue)new JsonFlowValue(document.RootElement));
        result.Method.ShouldBe("POST");
        result.Headers["x-tenant"].ShouldBe("north");
        result.Timeout.ShouldBe(TimeSpan.FromSeconds(3));
        result.Body!.Bytes.ToArray().ShouldBe(Encoding.UTF8.GetBytes(expected));
        result.Body.ContentType.ShouldBe(contentType);
        result.Body.Encoding.ShouldBe("utf-8");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"body\":null}")]
    public void Missing_or_null_body_remains_absent(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var result = HttpRequestJson.Read((FlowValue)new JsonFlowValue(document.RootElement));
        result.Body.ShouldBeNull();
    }

    [Fact]
    public void Exact_content_preserves_binary_bytes_and_metadata_case_insensitively()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            "{\"Body\":{\"FORMATVERSION\":1,\"BYTES\":\"AH//\",\"CONTENTTYPE\":\"application/octet-stream\",\"ENCODING\":null}}");
        var result = HttpRequestJson.Read((FlowValue)new JsonFlowValue(document.RootElement));
        result.Body!.Bytes.ToArray().ShouldBe(new byte[] { 0, 127, 255 });
        result.Body.ContentType.ShouldBe("application/octet-stream");
        result.Body.Encoding.ShouldBeNull();
    }

    [Theory]
    [InlineData("{\"body\":{\"formatVersion\":1,\"bytes\":\"!\"}}")]
    [InlineData("{\"body\":{\"formatVersion\":2,\"bytes\":\"\"}}")]
    [InlineData("{\"body\":{\"bytes\":\"\"}}")]
    public void Malformed_exact_content_keeps_http_error_contract(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var result = Should.Throw<JsonException>(() => HttpRequestJson.Read((FlowValue)new JsonFlowValue(document.RootElement)));
    }

    [Fact]
    public void Read_accepts_a_structural_request_and_normalizes_json_body()
    {
        var value = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new
        {
            method = "POST",
            url = "https://example.test/orders",
            body = new { orderId = 42 }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var result = HttpRequestJson.Read(value);

        result.Method.ShouldBe("POST");
        result.Url.ShouldBe("https://example.test/orders");
        result.Body.ShouldNotBeNull().ContentType.ShouldBe("application/json");
        Encoding.UTF8.GetString(result.Body.Bytes.ToArray()).ShouldBe("{\"orderId\":42}");
    }

    [Fact]
    public void Read_rejects_a_non_object_with_http_owned_error()
    {
        var value = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("invalid", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var result = Should.Throw<JsonException>(() => HttpRequestJson.Read(value));

    }
}
