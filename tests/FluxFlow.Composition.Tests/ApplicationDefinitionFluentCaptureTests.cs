using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Model;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ApplicationDefinitionFluentCaptureTests
{
    [Fact]
    public void Workflow_authoring_exposes_only_return_and_out_capture_shapes()
    {
        var methods = typeof(ApplicationDefinitionBuilder)
            .GetMethods(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.DeclaredOnly)
            .Where(static method => method.Name == nameof(ApplicationDefinitionBuilder.AddWorkflow))
            .OrderBy(static method => method.GetParameters().Length)
            .ToArray();

        methods.Length.ShouldBe(2);
        methods[0].ReturnType.ShouldBe(typeof(WorkflowDefinitionBuilder));
        methods[0].GetParameters().Select(static parameter => parameter.ParameterType)
            .ShouldBe([typeof(string)]);
        methods[1].ReturnType.ShouldBe(typeof(ApplicationDefinitionBuilder));
        var captureParameters = methods[1].GetParameters();
        captureParameters.Length.ShouldBe(2);
        captureParameters[0].ParameterType.ShouldBe(typeof(string));
        captureParameters[1].IsOut.ShouldBeTrue();
        captureParameters[1].ParameterType.ShouldBe(
            typeof(WorkflowDefinitionBuilder).MakeByRefType());
        methods.SelectMany(static method => method.GetParameters())
            .ShouldNotContain(static parameter =>
                typeof(Delegate).IsAssignableFrom(parameter.ParameterType));
    }

    [Fact]
    public void Capture_overloads_return_exact_builders_and_assign_exact_handles()
    {
        var application = new ApplicationDefinitionBuilder();

        application.AddResourceGroup("Infrastructure", out var infrastructure)
            .ShouldBeSameAs(application);
        infrastructure.AddResourceGroup("Messaging", out var messaging)
            .ShouldBeSameAs(infrastructure);
        application.AddResource("Root", "sample.root", out var root)
            .ShouldBeSameAs(application);
        application.AddResource<ClockResource>(
                "Clock",
                "sample.clock",
                resource => resource.Set("Zone", "UTC"),
                out var clock)
            .ShouldBeSameAs(application);
        messaging.AddResource(
                "Broker",
                "sample.broker",
                resource => resource.Set("Host", "broker.internal"),
                out var broker)
            .ShouldBeSameAs(messaging);
        messaging.AddResource<StoreResource>("Store", "sample.store", out var store)
            .ShouldBeSameAs(messaging);
        application.AddExternalResource<ExternalResource>("External", out var external)
            .ShouldBeSameAs(application);
        messaging.AddExternalResource<ExternalResource>("NestedExternal", out var nestedExternal)
            .ShouldBeSameAs(messaging);
        application.AddWorkflow("Main", out var workflow).ShouldBeSameAs(application);
        workflow.AddComponent("Untyped", "sample.untyped", out var untyped)
            .ShouldBeSameAs(workflow);
        workflow.AddComponent<WorkerComponent>(
                "Worker",
                "sample.worker",
                component => component.UseResource("Clock", clock),
                out var worker)
            .ShouldBeSameAs(workflow);

        infrastructure.ShouldNotBeSameAs(messaging);
        root.Address.Value.ShouldBe("Resources.Root");
        root.Type.ShouldBe("sample.root");
        clock.Address.Value.ShouldBe("Resources.Clock");
        clock.Type.ShouldBe("sample.clock");
        broker.Address.Value.ShouldBe("Resources.Infrastructure.Messaging.Broker");
        broker.Type.ShouldBe("sample.broker");
        store.Address.Value.ShouldBe("Resources.Infrastructure.Messaging.Store");
        store.Type.ShouldBe("sample.store");
        external.Address.Value.ShouldBe("Resources.External");
        external.Type.ShouldBe(ApplicationResourceTypes.External);
        nestedExternal.Address.Value.ShouldBe(
            "Resources.Infrastructure.Messaging.NestedExternal");
        workflow.Name.ShouldBe("Main");
        untyped.Address.Value.ShouldBe("Main.Untyped");
        untyped.Type.ShouldBe("sample.untyped");
        worker.Address.Value.ShouldBe("Main.Worker");
        worker.Type.ShouldBe("sample.worker");

        var definition = application.Build();
        definition.Resources.Keys.ShouldBe(
            ["Clock", "External", "Infrastructure", "Root"],
            ignoreOrder: true);
        definition.Workflows["Main"].Components.Keys.ShouldBe(
            ["Untyped", "Worker"],
            ignoreOrder: true);
    }

    [Fact]
    public void Same_chain_out_variables_are_definitely_assigned_and_connect_fluently()
    {
        var application = new ApplicationDefinitionBuilder();
        var returnedApplication = application
            .AddWorkflow("Main", out var main)
            .AddWorkflow("Audit", out var audit);

        var returnedMain = main
            .AddComponent<SourceComponent>("Source", "sample.source", out var source)
            .AddComponent<TransformComponent>("Transform", "sample.transform", out var transform)
            .Connect(
                source.Output<int>("Output"),
                transform.Input<int>("Input"),
                "value > 0")
            .AddComponent("Sink", "sample.sink", out var sink)
            .Connect(
                transform.Output<int>("Output"),
                sink.Input<int>("Input"));
        audit.AddComponent("Recorder", "sample.recorder", out var recorder);
        var returnedCrossWorkflow = application.Connect(
            transform.Output<string>("Audit"),
            recorder.Input<string>("Input"),
            "value != null");

        returnedApplication.ShouldBeSameAs(application);
        returnedMain.ShouldBeSameAs(main);
        returnedCrossWorkflow.ShouldBeSameAs(application);
        source.Address.Value.ShouldBe("Main.Source");
        transform.Address.Value.ShouldBe("Main.Transform");
        sink.Address.Value.ShouldBe("Main.Sink");
        recorder.Address.Value.ShouldBe("Audit.Recorder");

        var definition = application.Build();
        definition.Workflows["Main"].Components.Values
            .ShouldAllBe(static component => component.Properties.Count == 0);
        definition.Links.Select(static link =>
            (link.Source.Value, link.Target.Value, link.ConditionExpression)).ShouldBe(
        [
            ("Main.Source.Output", "Main.Transform.Input", "value > 0"),
            ("Main.Transform.Output", "Main.Sink.Input", null),
            ("Main.Transform.Audit", "Audit.Recorder.Input", "value != null")
        ]);
    }

    [Fact]
    public void Fluent_configuration_failures_are_atomic_and_allow_same_name_retry()
    {
        var application = new ApplicationDefinitionBuilder();
        application.AddWorkflow("Main", out var workflow);
        var resourceFailure = new DistinctiveException("resource callback failed");
        var componentFailure = new DistinctiveException("component callback failed");

        var caughtResource = Should.Throw<DistinctiveException>(() =>
            application.AddResource(
                "RetryResource",
                "sample.failed",
                resource =>
                {
                    resource.Set("Partial", true);
                    throw resourceFailure;
                },
                out _));
        var caughtComponent = Should.Throw<DistinctiveException>(() =>
            workflow.AddComponent(
                "RetryComponent",
                "sample.failed",
                component =>
                {
                    component.Set("Partial", true);
                    throw componentFailure;
                },
                out _));

        caughtResource.ShouldBeSameAs(resourceFailure);
        caughtComponent.ShouldBeSameAs(componentFailure);
        application.AddResource(
                "RetryResource",
                "sample.resource",
                resource => resource.Set("Committed", 17),
                out var resource)
            .ShouldBeSameAs(application);
        workflow.AddComponent(
                "RetryComponent",
                "sample.component",
                component => component.Set("Committed", 23),
                out var component)
            .ShouldBeSameAs(workflow);

        resource.Type.ShouldBe("sample.resource");
        component.Type.ShouldBe("sample.component");
        var definition = application.Build();
        var resourceDefinition = (ResourceInstanceDefinition)definition.Resources["RetryResource"];
        resourceDefinition.Properties.Keys.ShouldBe(["Committed"]);
        resourceDefinition.Properties["Committed"].GetInt32().ShouldBe(17);
        var componentDefinition = definition.Workflows["Main"].Components["RetryComponent"];
        componentDefinition.Properties.Keys.ShouldBe(["Committed"]);
        componentDefinition.Properties["Committed"].GetInt32().ShouldBe(23);
    }

    [Fact]
    public void Workflow_return_and_out_capture_shapes_build_equivalent_immutable_definitions()
    {
        var original = BuildWithOriginalReturns();
        var fluent = BuildWithFluentCaptures();

        original.Resources.Keys.ShouldBe(fluent.Resources.Keys);
        original.Workflows.Keys.ShouldBe(fluent.Workflows.Keys);
        var originalWorkflow = original.Workflows["Main"];
        var fluentWorkflow = fluent.Workflows["Main"];
        originalWorkflow.Components.Keys.ShouldBe(fluentWorkflow.Components.Keys);
        foreach (var componentName in originalWorkflow.Components.Keys)
        {
            var originalComponent = originalWorkflow.Components[componentName];
            var fluentComponent = fluentWorkflow.Components[componentName];
            fluentComponent.Type.ShouldBe(originalComponent.Type);
            fluentComponent.Properties.Keys.ShouldBe(originalComponent.Properties.Keys);
            foreach (var property in originalComponent.Properties)
            {
                fluentComponent.Properties[property.Key].GetRawText()
                    .ShouldBe(property.Value.GetRawText());
            }
        }

        original.Links.Count.ShouldBe(1);
        fluent.Links.Count.ShouldBe(1);
        fluent.Links[0].ShouldBe(original.Links[0]);
        fluent.Links[0].ConditionExpression.ShouldBe("value > 0");
    }

    private static ApplicationDefinition BuildWithOriginalReturns()
    {
        var application = new ApplicationDefinitionBuilder();
        var infrastructure = application.AddResourceGroup("Infrastructure");
        var store = infrastructure.AddResource<StoreResource>(
            "Store",
            "sample.store",
            resource => resource.Set("Enabled", true));
        var workflow = application.AddWorkflow("Main");
        var source = workflow.AddComponent<SourceComponent>(
            "Source",
            "sample.source",
            component => component.UseResource("Store", store));
        var sink = workflow.AddComponent("Sink", "sample.sink");
        workflow.Connect(source.Output<int>("Output"), sink.Input<int>("Input"), "value > 0");
        return application.Build();
    }

    private static ApplicationDefinition BuildWithFluentCaptures()
    {
        var application = new ApplicationDefinitionBuilder();
        application
            .AddResourceGroup("Infrastructure", out var infrastructure)
            .AddWorkflow("Main", out var workflow);
        infrastructure.AddResource<StoreResource>(
            "Store",
            "sample.store",
            resource => resource.Set("Enabled", true),
            out var store);
        workflow
            .AddComponent<SourceComponent>(
                "Source",
                "sample.source",
                component => component.UseResource("Store", store),
                out var source)
            .AddComponent("Sink", "sample.sink", out var sink)
            .Connect(source.Output<int>("Output"), sink.Input<int>("Input"), "value > 0");
        return application.Build();
    }

    private sealed class ClockResource;
    private sealed class ExternalResource;
    private sealed class StoreResource;
    private sealed class SourceComponent;
    private sealed class TransformComponent;
    private sealed class WorkerComponent;
    private sealed class DistinctiveException(string message) : Exception(message);
}
