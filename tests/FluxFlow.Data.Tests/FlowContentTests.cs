using FluxFlow.Data;
using Shouldly;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Xunit;

namespace FluxFlow.Data.Tests;

public sealed class FlowContentTests
{
    [Fact]
    public void FromBytesCopiesAndPreservesOriginalRepresentation()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var content = FlowContent.FromBytes(bytes, "application/octet-stream");

        bytes[0] = 9;

        content.HasOriginalRepresentation.ShouldBeTrue();
        content.OriginalBytes.ShouldBe([1, 2, 3]);
        content.ContentType.ShouldBe("application/octet-stream");
        JsonSerializer.Serialize(content).ShouldNotContain("OriginalBytes");
    }

    [Fact]
    public void DefaultCatalogDecodesJsonAndStructuredJsonSuffixes()
    {
        var content = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("{\"count\":2,\"enabled\":true}"),
            "application/problem+json");

        var value = content.ReadAsFlowValue(FlowContentCodecCatalog.CreateDefault());

        value.Kind.ShouldBe(FlowValueKind.Object);
        value.GetObject()["count"].GetInteger().ShouldBe(2);
        value.GetObject()["enabled"].GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void ResolutionUsesExactThenSuffixThenFamilyThenFallback()
    {
        var exact = new ConstantCodec("exact");
        var suffix = new ConstantCodec("suffix");
        var family = new ConstantCodec("family");
        var fallback = new ConstantCodec("fallback");
        var catalog = new FlowContentCodecCatalog(
        [
            new(FlowContentCodecMatch.MediaFamily, "application", family),
            new(FlowContentCodecMatch.StructuredSuffix, "json", suffix),
            new(FlowContentCodecMatch.ExactMediaType, "application/problem+json", exact)
        ],
        fallback);

        catalog.Resolve("application/problem+json").ShouldBeSameAs(exact);
        catalog.Resolve("application/custom+json").ShouldBeSameAs(suffix);
        catalog.Resolve("application/xml").ShouldBeSameAs(family);
        catalog.Resolve("image/png").ShouldBeSameAs(fallback);
    }

    [Fact]
    public void TextCodecUsesQuotedCharsetFromContentType()
    {
        var content = FlowContent.FromBytes(
            new byte[] { 0x63, 0x61, 0x66, 0xe9 },
            "text/plain; charset=\"iso-8859-1\"");

        content.ReadAsFlowValue(FlowContentCodecCatalog.CreateDefault())
            .GetString().ShouldBe("cafe".Replace('e', '\u00e9'));
    }

    [Fact]
    public void InvalidEncodingFallsBackToUtf8()
    {
        var content = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("value"),
            "text/plain",
            "not-an-encoding");

        content.ReadAsFlowValue(FlowContentCodecCatalog.CreateDefault())
            .GetString().ShouldBe("value");
    }

    [Fact]
    public void ExplicitEncodingOverridesContentTypeCharset()
    {
        var content = FlowContent.FromBytes(
            new byte[] { 0x63, 0x61, 0x66, 0xe9 },
            "text/plain; charset=utf-8",
            "iso-8859-1");

        content.ReadAsFlowValue(FlowContentCodecCatalog.CreateDefault())
            .GetString().ShouldBe("caf\u00e9");
    }

    [Fact]
    public void UnknownContentFallsBackToBinary()
    {
        var content = FlowContent.FromBytes(new byte[] { 4, 5 }, "application/x-unknown");

        content.ReadAsFlowValue(FlowContentCodecCatalog.CreateDefault())
            .GetBinary().ShouldBe([4, 5]);
    }

    [Fact]
    public async Task DecodeRunsOnceAcrossConcurrentReaders()
    {
        var codec = new CountingCodec();
        var catalog = new FlowContentCodecCatalog(
            [new(FlowContentCodecMatch.ExactMediaType, "application/test", codec)],
            new BinaryFlowContentCodec());
        var content = FlowContent.FromBytes(new byte[] { 1 }, "application/test");

        var reads = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => content.ReadAsFlowValue(catalog)))
            .ToArray();
        await Task.WhenAll(reads);

        codec.Count.ShouldBe(1);
        reads.ShouldAllBe(task => task.Result.GetString() == "decoded");
    }

    [Fact]
    public void DecodeFailureIsCached()
    {
        var codec = new CountingCodec(throwOnDecode: true);
        var catalog = new FlowContentCodecCatalog(
            [new(FlowContentCodecMatch.ExactMediaType, "application/test", codec)],
            new BinaryFlowContentCodec());
        var content = FlowContent.FromBytes(new byte[] { 1 }, "application/test");

        Should.Throw<InvalidOperationException>(() => content.ReadAsFlowValue(catalog));
        Should.Throw<InvalidOperationException>(() => content.ReadAsFlowValue(catalog));

        codec.Count.ShouldBe(1);
    }

    [Fact]
    public void InvalidJsonFailureIsCached()
    {
        var content = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("{ invalid"),
            "application/json");
        var catalog = FlowContentCodecCatalog.CreateDefault();

        var first = Should.Throw<JsonException>(() => content.ReadAsFlowValue(catalog));
        var second = Should.Throw<JsonException>(() => content.ReadAsFlowValue(catalog));

        second.ShouldBeSameAs(first);
    }

    [Fact]
    public void UnsupportedJsonNumberRangeIsReportedAsInvalidJson()
    {
        var content = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("1e9999"),
            "application/json");

        Should.Throw<JsonException>(() =>
            content.ReadAsFlowValue(FlowContentCodecCatalog.CreateDefault()));
    }

    [Fact]
    public void ValueContentDoesNotRequireAContentCodec()
    {
        var value = FlowValue.From("ready");
        var content = FlowContent.FromValue(value, "application/x-flow-value");

        content.HasOriginalRepresentation.ShouldBeFalse();
        content.ReadAsFlowValue(FlowContentCodecCatalog.CreateDefault()).ShouldBeSameAs(value);
    }

    private sealed class ConstantCodec(string value) : IFlowContentCodec
    {
        public FlowValue Decode(ImmutableArray<byte> content, string? encoding) => FlowValue.From(value);
    }

    private sealed class CountingCodec(bool throwOnDecode = false) : IFlowContentCodec
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public FlowValue Decode(ImmutableArray<byte> content, string? encoding)
        {
            Interlocked.Increment(ref _count);
            if (throwOnDecode)
                throw new InvalidOperationException("decode failed");
            return FlowValue.From("decoded");
        }
    }
}
