using FluxFlow.Data;
using Shouldly;
using System.Text.Json;
using Xunit;

namespace FluxFlow.Data.Tests;

public sealed class FlowResultTests
{
    [Fact]
    public void SuccessHasNoError()
    {
        var timestamp = new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero);
        var result = FlowResult<string>.Success("completed", "value", timestamp);

        result.Kind.ShouldBe("completed");
        result.Value.ShouldBe("value");
        result.Error.ShouldBeNull();
        result.IsError.ShouldBeFalse();
        result.Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public void FailureDerivesIsErrorAndPreservesWorkflowDetails()
    {
        var timestamp = new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero);
        var error = new FlowError(
            "client.unavailable",
            "Client is disconnected.",
            "availability",
            isTransient: true,
            FlowValue.FromObject([new("client", FlowValue.From("client1"))]));

        var result = FlowResult<string>.Failure("failed", error, timestamp);

        result.IsError.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
        result.Error!.Details.GetObject()["client"].GetString().ShouldBe("client1");
        result.Value.ShouldBeNull();
    }

    [Fact]
    public void ResultAndErrorNamesRejectWhitespace()
    {
        Should.Throw<ArgumentException>(() => FlowResult<string>.Success(" ", "value", DateTimeOffset.UtcNow));
        Should.Throw<ArgumentException>(() => new FlowError(" ", "message", "category"));
        Should.Throw<ArgumentException>(() => new FlowError("code", " ", "category"));
        Should.Throw<ArgumentException>(() => new FlowError("code", "message", " "));
    }

    [Fact]
    public void ErrorResultJsonContractIsStable()
    {
        var result = FlowResult<string>.Failure(
            "failed",
            new FlowError(
                "client.unavailable",
                "Client is disconnected.",
                "availability",
                isTransient: true,
                FlowValue.FromObject([new("client", FlowValue.From("client1"))])),
            new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero));

        JsonSerializer.Serialize(result).ShouldBe(
            "{\"Kind\":\"failed\",\"Value\":null,\"Error\":{" +
            "\"Code\":\"client.unavailable\",\"Message\":\"Client is disconnected.\"," +
            "\"Category\":\"availability\",\"IsTransient\":true," +
            "\"Details\":{\"kind\":\"object\",\"value\":{" +
            "\"client\":{\"kind\":\"string\",\"value\":\"client1\"}}}}," +
            "\"IsError\":true,\"Timestamp\":\"2026-07-17T01:02:03+00:00\"}");
    }
}
