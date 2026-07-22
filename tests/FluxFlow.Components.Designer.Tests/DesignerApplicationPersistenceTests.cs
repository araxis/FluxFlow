using System.Text.Json;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Designer.Persistence;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class DesignerApplicationPersistenceTests
{
    [Fact]
    public void Load_projects_flat_document_links_resources_and_references()
    {
        var persistence = CreatePersistence();

        var result = persistence.Load("""
            {
              "Resources": {
                "Messaging": {
                  "Client1": {
                    "Type": "mqtt.client",
                    "ClientId": "client-1"
                  }
                }
              },
              "Workflows": {
                "Main": {
                  "Producer": {
                    "Type": "test.source",
                    "Client": "Resources.Messaging.Client1",
                    "Output": "Consumer.Input"
                  },
                  "Consumer": {
                    "Type": "test.sink"
                  },
                  "InputDeclared": {
                    "Type": "test.sink",
                    "Input": "Other.Producer.Output"
                  }
                },
                "Other": {
                  "Producer": {
                    "Type": "test.source"
                  }
                }
              }
            }
            """);

        result.IsValid.ShouldBeTrue();
        result.Document.Workflows.Keys.ShouldBe(["Main", "Other"], ignoreOrder: true);
        result.Document.Workflows["Main"].Components["Producer"].Properties.Keys
            .ShouldBe(["Client"]);

        var outputLink = result.Document.Links.Single(link =>
            link.Source == ApplicationAddress.WorkflowPort("Main", "Producer", "Output"));
        outputLink.Target.ShouldBe(ApplicationAddress.WorkflowPort("Main", "Consumer", "Input"));
        outputLink.DeclarationSide.ShouldBe(ApplicationLinkDeclarationSide.Output);

        var inputLink = result.Document.Links.Single(link =>
            link.Target == ApplicationAddress.WorkflowPort("Main", "InputDeclared", "Input"));
        inputLink.Source.ShouldBe(ApplicationAddress.WorkflowPort("Other", "Producer", "Output"));
        inputLink.DeclarationSide.ShouldBe(ApplicationLinkDeclarationSide.Input);

        var messaging = result.Document.Resources.Entries["Messaging"]
            .ShouldBeOfType<DesignerResourceNamespace>();
        messaging.Path.ShouldBe("Resources.Messaging");
        var client = messaging.Entries["Client1"].ShouldBeOfType<DesignerResource>();
        client.Address.ShouldBe(ApplicationAddress.Resource("Messaging", "Client1"));
        client.Type.ShouldBe("mqtt.client");

        var reference = result.Document.ResourceReferences.ShouldHaveSingleItem();
        reference.Component.ShouldBe(ApplicationAddress.WorkflowComponent("Main", "Producer"));
        reference.PropertyName.ShouldBe("Client");
        reference.Address.ShouldBe(ApplicationAddress.Resource("Messaging", "Client1"));
        reference.IsRequired.ShouldBeTrue();
        reference.Exists.ShouldBeTrue();
    }

    [Fact]
    public void Serialize_preserves_loaded_declaration_sides()
    {
        var persistence = CreatePersistence();
        var loaded = persistence.Load("""
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Producer": {
                    "Type": "test.source",
                    "Output": "Consumer.Input"
                  },
                  "Consumer": {
                    "Type": "test.sink"
                  },
                  "InputDeclared": {
                    "Type": "test.sink",
                    "Input": "Producer.Output"
                  }
                }
              }
            }
            """);

        var definition = persistence.ToDefinition(loaded.Document);

        definition.Workflows["Main"].Components["Producer"].Properties
            .ContainsKey("Output").ShouldBeTrue();
        definition.Workflows["Main"].Components["Consumer"].Properties
            .ContainsKey("Input").ShouldBeFalse();
        definition.Workflows["Main"].Components["InputDeclared"].Properties
            .ContainsKey("Input").ShouldBeTrue();

        var reloaded = persistence.Load(definition);
        reloaded.Document.Links.Select(static link => link.DeclarationSide)
            .ShouldBe(
                [ApplicationLinkDeclarationSide.Input, ApplicationLinkDeclarationSide.Output],
                ignoreOrder: true);
    }

    [Fact]
    public void Legacy_types_load_with_migration_diagnostics_and_serialize_canonically()
    {
        var registry = CreateRegistry()
            .RegisterAlias("test.old-source", "test.source")
            .RegisterResourceTypeAlias("test.old-client", "test.client");
        var persistence = new DesignerApplicationPersistence(registry, CreateMetadata());

        var loaded = persistence.Load("""
            {
              "Resources": {
                "Client": { "Type": "test.old-client" }
              },
              "Workflows": {
                "Main": {
                  "Producer": { "Type": "test.old-source" }
                }
              }
            }
            """);

        loaded.NormalizationDiagnostics.Count.ShouldBe(2);
        loaded.Document.Workflows["Main"].Components["Producer"].Type.ShouldBe("test.source");
        persistence.Serialize(loaded.Document, writeIndented: false)
            .ShouldBe("{\"Resources\":{\"Client\":{\"Type\":\"test.client\"}},\"Workflows\":{\"Main\":{\"Producer\":{\"Type\":\"test.source\"}}}}");
    }

    [Fact]
    public void New_workflow_link_defaults_to_source_side_and_uses_local_reference()
    {
        var persistence = CreatePersistence();
        var loaded = persistence.Load("""
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Producer": { "Type": "test.source" },
                  "Consumer": { "Type": "test.sink" }
                }
              }
            }
            """);
        var link = DesignerApplicationLink.Create(
            ApplicationAddress.WorkflowPort("Main", "Producer", "Output"),
            ApplicationAddress.WorkflowPort("Main", "Consumer", "Input"));
        var document = loaded.Document with { Links = [link] };

        var definition = persistence.ToDefinition(document);

        link.DeclarationSide.ShouldBe(ApplicationLinkDeclarationSide.Output);
        definition.Workflows["Main"].Components["Producer"].Properties["Output"]
            .GetString().ShouldBe("Consumer.Input");
        definition.Workflows["Main"].Components["Consumer"].Properties
            .ContainsKey("Input").ShouldBeFalse();
    }

    [Fact]
    public void New_system_link_uses_the_only_valid_input_side_declaration()
    {
        var link = DesignerApplicationLink.Create(
            ApplicationAddress.SystemEvents,
            ApplicationAddress.WorkflowPort("Main", "Consumer", "Input"));

        link.DeclarationSide.ShouldBe(ApplicationLinkDeclarationSide.Input);
    }

    [Fact]
    public void Load_uses_runtime_link_diagnostics()
    {
        var registry = CreateRegistry();
        var compiler = new ApplicationLinkCompiler(registry);
        var persistence = new DesignerApplicationPersistence(registry, CreateMetadata(), compiler);
        var definition = ApplicationDefinitionJson.Deserialize("""
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Producer": {
                    "Type": "test.source",
                    "Output": "Missing.Input"
                  }
                }
              }
            }
            """);

        var expected = compiler.Compile(definition).Diagnostics;
        var actual = persistence.Load(definition).Diagnostics;

        actual.Select(static item => (item.Code, item.Message, item.WorkflowName, item.ComponentName, item.PropertyName))
            .ShouldBe(expected.Select(static item =>
                (item.Code, item.Message, item.WorkflowName, item.ComponentName, item.PropertyName)));
    }

    [Fact]
    public void Malformed_link_property_remains_raw_for_lossless_round_trip()
    {
        var persistence = CreatePersistence();
        var loaded = persistence.Load("""
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Consumer": {
                    "Type": "test.sink",
                    "Input": 42
                  }
                }
              }
            }
            """);

        loaded.IsValid.ShouldBeFalse();
        loaded.Diagnostics.ShouldContain(item =>
            item.Code == ApplicationLinkDiagnosticCode.InvalidLinkDeclaration);
        loaded.Document.Links.ShouldBeEmpty();
        loaded.Document.Workflows["Main"].Components["Consumer"].Properties["Input"]
            .GetInt32().ShouldBe(42);

        var definition = persistence.ToDefinition(loaded.Document);
        definition.Workflows["Main"].Components["Consumer"].Properties["Input"]
            .GetInt32().ShouldBe(42);
    }

    [Fact]
    public void Mixed_link_array_and_conditions_serialize_in_canonical_port_form()
    {
        var persistence = CreatePersistence();
        var loaded = persistence.Load("""
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Producer": { "Type": "test.source" },
                  "First": { "Type": "test.sink" },
                  "Second": { "Type": "test.sink" }
                }
              }
            }
            """);
        var source = ApplicationAddress.WorkflowPort("Main", "Producer", "Output");
        var document = loaded.Document with
        {
            Links =
            [
                DesignerApplicationLink.Create(
                    source,
                    ApplicationAddress.WorkflowPort("Main", "First", "Input")),
                DesignerApplicationLink.Create(
                    source,
                    ApplicationAddress.WorkflowPort("Main", "Second", "Input"),
                    "value == 1")
            ]
        };

        var declaration = persistence.ToDefinition(document)
            .Workflows["Main"].Components["Producer"].Properties["Output"];

        declaration.ValueKind.ShouldBe(JsonValueKind.Array);
        declaration[0].GetString().ShouldBe("First.Input");
        declaration[1].GetProperty("Port").GetString().ShouldBe("Second.Input");
        declaration[1].GetProperty("Condition").GetString().ShouldBe("value == 1");
    }

    private static DesignerApplicationPersistence CreatePersistence()
        => new(CreateRegistry(), CreateMetadata());

    private static CompositionNodeRegistry CreateRegistry()
        => new CompositionNodeRegistry()
            .Register(
                "test.source",
                static _ => throw new NotSupportedException(),
                outputs: [CompositionPortMetadata.Create<string>("Output")])
            .Register(
                "test.sink",
                static _ => throw new NotSupportedException(),
                inputs: [CompositionPortMetadata.Create<string>("Input")]);

    private static ComponentDesignMetadataCatalog CreateMetadata()
        => new ComponentDesignMetadataCatalog().Add(
            new ComponentDesignMetadataBuilder("test.source")
                .AddResource(
                    "Client",
                    displayName: "Client",
                    summary: "Client resource.",
                    valueType: "client",
                    isRequired: true)
                .Build());
}
