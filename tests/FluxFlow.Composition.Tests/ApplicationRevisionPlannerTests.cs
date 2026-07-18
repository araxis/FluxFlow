using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;
using FluxFlow.Composition.Revisions;
using FluxFlow.Data;
using Shouldly;
using System.Text.Json;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ApplicationRevisionPlannerTests
{
    [Fact]
    public void Changed_resource_expands_transitive_resource_and_workflow_dependents()
    {
        var current = Read("""
            {
              "Resources": {
                "Broker": { "Type": "broker", "Host": "old" },
                "Client": { "Type": "client", "Broker": "Resources.Broker" },
                "Publisher": { "Type": "publisher", "Client": "Resources.Client" }
              },
              "Workflows": {
                "Orders": {
                  "Publish": { "Type": "publish", "Client": "Resources.Publisher" }
                },
                "Unrelated": {
                  "Sink": { "Type": "sink" }
                }
              }
            }
            """);
        var next = Read("""
            {
              "Resources": {
                "Broker": { "Type": "broker", "Host": "new" },
                "Client": { "Type": "client", "Broker": "Resources.Broker" },
                "Publisher": { "Type": "publisher", "Client": "Resources.Client" }
              },
              "Workflows": {
                "Orders": {
                  "Publish": { "Type": "publish", "Client": "Resources.Publisher" }
                },
                "Unrelated": {
                  "Sink": { "Type": "sink" }
                }
              }
            }
            """);

        var plan = new ApplicationRevisionPlanner().Plan(current, next);

        plan.IsValid.ShouldBeTrue();
        plan.ResourceChanges.ShouldBe([
            new ApplicationResourceRevisionChange
            {
                Address = ApplicationAddress.Resource("Broker"),
                Kind = ApplicationRevisionChangeKind.Updated
            }
        ]);
        plan.AffectedResources.Select(static value => value.Value).ShouldBe([
            "Resources.Broker",
            "Resources.Client",
            "Resources.Publisher"
        ]);
        plan.AffectedWorkflows.ShouldBe(["Orders"]);
    }

    [Fact]
    public void Removing_referenced_resource_without_dependent_change_is_rejected()
    {
        var current = Read("""
            {
              "Resources": {
                "Client": { "Type": "client" }
              },
              "Workflows": {
                "Orders": {
                  "Publish": { "Type": "publish", "Client": "Resources.Client" }
                }
              }
            }
            """);
        var next = Read("""
            {
              "Resources": {},
              "Workflows": {
                "Orders": {
                  "Publish": { "Type": "publish", "Client": "Resources.Client" }
                }
              }
            }
            """);

        var plan = new ApplicationRevisionPlanner().Plan(current, next);

        plan.IsValid.ShouldBeFalse();
        plan.Diagnostics.Single().Code.ShouldBe(
            ApplicationRevisionDiagnosticCode.MissingResourceReference);
        plan.Diagnostics.Single().Resource.ShouldBe(ApplicationAddress.Resource("Client"));
        plan.AffectedWorkflows.ShouldBe(["Orders"]);
    }

    [Fact]
    public void Removing_resource_and_dependent_workflow_together_is_valid()
    {
        var current = Read("""
            {
              "Resources": {
                "Client": { "Type": "client" }
              },
              "Workflows": {
                "Orders": {
                  "Publish": { "Type": "publish", "Client": "Resources.Client" }
                }
              }
            }
            """);
        var next = Read("""{ "Resources": {}, "Workflows": {} }""");

        var plan = new ApplicationRevisionPlanner().Plan(current, next);

        plan.IsValid.ShouldBeTrue();
        plan.ResourceChanges.Single().Kind.ShouldBe(ApplicationRevisionChangeKind.Removed);
        plan.WorkflowChanges.Single().ShouldBe(new ApplicationWorkflowRevisionChange
        {
            Workflow = "Orders",
            Kind = ApplicationRevisionChangeKind.Removed
        });
        plan.AffectedResources.ShouldBeEmpty();
        plan.AffectedWorkflows.ShouldBeEmpty();
    }

    [Fact]
    public void Resource_dependency_cycles_are_rejected()
    {
        var definition = Read("""
            {
              "Resources": {
                "First": { "Type": "client", "Next": "Resources.Second" },
                "Second": { "Type": "client", "Next": "Resources.First" }
              },
              "Workflows": {}
            }
            """);

        var plan = new ApplicationRevisionPlanner().Plan(
            new ApplicationDefinition(),
            definition);

        plan.IsValid.ShouldBeFalse();
        plan.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == ApplicationRevisionDiagnosticCode.ResourceDependencyCycle);
    }

    [Fact]
    public void Object_property_order_does_not_create_a_revision()
    {
        var current = Read("""
            {
              "Resources": {
                "Client": {
                  "Type": "client",
                  "Options": { "Host": "localhost", "Port": 1883 }
                }
              },
              "Workflows": {}
            }
            """);
        var next = Read("""
            {
              "Resources": {
                "Client": {
                  "Type": "client",
                  "Options": { "Port": 1883, "Host": "localhost" }
                }
              },
              "Workflows": {}
            }
            """);

        var plan = new ApplicationRevisionPlanner().Plan(current, next);

        plan.IsValid.ShouldBeTrue();
        plan.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public void Workflow_changes_are_whole_workflow_revision_units()
    {
        var current = Read("""
            {
              "Resources": {},
              "Workflows": {
                "Orders": { "Sink": { "Type": "sink", "Mode": "old" } }
              }
            }
            """);
        var next = Read("""
            {
              "Resources": {},
              "Workflows": {
                "Orders": { "Sink": { "Type": "sink", "Mode": "new" } }
              }
            }
            """);

        var plan = new ApplicationRevisionPlanner().Plan(current, next);

        plan.IsValid.ShouldBeTrue();
        plan.WorkflowChanges.Single().Kind.ShouldBe(ApplicationRevisionChangeKind.Updated);
        plan.AffectedWorkflows.ShouldBe(["Orders"]);
    }

    [Fact]
    public void Revision_event_copies_and_orders_transport_values()
    {
        var resources = new List<ApplicationAddress>
        {
            ApplicationAddress.Resource("Second"),
            ApplicationAddress.Resource("First")
        };
        var workflows = new List<string> { "Second", "First" };
        var revisionEvent = new ApplicationRevisionEvent(
            7,
            "revision-7",
            new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero),
            ApplicationRevisionPhase.Rejected,
            resources,
            workflows,
            new FlowError("revision.invalid", "Invalid revision.", "revision"));

        resources.Clear();
        workflows.Clear();

        revisionEvent.Resources.Select(static value => value.Value).ShouldBe([
            "Resources.First",
            "Resources.Second"
        ]);
        revisionEvent.Workflows.ShouldBe(["First", "Second"]);
        JsonSerializer.Serialize(revisionEvent).ShouldBe(
            "{\"Sequence\":7,\"RevisionId\":\"revision-7\"," +
            "\"Timestamp\":\"2026-07-17T01:02:03+00:00\",\"Phase\":3," +
            "\"Resources\":[{\"Kind\":0,\"Value\":\"Resources.First\"," +
            "\"Segments\":[\"Resources\",\"First\"]},{\"Kind\":0," +
            "\"Value\":\"Resources.Second\"," +
            "\"Segments\":[\"Resources\",\"Second\"]}]," +
            "\"Workflows\":[\"First\",\"Second\"]," +
            "\"Error\":{\"Code\":\"revision.invalid\"," +
            "\"Message\":\"Invalid revision.\",\"Category\":\"revision\"," +
            "\"IsTransient\":false," +
            "\"Details\":{\"kind\":\"object\",\"value\":{}}}}" );
    }

    private static ApplicationDefinition Read(string json)
        => ApplicationDefinitionJson.Deserialize(json);
}
