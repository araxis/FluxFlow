using FluxFlow.Components.FileSystem.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.FileSystem.Tests;

public sealed class BoundedFileReaderTests
{
    [Fact]
    public async Task ReadAsync_StopsAfterOneByteBeyondLimit()
    {
        await using var stream = new MemoryStream(Enumerable.Range(0, 100).Select(value => (byte)value).ToArray());

        var result = await BoundedFileReader.ReadAsync(stream, maxBytes: 3);

        result.LimitExceeded.ShouldBeTrue();
        result.Bytes.ShouldBeEmpty();
        result.BytesRead.ShouldBe(4);
        stream.Position.ShouldBe(4);
    }

    [Fact]
    public async Task ReadAsync_WithNoLimitReadsEntireStream()
    {
        byte[] expected = [1, 2, 3, 4];
        await using var stream = new MemoryStream(expected);

        var result = await BoundedFileReader.ReadAsync(stream, maxBytes: null);

        result.LimitExceeded.ShouldBeFalse();
        result.Bytes.ShouldBe(expected);
        result.BytesRead.ShouldBe(expected.Length);
    }
}
