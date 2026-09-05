using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.ReportingSample.Tests;

public sealed class ReportingFilterTests
{
    [Theory]
    [InlineData("/a~1b/~0key", ApplicationReportComparison.Equal, "12", true)]
    [InlineData("/a~1b/~0key", ApplicationReportComparison.Equal, "\"12\"", false)]
    [InlineData("/a~1b/~0key", ApplicationReportComparison.GreaterThan, "12", false)]
    [InlineData("/a~1b/~0key", ApplicationReportComparison.GreaterThan, "11", true)]
    [InlineData("/a~1b/~0key", ApplicationReportComparison.GreaterThanOrEqual, "12", true)]
    [InlineData("/a~1b/~0key", ApplicationReportComparison.GreaterThanOrEqual, "13", false)]
    [InlineData("/a~1b/~0key", ApplicationReportComparison.LessThan, "13", true)]
    [InlineData("/a~1b/~0key", ApplicationReportComparison.LessThan, "12", false)]
    [InlineData("/a~1b/~0key", ApplicationReportComparison.LessThanOrEqual, "11", false)]
    [InlineData("/a~1b/~0key", ApplicationReportComparison.LessThanOrEqual, "12", true)]
    [InlineData("/label", ApplicationReportComparison.Contains, "\"Ab\"", true)]
    [InlineData("/label", ApplicationReportComparison.Contains, "\"ab\"", false)]
    [InlineData("/label", ApplicationReportComparison.StartsWith, "\"Ab\"", true)]
    [InlineData("/label", ApplicationReportComparison.StartsWith, "\"bc\"", false)]
    [InlineData("/flag", ApplicationReportComparison.Equal, "true", true)]
    [InlineData("/flag", ApplicationReportComparison.NotEqual, "false", true)]
    [InlineData("/flag", ApplicationReportComparison.NotEqual, "1", false)]
    [InlineData("/Label", ApplicationReportComparison.Equal, "\"Abc\"", false)]
    public void Scalar_filters_are_type_strict_ordinal_and_decode_pointer_segments(
        string path, ApplicationReportComparison comparison, string expected, bool matches)
    {
        var filter = Filter(path, comparison, expected).Compile();
        filter.IsMatch(Record("""{"a/b":{"~key":12},"label":"Abc","flag":true}""")).ShouldBe(matches);
    }

    [Theory]
    [InlineData("/absent", ApplicationReportComparison.Missing, true)]
    [InlineData("/absent", ApplicationReportComparison.Exists, false)]
    [InlineData("/absent", ApplicationReportComparison.IsNull, false)]
    [InlineData("/absent", ApplicationReportComparison.NotEqual, false)]
    [InlineData("/value", ApplicationReportComparison.Missing, false)]
    [InlineData("/value", ApplicationReportComparison.Exists, true)]
    [InlineData("/value", ApplicationReportComparison.IsNull, true)]
    [InlineData("/value", ApplicationReportComparison.Equal, true)]
    public void Missing_and_explicit_null_have_distinct_semantics(
        string path, ApplicationReportComparison comparison, bool expected)
        => Filter(path, comparison, "null").Compile().IsMatch(Record("""{"value":null}""")).ShouldBe(expected);

    [Theory]
    [InlineData("/items/0/a~1b", ApplicationReportComparison.Equal, "\"first\"", true)]
    [InlineData("/items/1/a~1b", ApplicationReportComparison.Equal, "\"second\"", true)]
    [InlineData("/items/0/a~1b", ApplicationReportComparison.Equal, "\"second\"", false)]
    [InlineData("/items/2/a~1b", ApplicationReportComparison.Missing, null, true)]
    [InlineData("/items/-1", ApplicationReportComparison.Missing, null, true)]
    [InlineData("/items/01", ApplicationReportComparison.Missing, null, true)]
    [InlineData("/items/99999999999999999", ApplicationReportComparison.Missing, null, true)]
    public void Json_pointer_array_indices_follow_order_and_do_not_coerce_invalid_indices(
        string path, ApplicationReportComparison comparison, string? value, bool expected)
        => Filter(path, comparison, value).Compile().IsMatch(Record("""{"items":[{"a/b":"first"},{"a/b":"second"}]}""")).ShouldBe(expected);

    [Fact]
    public void Nested_groups_and_half_open_message_time_window_survive_serialization()
    {
        var start = new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
        var filter = new ApplicationReportFilter
        {
            FromInclusive = start,
            ToExclusive = start.AddHours(1),
            All = [Filter("/value", ApplicationReportComparison.GreaterThan, "5")],
            Any = [Filter("/kind", ApplicationReportComparison.Equal, "\"a\""),
                Filter("/kind", ApplicationReportComparison.Equal, "\"b\"")]
        };
        var restored = JsonSerializer.Deserialize<ApplicationReportFilter>(JsonSerializer.Serialize(filter))!.Compile();

        restored.IsMatch(Record("""{"value":6,"kind":"b"}""", start)).ShouldBeTrue();
        restored.IsMatch(Record("""{"value":6,"kind":"a"}""", start.AddHours(1))).ShouldBeFalse();
        restored.IsMatch(Record("""{"value":6,"kind":"a"}""", start.AddTicks(-1))).ShouldBeFalse();
        restored.IsMatch(Record("""{"value":5,"kind":"a"}""", start)).ShouldBeFalse();
        restored.IsMatch(Record("""{"value":6,"kind":"c"}""", start)).ShouldBeFalse();
    }

    [Fact]
    public void Envelope_fields_filter_provenance_without_parsing_message_text()
    {
        var record = Record("{}") with
        {
            Application = "app", RevisionId = "rev-2", Phase = ApplicationReportPhase.Draining,
            Message = FlowMessage.Create(FlowValue.Null, headers: new Dictionary<string, string>
            {
                [FlowEventHeaders.Workflow] = "orders", [FlowEventHeaders.Component] = "source",
                [FlowEventHeaders.Source] = "orders.source.Events"
            })
        };
        var filter = new ApplicationReportFilter
        {
            All = [Filter("@application", ApplicationReportComparison.Equal, "\"app\""),
                Filter("@revision", ApplicationReportComparison.Equal, "\"rev-2\""),
                Filter("@phase", ApplicationReportComparison.Equal, "\"Draining\""),
                Filter("@workflow", ApplicationReportComparison.Equal, "\"orders\""),
                Filter("@component", ApplicationReportComparison.Equal, "\"source\""),
                Filter("@source", ApplicationReportComparison.StartsWith, "\"orders.\"")]
        }.Compile();
        filter.IsMatch(record).ShouldBeTrue();
        filter.IsMatch(record with { RevisionId = "rev-1" }).ShouldBeFalse();
    }

    [Fact]
    public void Compiled_filter_clones_scalar_values_and_mutable_groups()
    {
        using var document = JsonDocument.Parse("12");
        var conditions = new List<ApplicationReportCondition>
        {
            new() { Path = "/value", Value = document.RootElement }
        };
        var children = new List<ApplicationReportFilter> { new() { Conditions = conditions } };
        var compiled = new ApplicationReportFilter { All = children }.Compile();
        conditions.Clear();
        children.Clear();
        document.Dispose();

        compiled.IsMatch(Record("""{"value":12}""")).ShouldBeTrue();
        compiled.IsMatch(Record("""{"value":13}""")).ShouldBeFalse();
    }

    [Fact]
    public void Error_envelope_has_missing_event_data_but_remains_filterable_by_provenance()
    {
        var record = Record("{}") with
        {
            Application = "orders",
            Message = FlowMessage.CreateError(new FlowError("failed", "Failure", "test", false))
        };
        Filter("/type", ApplicationReportComparison.Missing).Compile().IsMatch(record).ShouldBeTrue();
        Filter("/type", ApplicationReportComparison.IsNull).Compile().IsMatch(record).ShouldBeFalse();
        Filter("@application", ApplicationReportComparison.Equal, "\"orders\"").Compile().IsMatch(record).ShouldBeTrue();
    }

    [Theory]
    [InlineData("relative", ApplicationReportComparison.Exists, null)]
    [InlineData("/bad~2escape", ApplicationReportComparison.Exists, null)]
    [InlineData("@unknown", ApplicationReportComparison.Exists, null)]
    [InlineData("/value", ApplicationReportComparison.Equal, "{}")]
    [InlineData("/value", ApplicationReportComparison.Contains, "2")]
    [InlineData("/value", ApplicationReportComparison.LessThan, "\"2\"")]
    [InlineData("/value", ApplicationReportComparison.Equal, "1e100")]
    [InlineData("/value", (ApplicationReportComparison)999, "2")]
    public void Invalid_conditions_fail_during_compilation(string path, ApplicationReportComparison comparison, string? value)
        => Should.Throw<ArgumentException>(() => Filter(path, comparison, value).Compile());

    [Fact]
    public void Invalid_windows_and_unbounded_filter_graphs_are_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        Should.Throw<ArgumentException>(() => new ApplicationReportFilter { FromInclusive = now, ToExclusive = now }.Compile());
        var deep = new ApplicationReportFilter();
        for (var i = 0; i < 8; i++) deep = new ApplicationReportFilter { All = [deep] };
        deep.Compile().IsMatch(Record("{}")).ShouldBeTrue();
        deep = new ApplicationReportFilter { All = [deep] };
        Should.Throw<ArgumentException>(() => deep.Compile());
        var cyclic = new List<ApplicationReportFilter>();
        var root = new ApplicationReportFilter { Any = cyclic };
        cyclic.Add(root);
        Should.Throw<ArgumentException>(() => root.Compile());
        new ApplicationReportFilter
        {
            Conditions = Enumerable.Range(0, 63).Select(_ => new ApplicationReportCondition
            { Path = "/value", Comparison = ApplicationReportComparison.Exists }).ToArray()
        }.Compile().IsMatch(Record("""{"value":1}""")).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => new ApplicationReportFilter
        {
            Conditions = Enumerable.Range(0, 64).Select(_ => new ApplicationReportCondition
            { Path = "/value", Comparison = ApplicationReportComparison.Exists }).ToArray()
        }.Compile());
    }

    internal static ApplicationReportFilter Filter(string path, ApplicationReportComparison comparison, string? value = null)
        => new() { Conditions = [new ApplicationReportCondition
        { Path = path, Comparison = comparison, Value = value is null ? null : JsonSerializer.Deserialize<JsonElement>(value) }] };

    internal static ApplicationReportEvent Record(string json, DateTimeOffset? timestamp = null)
    {
        var initial = FlowMessage.Create(FlowValue.FromJson(JsonSerializer.Deserialize<JsonElement>(json)));
        return new ApplicationReportEvent
        {
            Cursor = new ApplicationReportCursor(Guid.NewGuid(), 1),
            ObservedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Message = timestamp is null ? initial : FlowMessage.Restore(initial.Value!, initial.MessageId, initial.TraceId, timestamp.Value)
        };
    }
}
