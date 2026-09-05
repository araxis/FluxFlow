using System.Text.Json;
using FluxFlow.Nodes;

namespace FluxFlow.ReportingSample;

public enum ApplicationReportComparison
{
    Exists, Missing, IsNull, Equal, NotEqual, GreaterThan, GreaterThanOrEqual,
    LessThan, LessThanOrEqual, Contains, StartsWith
}

public sealed record ApplicationReportCondition
{
    /// <summary>JSON pointer into event data, or @application, @revision, @schemaVersion, @phase, @source, @workflow, @component, @traceId.</summary>
    public required string Path { get; init; }
    public ApplicationReportComparison Comparison { get; init; } = ApplicationReportComparison.Equal;
    public JsonElement? Value { get; init; }
}

/// <summary>Portable, ordinal, type-strict filtering; no executable expressions or authorization policy.</summary>
public sealed record ApplicationReportFilter
{
    public DateTimeOffset? FromInclusive { get; init; }
    public DateTimeOffset? ToExclusive { get; init; }
    public IReadOnlyList<ApplicationReportCondition> Conditions { get; init; } = [];
    public IReadOnlyList<ApplicationReportFilter> All { get; init; } = [];
    public IReadOnlyList<ApplicationReportFilter> Any { get; init; } = [];

    public CompiledApplicationReportFilter Compile() => new(this);
}

/// <summary>An immutable validated filter snapshot reusable for live and historical records.</summary>
public sealed class CompiledApplicationReportFilter
{
    private readonly Node _root;

    internal CompiledApplicationReportFilter(ApplicationReportFilter filter)
    {
        var budget = 64;
        _root = CompileNode(filter, 0, ref budget);
    }

    public bool IsMatch(ApplicationReportEvent record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return _root.Match(record, record.Message.Value?.ToJsonElement() ?? default);
    }

    private static Node CompileNode(ApplicationReportFilter filter, int depth, ref int budget)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (depth > 8 || --budget < 0)
            throw new ArgumentException("Filters support at most 64 nodes/conditions and eight nested groups.");
        if (filter.FromInclusive is { } from && filter.ToExclusive is { } to && from >= to)
            throw new ArgumentException("The inclusive start must precede the exclusive end.");
        ArgumentNullException.ThrowIfNull(filter.Conditions);
        ArgumentNullException.ThrowIfNull(filter.All);
        ArgumentNullException.ThrowIfNull(filter.Any);
        var conditions = new List<Condition>();
        foreach (var condition in filter.Conditions)
        {
            if (--budget < 0)
                throw new ArgumentException("Filter condition limit exceeded.");
            ArgumentNullException.ThrowIfNull(condition);
            ArgumentException.ThrowIfNullOrWhiteSpace(condition.Path);
            if (!Enum.IsDefined(condition.Comparison))
                throw new ArgumentException("Unknown filter comparison.");
            if (condition.Path.Length > 512)
                throw new ArgumentException("Filter path is too long.");
            string[] segments;
            if (condition.Path.StartsWith('@'))
            {
                if (condition.Path is not ("@application" or "@revision" or "@phase" or "@source" or
                    "@workflow" or "@component" or "@traceId" or "@schemaVersion"))
                    throw new ArgumentException($"Unsupported envelope field '{condition.Path}'.");
                segments = [];
            }
            else
            {
                if (!condition.Path.StartsWith('/'))
                    throw new ArgumentException("Data paths must be JSON pointers beginning with '/'.");
                segments = condition.Path[1..].Split('/').Select(Unescape).ToArray();
            }
            var value = condition.Value?.Clone();
            var unary = condition.Comparison is ApplicationReportComparison.Exists or
                ApplicationReportComparison.Missing or ApplicationReportComparison.IsNull;
            if (!unary && (value is null || value.Value.ValueKind is not
                (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)))
                throw new ArgumentException("A comparison requires a scalar JSON value.");
            if (value?.ValueKind == JsonValueKind.String && value.Value.GetString()!.Length > 2048)
                throw new ArgumentException("Filter string is too long.");
            if (condition.Comparison is ApplicationReportComparison.Contains or ApplicationReportComparison.StartsWith &&
                value?.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Text comparisons require a string.");
            if (condition.Comparison is ApplicationReportComparison.GreaterThan or ApplicationReportComparison.GreaterThanOrEqual or
                ApplicationReportComparison.LessThan or ApplicationReportComparison.LessThanOrEqual &&
                value?.ValueKind != JsonValueKind.Number)
                throw new ArgumentException("Ordered comparisons require a number.");
            if (value?.ValueKind == JsonValueKind.Number && !value.Value.TryGetDecimal(out _))
                throw new ArgumentException("Numeric filters require a decimal-representable value.");
            conditions.Add(new Condition(condition.Path, segments, condition.Comparison, value));
        }
        var all = new List<Node>();
        foreach (var child in filter.All) all.Add(CompileNode(child, depth + 1, ref budget));
        var any = new List<Node>();
        foreach (var child in filter.Any) any.Add(CompileNode(child, depth + 1, ref budget));
        return new Node(filter.FromInclusive, filter.ToExclusive, conditions.ToArray(), all.ToArray(), any.ToArray());
    }

    private static string Unescape(string segment)
    {
        for (var i = 0; i < segment.Length; i++)
            if (segment[i] == '~' && (++i >= segment.Length || segment[i] is not ('0' or '1')))
                throw new ArgumentException("Invalid JSON pointer escape.");
        return segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
    }

    private sealed record Node(DateTimeOffset? From, DateTimeOffset? To, Condition[] Conditions, Node[] All, Node[] Any)
    {
        public bool Match(ApplicationReportEvent record, JsonElement data)
            => (From is null || record.Message.Timestamp >= From) &&
               (To is null || record.Message.Timestamp < To) &&
               Conditions.All(condition => condition.Match(record, data)) &&
               All.All(child => child.Match(record, data)) &&
               (Any.Length == 0 || Any.Any(child => child.Match(record, data)));
    }

    private sealed record Condition(string Path, string[] Segments, ApplicationReportComparison Comparison, JsonElement? Value)
    {
        public bool Match(ApplicationReportEvent record, JsonElement data)
        {
            var actual = data;
            if (Path.StartsWith('@'))
            {
                var text = Path switch
                {
                    "@application" => record.Application,
                    "@revision" => record.RevisionId,
                    "@phase" => record.Phase.ToString(),
                    "@traceId" => record.Message.TraceId.Value,
                    "@source" => Header(FlowEventHeaders.Source),
                    "@workflow" => Header(FlowEventHeaders.Workflow),
                    "@component" => Header(FlowEventHeaders.Component),
                    _ => null
                };
                actual = Path == "@schemaVersion" ? JsonSerializer.SerializeToElement(record.SchemaVersion)
                    : text is null ? default : JsonSerializer.SerializeToElement(text);
            }
            else
            {
                foreach (var segment in Segments)
                {
                    if (actual.ValueKind == JsonValueKind.Object && actual.TryGetProperty(segment, out var property))
                        actual = property;
                    else if (actual.ValueKind == JsonValueKind.Array &&
                        (segment == "0" || segment.Length > 0 && segment[0] is >= '1' and <= '9') &&
                        int.TryParse(segment, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) &&
                        index < actual.GetArrayLength())
                        actual = actual[index];
                    else { actual = default; break; }
                }
            }
            if (Comparison == ApplicationReportComparison.Exists) return actual.ValueKind != JsonValueKind.Undefined;
            if (Comparison == ApplicationReportComparison.Missing) return actual.ValueKind == JsonValueKind.Undefined;
            if (Comparison == ApplicationReportComparison.IsNull) return actual.ValueKind == JsonValueKind.Null;
            if (actual.ValueKind == JsonValueKind.Undefined) return false;
            var expected = Value!.Value;
            int comparison;
            if (actual.ValueKind == JsonValueKind.Number && expected.ValueKind == JsonValueKind.Number)
            {
                if (!actual.TryGetDecimal(out var left) || !expected.TryGetDecimal(out var right)) return false;
                comparison = left.CompareTo(right);
            }
            else if (actual.ValueKind == JsonValueKind.String && expected.ValueKind == JsonValueKind.String)
            {
                var left = actual.GetString()!;
                var right = expected.GetString()!;
                if (Comparison == ApplicationReportComparison.Contains) return left.Contains(right, StringComparison.Ordinal);
                if (Comparison == ApplicationReportComparison.StartsWith) return left.StartsWith(right, StringComparison.Ordinal);
                comparison = string.CompareOrdinal(left, right);
            }
            else if (actual.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                     expected.ValueKind is JsonValueKind.True or JsonValueKind.False)
                comparison = actual.GetBoolean().CompareTo(expected.GetBoolean());
            else if (actual.ValueKind == JsonValueKind.Null && expected.ValueKind == JsonValueKind.Null)
                comparison = 0;
            else return false;

            return Comparison switch
            {
                ApplicationReportComparison.Equal => comparison == 0,
                ApplicationReportComparison.NotEqual => comparison != 0,
                ApplicationReportComparison.GreaterThan => comparison > 0,
                ApplicationReportComparison.GreaterThanOrEqual => comparison >= 0,
                ApplicationReportComparison.LessThan => comparison < 0,
                ApplicationReportComparison.LessThanOrEqual => comparison <= 0,
                _ => false
            };

            string? Header(string name) => record.Message.Headers.GetValueOrDefault(name);
        }
    }
}
