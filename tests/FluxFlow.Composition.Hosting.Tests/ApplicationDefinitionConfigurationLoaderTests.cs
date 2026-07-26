using FluxFlow.Composition.Model;
using Microsoft.Extensions.Configuration;
using Shouldly;
using System.Text;
using Xunit;

namespace FluxFlow.Composition.Hosting.Tests;

public sealed class ApplicationDefinitionConfigurationLoaderTests
{
    [Fact]
    public void LoadsCanonicalDefinitionFromConfigurationRoot()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
        {
          "Resources": {},
          "Workflows": {
            "Orders": {
              "Source": {
                "Type": "sample.source",
                "Items": [ "one", "two" ]
              }
            }
          }
        }
        """));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        var definition = new ApplicationDefinitionConfigurationLoader().Load(configuration);

        definition.Workflows["Orders"].Components["Source"]
            .Properties["Items"].GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public void LoadsCanonicalDefinitionFromExplicitHostSection()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
        {
          "Definition": {
            "Resources": {},
            "Workflows": {}
          },
          "HostSetting": true
        }
        """));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        var definition = new ApplicationDefinitionConfigurationLoader()
            .Load(configuration, "Definition");

        definition.Resources.ShouldBeEmpty();
        definition.Workflows.ShouldBeEmpty();
    }

    [Fact]
    public void RestoresEmptyWorkflowAndResourceGroupObjectsLostByConfigurationProviders()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
        {
          "Resources": {
            "Messaging": {
              "Unused": {}
            }
          },
          "Workflows": {
            "Idle": {}
          }
        }
        """));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        var definition = new ApplicationDefinitionConfigurationLoader().Load(configuration);

        definition.Resources["Messaging"]
            .ShouldBeOfType<ResourceGroupDefinition>()
            .Resources["Unused"]
            .ShouldBeOfType<ResourceGroupDefinition>()
            .Resources.ShouldBeEmpty();
        definition.Workflows["Idle"].Components.ShouldBeEmpty();
    }

    [Fact]
    public void RejectsMissingSectionsAndLegacyWrappers()
    {
        var missing = new ConfigurationBuilder().Build();
        using var legacyStream = new MemoryStream(Encoding.UTF8.GetBytes("""
        {
          "Resources": {},
          "Workflows": {
            "Orders": {
              "Node": {
                "Type": "sample",
                "Configuration": { "Count": 1 }
              }
            }
          }
        }
        """));
        var legacy = new ConfigurationBuilder().AddJsonStream(legacyStream).Build();

        Should.Throw<CompositionConfigurationException>(() =>
            new ApplicationDefinitionConfigurationLoader().Load(missing));
        Should.Throw<CompositionConfigurationException>(() =>
            new ApplicationDefinitionConfigurationLoader().Load(legacy));
    }
}
