using FluxFlow.Studio.UI.Features.Designer.Components.Expressions;
using Jsonata.Net.Native;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class JsonataFormattingTests
{
    [Fact]
    public void Formats_a_mapping_without_changing_its_result()
    {
        const string source = "{\"Reading\":Reading+1,\"Device\":{\"Name\":Name},\"Empty\":[]}";
        var formatted = JsonataExpressionFormatter.Format(source);
        formatted.ShouldBe("{\n  \"Reading\": Reading+1,\n  \"Device\": {\n    \"Name\": Name\n  },\n  \"Empty\": []\n}");
        new JsonataQuery(formatted).Eval("{\"Reading\":2,\"Name\":\"sensor\"}")
            .ShouldBe(new JsonataQuery(source).Eval("{\"Reading\":2,\"Name\":\"sensor\"}"));
    }

    [Theory]
    [InlineData("($x:=[1,2,3];$map($x,function($v){$v*2}))")]
    [InlineData("{\"Match\":$match('a/b',{\"r\":/a\\/b/}.r),\"Ratio\":12/3}")]
    [InlineData("{\"Match\":$match('a,b',/[{},:;]/),\"Ratio\":12/3/2}")]
    [InlineData("{\"Text\":\"quotes \\\" and {,}\",\"Path\":`a.b`,\"Unicode\":\"سلام 😀\"}")]
    [InlineData("/* preserve { , ; } */ {\"a\":1, /* field note */ \"b\":2}")]
    [InlineData("Account.Order.{\"Product\":Product,\"Price\":Price}[Price>10]")]
    [InlineData("[1..5].($string($))")]
    [InlineData("($f:=function($n)<n:n>{$n+1};$f(2))")]
    [InlineData("{\"n\":1.2e-3,\"x\":-2,\"t\":true?1:2}")]
    [InlineData("$ ~> |Account|{\"Active\":true},['Old']|")]
    public void Preserves_syntax_and_is_idempotent(string source)
    {
        var formatted = JsonataExpressionFormatter.Format(source);
        new JsonataQuery(source).GetAst().Equals(new JsonataQuery(formatted).GetAst()).ShouldBeTrue();
        JsonataExpressionFormatter.Format(formatted).ShouldBe(formatted);
    }

    [Fact]
    public void Preserves_comments_and_literal_spelling()
    {
        const string source = "/* Keep  spacing\n and punctuation {} */ { 'A': 'x  y', `B.C`: /[{},;:]/i }";
        var formatted = JsonataExpressionFormatter.Format(source);
        formatted.ShouldContain("/* Keep  spacing\n and punctuation {} */");
        formatted.ShouldContain("'x  y'");
        formatted.ShouldContain("`B.C`");
        formatted.ShouldContain("/[{},;:]/i");
    }

    [Theory]
    [InlineData("inline:")]
    [InlineData("  InLiNe:")]
    public void Preserves_an_explicit_inline_prefix(string prefix)
    {
        JsonataExpressionFormatter.Format(prefix + "{\"A\":1}").ShouldBe(prefix + "{\n  \"A\": 1\n}");
    }

    [Fact]
    public void Formatting_keeps_a_file_name_that_also_parses_as_an_expression()
    {
        const string path = "map(1,2).jsonata";
        JsonataExpressionFormatter.Format(path).ShouldBe(path);
        JsonataExpressionFormatter.Format("file:" + path).ShouldBe("file:" + path);
        JsonataExpressionFormatter.Format("inline:" + path).ShouldBe("inline:map(1, 2).jsonata");
        JsonataExpressionFormatter.Format(path, isCondition: true).ShouldBe("map(1, 2).jsonata");
    }

    [Theory]
    [InlineData("{\"A\":")]
    [InlineData("base64:eyJBIjoxfQ==")]
    public void Invalid_or_encoded_sources_are_not_rewritten(string source)
    {
        Should.Throw<JsonataException>(() => JsonataExpressionFormatter.Format(source));
    }
}
