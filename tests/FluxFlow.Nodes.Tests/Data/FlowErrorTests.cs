using System.Text.Json;
using FluxFlow.Data;
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
                details: document.RootElement);
        }

        error.Details!.Value.GetProperty("providerCode").GetInt32().ShouldBe(42);
    }

    [Fact]
    public void Json_RoundTripsStableShapeWithoutExceptions()
    {
        var details = JsonSerializer.SerializeToElement(new { status = 503 });
        var error = new FlowError(
            "transport.unavailable",
            "Transport unavailable.",
            "transport",
            true,
            details);

        var json = JsonSerializer.Serialize(error);
        var restored = JsonSerializer.Deserialize<FlowError>(json).ShouldNotBeNull();

        json.ShouldBe(
            "{\"code\":\"transport.unavailable\",\"message\":\"Transport unavailable.\",\"category\":\"transport\",\"isTransient\":true,\"details\":{\"status\":503}}");
        restored.Code.ShouldBe(error.Code);
        restored.Message.ShouldBe(error.Message);
        restored.Category.ShouldBe(error.Category);
        restored.IsTransient.ShouldBe(error.IsTransient);
        restored.Details!.Value.GetProperty("status").GetInt32().ShouldBe(503);
        json.ShouldNotContain("exception", Case.Insensitive);
    }
}
