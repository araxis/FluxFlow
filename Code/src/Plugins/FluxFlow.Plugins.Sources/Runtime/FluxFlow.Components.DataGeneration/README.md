# FluxFlow.Components.DataGeneration

DataGenerator combines JSONata expressions and built-in Bogus functions. Each ordinary
input message produces one generated payload. JSONata can mix input fields, message
context and fake values. Arrays remain one payload. Upstream errors bypass generation;
evaluation failures become ordinary processing errors. WithValue preserves lineage.

Use Scheduler to determine when to send inputs. The component has no timer, interval,
seed setting, background producer or dependency on the Mapper component.

```csharp
var generator = new DataGenerator(new DataGeneratorOptions
{
    Expression = "{'deviceId': deviceId, 'temperature': $fakeDecimal(15, 35)}"
});
```

The input payload is `$`, `$input`, `$payload` and `$value`. Message context follows
the existing expression convention: `$headers`, `$messageId`, `$traceId`,
`$correlationId`, `$causationId`, `$timestamp` (ISO 8601), and `$message`.

Built-in functions:

Numbers and identifiers:

| Function | Result |
| --- | --- |
| `$fakeInt(min, max)` | Integer, both bounds inclusive |
| `$fakeDecimal(min, max)` | Decimal between the bounds, upper bound exclusive unless equal |
| `$fakeBool()` | Boolean |
| `$fakeString(length)` | Alphanumeric string |
| `$fakePattern(pattern)` | Bogus replacement pattern: `#` digit, `?` letter, `*` either |
| `$fakeUuid()` | UUID string |

Dates and times:

| Function | Result |
| --- | --- |
| `$fakeDate(min, max)` | Date between inclusive `yyyy-MM-dd` bounds, returned as `yyyy-MM-dd` |
| `$fakeDateTime(min, max)` | UTC timestamp between inclusive ISO 8601 timestamp bounds |
| `$fakeTime(min, max)` | Time of day between inclusive bounds, returned as `HH:mm:ss.fffffff` |
| `$fakeTimeSpan(max)` | Duration from zero to max, using invariant .NET `c` format |
| `$fakePast(years)` | UTC timestamp within the preceding number of calendar years |
| `$fakeFuture(years)` | UTC timestamp within the following number of calendar years |
| `$fakeRecent(days)` | UTC timestamp within the preceding number of days |
| `$fakeSoon(days)` | UTC timestamp within the following number of days |
| `$fakeMonth()` | Localized month name |
| `$fakeWeekday()` | Localized weekday name |

Date/time values are JSON strings. Timestamp inputs require seconds and `Z` or an
explicit `+/-HH:mm` offset; fractional seconds are optional (up to seven digits).
Offsets are normalized before generation. Timestamp output uses .NET's round-trip
format, for example `2026-10-04T12:30:00.1234567+00:00`.
Time bounds accept `HH:mm` or `HH:mm:ss` with optional fractional seconds; they cannot
cross midnight. Duration input uses `[-][d.]hh:mm:ss[.fffffff]`, but negative bounds
are rejected. Relative date arguments must be positive integers and use the current
UTC clock at each call. These functions generate values; they do not schedule work.
Equal range bounds return that value; reversed or malformed bounds produce a
processing error. Formats do not depend on the host culture or configured locale.

```jsonata
{
  'deviceId': deviceId,
  'sampledAt': $fakeRecent(1),
  'manufacturedOn': $fakeDate('2025-01-01', '2025-12-31'),
  'shiftTime': $fakeTime('08:00', '16:00'),
  'latency': $fakeTimeSpan('00:00:02'),
  'temperature': $fakeDecimal(15, 35)
}
```

People, contact details and locations:

| Function | Result |
| --- | --- |
| `$fakeFirstName()` | First name |
| `$fakeLastName()` | Last name |
| `$fakeFullName()` | Full name |
| `$fakeUserName()` | User name |
| `$fakeEmail()` | Email address |
| `$fakePhone()` | Phone number in the locale's default format |
| `$fakeUrl()` | URL |
| `$fakeIp()` | IPv4 address string |
| `$fakeStreetAddress()` | Street address |
| `$fakeCity()` | City name |
| `$fakeZipCode()` | Postal code in the locale's default format |
| `$fakeCountry()` | Country name |
| `$fakeLatitude()` | Number between -90 and 90 |
| `$fakeLongitude()` | Number between -180 and 180 |

Text and business data:

| Function | Result |
| --- | --- |
| `$fakeCompanyName()` | Company name |
| `$fakeProductName()` | Product name |
| `$fakeWord()` | Lorem word |
| `$fakeSentence()` | Lorem sentence |
| `$fakeParagraph()` | Lorem paragraph |

Each call generates an independent value. Use JSONata variables when the same
generated value must appear in several fields. These are explicit bindings to the
common [Bogus datasets](https://github.com/bchavez/Bogus#api-support), not an automatic
exposure of every Bogus API.

Locale defaults to `en`; supported locale keys come from Bogus. Bogus may fall back
to English for datasets missing in a locale. One Faker belongs to each component;
execution is sequential (Execution.MaxDegreeOfParallelism must be 1). Native capacity
and cancellation options remain available. No global random state is modified.

The expression is parsed once during construction; environments and JSON snapshots
are fresh per message. Explicit JSON null is valid; undefined is a processing error.
Cancellation is checked around synchronous evaluation, not inside it. Expressions
are trusted configuration; this is not a sandbox or a physical simulation engine.

See the [Data Generator setup guide](../../Registration/DataGenerator.md) for configuration and registration.
Register `services.AddDataGenerator()` and use Type `dataGenerator`, Expression,
optional Locale/OutputCapacity and the ordinary Input/Output/Events/Error references.
ExpressionSource supports inline, file and Base64 forms. Files are read at Build.
No expression-engine registration or Engine property is needed for this component.
