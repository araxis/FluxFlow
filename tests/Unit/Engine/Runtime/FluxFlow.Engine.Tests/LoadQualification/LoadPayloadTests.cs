using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Engine.Benchmarks.LoadQualification;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests.LoadQualification;

public sealed class LoadPayloadTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(257)]
    public void Json_payload_preserves_the_requested_ascii_data(int bytes)
    {
        var value = LoadPayload.Create(new() { PayloadBytes = bytes });
        value.Kind.ShouldBe(JsonValueKind.Object);
        var json = value.ToJsonElement();
        json.EnumerateObject().Select(property => property.Name).ShouldBe(new[] { "data" });
        json.GetProperty("data").GetString().ShouldBe(new string('x', bytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(257)]
    public void Binary_payload_preserves_exact_content_and_metadata(int bytes)
    {
        var value = LoadPayload.Create(new() { PayloadKind = "binary", PayloadBytes = bytes });
        value.Kind.ShouldBe(JsonValueKind.Object);
        var content = value.Deserialize<FlowContent>().ShouldNotBeNull();
        content.Bytes.Length.ShouldBe(bytes);
        content.ContentType.ShouldBe("application/octet-stream");
        content.Encoding.ShouldBeNull();
        content.Bytes.ShouldBe(Enumerable.Range(0, bytes).Select(index => (byte)(index * 31 + 7)));
        if (bytes != 0) content.Bytes.Take(4).ShouldBe(new byte[] { 7, 38, 69, 100 });
    }
}
