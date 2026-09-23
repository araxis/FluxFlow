using System.Text;
using System.Text.Json;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Configuration.Links;
using FluxFlow.Configuration.Mapping;
using FluxFlow.Configuration.Routing;
using FluxFlow.Core.Execution;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;

public sealed class ConfigurationValueTests
{
    [Theory]
    [InlineData("\"sink.Input\"", 1)]
    [InlineData("{\"Port\":\"sink.Input\"}", 1)]
    [InlineData("[\"sink.Input\",\"audit.Input\"]", 2)]
    [InlineData("[{\"Port\":\"sink.Input\"},{\"Port\":\"audit.Input\"}]", 2)]
    [InlineData("[\"sink.Input\",{\"Port\":\"audit.Input\",\"Condition\":\"value > 0\",\"Engine\":\"csharp\"}]", 2)]
    public void One_DTO_read_supports_all_port_link_shapes(string output, int count)
    {
        using var configuration = Read("""{"map":{"Engine":"csharp","Expression":"value","Input":"source.Output","Output":OUTPUT}}""".Replace("OUTPUT", output));
        var model = configuration.GetSection("map").ReadConfiguration<MapperConfiguration>()!;
        model.Input.Single().Port.ShouldBe("source.Output");
        model.Output.Count.ShouldBe(count);
        model.Output[0].Port.ShouldBe("sink.Input");
        model.Events.ShouldBeEmpty();
        model.OutputCapacity.ShouldBe(128);
        model.Expression!.Value.ShouldBe("value");
        if (model.Output.Last().Condition is not null)
        {
            model.Output.Last().Condition.ShouldBe("value > 0");
            model.Output.Last().Engine.ShouldBe("csharp");
        }
    }

    [Fact]
    public void Link_data_is_snapshotted_and_preserves_parameters_and_policies()
    {
        using var configuration = Read("""
            {"map":{"Output":[
              {"Port":"sink.Input","Condition":"value > minimum","Engine":"csharp","Parameters":{"minimum":12,"object":{"code":"\"001\""}}},
              {"Port":"other.Input","Fallback":true,"Delivery":"RejectWhenFull"}
            ]}}
            """);
        var model = configuration.GetSection("map").ReadConfiguration<MapperConfiguration>()!;
        configuration["map:Output:0:Port"] = "changed.Input";
        configuration["map:Output:0:Parameters:minimum"] = "99";
        model.Output[0].Port.ShouldBe("sink.Input");
        model.Output[0].Parameters!["minimum"].GetInt32().ShouldBe(12);
        model.Output[0].Parameters!["object"].GetProperty("code").GetString().ShouldBe("001");
        model.Output[1].Fallback.ShouldBeTrue();
        model.Output[1].Delivery.ShouldBe(FlowDeliveryPolicy.RejectWhenFull);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("{\"Condition\":\"true\"}")]
    [InlineData("{\"Port\":\"sink.Input\",\"Engine\":\"csharp\"}")]
    [InlineData("{\"Port\":\"sink.Input\",\"Unexpected\":1}")]
    [InlineData("[[\"sink.Input\"]]")]
    public void Invalid_links_fail_the_DTO_read_with_the_property_location(string output)
    {
        using var configuration = Read("{\"map\":{\"Output\":" + output + "}}");
        Should.Throw<JsonException>(() => configuration.GetSection("map").ReadConfiguration<MapperConfiguration>())
            .Message.ShouldContain("map:Output");
    }

    [Fact]
    public void In_memory_binding_uses_key_names_and_native_setting_conversions()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["component:incoming:0"] = "a.Output",
            ["component:incoming:1:Port"] = "b.Output",
            ["component:Wait"] = "00:00:05",
            ["component:Nested:Capacity"] = "42"
        }).Build();
        var model = configuration.GetSection("component").ReadConfiguration<CustomConfiguration>()!;
        model.Input.Select(link => link.Port).ShouldBe(new[] { "a.Output", "b.Output" });
        model.Wait.ShouldBe(TimeSpan.FromSeconds(5));
        model.Nested.Capacity.ShouldBe(42);
        model.DefaultValue.ShouldBe(17);
        configuration.GetSection("missing").ReadConfiguration<CustomConfiguration>().ShouldBeNull();
    }

    [Theory]
    [InlineData("value.number", "value.number")]
    [InlineData("inline:map.cs", "map.cs")]
    [InlineData("inline:base64:dmFsdWU=", "base64:dmFsdWU=")]
    [InlineData("BASE64:dmFsdWU=", "value")]
    [InlineData("dmFsdWU=", "dmFsdWU=")]
    public void Expression_source_binds_with_the_standard_binder(string source, string expected)
    {
        using var configuration = Read(JsonSerializer.Serialize(new { map = new { Expression = source } }));
        var model = configuration.GetSection("map").Get<MapperConfiguration>()!;
        model.Expression!.Read(AppContext.BaseDirectory).ShouldBe(expected);
    }

    [Fact]
    public void Base64_uses_UTF8_and_does_not_reinterpret_decoded_text_as_a_file()
    {
        const string text = "'سلام' & '.cs'";
        ExpressionSource source = "base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        source.Read(AppContext.BaseDirectory).ShouldBe(text);
        new ExpressionSource("base64:bWFwLmNz").Read(AppContext.BaseDirectory).ShouldBe("map.cs");
    }

    [Theory]
    [InlineData("base64:???")]
    [InlineData("base64:/w==")]
    [InlineData("base64:")]
    [InlineData("base64:IA==")]
    public void Invalid_base64_or_empty_text_is_rejected(string source)
        => Should.Throw<Exception>(() => new ExpressionSource(source).Read(AppContext.BaseDirectory));

    [Fact]
    public void Router_sources_are_typed_and_invalid_entries_are_not_silently_dropped()
    {
        using var configuration = Read("""{"router":{"Routes":{"good":"value > 0","bad":" "}}}""");
        Should.Throw<InvalidOperationException>(() => configuration.GetSection("router").ReadConfiguration<RouterConfiguration>());
        configuration["router:Routes:bad"] = "base64:dHJ1ZQ==";
        var model = configuration.GetSection("router").ReadConfiguration<RouterConfiguration>()!;
        model.Routes.Count.ShouldBe(2);
        model.Routes["bad"].Read(AppContext.BaseDirectory).ShouldBe("true");
    }

    private static ConfigurationRoot Read(string json)
        => (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();

    public sealed class CustomConfiguration
    {
        [ConfigurationKeyName("incoming")]
        public OneOrManyLinks Input { get; init; } = OneOrManyLinks.Empty;
        public TimeSpan Wait { get; init; }
        public NestedConfiguration Nested { get; init; } = new();
        public int DefaultValue { get; init; } = 17;
    }

    public sealed class NestedConfiguration
    {
        public int Capacity { get; init; }
    }
}
