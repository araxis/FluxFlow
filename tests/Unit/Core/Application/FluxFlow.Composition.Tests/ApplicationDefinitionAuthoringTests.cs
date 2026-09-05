using System.Text.Json;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Model;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ApplicationDefinitionAuthoringTests
{
    [Fact]
    public void Build_projects_values_json_resources_and_typed_handle_addresses()
    {
        var builder = new ApplicationDefinitionBuilder();
        var infrastructure = builder.AddResourceGroup("Infrastructure");
        var messaging = infrastructure.AddResourceGroup("Messaging");
        ResourceHandle<BrokerResource> broker;
        using (var document = JsonDocument.Parse(
                   """{"Mode":"strict","Thresholds":[1,2]}"""))
        {
            broker = messaging.AddResource<BrokerResource>(
                "Broker",
                "sample.broker",
                resource =>
                {
                    resource.Set("Host", "broker.internal");
                    resource.SetJson("Advanced", document.RootElement);
                });
        }

        var firstSubscription = messaging.AddResource<SubscriptionResource>(
            "Commands",
            "sample.subscription");
        var secondSubscription = messaging.AddResource<SubscriptionResource>(
            "Events",
            "sample.subscription");
        var client = messaging.AddResource<ClientResource>(
            "Client",
            "sample.client",
            resource =>
            {
                resource.UseResource("Broker", broker);
                resource.UseResources(
                    "Subscriptions",
                    [firstSubscription, secondSubscription]);
                resource.Set("Enabled", true);
            });
        var tags = new[] { "first", "second" };
        var workflow = builder.AddWorkflow("Orders");
        var source = workflow.AddComponent<SourceComponent>(
            "Source",
            "sample.source",
            component =>
            {
                component.Set("Options", new SampleOptions(true, [3, 5]));
                component.Set("Tags", tags);
                component.Set<string?>("Optional", null);
                component.UseResource("Client", client);
            });
        var sink = workflow.AddComponent("Sink", "sample.sink");
        var output = source.Output<string>(
            "Output",
            ComponentPortLinkCardinality.Single);
        var input = sink.Input<string>("Input");
        var signal = sink.SignalInput("Cancel");

        tags[0] = "changed";
        var definition = builder.Build();

        broker.ShouldBeOfType<ResourceHandle<BrokerResource>>();
        broker.Address.Value.ShouldBe("Resources.Infrastructure.Messaging.Broker");
        broker.Name.ShouldBe("Broker");
        broker.Type.ShouldBe("sample.broker");
        broker.ToString().ShouldBe(broker.Address.Value);
        client.Address.Value.ShouldBe("Resources.Infrastructure.Messaging.Client");
        source.ShouldBeOfType<ComponentHandle<SourceComponent>>();
        source.Address.Value.ShouldBe("Orders.Source");
        source.Name.ShouldBe("Source");
        source.Type.ShouldBe("sample.source");
        source.ToString().ShouldBe(source.Address.Value);
        output.Address.Value.ShouldBe("Orders.Source.Output");
        output.Name.ShouldBe("Output");
        output.LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
        input.Address.Value.ShouldBe("Orders.Sink.Input");
        signal.Address.Value.ShouldBe("Orders.Sink.Cancel");

        var resources = definition.Resources["Infrastructure"]
            .ShouldBeOfType<ResourceGroupDefinition>()
            .Resources["Messaging"]
            .ShouldBeOfType<ResourceGroupDefinition>()
            .Resources;
        var brokerDefinition = resources["Broker"]
            .ShouldBeOfType<ResourceInstanceDefinition>();
        brokerDefinition.Type.ShouldBe("sample.broker");
        brokerDefinition.Properties["Host"].GetString().ShouldBe("broker.internal");
        brokerDefinition.Properties["Advanced"].GetProperty("Mode")
            .GetString().ShouldBe("strict");
        brokerDefinition.Properties["Advanced"].GetProperty("Thresholds")
            .EnumerateArray().Select(static value => value.GetInt32())
            .ShouldBe([1, 2]);
        var clientDefinition = resources["Client"]
            .ShouldBeOfType<ResourceInstanceDefinition>();
        clientDefinition.Properties["Broker"].GetString()
            .ShouldBe("Resources.Infrastructure.Messaging.Broker");
        clientDefinition.Properties["Subscriptions"].EnumerateArray()
            .Select(static value => value.GetString())
            .ShouldBe([
                "Resources.Infrastructure.Messaging.Commands",
                "Resources.Infrastructure.Messaging.Events"
            ]);
        clientDefinition.Properties["Enabled"].GetBoolean().ShouldBeTrue();

        var sourceDefinition = definition.Workflows["Orders"].Components["Source"];
        sourceDefinition.Properties["Client"].GetString()
            .ShouldBe("Resources.Infrastructure.Messaging.Client");
        sourceDefinition.Properties["Options"].GetProperty("Enabled")
            .GetBoolean().ShouldBeTrue();
        sourceDefinition.Properties["Options"].GetProperty("Thresholds")
            .EnumerateArray().Select(static value => value.GetInt32())
            .ShouldBe([3, 5]);
        sourceDefinition.Properties["Tags"].EnumerateArray()
            .Select(static value => value.GetString())
            .ShouldBe(["first", "second"]);
        sourceDefinition.Properties["Optional"].ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void Connect_records_first_class_local_cross_workflow_conditional_and_signal_links()
    {
        var builder = new ApplicationDefinitionBuilder();
        var orders = builder.AddWorkflow("Orders");
        var source = orders.AddComponent(
            "Source",
            "sample.source",
            component => component.Set("Mode", "ordered"));
        var local = orders.AddComponent("Local", "sample.sink");
        var shipping = builder.AddWorkflow("Shipping");
        var remote = shipping.AddComponent("Target", "sample.target");
        var output = source.Output<Envelope>("Output");

        orders.Connect(output, local.Input<Envelope>("Input"));
        orders.Connect(output, local.SignalInput("Cancel"), "cancel == true");
        builder.Connect(output, remote.Input<Envelope>("Input"), "route == 'shipping'");

        var definition = builder.Build();

        var sourceProperties = definition.Workflows["Orders"].Components["Source"].Properties;
        sourceProperties["Mode"].GetString().ShouldBe("ordered");
        sourceProperties.ContainsKey("Output").ShouldBeFalse();
        definition.Links.Count.ShouldBe(3);
        definition.Links.Select(static link => link.Source.Value)
            .ShouldAllBe(static source => source == "Orders.Source.Output");
        definition.Links.Select(static link => link.Target.Value).ShouldBe(
        [
            "Orders.Local.Input",
            "Orders.Local.Cancel",
            "Shipping.Target.Input"
        ]);
        definition.Links.Select(static link => link.ConditionExpression).ShouldBe(
        [
            null,
            "cancel == true",
            "route == 'shipping'"
        ]);
        definition.Links.ShouldAllBe(static link => link.MessageType == typeof(Envelope));
        definition.Workflows["Orders"].Components["Local"].Properties.ShouldBeEmpty();
        definition.Workflows["Shipping"].Components["Target"].Properties.ShouldBeEmpty();
    }

    [Fact]
    public void Connect_rejects_duplicates_invalid_conditions_and_single_cardinality_atomically()
    {
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("Main");
        var firstSource = workflow.AddComponent("First", "sample.source");
        var secondSource = workflow.AddComponent("Second", "sample.source");
        var sink = workflow.AddComponent("Sink", "sample.sink");
        var singleOutput = firstSource.Output<int>(
            "SingleOutput",
            ComponentPortLinkCardinality.Single);
        var singleInput = sink.Input<int>(
            "SingleInput",
            ComponentPortLinkCardinality.Single);
        var multipleOutput = firstSource.Output<int>("MultipleOutput");
        var duplicateOutput = firstSource.Output<int>("DuplicateOutput");
        var duplicateInput = sink.Input<int>("DuplicateInput");

        workflow.Connect(singleOutput, sink.Input<int>("FirstInput"));
        Should.Throw<InvalidOperationException>(() => workflow.Connect(
            singleOutput,
            sink.Input<int>("SecondInput")));
        workflow.Connect(multipleOutput, singleInput);
        Should.Throw<InvalidOperationException>(() => workflow.Connect(
            secondSource.Output<int>("Output"),
            singleInput));
        workflow.Connect(duplicateOutput, duplicateInput);
        Should.Throw<InvalidOperationException>(() => workflow.Connect(
            duplicateOutput,
            duplicateInput));
        Should.Throw<ArgumentException>(() => workflow.Connect(
            firstSource.Output<int>("InvalidCondition"),
            sink.Input<int>("InvalidCondition"),
            " "));

        var definition = builder.Build();

        var firstProperties = definition.Workflows["Main"].Components["First"].Properties;
        firstProperties.ShouldBeEmpty();
        definition.Links.Select(static link => (link.Source.Value, link.Target.Value)).ShouldBe(
        [
            ("Main.First.SingleOutput", "Main.Sink.FirstInput"),
            ("Main.First.MultipleOutput", "Main.Sink.SingleInput"),
            ("Main.First.DuplicateOutput", "Main.Sink.DuplicateInput")
        ]);
        definition.Links.ShouldAllBe(static link => !link.IsConditional);
        definition.Workflows["Main"].Components["Second"].Properties.ShouldBeEmpty();
    }

    [Fact]
    public void Configuration_callbacks_commit_atomically_and_allow_same_name_retry()
    {
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("Orders");
        var resourceFailure = new DistinctiveException("resource callback failed");
        var componentFailure = new DistinctiveException("component callback failed");

        Should.Throw<DistinctiveException>(() => builder.AddResource(
                "Shared",
                "sample.partial",
                resource =>
                {
                    resource.Set("Partial", true);
                    throw resourceFailure;
                }))
            .ShouldBeSameAs(resourceFailure);
        Should.Throw<DistinctiveException>(() => workflow.AddComponent(
                "Worker",
                "sample.partial",
                component =>
                {
                    component.Set("Partial", true);
                    throw componentFailure;
                }))
            .ShouldBeSameAs(componentFailure);

        var sharedResource = builder.AddResource(
            "Shared",
            "sample.resource",
            resource => resource.Set("Committed", "resource"));
        workflow.AddComponent(
            "Worker",
            "sample.worker",
            component => component.Set("Committed", "component"));
        Should.Throw<ArgumentException>(() => workflow.AddComponent(
            "Bound",
            "sample.partial",
            component => component.Set("Resource", sharedResource)));
        workflow.AddComponent(
            "Bound",
            "sample.bound",
            component => component.UseResource("Resource", sharedResource));
        var definition = builder.Build();

        var resource = definition.Resources["Shared"]
            .ShouldBeOfType<ResourceInstanceDefinition>();
        resource.Type.ShouldBe("sample.resource");
        resource.Properties.Keys.ShouldBe(["Committed"], ignoreOrder: false);
        resource.Properties["Committed"].GetString().ShouldBe("resource");
        var component = definition.Workflows["Orders"].Components["Worker"];
        component.Type.ShouldBe("sample.worker");
        component.Properties.Keys.ShouldBe(["Committed"], ignoreOrder: false);
        component.Properties["Committed"].GetString().ShouldBe("component");
        var bound = definition.Workflows["Orders"].Components["Bound"];
        bound.Type.ShouldBe("sample.bound");
        bound.Properties["Resource"].GetString().ShouldBe("Resources.Shared");
    }

    [Fact]
    public void Builder_rejects_noncanonical_names_types_properties_and_duplicates()
    {
        var builder = new ApplicationDefinitionBuilder();
        var group = builder.AddResourceGroup("Group");
        var workflow = builder.AddWorkflow("Main");

        Should.Throw<ArgumentException>(() => builder.AddResourceGroup("Type"));
        Should.Throw<ArgumentException>(() => builder.AddResource("Bad.Name", "sample"));
        Should.Throw<ArgumentException>(() => builder.AddResource("BadType", " sample"));
        Should.Throw<ArgumentException>(() => builder.AddWorkflow("System"));
        Should.Throw<ArgumentException>(() => workflow.AddComponent("Bad.Name", "sample"));
        Should.Throw<ArgumentException>(() => group.AddResource(
            "Leaf",
            "sample.partial",
            resource => resource.Set("Type", "forbidden")));
        Should.Throw<ArgumentException>(() => workflow.AddComponent(
            "Undefined",
            "sample.partial",
            component => component.SetJson("Value", default)));

        group.AddResource("Leaf", "sample.resource");
        workflow.AddComponent("Worker", "sample.worker");
        workflow.AddComponent("Undefined", "sample.valid");
        Should.Throw<ArgumentException>(() => group.AddResource("Leaf", "sample.duplicate"));
        Should.Throw<ArgumentException>(() => workflow.AddComponent(
            "Worker",
            "sample.duplicate"));
        Should.Throw<ArgumentException>(() => builder.AddWorkflow("Main"));

        var definition = builder.Build();

        definition.Resources["Group"].ShouldBeOfType<ResourceGroupDefinition>()
            .Resources["Leaf"].ShouldBeOfType<ResourceInstanceDefinition>()
            .Type.ShouldBe("sample.resource");
        definition.Workflows["Main"].Components["Worker"].Type
            .ShouldBe("sample.worker");
        definition.Workflows["Main"].Components["Undefined"].Type
            .ShouldBe("sample.valid");
    }

    [Fact]
    public void Cross_owner_handles_are_rejected_without_partial_properties_or_links()
    {
        var first = new ApplicationDefinitionBuilder();
        var firstResource = first.AddResource("Shared", "sample.resource");
        var firstOrders = first.AddWorkflow("Orders");
        var firstSource = firstOrders.AddComponent("Source", "sample.source");
        var firstLocal = firstOrders.AddComponent("Local", "sample.sink");
        var firstShipping = first.AddWorkflow("Shipping");
        var firstRemote = firstShipping.AddComponent("Remote", "sample.sink");

        var second = new ApplicationDefinitionBuilder();
        var secondResource = second.AddResource("Shared", "sample.resource");
        var secondWorkflow = second.AddWorkflow("Other");
        var secondTarget = secondWorkflow.AddComponent("Target", "sample.sink");

        var receiverMismatch = firstSource.Output<string>("ReceiverMismatch");
        var mixedOwners = firstSource.Output<string>("MixedOwners");
        var nonLocal = firstSource.Output<string>("NonLocal");

        Should.Throw<InvalidOperationException>(() => second.Connect(
            receiverMismatch,
            firstLocal.Input<string>("ReceiverMismatch")));
        Should.Throw<InvalidOperationException>(() => first.Connect(
            mixedOwners,
            secondTarget.Input<string>("MixedOwners")));
        Should.Throw<InvalidOperationException>(() => firstOrders.Connect(
            nonLocal,
            firstRemote.Input<string>("NonLocal")));
        Should.Throw<InvalidOperationException>(() => secondWorkflow.AddComponent(
            "Bound",
            "sample.bound",
            component => component.UseResource("Resource", firstResource)));

        secondWorkflow.AddComponent(
            "Bound",
            "sample.bound",
            component => component.UseResource("Resource", secondResource));
        var firstDefinition = first.Build();
        var secondDefinition = second.Build();

        firstDefinition.Workflows["Orders"].Components["Source"]
            .Properties.ShouldBeEmpty();
        secondDefinition.Workflows["Other"].Components["Target"]
            .Properties.ShouldBeEmpty();
        secondDefinition.Workflows["Other"].Components["Bound"]
            .Properties["Resource"].GetString().ShouldBe("Resources.Shared");
    }

    [Fact]
    public void Build_freezes_root_and_all_retained_child_mutation_paths()
    {
        var builder = new ApplicationDefinitionBuilder();
        var group = builder.AddResourceGroup("Group");
        ResourceDefinitionBuilder? retainedResourceBuilder = null;
        group.AddResource(
            "Leaf",
            "sample.resource",
            resource =>
            {
                retainedResourceBuilder = resource;
                resource.Set("Value", 1);
            });
        var workflow = builder.AddWorkflow("Orders");
        ComponentDefinitionBuilder? retainedComponentBuilder = null;
        var source = workflow.AddComponent(
            "Source",
            "sample.source",
            component =>
            {
                retainedComponentBuilder = component;
                component.Set("Value", 2);
            });
        var sink = workflow.AddComponent("Sink", "sample.sink");
        var output = source.Output<int>("Output");
        var input = sink.Input<int>("Input");

        Should.Throw<InvalidOperationException>(() => retainedResourceBuilder!.Set("Later", 3));
        Should.Throw<InvalidOperationException>(() => retainedComponentBuilder!.Set("Later", 4));
        var definition = builder.Build();

        Should.Throw<InvalidOperationException>(() => builder.AddResourceGroup("Later"));
        Should.Throw<InvalidOperationException>(() => builder.AddResource("Later", "sample"));
        Should.Throw<InvalidOperationException>(() => group.AddResource("Later", "sample"));
        Should.Throw<InvalidOperationException>(() => builder.AddWorkflow("Later"));
        Should.Throw<InvalidOperationException>(() => workflow.AddComponent("Later", "sample"));
        Should.Throw<InvalidOperationException>(() => workflow.Connect(output, input));
        Should.Throw<InvalidOperationException>(() => builder.Connect(output, input));
        Should.Throw<InvalidOperationException>(() => builder.Build());

        definition.Resources.Keys.ShouldBe(["Group"], ignoreOrder: false);
        definition.Workflows.Keys.ShouldBe(["Orders"], ignoreOrder: false);
        definition.Workflows["Orders"].Components.Keys
            .ShouldBe(["Sink", "Source"], ignoreOrder: true);
        var mutableResources = (IDictionary<string, ResourceDefinition>)definition.Resources;
        Should.Throw<NotSupportedException>(() => mutableResources.Add(
            "Later",
            new ResourceGroupDefinition()));
        var mutableWorkflows = (IDictionary<string, WorkflowDefinition>)definition.Workflows;
        Should.Throw<NotSupportedException>(() => mutableWorkflows.Add(
            "Later",
            new WorkflowDefinition()));
    }

    [Fact]
    public void First_class_connection_does_not_conflict_with_raw_component_property()
    {
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("Main");
        var source = workflow.AddComponent(
            "Source",
            "sample.source",
            component => component.Set("Output", "manual.reference"));
        var sink = workflow.AddComponent("Sink", "sample.sink");
        workflow.Connect(
            source.Output<int>("Output"),
            sink.Input<int>("Input"));

        var definition = builder.Build();

        definition.Workflows["Main"].Components["Source"].Properties["Output"]
            .GetString().ShouldBe("manual.reference");
        var link = definition.Links.ShouldHaveSingleItem();
        link.Source.Value.ShouldBe("Main.Source.Output");
        link.Target.Value.ShouldBe("Main.Sink.Input");
        Should.Throw<InvalidOperationException>(() => builder.AddWorkflow("Frozen"));
    }

    [Fact]
    public void Portable_json_round_trip_remains_canonical_and_never_creates_code_predicates()
    {
        const string expected =
            "{\"Resources\":{\"Data\":{\"Store\":{\"Type\":\"sample.store\"," +
            "\"Enabled\":true,\"Limits\":[2,4]}}},\"Workflows\":{\"Main\":{" +
            "\"Sink\":{\"Type\":\"sample.sink\"},\"Source\":{\"Type\":\"sample.source\"," +
            "\"Output\":{\"Condition\":\"value \\u003E 0\",\"Port\":\"Sink.Input\"}," +
            "\"Store\":\"Resources.Data.Store\"}}}}";

        var definition = ApplicationDefinitionJson.Deserialize(expected);
        var json = ApplicationDefinitionJson.Serialize(definition);

        definition.Links.ShouldBeEmpty();
        json.ShouldBe(expected);
        ApplicationDefinitionJson.Serialize(
            ApplicationDefinitionJson.Deserialize(json)).ShouldBe(expected);
    }

    private sealed class BrokerResource;

    private sealed class SubscriptionResource;

    private sealed class ClientResource;

    private sealed class StoreResource;

    private sealed class SourceComponent;

    private sealed class Envelope;

    private sealed record SampleOptions(bool Enabled, int[] Thresholds);

    private sealed class DistinctiveException(string message) : Exception(message);
}
