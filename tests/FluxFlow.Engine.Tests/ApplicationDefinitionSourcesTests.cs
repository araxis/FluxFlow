using System.Text;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationDefinitionSourcesTests
{
    [Fact]
    public async Task Configuration_source_reconstructs_the_canonical_tree_in_engine()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """
            {
              "Application": {
                "Resources": {},
                "Workflows": {
                  "Main": {
                    "Source": {
                      "Type": "source",
                      "Values": [1, 2]
                    }
                  }
                }
              }
            }
            """));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        var definition = await new ConfigurationApplicationDefinitionSource(
                configuration,
                "Application")
            .LoadAsync();

        definition.Resources.ShouldBeEmpty();
        var source = definition.Workflows["Main"].Components["Source"];
        source.Type.ShouldBe("source");
        source.Properties["Values"].GetArrayLength().ShouldBe(2);
    }
}
