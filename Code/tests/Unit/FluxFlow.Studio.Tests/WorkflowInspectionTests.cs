using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Authoring.Definitions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class WorkflowInspectionTests
{
    private const string Configuration = """
        {"KeepCase":{"Host":true},"Application":{"Resources":{"broker":{"Type":"external"}},"Workflows":{
        "main":{"Resources":{"local":{"Type":"custom"}},"map":{"Type":"mapper","Engine":"csharp","Expression":"value",
          "UPPER":{"InnerCase":42},"Output":["other.sink.Input",{"Address":"other.sink.Input","Condition":"true","Engine":"jsonata"}],"Error":"other.sink.Input"}},
        "other":{"sink":{"Type":"batch","BatchSize":2,"Input":"main.map.Output"},
          "source":{"Type":"source","Values":"./unavailable-input.json","Output":"main.map.Input"},
          "unknown":{"Type":"custom","Output":"main.map.Input","Extra":[1,null,true]}}}}}
        """;

    [Fact]
    public void Discovery_uses_headless_DI_and_exposes_fresh_creation_defaults()
    {
        using var services = new ServiceCollection().AddCatalogComponents(StudioTestServices.ProcessingCatalog).BuildServiceProvider();
        var descriptions = services.GetServices<IComponentDescription>().ToArray();
        descriptions.Select(item => item.Palette.ComponentType).Distinct(StringComparer.Ordinal).Count().ShouldBe(descriptions.Length);
        descriptions.ShouldAllBe(item => item is FluxFlow.Plugins.PluginComponent || item.GetType().Assembly == typeof(DesignerConfiguration).Assembly);
        typeof(DesignerConfiguration).Assembly.GetReferencedAssemblies().ShouldNotContain(reference =>
            reference.Name == "MudBlazor" || reference.Name!.StartsWith("Microsoft.AspNetCore.Components") || reference.Name == "FluxFlow.Studio.UI");
        var mapper = descriptions.Single(item => item.Palette.ComponentType == "mapper");
        mapper.Inspector.Sections.SelectMany(section => section.Options).ShouldContain(option => option.Name == "Expression");
        mapper.Palette.Inputs.ShouldContain(port => port.Name == "Input");
        mapper.Palette.Outputs.ShouldContain(port => port.Name == "Output");
        var first = new Dictionary<string, JsonElement>();
        mapper.InitializeConfiguration(first);
        first["Engine"].GetString().ShouldBe("jsonata");
        first["Engine"] = JsonSerializer.SerializeToElement("other");
        var second = new Dictionary<string, JsonElement>();
        mapper.InitializeConfiguration(second);
        second["Engine"].GetString().ShouldBe("jsonata");
    }

    [Fact]
    public void Inspection_preserves_imported_settings_and_reports_known_cross_workflow_links()
    {
        using var provider = new ServiceCollection().AddCatalogComponents(StudioTestServices.ProcessingCatalog).BuildServiceProvider();
        var document = new DesignerConfiguration(Configuration, "Application");
        var before = document.ToJson();
        var inspection = document.InspectWorkflow("main", provider.GetServices<IComponentDescription>());
        inspection.Name.ShouldBe("main");
        var component = inspection.Components.Single();
        component.Presentation!.DisplayName.ShouldBe("Mapper");
        component.Inspector!.Sections.SelectMany(section => section.Options).ShouldContain(option => option.Name == "Engine");
        component.Configuration.Values["Engine"].GetString().ShouldBe("csharp");
        component.Configuration.Values["UPPER"].GetProperty("InnerCase").GetInt32().ShouldBe(42);
        inspection.Links.Count.ShouldBe(5);
        inspection.Links.Count(link => link.IsError).ShouldBe(1);
        inspection.Links.Count(link => link.IsInput).ShouldBe(1);
        inspection.Links.ShouldContain(link => link.Condition == "true" && link.Engine == "jsonata");
        inspection.Links.ShouldContain(link => link.Source.Value == "other.source.Output" && link.Target.Value == "main.map.Input");
        inspection.Diagnostics.Single().Address!.Value.ShouldBe("other.unknown");
        document.ToJson().ShouldBe(before);
    }

    [Fact]
    public void Returned_configuration_and_link_declarations_are_detached()
    {
        using var provider = new ServiceCollection().AddCatalogComponents(StudioTestServices.ProcessingCatalog).BuildServiceProvider();
        var document = new DesignerConfiguration(Configuration, "Application");
        var before = document.ToJson();
        var inspection = document.InspectWorkflow("main", provider.GetServices<IComponentDescription>());
        inspection.Components.Single().Configuration.Values.Clear();
        inspection.Links.First().Declaration.ReplaceWith(JsonValue.Create("changed.Input"));
        document.ToJson().ShouldBe(before);
        var again = document.InspectWorkflow("main", provider.GetServices<IComponentDescription>());
        again.Components.Single().Configuration.Values["Engine"].GetString().ShouldBe("csharp");
        again.Links.ShouldNotContain(link => link.Target.Value == "changed.Input");
    }

    [Fact]
    public void Unknown_components_remain_visible_and_custom_descriptions_use_the_same_DI_path()
    {
        var custom = new CustomDescription();
        using var provider = new ServiceCollection().AddCatalogComponents(StudioTestServices.ProcessingCatalog)
            .AddSingleton<IComponentDescription>(custom).BuildServiceProvider();
        var document = new DesignerConfiguration(Configuration, "Application");
        var unknown = document.InspectWorkflow("other", []).Components.Single(item => item.Configuration.Type == "custom");
        unknown.Presentation.ShouldBeNull();
        unknown.Inspector.ShouldBeNull();
        unknown.Configuration.Values["Extra"].GetArrayLength().ShouldBe(3);
        var inspection = document.InspectWorkflow("main", provider.GetServices<IComponentDescription>());
        inspection.Diagnostics.ShouldBeEmpty();
        inspection.Links.Count.ShouldBe(6);
        custom.DefaultCalls.ShouldBe(0);
        document.InspectWorkflow("other", provider.GetServices<IComponentDescription>())
            .Components.Single(item => item.Configuration.Type == "custom").Presentation!.DisplayName.ShouldBe("Configured custom");
        custom.DefaultCalls.ShouldBe(0);
    }

    [Fact]
    public void Inspection_uses_each_owners_configured_ports_including_incoming_links()
    {
        const string json = """
            {"Workflows":{
              "main":{"sink":{"Type":"batch","BatchSize":2}},
              "other":{
                "first":{"Type":"custom","Port":"First","First":"main.sink.Input"},
                "second":{"Type":"custom","Port":"Second","Second":"main.sink.Input"}}}}
            """;
        using var provider = new ServiceCollection().AddCatalogComponents(StudioTestServices.ProcessingCatalog)
            .AddSingleton<IComponentDescription, CustomDescription>().BuildServiceProvider();
        var document = new DesignerConfiguration(json);
        var before = document.ToJson();
        var inspection = document.InspectWorkflow("main", provider.GetServices<IComponentDescription>());
        inspection.Diagnostics.ShouldBeEmpty();
        inspection.Links.Select(link => link.Source.Value).ShouldBe(["other.first.First", "other.second.Second"]);
        inspection.Links.ShouldAllBe(link => link.Target.Value == "main.sink.Input");
        document.ToJson().ShouldBe(before);
    }

    [Fact]
    public void Inspection_does_not_change_Studio_history_or_pending_proposals()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(ConfigurationEditingTests.Original, "Application");
        graph.ResetHistory();
        graph.ApplyEdits(graph.PrepareEdits(ConfigurationEditingTests.Batch()));
        graph.Undo();
        var proposal = graph.PrepareEdits(ConfigurationEditingTests.Batch());
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        var document = new DesignerConfiguration(graph.ToJson(), graph.SectionPath);
        var inspection = document.InspectWorkflow("main", graph.Components);
        inspection.Components.Single().Configuration.Address.Value.ShouldBe("main.source");
        graph.ToProjectJson().ShouldBe(before);
        graph.Revision.ShouldBe(revision);
        graph.CanUndo.ShouldBeFalse();
        graph.CanRedo.ShouldBeTrue();
        graph.ApplyEdits(proposal).ShouldBeTrue();
    }

    [Fact]
    public void Missing_workflow_and_ambiguous_descriptions_fail_without_modifying_configuration()
    {
        var document = new DesignerConfiguration(Configuration, "Application");
        var before = document.ToJson();
        Should.Throw<JsonException>(() => document.InspectWorkflow("MAIN", []));
        Should.Throw<ArgumentException>(() => document.InspectWorkflow("main", [new CustomDescription(), new CustomDescription()]));
        document.ToJson().ShouldBe(before);
    }

    private sealed class CustomDescription : IComponentDescription
    {
        public int DefaultCalls { get; private set; }
        public PaletteItemModel Palette { get; } = new()
        {
            ComponentType = "custom", DisplayName = "Custom", Category = "Custom",
            Outputs = [new() { Name = "Output", Kind = PortKind.Output }]
        };
        public NodeInspectorModel Inspector { get; } = new() { ComponentType = "custom" };
        public void InitializeConfiguration(IDictionary<string, JsonElement> configuration) => DefaultCalls++;
        public PaletteItemModel GetPresentation(IReadOnlyDictionary<string, JsonElement> configuration) =>
            Palette with
            {
                DisplayName = configuration.ContainsKey("Extra") ? "Configured custom" : "Custom",
                Outputs = configuration.TryGetValue("Port", out var port)
                    ? [new() { Name = port.GetString()!, Kind = PortKind.Output }] : Palette.Outputs
            };
    }
}
