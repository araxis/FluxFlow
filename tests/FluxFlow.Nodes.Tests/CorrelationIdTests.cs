using FluxFlow.Nodes;
using Shouldly;
using System.Text.Json;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class CorrelationIdTests
{
    [Fact]
    public void New_IsNonEmptyAndUnique()
    {
        var a = CorrelationId.New();
        var b = CorrelationId.New();

        a.IsEmpty.ShouldBeFalse();
        a.Value.ShouldNotBeNullOrWhiteSpace();
        a.ShouldNotBe(b);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_RejectsNullOrWhitespace(string? value)
        => Should.Throw<ArgumentException>(() => new CorrelationId(value!));

    [Fact]
    public void Equality_IsStructural_AndUsableAsDictionaryKey()
    {
        new CorrelationId("abc").ShouldBe(new CorrelationId("abc"));
        new CorrelationId("abc").ShouldNotBe(new CorrelationId("xyz"));

        var map = new Dictionary<CorrelationId, int> { [new CorrelationId("abc")] = 1 };
        map[new CorrelationId("abc")].ShouldBe(1);
        map.ContainsKey(new CorrelationId("xyz")).ShouldBeFalse();
    }

    [Fact]
    public void Json_RoundTripsAsBareString()
    {
        var id = new CorrelationId("trace-123");

        var json = JsonSerializer.Serialize(id);
        json.ShouldBe("\"trace-123\"");   // a bare string, not an object

        JsonSerializer.Deserialize<CorrelationId>(json).ShouldBe(id);
    }
}
