using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.DataGeneration.Tests;

public sealed class BogusFunctionTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("fa-IR")]
    public async Task Date_and_time_ranges_use_invariant_formats_and_absolute_timestamp_bounds(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var result = await Generate("""
            {
              'date': $fakeDate('2024-02-28', '2024-03-01'),
              'fixedDate': $fakeDate('2024-02-29', '2024-02-29'),
              'timestamp': $fakeDateTime('2026-01-01T12:00:00+02:00', '2026-01-01T10:00:01Z'),
              'fixedTimestamp': $fakeDateTime('2026-01-01T12:00:00.1234567+02:00', '2026-01-01T10:00:00.1234567Z'),
              'time': $fakeTime('00:00', '23:59:59.9999999'),
              'fixedTime': $fakeTime('12:34:56.1234567', '12:34:56.1234567'),
              'midnight': $fakeTime('00:00:00', '00:00'),
              'duration': $fakeTimeSpan('2.01:02:03.1234567'),
              'zero': $fakeTimeSpan('00:00:00')
            }
            """);
            DateOnly.ParseExact(Text(result, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .ShouldBeInRange(new DateOnly(2024, 2, 28), new DateOnly(2024, 3, 1));
            Text(result, "fixedDate").ShouldBe("2024-02-29");
            var timestamp = ParseTimestamp(result, "timestamp");
            timestamp.Offset.ShouldBe(TimeSpan.Zero);
            timestamp.ShouldBeInRange(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 1, 10, 0, 1, TimeSpan.Zero));
            Text(result, "fixedTimestamp").ShouldBe("2026-01-01T10:00:00.1234567+00:00");
            TimeOnly.ParseExact(Text(result, "time"), "HH:mm:ss.fffffff", CultureInfo.InvariantCulture)
                .ShouldBeInRange(TimeOnly.MinValue, TimeOnly.MaxValue);
            Text(result, "fixedTime").ShouldBe("12:34:56.1234567");
            Text(result, "midnight").ShouldBe("00:00:00.0000000");
            TimeSpan.ParseExact(Text(result, "duration"), "c", CultureInfo.InvariantCulture)
                .ShouldBeInRange(TimeSpan.Zero, TimeSpan.ParseExact("2.01:02:03.1234567", "c", CultureInfo.InvariantCulture));
            Text(result, "zero").ShouldBe("00:00:00");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task Relative_dates_are_utc_timestamps_within_the_requested_window()
    {
        var before = DateTimeOffset.UtcNow;
        var result = await Generate("{'past': $fakePast(2), 'future': $fakeFuture(2), 'recent': $fakeRecent(3), 'soon': $fakeSoon(3)}");
        var after = DateTimeOffset.UtcNow;
        ParseTimestamp(result, "past").ShouldBeInRange(before.AddYears(-2), after);
        ParseTimestamp(result, "future").ShouldBeInRange(before, after.AddYears(2));
        ParseTimestamp(result, "recent").ShouldBeInRange(before.AddDays(-3), after);
        ParseTimestamp(result, "soon").ShouldBeInRange(before, after.AddDays(3));
        foreach (var property in result.EnumerateObject())
            ParseTimestamp(result, property.Name).Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Contact_location_text_and_product_functions_are_available_in_the_expression()
    {
        var result = await Generate("""
        {
          'firstName': $fakeFirstName(), 'lastName': $fakeLastName(), 'userName': $fakeUserName(),
          'phone': $fakePhone(), 'url': $fakeUrl(), 'ip': $fakeIp(),
          'street': $fakeStreetAddress(), 'city': $fakeCity(), 'zip': $fakeZipCode(), 'country': $fakeCountry(),
          'latitude': $fakeLatitude(), 'longitude': $fakeLongitude(),
          'company': $fakeCompanyName(), 'product': $fakeProductName(),
          'word': $fakeWord(), 'sentence': $fakeSentence(), 'paragraph': $fakeParagraph(),
          'month': $fakeMonth(), 'weekday': $fakeWeekday()
        }
        """, "sv");
        foreach (var property in result.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
            property.Value.GetString().ShouldNotBeNullOrWhiteSpace(property.Name);
        // All bindings must return a value: JSONata otherwise omits undefined object properties.
        result.EnumerateObject().Count().ShouldBe(19);
        Uri.TryCreate(Text(result, "url"), UriKind.Absolute, out _).ShouldBeTrue();
        IPAddress.TryParse(Text(result, "ip"), out _).ShouldBeTrue();
        result.GetProperty("latitude").GetDouble().ShouldBeInRange(-90, 90);
        result.GetProperty("longitude").GetDouble().ShouldBeInRange(-180, 180);
    }

    private static string Text(JsonElement result, string property) => result.GetProperty(property).GetString()!;

    private static DateTimeOffset ParseTimestamp(JsonElement result, string property)
        => DateTimeOffset.ParseExact(Text(result, property), "O", CultureInfo.InvariantCulture);

    private static async Task<JsonElement> Generate(string expression, string locale = "en")
    {
        await using var generator = new DataGenerator(new() { Expression = expression, Locale = locale });
        var capture = new BufferBlock<FlowMessage>();
        using var link = generator.Output.LinkTo(capture);
        await generator.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(new { }))));
        var result = await capture.ReceiveAsync(TimeSpan.FromSeconds(10));
        result.IsError.ShouldBeFalse(result.Error?.Message);
        return result.Value.Json;
    }
}
