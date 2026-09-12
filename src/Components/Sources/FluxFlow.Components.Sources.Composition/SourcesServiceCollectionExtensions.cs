using System.Text.Json;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Sources.Composition;

public static class SourcesServiceCollectionExtensions
{
    private const string GeneratedItemsConfigurationName = "items";

    public static FluxFlowRegistrationBuilder AddSources(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(SourcesComponents.GeneratedSource)
            .AddComponent(SourcesComponents.SequenceSource);
    }

    internal static void ConfigureGenerated(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<TimeProvider>(SourcesComponentDefinition.Resources.Clock);
        component.AddOption<string>(SourcesComponentDefinition.Options.Name);
        component.AddOption<JsonElement>(SourcesComponentDefinition.Options.Items);
        component.AddOption<bool>(SourcesComponentDefinition.Options.Loop);
        component.AddOption<int?>(SourcesComponentDefinition.Options.MaxItems);
        component.AddOption<int>(SourcesComponentDefinition.Options.InitialDelayMilliseconds);
        component.AddOption<int>(SourcesComponentDefinition.Options.IntervalMilliseconds);
        component.AddOption<int>(SourcesComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateGeneratedSourceNode)
            .HasOutput(SourcesComponentDefinition.Ports.Output, static node => node.Output)
            .HasEvents(SourcesComponentDefinition.Ports.Events, static node => node.Events);
    }

    internal static void ConfigureSequence(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<TimeProvider>(SourcesComponentDefinition.Resources.Clock);
        component.AddOption<string>(SourcesComponentDefinition.Options.Name);
        component.AddOption<long>(SourcesComponentDefinition.Options.Start);
        component.AddOption<long>(SourcesComponentDefinition.Options.Step);
        component.AddOption<int>(SourcesComponentDefinition.Options.Count);
        component.AddOption<int>(SourcesComponentDefinition.Options.InitialDelayMilliseconds);
        component.AddOption<int>(SourcesComponentDefinition.Options.IntervalMilliseconds);
        component.AddOption<int>(SourcesComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateSequenceSourceNode)
            .HasOutput(SourcesComponentDefinition.Ports.Output, static node => node.Output)
            .HasEvents(SourcesComponentDefinition.Ports.Events, static node => node.Events);
    }

    private static GeneratedSourceNode CreateGeneratedSourceNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<GeneratedSourceOptions>();
        var items = context.GetConfigurationValue<JsonElement>(GeneratedItemsConfigurationName);
        var clock = context.GetResource<TimeProvider>(
            SourcesComponentDefinition.Resources.Clock);
        return GeneratedSourceNode.Create(options, items, clock);
    }

    private static SequenceSourceNode CreateSequenceSourceNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SequenceSourceOptions>();
        var clock = context.GetResource<TimeProvider>(
            SourcesComponentDefinition.Resources.Clock);
        return new SequenceSourceNode(options, clock);
    }
}
