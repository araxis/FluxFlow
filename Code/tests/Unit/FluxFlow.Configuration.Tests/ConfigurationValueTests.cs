using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using System.Text;
using System.Text.Json;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Routing;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;

public sealed class ConfigurationValueTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Component_DTO_binding_rejects_scalar_sections_instead_of_returning_defaults(bool hasChildren, bool required)
    {
        const string path = "Host:Flows:Workflows:main:component";
        var values = new Dictionary<string, string?> { [path] = "invalid settings" };
        if (hasChildren) values[$"{path}:DefaultValue"] = "42";
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var section = configuration.GetSection(path);
        var error = Should.Throw<ArgumentException>(() =>
        {
            if (required) section.ReadRequiredConfiguration<CustomConfiguration>();
            else section.ReadConfiguration<CustomConfiguration>();
        });
        error.Message.ShouldContain(path);
        error.Message.ShouldContain("child sections");
    }

    [Theory]
    [InlineData("Address", false)]
    [InlineData("Address", true)]
    [InlineData("0", true)]
    public void Port_references_reject_mixed_scalar_and_child_representations(string child, bool many)
    {
        const string path = "Workflows:main:component:Output";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [path] = "first.Input",
            [$"{path}:{child}"] = "second.Input"
        }).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var component = configuration.GetSection("Workflows:main:component").GetComponentAddress();
        var error = Should.Throw<JsonException>(() =>
        {
            if (many) OneOrManyPortReferences.Read(configuration.GetSection(path), component);
            else PortReference.Read(configuration.GetSection(path), component);
        });
        error.Message.ShouldContain(path);
    }

    [Theory]
    [InlineData("\"sink.Input\"", "main.sink.Input", null)]
    [InlineData("\"other.sink.Input\"", "other.sink.Input", null)]
    [InlineData("\"System.Events.Output\"", "System.Events.Output", null)]
    [InlineData("{\"address\":\"sink.Input\",\"condition\":\"value > 0\",\"engine\":\"rules\"}", "main.sink.Input", "value > 0")]
    public void Single_link_values_bind_and_share_the_collection_reader(string json, string address, string? condition)
    {
        using var configuration = Read("{\"component\":{\"Destination\":" + json + "}}");
        var section = configuration.GetSection("Workflows:main:component");
        var model = section.ReadRequiredConfiguration<SingleLinkConfiguration>();
        var link = model.Output.ShouldNotBeNull();
        link.Address.Value.ShouldBe(address);
        link.Condition.ShouldBe(condition);
        var property = section.GetSection("Destination");
        PortReference.Read(property, section.GetComponentAddress()).ShouldBe(link);
        OneOrManyPortReferences.Read(property, section.GetComponentAddress()).Single().ShouldBe(link);
        configuration["Workflows:main:component:Destination"] = "changed.Input";
        link.Address.Value.ShouldBe(address);
    }

    [Theory]
    [InlineData("[\"sink.Input\"]")]
    [InlineData("[{\"Address\":\"sink.Input\"}]")]
    [InlineData("\"broken\"")]
    [InlineData("{\"Address\":\"sink.Input\",\"Engine\":\"rules\"}")]
    public void Single_link_values_reject_arrays_and_invalid_links_at_the_property(string json)
    {
        using var configuration = Read("{\"component\":{\"Destination\":" + json + "}}");
        var error = Should.Throw<JsonException>(() => configuration.GetSection("Workflows:main:component")
            .ReadRequiredConfiguration<SingleLinkConfiguration>());
        error.Message.ShouldContain("component:Destination");
        if (json.StartsWith('[')) error.Message.ShouldContain("expected one link");
    }

    [Fact]
    public void Missing_single_link_retains_the_DTO_default()
    {
        using var configuration = Read("""{"component":{"Type":"custom"}}""");
        configuration.GetSection("Workflows:main:component").ReadRequiredConfiguration<SingleLinkConfiguration>()
            .Output.ShouldBeNull();
    }

    [Theory]
    [InlineData("\"sink.Input\"", 1)]
    [InlineData("{\"Address\":\"sink.Input\"}", 1)]
    [InlineData("{\"address\":\"sink.Input\"}", 1)]
    [InlineData("[\"sink.Input\",\"audit.Input\"]", 2)]
    [InlineData("[{\"Address\":\"sink.Input\"},{\"Address\":\"audit.Input\"}]", 2)]
    [InlineData("[\"sink.Input\",{\"Address\":\"audit.Input\",\"Condition\":\"value > 0\",\"Engine\":\"csharp\"}]", 2)]
    public void One_DTO_read_supports_all_port_link_shapes(string output, int count)
    {
        using var configuration = Read("""{"map":{"Engine":"csharp","Expression":"value","Input":"source.Output","Output":OUTPUT}}""".Replace("OUTPUT", output));
        var model = configuration.GetSection("Workflows:main:map").ReadConfiguration<MapperConfiguration>()!;
        model.Input.Single().Address.Value.ShouldBe("main.source.Output");
        model.Output.Count.ShouldBe(count);
        model.Output[0].Address.Value.ShouldBe("main.sink.Input");
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
    public void Link_data_is_snapshotted_and_preserves_conditions_and_engines()
    {
        using var configuration = Read("""
            {"map":{"Output":[
              {"Address":"sink.Input","Condition":"value > 12","Engine":"csharp"},
              {"Address":"other.Input"}
            ]}}
            """);
        var model = configuration.GetSection("Workflows:main:map").ReadConfiguration<MapperConfiguration>()!;
        configuration["Workflows:main:map:Output:0:Address"] = "changed.Input";
        configuration["Workflows:main:map:Output:0:Condition"] = "value > 99";
        configuration["Workflows:main:map:Output:0:Engine"] = "changed";
        model.Output[0].Address.Value.ShouldBe("main.sink.Input");
        model.Output[0].Condition.ShouldBe("value > 12");
        model.Output[0].Engine.ShouldBe("csharp");
        model.Output[1].Address.Value.ShouldBe("main.other.Input");
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("{\"Condition\":\"true\"}")]
    [InlineData("{\"Address\":\"sink.Input\",\"Engine\":\"csharp\"}")]
    [InlineData("{\"Address\":\"sink.Input\",\"Unexpected\":1}")]
    [InlineData("{\"Port\":\"sink.Input\"}")]
    [InlineData("{\"Address\":\"sink.Input\",\"Condition\":\"true\",\"Parameters\":{\"minimum\":12}}")]
    [InlineData("[[\"sink.Input\"]]")]
    [InlineData("\"not-an-address\"")]
    [InlineData("\"sink..Input\"")]
    [InlineData("{\"Address\":\"Resources.connection.host\"}")]
    [InlineData("{\"Address\":\"main.sink.Input.extra\"}")]
    [InlineData("{\"Address\":\"sink.Input \"}")]
    public void Invalid_links_fail_the_DTO_read_with_the_property_location(string output)
    {
        using var configuration = Read("{\"map\":{\"Output\":" + output + "}}");
        Should.Throw<JsonException>(() => configuration.GetSection("Workflows:main:map").ReadConfiguration<MapperConfiguration>())
            .Message.ShouldContain("map:Output");
    }

    [Fact]
    public void Invalid_array_entries_report_each_location_without_losing_other_errors()
    {
        using var configuration = Read("""
            {"map":{"Output":[
              {"Address":"sink.Input","Engine":"csharp"},
              "audit.Input",
              {"Address":"","Condition":"","Engine":""}
            ]}}
            """);
        var error = Should.Throw<JsonException>(() =>
            configuration.GetSection("Workflows:main:map").ReadConfiguration<MapperConfiguration>());
        error.Message.ShouldContain("map:Output[0]: 'Engine' requires a 'Condition'");
        error.Message.ShouldContain("map:Output[2]: a non-empty 'Address' string is required");
        error.Message.ShouldContain("map:Output[2]: 'Condition' cannot be empty");
        error.Message.ShouldContain("map:Output[2]: 'Engine' must be a non-empty string");
        error.Message.ShouldNotContain("map:Output[1]");
    }

    [Fact]
    public void Binding_resolves_typed_ports_under_a_host_selected_root_before_registration()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Host:Flows:Workflows:orders:map:Input:0"] = "source.Output",
            ["Host:Flows:Workflows:orders:map:Input:1"] = "remote.source.Output",
            ["Host:Flows:Workflows:orders:map:Input:2"] = "System.Events.Output"
        }).Build();
        var section = configuration.GetSection("Host:Flows:Workflows:orders:map");
        section.GetComponentAddress().Value.ShouldBe("orders.map");
        var model = section.ReadConfiguration<MapperConfiguration>()!;
        model.Input.Select(link => link.Address.Value).ShouldBe(new[]
        { "orders.source.Output", "remote.source.Output", "System.Events.Output" });
        model.Input[2].Address.IsSystem.ShouldBeTrue();
    }

    [Fact]
    public void Invalid_address_array_entries_report_all_locations_during_binding()
    {
        using var configuration = Read("""{"map":{"Input":["broken", "valid.Output", {"Address":"main..Output"}]}}""");
        var error = Should.Throw<JsonException>(() => configuration.GetSection("Workflows:main:map")
            .ReadConfiguration<MapperConfiguration>());
        error.Message.ShouldContain("map:Input[0]");
        error.Message.ShouldContain("map:Input[2]");
        error.Message.ShouldNotContain("map:Input[1]");
    }

    [Fact]
    public void In_memory_binding_uses_key_names_and_native_setting_conversions()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflows:main:component:incoming:0"] = "a.Output",
            ["Workflows:main:component:incoming:1:Address"] = "b.Output",
            ["Workflows:main:component:Wait"] = "00:00:05",
            ["Workflows:main:component:Nested:Capacity"] = "42"
        }).Build();
        var model = configuration.GetSection("Workflows:main:component").ReadRequiredConfiguration<CustomConfiguration>();
        model.Input.Select(link => link.Address.Value).ShouldBe(new[] { "main.a.Output", "main.b.Output" });
        model.Wait.ShouldBe(TimeSpan.FromSeconds(5));
        model.Nested.Capacity.ShouldBe(42);
        model.DefaultValue.ShouldBe(17);
        configuration.GetSection("missing").ReadConfiguration<CustomConfiguration>().ShouldBeNull();
        var error = Should.Throw<ArgumentException>(() => configuration.GetSection("missing")
            .ReadRequiredConfiguration<CustomConfiguration>());
        error.Message.ShouldContain("missing");
        error.Message.ShouldContain(nameof(CustomConfiguration));
        Should.Throw<ArgumentNullException>(() => ConfigurationBindingExtensions
            .ReadRequiredConfiguration<CustomConfiguration>(null!));
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
        var model = configuration.GetSection("Workflows:main:map").Get<MapperConfiguration>()!;
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
        Should.Throw<InvalidOperationException>(() => configuration.GetSection("Workflows:main:router").ReadConfiguration<RouterConfiguration>());
        configuration["Workflows:main:router:Routes:bad"] = "base64:dHJ1ZQ==";
        var model = configuration.GetSection("Workflows:main:router").ReadConfiguration<RouterConfiguration>()!;
        model.Routes.Count.ShouldBe(2);
        model.Routes["bad"].Read(AppContext.BaseDirectory).ShouldBe("true");
    }

    private static ConfigurationRoot Read(string json)
        => (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"Workflows\":{\"main\":" + json + "}}"))).Build();

    public sealed class CustomConfiguration
    {
        [ConfigurationKeyName("incoming")]
        public OneOrManyPortReferences Input { get; init; } = OneOrManyPortReferences.Empty;
        public TimeSpan Wait { get; init; }
        public NestedConfiguration Nested { get; init; } = new();
        public int DefaultValue { get; init; } = 17;
    }

    public sealed class SingleLinkConfiguration
    {
        [ConfigurationKeyName("Destination")]
        public PortReference? Output { get; init; }
    }

    public sealed class NestedConfiguration
    {
        public int Capacity { get; init; }
    }
}
