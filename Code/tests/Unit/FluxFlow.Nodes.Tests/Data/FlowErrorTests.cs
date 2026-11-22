using System.Text.Json;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Data.Tests;

public sealed class FlowErrorTests
{
    [Fact]
    public void Constructor_NormalizesRequiredFields()
    {
        var error = new FlowError(" code ", " message ", " category ", true);

        error.Code.ShouldBe("code");
        error.Message.ShouldBe("message");
        error.Category.ShouldBe("category");
        error.IsTransient.ShouldBeTrue();
        error.Details.ShouldBeNull();
    }

    [Theory]
    [InlineData("", "message", "category")]
    [InlineData("code", " ", "category")]
    [InlineData("code", "message", "")]
    public void Constructor_RejectsMissingRequiredFields(
        string code,
        string message,
        string category)
    {
        Should.Throw<ArgumentException>(() => new FlowError(code, message, category));
    }

    [Fact]
    public void Details_OutliveCallerDocument()
    {
        FlowError error;
        using (var document = JsonDocument.Parse("{\"providerCode\":42}"))
        {
            error = new FlowError(
                "provider.failed",
                "Provider failed.",
                "provider",
                details: new JsonFlowValue(document.RootElement));
        }

        error.Details!.Json.GetProperty("providerCode").GetInt32().ShouldBe(42);
    }

    [Fact]
    public void Json_RoundTripsStableShapeWithoutExceptions()
    {
        var details = new JsonFlowValue(JsonSerializer.SerializeToElement(new { status = 503 }));
        var error = new FlowError(
            "transport.unavailable",
            "Transport unavailable.",
            "transport",
            true,
            details);

        var json = JsonSerializer.Serialize(error);
        error.Json.GetRawText().ShouldBe(json);
        var restored = JsonSerializer.Deserialize<FlowError>(json).ShouldNotBeNull();

        json.ShouldBe(
            "{\"Code\":\"transport.unavailable\",\"Message\":\"Transport unavailable.\",\"Category\":\"transport\",\"IsTransient\":true,\"Details\":{\"status\":503}}");
        restored.Code.ShouldBe(error.Code);
        restored.Message.ShouldBe(error.Message);
        restored.Category.ShouldBe(error.Category);
        restored.IsTransient.ShouldBe(error.IsTransient);
        restored.Details!.Json.GetProperty("status").GetInt32().ShouldBe(503);
        json.ShouldNotContain("exception", Case.Insensitive);
    }

    [Fact]
    public void Typed_details_retain_identity_and_convert_only_when_json_is_needed()
    {
        var details = new RetryDetails(5);
        var error = new FlowError("busy", "Try later.", "provider", true, details);
        var message = FlowMessage.Create(error);
        message.Error!.Details.ShouldBeSameAs(details);
        details.Seconds.ShouldBe(5);
        details.JsonBuilds.ShouldBe(0);

        // This exercises the source-generated error writer with a user-defined value.
        error.Json.GetProperty("Details").GetProperty("retryAfterSeconds").GetInt32().ShouldBe(5);
        var json = JsonSerializer.Serialize(message);
        var restored = JsonSerializer.Deserialize<FlowMessage>(json).ShouldNotBeNull();
        restored.Error!.Details.ShouldBeOfType<JsonFlowValue>()
            .Json.GetProperty("retryAfterSeconds").GetInt32().ShouldBe(5);
        JsonSerializer.Serialize(restored).ShouldBe(json);
        details.JsonBuilds.ShouldBe(1);
    }

    [Theory]
    [InlineData("{\"Code\":\"busy\",\"Message\":\"Try later.\",\"Category\":\"provider\"}")]
    [InlineData("{\"Code\":\"busy\",\"Message\":\"Try later.\",\"Category\":\"provider\",\"Details\":null}")]
    public void Missing_and_null_details_remain_optional(string json)
    {
        var error = JsonSerializer.Deserialize<FlowError>(json).ShouldNotBeNull();
        error.Details.ShouldBeNull();
        error.Json.GetProperty("Details").ValueKind.ShouldBe(JsonValueKind.Null);
        JsonSerializer.Deserialize<FlowError>(error.Json.GetRawText())!.Details.ShouldBeNull();
    }

    [Theory]
    [InlineData("123")]
    [InlineData("\"123\"")]
    [InlineData("false")]
    [InlineData("[1,\"two\"]")]
    public void Dynamic_details_preserve_json_value_shapes(string json)
    {
        using var document = JsonDocument.Parse(json);
        var error = new FlowError("failed", "Failure.", "provider", details: new JsonFlowValue(document.RootElement));
        var restored = JsonSerializer.Deserialize<FlowError>(error.Json.GetRawText()).ShouldNotBeNull();
        restored.Details!.Json.GetRawText().ShouldBe(json);
    }

    private sealed class RetryDetails(int seconds) : FlowValue
    {
        public int Seconds { get; } = seconds;
        public int JsonBuilds { get; private set; }
        protected override JsonElement CreateJson()
        {
            JsonBuilds++;
            return JsonSerializer.SerializeToElement(new { retryAfterSeconds = Seconds });
        }
    }
}
