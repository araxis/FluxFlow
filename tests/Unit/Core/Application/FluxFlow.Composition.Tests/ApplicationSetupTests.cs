using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ApplicationSetupTests
{
    [Fact]
    public void Setup_snapshots_live_collections_and_preserves_contract_identity()
    {
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("main");
        var source = workflow.AddComponent("source", "source");
        var target = workflow.AddComponent("target", "target");
        source.Output<int>("Output").LinkTo(target.Input<int>("Input"), value => value > 0);
        var authored = builder.Build();
        var links = authored.Links.ToList();
        var descriptor = new ComponentDescriptor("source", _ =>
            throw new InvalidOperationException("Construction must not activate components."));
        var descriptors = new List<ComponentDescriptor> { descriptor };

        var setup = new ApplicationSetup(authored.Definition, links, descriptors);
        links.Clear();
        descriptors.Clear();

        setup.Definition.ShouldBeSameAs(authored.Definition);
        setup.Links.ShouldHaveSingleItem().ShouldBeSameAs(authored.Links[0]);
        setup.ComponentDescriptors.ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
        setup.ResourceContracts.ShouldBeEmpty();
    }

    [Fact]
    public void Setup_rejects_missing_documents_and_null_runtime_entries()
    {
        Should.Throw<ArgumentNullException>(() => new ApplicationSetup(null!)).ParamName.ShouldBe("definition");
        var document = new ApplicationDefinition();
        Should.Throw<ArgumentException>(() => new ApplicationSetup(document, links: new ApplicationLinkDefinition[] { null! }));
        Should.Throw<ArgumentException>(() => new ApplicationSetup(document, componentDescriptors: new ComponentDescriptor[] { null! }));
        Should.Throw<ArgumentException>(() => new ApplicationSetup(document, resourceContracts: new ApplicationResourceContract[] { null! }));
    }

    [Fact]
    public void Runtime_activation_contracts_cannot_be_silently_exported()
    {
        var descriptor = new ComponentDescriptor("source", _ =>
            throw new InvalidOperationException("Export must not activate components."));
        var setup = new ApplicationSetup(new ApplicationDefinition(), componentDescriptors: [descriptor]);

        var error = Should.Throw<NotSupportedException>(() => setup.ToDefinition());

        error.Message.ShouldContain("Runtime activation contracts");
        setup.ComponentDescriptors.ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
    }
}
