using System.Text.Json.Nodes;
using FluxFlow.Plugins.RabbitMQ;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class RabbitArgumentDraftTests
{
    [Theory]
    [InlineData("String", "\"hello\"")]
    [InlineData("Bytes", "\"AP8=\"")]
    [InlineData("Boolean", "true")]
    [InlineData("Byte", "255")]
    [InlineData("SByte", "-128")]
    [InlineData("Int16", "-32768")]
    [InlineData("UInt16", "65535")]
    [InlineData("Int32", "-2147483648")]
    [InlineData("UInt32", "4294967295")]
    [InlineData("Int64", "\"9223372036854775807\"")]
    [InlineData("Single", "\"1.234567\"")]
    [InlineData("Double", "\"1.23456789012345\"")]
    [InlineData("Decimal", "\"79228162514264337593543950335\"")]
    [InlineData("Timestamp", "\"1730000000\"")]
    [InlineData("Null", "null")]
    [InlineData("Array", "[]")]
    [InlineData("Table", "{}")]
    public void Existing_typed_arguments_round_trip_without_rewriting(string type, string value)
    {
        var original = JsonNode.Parse($$"""{"tYpE":"{{type}}","vAlUe":{{value}}} """)!.AsObject();
        var draft = new RabbitArgumentDraft("argument", original);
        draft.Save().ToJsonString().ShouldBe(original.ToJsonString());
    }

    [Theory]
    [InlineData("Byte", "256")]
    [InlineData("UInt16", "-1")]
    [InlineData("Int32", "2147483648")]
    [InlineData("Int64", "9223372036854775808")]
    [InlineData("Decimal", "invalid")]
    [InlineData("Bytes", "not base64")]
    [InlineData("Boolean", "yes")]
    [InlineData("Timestamp", "1.5")]
    public void Invalid_values_cannot_be_saved_and_original_remains_unchanged(string type, string text)
    {
        var original = JsonNode.Parse("""{"Type":"String","Value":"original"}""")!.AsObject();
        var draft = new RabbitArgumentDraft("argument", original);
        draft.ChangeType(type);
        draft.Text = text;
        Should.Throw<Exception>(() => draft.Save());
        original["Value"]!.GetValue<string>().ShouldBe("original");
    }

    [Fact]
    public void Nested_visual_edits_preserve_siblings_and_validate_names()
    {
        var original = JsonNode.Parse("""{"Type":"Table","Value":{"bytes":{"Type":"Bytes","Value":"AP8="},"items":{"Type":"Array","Value":[{"Type":"Int64","Value":"9223372036854775807"},{"Type":"String","Value":"before"}]}}}""")!.AsObject();
        var before = original.ToJsonString();
        var draft = new RabbitArgumentDraft("data", original);
        draft.Children[1].Children[1].Text = "after";
        var result = draft.Save();
        result["Value"]!["bytes"]!.ToJsonString().ShouldBe(original["Value"]!["bytes"]!.ToJsonString());
        result["Value"]!["items"]!["Value"]![0]!.ToJsonString().ShouldBe(original["Value"]!["items"]!["Value"]![0]!.ToJsonString());
        result["Value"]!["items"]!["Value"]![1]!["Value"]!.GetValue<string>().ShouldBe("after");
        draft.Children.Add(new("BYTES"));
        Should.Throw<ArgumentException>(() => draft.Save()).Message.ShouldContain("duplicated");
        draft.Children[^1].Name = "nested:path";
        Should.Throw<ArgumentException>(() => draft.Save()).Message.ShouldContain("colon");
        original.ToJsonString().ShouldBe(before);
    }

    [Fact]
    public void Table_keys_respect_the_AMQP_byte_limit_even_in_an_unchanged_nested_value()
    {
        var key = new string('é', 128);
        var table = new JsonObject { ["Type"] = "Table", ["Value"] = new JsonObject
            { [key] = new JsonObject { ["Type"] = "String", ["Value"] = "unchanged" } } };
        var before = table.ToJsonString();
        var draft = new RabbitArgumentDraft("headers", table);
        Should.Throw<ArgumentException>(() => draft.Save()).Message.ShouldContain("255");
        table.ToJsonString().ShouldBe(before);
        draft.Children[0].Name = key[..127] + "a";
        draft.Save()["Value"]![draft.Children[0].Name]!["Value"]!.ToString().ShouldBe("unchanged");
    }

    [Fact]
    public void Scalar_type_changes_produce_native_configuration_values()
    {
        var draft = new RabbitArgumentDraft("ttl");
        draft.ChangeType("Int32"); draft.Text = "1000";
        draft.Save()["Value"]!.GetValue<long>().ShouldBe(1000);
        draft.ChangeType("Boolean"); draft.Text = "true";
        draft.Save()["Value"]!.GetValue<bool>().ShouldBeTrue();
        draft.ChangeType("Null"); draft.Save()["Value"].ShouldBeNull();
        draft.ChangeType("Array"); draft.Children.Add(new("", new JsonObject { ["Type"] = "String", ["Value"] = "item" }));
        draft.Save()["Value"]!.AsArray().Count.ShouldBe(1);
        draft.ChangeType("String"); draft.Text = "done";
        draft.Save()["Value"]!.GetValue<string>().ShouldBe("done");
    }
}
