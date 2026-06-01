using System.Globalization;

namespace FluxFlow.Components.Timers.Options;

internal sealed class CronSchedule
{
    private static readonly IReadOnlyDictionary<string, int> MonthNames =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["JAN"] = 1,
            ["FEB"] = 2,
            ["MAR"] = 3,
            ["APR"] = 4,
            ["MAY"] = 5,
            ["JUN"] = 6,
            ["JUL"] = 7,
            ["AUG"] = 8,
            ["SEP"] = 9,
            ["OCT"] = 10,
            ["NOV"] = 11,
            ["DEC"] = 12
        };

    private static readonly IReadOnlyDictionary<string, int> DayNames =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["SUN"] = 0,
            ["MON"] = 1,
            ["TUE"] = 2,
            ["WED"] = 3,
            ["THU"] = 4,
            ["FRI"] = 5,
            ["SAT"] = 6
        };

    private CronSchedule(
        string expression,
        CronField seconds,
        CronField minutes,
        CronField hours,
        CronField daysOfMonth,
        CronField months,
        CronField daysOfWeek)
    {
        Expression = expression;
        Seconds = seconds;
        Minutes = minutes;
        Hours = hours;
        DaysOfMonth = daysOfMonth;
        Months = months;
        DaysOfWeek = daysOfWeek;
    }

    public string Expression { get; }

    private CronField Seconds { get; }

    private CronField Minutes { get; }

    private CronField Hours { get; }

    private CronField DaysOfMonth { get; }

    private CronField Months { get; }

    private CronField DaysOfWeek { get; }

    public static CronSchedule Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var fields = expression.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length is not 5 and not 6)
        {
            throw new InvalidOperationException(
                "timer.schedule cron must contain five or six fields.");
        }

        var hasSeconds = fields.Length == 6;
        var index = 0;
        var seconds = hasSeconds
            ? CronField.Parse(fields[index++], 0, 59)
            : CronField.Single(0);
        var minutes = CronField.Parse(fields[index++], 0, 59);
        var hours = CronField.Parse(fields[index++], 0, 23);
        var daysOfMonth = CronField.Parse(fields[index++], 1, 31, allowQuestion: true);
        var months = CronField.Parse(fields[index++], 1, 12, MonthNames);
        var daysOfWeek = CronField.Parse(fields[index], 0, 7, DayNames, allowQuestion: true, normalizeDayOfWeek: true);

        return new CronSchedule(
            expression.Trim(),
            seconds,
            minutes,
            hours,
            daysOfMonth,
            months,
            daysOfWeek);
    }

    public DateTimeOffset? GetNextOccurrence(
        DateTimeOffset after,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var localAfter = TimeZoneInfo.ConvertTime(after, timeZone).DateTime;
        var localStart = localAfter
            .AddTicks(-(localAfter.Ticks % TimeSpan.TicksPerSecond))
            .AddSeconds(1);
        var candidateDate = localStart.Date;
        var currentTime = localStart.TimeOfDay;

        for (var dayOffset = 0; dayOffset <= 366 * 5; dayOffset++)
        {
            var date = candidateDate.AddDays(dayOffset);
            if (!Months.Contains(date.Month) || !MatchesDay(date))
            {
                currentTime = TimeSpan.Zero;
                continue;
            }

            foreach (var hour in Hours.Values)
            {
                if (date == candidateDate && hour < currentTime.Hours)
                {
                    continue;
                }

                foreach (var minute in Minutes.Values)
                {
                    if (date == candidateDate &&
                        hour == currentTime.Hours &&
                        minute < currentTime.Minutes)
                    {
                        continue;
                    }

                    foreach (var second in Seconds.Values)
                    {
                        if (date == candidateDate &&
                            hour == currentTime.Hours &&
                            minute == currentTime.Minutes &&
                            second < currentTime.Seconds)
                        {
                            continue;
                        }

                        var local = date.AddHours(hour).AddMinutes(minute).AddSeconds(second);
                        if (timeZone.IsInvalidTime(local))
                        {
                            continue;
                        }

                        return new DateTimeOffset(
                            local,
                            timeZone.GetUtcOffset(local));
                    }
                }
            }

            currentTime = TimeSpan.Zero;
        }

        return null;
    }

    private bool MatchesDay(DateTime date)
    {
        var dayOfMonthMatches = DaysOfMonth.Contains(date.Day);
        var dayOfWeek = (int)date.DayOfWeek;
        var dayOfWeekMatches = DaysOfWeek.Contains(dayOfWeek);

        return (DaysOfMonth.IsWildcard, DaysOfWeek.IsWildcard) switch
        {
            (true, true) => true,
            (false, true) => dayOfMonthMatches,
            (true, false) => dayOfWeekMatches,
            _ => dayOfMonthMatches || dayOfWeekMatches
        };
    }

    private sealed class CronField
    {
        private CronField(
            int[] values,
            bool isWildcard)
        {
            Values = values;
            IsWildcard = isWildcard;
        }

        public int[] Values { get; }

        public bool IsWildcard { get; }

        public bool Contains(int value)
            => Array.BinarySearch(Values, value) >= 0;

        public static CronField Single(int value)
            => new([value], isWildcard: false);

        public static CronField Parse(
            string field,
            int minimum,
            int maximum,
            IReadOnlyDictionary<string, int>? names = null,
            bool allowQuestion = false,
            bool normalizeDayOfWeek = false)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                throw new InvalidOperationException("Cron field cannot be empty.");
            }

            var values = new SortedSet<int>();
            var isWildcard = false;
            foreach (var part in field.Split(',', StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(part))
                {
                    throw new InvalidOperationException("Cron field cannot contain empty list values.");
                }

                AddPart(
                    part,
                    minimum,
                    maximum,
                    names,
                    allowQuestion,
                    normalizeDayOfWeek,
                    values,
                    ref isWildcard);
            }

            if (values.Count == 0)
            {
                throw new InvalidOperationException("Cron field did not contain any values.");
            }

            var expectedValueCount = normalizeDayOfWeek && minimum == 0 && maximum == 7
                ? 7
                : maximum - minimum + 1;
            return new CronField(values.ToArray(), isWildcard && values.Count == expectedValueCount);
        }

        private static void AddPart(
            string part,
            int minimum,
            int maximum,
            IReadOnlyDictionary<string, int>? names,
            bool allowQuestion,
            bool normalizeDayOfWeek,
            SortedSet<int> values,
            ref bool isWildcard)
        {
            var stepSplit = part.Split('/', StringSplitOptions.TrimEntries);
            if (stepSplit.Length > 2)
            {
                throw new InvalidOperationException($"Cron field value '{part}' has an invalid step.");
            }

            var basePart = stepSplit[0];
            var step = 1;
            if (stepSplit.Length == 2 &&
                (!int.TryParse(stepSplit[1], NumberStyles.None, CultureInfo.InvariantCulture, out step) ||
                 step <= 0))
            {
                throw new InvalidOperationException($"Cron field value '{part}' has an invalid step.");
            }

            if (basePart == "*" || (allowQuestion && basePart == "?"))
            {
                isWildcard = true;
                AddRange(minimum, maximum, step, normalizeDayOfWeek, values);
                return;
            }

            var rangeSplit = basePart.Split('-', StringSplitOptions.TrimEntries);
            if (rangeSplit.Length > 2)
            {
                throw new InvalidOperationException($"Cron field value '{part}' has an invalid range.");
            }

            var start = ParseValue(rangeSplit[0], minimum, maximum, names);
            var end = rangeSplit.Length == 2
                ? ParseValue(rangeSplit[1], minimum, maximum, names)
                : start;
            if (end < start)
            {
                throw new InvalidOperationException($"Cron field value '{part}' has an invalid range.");
            }

            AddRange(start, end, step, normalizeDayOfWeek, values);
        }

        private static int ParseValue(
            string value,
            int minimum,
            int maximum,
            IReadOnlyDictionary<string, int>? names)
        {
            if (names is not null &&
                names.TryGetValue(value, out var namedValue))
            {
                return namedValue;
            }

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new InvalidOperationException($"Cron field value '{value}' is not supported.");
            }

            if (parsed < minimum || parsed > maximum)
            {
                throw new InvalidOperationException(
                    $"Cron field value '{value}' must be between {minimum} and {maximum}.");
            }

            return parsed;
        }

        private static void AddRange(
            int start,
            int end,
            int step,
            bool normalizeDayOfWeek,
            SortedSet<int> values)
        {
            for (var value = start; value <= end; value += step)
            {
                values.Add(normalizeDayOfWeek && value == 7 ? 0 : value);
            }
        }
    }
}
