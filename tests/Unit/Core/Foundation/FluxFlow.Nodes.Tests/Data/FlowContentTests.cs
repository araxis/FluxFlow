using System.Collections.Immutable;
using System.Text.Json;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Data.Tests;

public sealed class FlowContentTests
{
    [Fact]
    public void FromBytes_CopiesExternalBufferAndPreservesExactBytes()
    {
        var source = new byte[] { 1, 2, 3 };

        var content = FlowContent.FromBytes(source, " application/octet-stream ", " binary ");
        source[0] = 9;

        content.Bytes.ShouldBe(ImmutableArray.Create<byte>(1, 2, 3));
        content.ContentType.ShouldBe("application/octet-stream");
        content.Encoding.ShouldBe("binary");
    }

    [Fact]
    public void FromBytes_SupportsEmptyContentAndNormalizesMetadata()
    {
        var content = FlowContent.FromBytes(ReadOnlyMemory<byte>.Empty, " ", null);

        content.Bytes.ShouldBeEmpty();
        content.ContentType.ShouldBeNull();
        content.Encoding.ShouldBeNull();
    }

    [Fact]
    public void Json_RoundTripsBytesAndMetadata()
    {
        var content = FlowContent.FromBytes(new byte[] { 4, 5 }, "application/test", "utf-8");

        var json = JsonSerializer.Serialize(content);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("formatVersion").GetInt32().ShouldBe(1);
        root.GetProperty("bytes").GetString().ShouldBe(Convert.ToBase64String([4, 5]));
        root.GetProperty("contentType").GetString().ShouldBe("application/test");
        root.GetProperty("encoding").GetString().ShouldBe("utf-8");

        var restored = JsonSerializer.Deserialize<FlowContent>(json).ShouldNotBeNull();

        restored.Bytes.ShouldBe(content.Bytes);
        restored.ContentType.ShouldBe(content.ContentType);
        restored.Encoding.ShouldBe(content.Encoding);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"formatVersion\":2,\"bytes\":\"AQ==\"}")]
    [InlineData("{\"formatVersion\":1,\"bytes\":\"not-base64\"}")]
    [InlineData("{\"formatVersion\":1,\"bytes\":1}")]
    public void Json_RejectsInvalidVersionedContent(string json)
        => Should.Throw<JsonException>(() => JsonSerializer.Deserialize<FlowContent>(json));

    [Fact]
    public void Json_ReadsExistingVersionedShapeCaseInsensitively()
    {
        const string json =
            """{"FormatVersion":1,"Bytes":"AAf/","ContentType":" application/test ","Encoding":null}""";

        var restored = JsonSerializer.Deserialize<FlowContent>(json).ShouldNotBeNull();

        restored.Bytes.AsSpan().ToArray().ShouldBe(new byte[] { 0, 7, 255 });
        restored.ContentType.ShouldBe("application/test");
        restored.Encoding.ShouldBeNull();
    }

    [Fact]
    public void Contract_HasNoHiddenDecodeOrValueState()
    {
        var publicMembers = typeof(FlowContent).GetMembers()
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

        publicMembers.ShouldNotContain("ReadAsFlowValue");
        publicMembers.ShouldNotContain("FromValue");
        publicMembers.ShouldNotContain("HasOriginalRepresentation");
        publicMembers.ShouldNotContain("OriginalBytes");
    }
}
