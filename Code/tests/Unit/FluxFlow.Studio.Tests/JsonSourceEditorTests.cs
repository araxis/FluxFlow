using System.Text.Json;
using FluxFlow.Studio.UI.Shared.Configuration;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class JsonSourceEditorTests
{
    [Theory]
    [InlineData(JsonValueKind.Number, "9007199254740993", "9007199254740993")]
    [InlineData(JsonValueKind.Number, "1e400", "1e400")]
    [InlineData(JsonValueKind.String, "", "\"\"")]
    [InlineData(JsonValueKind.String, "123", "\"123\"")]
    [InlineData(JsonValueKind.Null, "ignored", "null")]
    [InlineData(JsonValueKind.True, "ignored", "false")]
    [InlineData(JsonValueKind.Object, "{\"x\":1,\"x\":2}", "{\"x\":1,\"x\":2}")]
    [InlineData(JsonValueKind.Array, "[null,9007199254740993]", "[null,9007199254740993]")]
    public void Typed_values_keep_exact_JSON_types_and_payloads(JsonValueKind kind, string text, string expected)
        => JsonValueEntry.Read(kind, text, false).GetRawText().ShouldBe(expected);

    [Theory]
    [InlineData(JsonValueKind.Number, "NaN")]
    [InlineData(JsonValueKind.Number, "01")]
    [InlineData(JsonValueKind.Number, "\"1\"")]
    [InlineData(JsonValueKind.Object, "[]")]
    [InlineData(JsonValueKind.Array, "{}")]
    public void Typed_values_reject_invalid_JSON_and_wrong_types(JsonValueKind kind, string text)
    {
        var error = Record.Exception(() => JsonValueEntry.Read(kind, text, false));
        (error is JsonException or ArgumentException).ShouldBeTrue();
    }

    [Theory]
    [InlineData("  INLINE: [1,\"1\",true,null]  ", false, " [1,\"1\",true,null]")]
    [InlineData(" FILE:somewhere/data.json ", true, "somewhere/data.json")]
    [InlineData("./values.json", true, "./values.json")]
    [InlineData("base64:W10=", true, "base64:W10=")]
    [InlineData("123", false, "123")]
    [InlineData("true", false, "true")]
    [InlineData("null", false, "null")]
    [InlineData("\"file:data.json\"", false, "\"file:data.json\"")]
    [InlineData("{}", false, "{}")]
    public void Existing_sources_round_trip_exactly_without_opening_files(string original, bool file, string content)
    {
        var source = JsonSourceValue.Parse(original);
        source.IsFile.ShouldBe(file); source.Content.ShouldBe(content);
        source.Save(file, content, false).ShouldBe(original);
    }

    [Theory]
    [InlineData("[broken")]
    [InlineData("{broken")]
    [InlineData("\"broken")]
    [InlineData("inline:")]
    [InlineData("inline:missing.json")]
    public void Invalid_inline_JSON_is_not_reclassified_as_a_file(string value)
    {
        var source = JsonSourceValue.Parse(value);
        source.IsFile.ShouldBeFalse();
        Should.Throw<JsonException>(() => source.Save(false, source.Content, false));
    }

    [Fact]
    public void Sources_require_arrays_but_initial_state_accepts_any_JSON_value()
    {
        var source = JsonSourceValue.Parse(null);
        Should.Throw<ArgumentException>(() => source.Save(false, "{}", true));
        source.Save(false, "null", false).ShouldBe("inline:null");
        source.Save(false, "[123,\"123\",true,\"true\",null,{},[]]", true)
            .ShouldBe("inline:[123,\"123\",true,\"true\",null,{},[]]");
        source.Save(false, "{\"Key\":1,\"key\":2}", false).ShouldBe("inline:{\"Key\":1,\"key\":2}");
    }

    [Fact]
    public void New_file_references_are_explicit_even_when_the_filename_is_a_JSON_scalar()
    {
        var source = JsonSourceValue.Parse("123");
        source.Save(true, "123", false).ShouldBe("file:123");
        Should.Throw<ArgumentException>(() => source.Save(true, " ", false));
        source.Save(true, "Z:/unavailable/runner.json", true).ShouldBe("file:Z:/unavailable/runner.json");
    }
}
