using System.Text.Json;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Sources.Composition;

public static class SourcesComponents
{
    public static ComponentContract<GeneratedSourceComponentBuilder, OutputComponentHandle<JsonElement>> GeneratedSource { get; } =
        DesignedComponentContract.Create(
            SourcesComponentDefinition.Types.Generated,
            SourcesServiceCollectionExtensions.ConfigureGenerated,
            static () => new GeneratedSourceComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new OutputComponentHandle<JsonElement>(component, SourcesComponentDefinition.Ports.Output, SourcesComponentDefinition.Ports.Events));

    public static ComponentContract<SequenceSourceComponentBuilder, OutputComponentHandle<SequenceItem>> SequenceSource { get; } =
        DesignedComponentContract.Create(
            SourcesComponentDefinition.Types.Sequence,
            SourcesServiceCollectionExtensions.ConfigureSequence,
            static () => new SequenceSourceComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new OutputComponentHandle<SequenceItem>(component, SourcesComponentDefinition.Ports.Output, SourcesComponentDefinition.Ports.Events));
}

public static class SourcesAuthoringExtensions
{
    public static OutputComponentHandle<JsonElement> AddGeneratedSource(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<GeneratedSourceComponentBuilder> configure)
        => workflow.AddComponent(name, SourcesComponents.GeneratedSource, configure);

    public static WorkflowDefinitionBuilder AddGeneratedSource(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<GeneratedSourceComponentBuilder> configure,
        out OutputComponentHandle<JsonElement> source)
    {
        source = workflow.AddGeneratedSource(name, configure);
        return workflow;
    }

    public static OutputComponentHandle<SequenceItem> AddSequenceSource(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SequenceSourceComponentBuilder> configure)
        => workflow.AddComponent(name, SourcesComponents.SequenceSource, configure);

    public static WorkflowDefinitionBuilder AddSequenceSource(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SequenceSourceComponentBuilder> configure,
        out OutputComponentHandle<SequenceItem> source)
    {
        source = workflow.AddSequenceSource(name, configure);
        return workflow;
    }

}

public abstract class SourceComponentBuilder
{
    public string? Name { get; set; }
    public int? InitialDelayMilliseconds { get; set; }
    public int? IntervalMilliseconds { get; set; }
    public int? BoundedCapacity { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    private protected void ApplyCommon(ComponentDefinitionBuilder definition)
    {
        Set(definition, SourcesComponentDefinition.Options.Name, Name);
        Set(definition, SourcesComponentDefinition.Options.InitialDelayMilliseconds, InitialDelayMilliseconds);
        Set(definition, SourcesComponentDefinition.Options.IntervalMilliseconds, IntervalMilliseconds);
        Set(definition, SourcesComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        if (Clock is not null)
            definition.UseResource(SourcesComponentDefinition.Resources.Clock, Clock);
    }

    private protected static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}

public sealed class GeneratedSourceComponentBuilder : SourceComponentBuilder
{
    public JsonElement? Items { get; set; }
    public bool? Loop { get; set; }
    public int? MaxItems { get; set; }

    public void SetItems<T>(T items)
        => Items = JsonSerializer.SerializeToElement(items);

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        Set(definition, SourcesComponentDefinition.Options.Items, Items);
        Set(definition, SourcesComponentDefinition.Options.Loop, Loop);
        Set(definition, SourcesComponentDefinition.Options.MaxItems, MaxItems);
    }
}

public sealed class SequenceSourceComponentBuilder : SourceComponentBuilder
{
    public long? Start { get; set; }
    public long? Step { get; set; }
    public int? Count { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        Set(definition, SourcesComponentDefinition.Options.Start, Start);
        Set(definition, SourcesComponentDefinition.Options.Step, Step);
        Set(definition, SourcesComponentDefinition.Options.Count, Count);
    }
}
