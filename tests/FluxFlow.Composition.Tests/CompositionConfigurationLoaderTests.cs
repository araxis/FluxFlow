using System.Text;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class CompositionConfigurationLoaderTests
{
    [Fact]
    public void Loader_reads_appsettings_style_json_into_definition_model()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
        {
          "FluxFlow": {
            "Composition": {
              "workflows": {
                " main ": {
                  "nodes": {
                    " source ": {
                      "type": "test.source",
                      "configuration": {
                        " messages ": [ "one", "two" ]
                      },
                      "resources": {
                        " store ": "primary-store"
                      }
                    },
                    "sink": {
                      "type": " test.sink "
                    }
                  },
                  "links": [
                    { "from": "source.Output", "to": "sink.Input" }
                  ]
                }
              }
            }
          }
        }
        """));

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        var definition = new CompositionConfigurationLoader().Load(configuration);

        definition.Workflows.ContainsKey("main").ShouldBeTrue();
        var workflow = definition.Workflows["main"];
        workflow.Nodes.ContainsKey("source").ShouldBeTrue();
        workflow.Nodes["source"].Type.ShouldBe(TestNodeTypes.Source);
        workflow.Nodes["sink"].Type.ShouldBe(TestNodeTypes.Sink);
        workflow.Nodes["source"].Resources["store"].ShouldBe("primary-store");
        workflow.Nodes["source"].Configuration["messages"].GetArrayLength().ShouldBe(2);
        workflow.Links.ShouldHaveSingleItem();
        workflow.Links[0].From.ShouldBe(new PortReference { Node = "source", Port = "Output" });
        workflow.Links[0].To.ShouldBe(new PortReference { Node = "sink", Port = "Input" });
    }

    [Fact]
    public void Loader_returns_empty_definition_when_default_section_is_missing()
    {
        var configuration = new ConfigurationBuilder().Build();

        var definition = new CompositionConfigurationLoader().Load(configuration);

        definition.Workflows.ShouldBeEmpty();
    }
}
