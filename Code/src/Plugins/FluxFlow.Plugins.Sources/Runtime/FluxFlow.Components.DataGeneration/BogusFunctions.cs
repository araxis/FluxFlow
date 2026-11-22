using System.Globalization;
using Bogus;
using Jsonata.Net.Native;

namespace FluxFlow.Components.DataGeneration;

/// <summary>Explicit function bindings over one component's Faker instance.</summary>
internal sealed class BogusFunctions
{
    private readonly Faker _faker;

    internal BogusFunctions(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        if (!Database.LocaleResourceExists(locale))
            throw new ArgumentException($"Unknown Bogus locale '{locale}'.", nameof(locale));
        _faker = new Faker(locale) { Random = new Randomizer(Random.Shared.Next()) };
    }

    internal void Bind(EvaluationEnvironment environment)
    {
        environment.BindFunction("fakeInt", (Func<int, int, int>)Integer);
        environment.BindFunction("fakeDecimal", (Func<decimal, decimal, decimal>)Decimal);
        environment.BindFunction("fakeBool", (Func<bool>)(() => _faker.Random.Bool()));
        environment.BindFunction("fakeString", (Func<int, string>)Text);
        environment.BindFunction("fakePattern", (Func<string, string>)(pattern => _faker.Random.Replace(pattern)));
        environment.BindFunction("fakeFullName", (Func<string>)(() => _faker.Name.FullName()));
        environment.BindFunction("fakeEmail", (Func<string>)(() => _faker.Internet.Email()));
        environment.BindFunction("fakeUuid", (Func<string>)(() => _faker.Random.Guid().ToString()));
        environment.BindFunction("fakeFirstName", (Func<string>)(() => _faker.Name.FirstName()));
        environment.BindFunction("fakeLastName", (Func<string>)(() => _faker.Name.LastName()));
        environment.BindFunction("fakeUserName", (Func<string>)(() => _faker.Internet.UserName()));
        environment.BindFunction("fakePhone", (Func<string>)(() => _faker.Phone.PhoneNumber()));
        environment.BindFunction("fakeUrl", (Func<string>)(() => _faker.Internet.Url()));
        environment.BindFunction("fakeIp", (Func<string>)(() => _faker.Internet.Ip()));
        environment.BindFunction("fakeStreetAddress", (Func<string>)(() => _faker.Address.StreetAddress()));
        environment.BindFunction("fakeCity", (Func<string>)(() => _faker.Address.City()));
        environment.BindFunction("fakeZipCode", (Func<string>)(() => _faker.Address.ZipCode()));
        environment.BindFunction("fakeCountry", (Func<string>)(() => _faker.Address.Country()));
        environment.BindFunction("fakeLatitude", (Func<double>)(() => _faker.Address.Latitude()));
        environment.BindFunction("fakeLongitude", (Func<double>)(() => _faker.Address.Longitude()));
        environment.BindFunction("fakeCompanyName", (Func<string>)(() => _faker.Company.CompanyName()));
        environment.BindFunction("fakeProductName", (Func<string>)(() => _faker.Commerce.ProductName()));
        environment.BindFunction("fakeWord", (Func<string>)(() => _faker.Lorem.Word()));
        environment.BindFunction("fakeSentence", (Func<string>)(() => _faker.Lorem.Sentence()));
        environment.BindFunction("fakeParagraph", (Func<string>)(() => _faker.Lorem.Paragraph()));
        environment.BindFunction("fakeDate", (Func<string, string, string>)Date);
        environment.BindFunction("fakeDateTime", (Func<string, string, string>)DateTime);
        environment.BindFunction("fakeTime", (Func<string, string, string>)Time);
        environment.BindFunction("fakeTimeSpan", (Func<string, string>)Duration);
        environment.BindFunction("fakePast", (Func<int, string>)Past);
        environment.BindFunction("fakeFuture", (Func<int, string>)Future);
        environment.BindFunction("fakeRecent", (Func<int, string>)Recent);
        environment.BindFunction("fakeSoon", (Func<int, string>)Soon);
        environment.BindFunction("fakeMonth", (Func<string>)(() => _faker.Date.Month()));
        environment.BindFunction("fakeWeekday", (Func<string>)(() => _faker.Date.Weekday()));
    }

    private int Integer(int minimum, int maximum)
    {
        if (minimum > maximum) throw new ArgumentException("fakeInt minimum must not exceed maximum.");
        return _faker.Random.Int(minimum, maximum);
    }

    private decimal Decimal(decimal minimum, decimal maximum)
    {
        if (minimum > maximum) throw new ArgumentException("fakeDecimal minimum must not exceed maximum.");
        return _faker.Random.Decimal(minimum, maximum);
    }

    private string Text(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return _faker.Random.AlphaNumeric(length);
    }

    private string Date(string minimum, string maximum)
    {
        var start = DateOnly.ParseExact(minimum, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = DateOnly.ParseExact(maximum, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (start > end) throw new ArgumentException("fakeDate minimum must not exceed maximum.");
        return DateOnly.FromDayNumber(_faker.Random.Int(start.DayNumber, end.DayNumber))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private string DateTime(string minimum, string maximum)
    {
        var start = ParseTimestamp(minimum);
        var end = ParseTimestamp(maximum);
        if (start > end) throw new ArgumentException("fakeDateTime minimum must not exceed maximum.");
        return Timestamp(_faker.Date.BetweenOffset(start, end));
    }

    private static DateTimeOffset ParseTimestamp(string value)
        // Bogus compares wall-clock ticks; normalize offsets before choosing between them.
        => DateTimeOffset.ParseExact(value,
            ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private string Time(string minimum, string maximum)
    {
        string[] formats = ["HH:mm", "HH:mm:ss.FFFFFFF"];
        var start = TimeOnly.ParseExact(minimum, formats, CultureInfo.InvariantCulture);
        var end = TimeOnly.ParseExact(maximum, formats, CultureInfo.InvariantCulture);
        if (start > end) throw new ArgumentException("fakeTime minimum must not exceed maximum; ranges cannot cross midnight.");
        return start.Add(_faker.Date.Timespan(end - start)).ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
    }

    private string Duration(string maximum)
    {
        var duration = TimeSpan.ParseExact(maximum, "c", CultureInfo.InvariantCulture);
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximum), "fakeTimeSpan maximum must not be negative.");
        return _faker.Date.Timespan(duration).ToString("c", CultureInfo.InvariantCulture);
    }

    private string Past(int years)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(years, 1);
        return Timestamp(_faker.Date.PastOffset(years, DateTimeOffset.UtcNow));
    }

    private string Future(int years)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(years, 1);
        return Timestamp(_faker.Date.FutureOffset(years, DateTimeOffset.UtcNow));
    }

    private string Recent(int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        return Timestamp(_faker.Date.RecentOffset(days, DateTimeOffset.UtcNow));
    }

    private string Soon(int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        return Timestamp(_faker.Date.SoonOffset(days, DateTimeOffset.UtcNow));
    }

    private static string Timestamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
