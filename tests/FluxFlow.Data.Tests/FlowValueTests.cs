using FluxFlow.Data;
using Shouldly;
using System.Globalization;
using System.Numerics;
using Xunit;

namespace FluxFlow.Data.Tests;

public sealed class FlowValueTests
{
    [Fact]
    public void SupportsEveryFoundationKind()
    {
        var timestamp = new DateTimeOffset(2026, 7, 17, 12, 30, 40, TimeSpan.FromHours(2));
        var values = new[]
        {
            FlowValue.Null,
            FlowValue.From(true),
            FlowValue.From(BigInteger.Parse("123456789012345678901234567890")),
            FlowValue.From(12.50m),
            FlowValue.From(12.5d),
            FlowValue.From("text"),
            FlowValue.FromBinary(new byte[] { 1, 2, 3 }),
            FlowValue.From(timestamp),
            FlowValue.From(new DateOnly(2026, 7, 17)),
            FlowValue.From(new TimeOnly(12, 30, 40)),
            FlowValue.From(TimeSpan.FromSeconds(90)),
            FlowValue.From(Guid.Parse("7c79ec31-c949-4dd2-a711-3df360e5782f")),
            FlowValue.FromArray([FlowValue.From(1L)]),
            FlowValue.FromObject([new("name", FlowValue.From("value"))])
        };

        values.Select(value => value.Kind).ShouldBe(Enum.GetValues<FlowValueKind>());
    }

    [Fact]
    public void ObjectEqualityIgnoresPropertyOrderButUsesOrdinalKeys()
    {
        var first = FlowValue.FromObject(
        [
            new("b", FlowValue.From(2L)),
            new("a", FlowValue.From(1L))
        ]);
        var second = FlowValue.FromObject(
        [
            new("a", FlowValue.From(1L)),
            new("b", FlowValue.From(2L))
        ]);
        var differentCase = FlowValue.FromObject(
        [
            new("A", FlowValue.From(1L)),
            new("b", FlowValue.From(2L))
        ]);

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
        first.ShouldNotBe(differentCase);
        FlowValueCanonicalJson.Serialize(first).ShouldBe(FlowValueCanonicalJson.Serialize(second));
    }

    [Fact]
    public void ArrayOrderAndNumericKindsRemainDistinct()
    {
        FlowValue.FromArray([FlowValue.From(1L), FlowValue.From(2L)])
            .ShouldNotBe(FlowValue.FromArray([FlowValue.From(2L), FlowValue.From(1L)]));
        FlowValue.From(1L).ShouldNotBe(FlowValue.From(1m));
        FlowValue.From(1m).ShouldNotBe(FlowValue.From(1d));
    }

    [Fact]
    public void ConstructionCopiesMutableInputs()
    {
        var bytes = new byte[] { 1, 2 };
        var array = new List<FlowValue> { FlowValue.From("first") };
        var properties = new Dictionary<string, FlowValue> { ["name"] = FlowValue.From("before") };

        var binaryValue = FlowValue.FromBinary(bytes);
        var arrayValue = FlowValue.FromArray(array);
        var objectValue = FlowValue.FromObject(properties);

        bytes[0] = 9;
        array[0] = FlowValue.From("after");
        properties["name"] = FlowValue.From("after");

        binaryValue.GetBinary().ShouldBe([1, 2]);
        arrayValue.GetArray()[0].GetString().ShouldBe("first");
        objectValue.GetObject()["name"].GetString().ShouldBe("before");
    }

    [Fact]
    public void ObjectConstructionRejectsDuplicateOrdinalKeys()
    {
        Should.Throw<ArgumentException>(() => FlowValue.FromObject(
        [
            new("name", FlowValue.From(1L)),
            new("name", FlowValue.From(2L))
        ]));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FloatingPointRejectsNonFiniteValues(double value)
        => Should.Throw<ArgumentOutOfRangeException>(() => FlowValue.From(value));

    [Fact]
    public void CanonicalJsonRoundTripsEveryKind()
    {
        var value = FlowValue.FromObject(
        [
            new("null", FlowValue.Null),
            new("boolean", FlowValue.From(true)),
            new("integer", FlowValue.From(BigInteger.Parse("12345678901234567890"))),
            new("decimal", FlowValue.From(1.25m)),
            new("floating", FlowValue.From(1.25d)),
            new("string", FlowValue.From("value")),
            new("binary", FlowValue.FromBinary(new byte[] { 1, 2, 3 })),
            new("timestamp", FlowValue.From(new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.FromHours(2)))),
            new("date", FlowValue.From(new DateOnly(2026, 7, 17))),
            new("time", FlowValue.From(new TimeOnly(1, 2, 3, 456))),
            new("duration", FlowValue.From(TimeSpan.FromMilliseconds(1500))),
            new("guid", FlowValue.From(Guid.Parse("7c79ec31-c949-4dd2-a711-3df360e5782f"))),
            new("array", FlowValue.FromArray([FlowValue.From("item")]))
        ]);

        var json = FlowValueCanonicalJson.Serialize(value);
        var restored = FlowValueCanonicalJson.Deserialize(json);

        restored.ShouldBe(value);
        FlowValueCanonicalJson.Serialize(restored).ShouldBe(json);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void CanonicalJsonIsCultureIndependentAndDeterministic(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            var value = FlowValue.FromObject(
            [
                new("z", FlowValue.From(1234.50m)),
                new("a", FlowValue.From(12.5d))
            ]);

            FlowValueCanonicalJson.Serialize(value).ShouldBe(
                "{\"kind\":\"object\",\"value\":{" +
                "\"a\":{\"kind\":\"floatingPoint\",\"value\":\"12.5\"}," +
                "\"z\":{\"kind\":\"decimal\",\"value\":\"1234.5\"}}}");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void FloatingPointCanonicalizationNormalizesSignedZero()
    {
        var positive = FlowValue.From(0d);
        var negative = FlowValue.From(-0d);

        negative.ShouldBe(positive);
        FlowValueCanonicalJson.Serialize(negative).ShouldBe(
            "{\"kind\":\"floatingPoint\",\"value\":\"0\"}");
    }

    [Theory]
    [InlineData("{\"kind\":\"null\",\"value\":null}")]
    [InlineData("{\"kind\":\"string\",\"kind\":\"string\",\"value\":\"x\"}")]
    [InlineData("{\"kind\":\"string\",\"value\":\"x\",\"extra\":true}")]
    [InlineData("{\"kind\":\"decimal\",\"value\":\"79228162514264337593543950336\"}")]
    public void CanonicalJsonRejectsAmbiguousShapes(string json)
        => Should.Throw<System.Text.Json.JsonException>(() => FlowValueCanonicalJson.Deserialize(json));
}
