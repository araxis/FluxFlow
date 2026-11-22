using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Plugins.Sources;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class PublishSourceFilesTests
{
    [Fact]
    public void Equivalent_paths_share_one_file_and_leave_inline_and_absolute_inputs_external()
    {
        Find("file:orders/a.json", @"orders\.\a.json", "FILE:orders/replaced/../a.json",
            "inline:[1]", "[", "42", "\"some text\"", "C:/orders/a.json", "/orders/a.json")
            .ShouldBe(new[] { "orders/a.json" });
    }

    [Theory]
    [InlineData("../orders.json")]
    [InlineData("data/../../orders.json")]
    [InlineData("file:")]
    [InlineData("data/nul.json")]
    [InlineData("data/a:stream")]
    [InlineData("data/a.")]
    [InlineData("C:relative.json")]
    [InlineData("appsettings.Production.json")]
    public void Invalid_destination_is_rejected(string value) =>
        Should.Throw<InvalidOperationException>(() => Find(value));

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Production.json")]
    [InlineData("FluxFlow.Runner.runtimeconfig.dev.json")]
    public void Generator_can_embed_a_file_whose_name_is_reserved_in_the_runner(string path)
    {
        var document = new DesignerConfiguration(JsonSerializer.Serialize(new
        {
            Workflows = new { main = new { input = new { Type = "source", Values = "file:" + path } } }
        }));
        var components = new SourcesPlugin().Description.Components;
        PublishSourceFiles.Find(document, components, forRunner: false).ShouldBe(new[] { path });
        Should.Throw<InvalidOperationException>(() => PublishSourceFiles.Find(document, components)).Message.ShouldContain(path);
    }

    [Theory]
    [InlineData("orders", "Orders/a.json")]
    [InlineData("orders/a.json", "orders/A.json")]
    public void Conflicting_file_and_directory_names_are_rejected(string first, string second) =>
        Should.Throw<InvalidOperationException>(() => Find(first, second));

    private static string[] Find(params string[] values)
    {
        var workflows = values.Select((value, i) => (value, i)).ToDictionary(x => "flow" + x.i,
            x => new { input = new { Type = "source", values = x.value } });
        var document = new DesignerConfiguration(JsonSerializer.Serialize(new { Workflows = workflows }));
        return PublishSourceFiles.Find(document, new SourcesPlugin().Description.Components);
    }
}
