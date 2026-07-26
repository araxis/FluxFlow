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

        var restored = JsonSerializer.Deserialize<FlowContent>(
            JsonSerializer.Serialize(content)).ShouldNotBeNull();

        restored.Bytes.ShouldBe(content.Bytes);
        restored.ContentType.ShouldBe(content.ContentType);
        restored.Encoding.ShouldBe(content.Encoding);
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
