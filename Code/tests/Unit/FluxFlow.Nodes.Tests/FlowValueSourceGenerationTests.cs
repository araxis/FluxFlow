using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowValueSourceGenerationTests
{
    [Theory]
    [InlineData("\"hello\"")]
    [InlineData("42")]
    [InlineData("[1,null,true]")]
    [InlineData("{\"nested\":{\"value\":7}}")]
    [InlineData("null")]
    public void External_generated_metadata_preserves_owned_json_shapes(string json)
    {
        FlowValue value;
        using (var document = JsonDocument.Parse(json))
            value = ((FlowValue)new JsonFlowValue(document.RootElement));

        var metadata = FlowValueTestJsonContext.Default.FlowValue;
        var serialized = JsonSerializer.Serialize(value, metadata);
        var restored = JsonSerializer.Deserialize(serialized, metadata);

        serialized.ShouldBe(json);
        restored.ShouldNotBeNull().Json.GetRawText().ShouldBe(json);
        JsonSerializer.Serialize(restored, metadata).ShouldBe(json);
    }

    [Fact]
    public void Null_reference_serializes_as_json_null_at_the_optional_boundary()
    {
        FlowValue? missing = null;

        JsonSerializer.Serialize<FlowValue>(missing!, FlowValueTestJsonContext.Default.FlowValue).ShouldBe("null");
    }
}

[JsonSerializable(typeof(FlowValue))]
internal sealed partial class FlowValueTestJsonContext : JsonSerializerContext;
