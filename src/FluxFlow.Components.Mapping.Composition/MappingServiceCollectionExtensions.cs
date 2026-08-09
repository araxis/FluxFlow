using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Nodes;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Mapping.Composition;

public static class MappingServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddMapping(
        this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddDesignedComponent(MappingComponents.Mapper);
    }

    internal static void ConfigureMapper(ComponentRegistrationBuilder component)
    {
        component.UseProcessing(CompositionProcessingCapabilities.Sequential);
        component.WithDisplay(
            displayName: "Mapper",
            category: "Mapping",
            summary: "Maps schema-less JSON inputs and carries mapping failures as workflow errors.",
            iconKey: "map",
            preferredNodeName: "map",
            suggestedEditorWidth: 420);

        component
            .UseFactory(CreateJsonMapperNode)
            .HasInput(
                MappingComponentDefinition.Ports.Input,
                static node => node.Input,
                displayName: MappingComponentDefinition.Ports.Input,
                group: "Values",
                order: 0,
                summary: "Immutable value to map.",
                isPrimary: true)
            .HasOutput(
                MappingComponentDefinition.Ports.Output,
                static node => node.Output,
                displayName: MappingComponentDefinition.Ports.Output,
                group: "Results",
                order: 1,
                summary: "Mapped JSON value; mapping failures use the message error case.",
                isPrimary: true)
            .HasEvents(
                MappingComponentDefinition.Ports.Events,
                static node => node.Events,
                displayName: "Events",
                group: "Diagnostics",
                order: 2,
                summary: "Best-effort mapping diagnostics.");

        component.AddOption<string>(
            MappingComponentDefinition.Options.Expression,
            OptionValueKind.Expression,
            displayName: "Expression",
            helperText: "Expression evaluated for each input message.",
            isRequired: true,
            section: "Mapping",
            importance: OptionDesignMetadataAttributeValues.Primary,
            editor: OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: MappingComponentDefinition.Resources.Engine);
        component.AddOption<string>(
            MappingComponentDefinition.Options.ExpressionId,
            OptionValueKind.Text,
            displayName: "Expression ID",
            helperText: "Optional diagnostic identifier emitted with mapper diagnostics.",
            section: "Diagnostics",
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(
            MappingComponentDefinition.Options.ExpressionName,
            OptionValueKind.Text,
            displayName: "Expression Name",
            helperText: "Optional diagnostic name emitted with mapper diagnostics.",
            section: "Diagnostics",
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(
            MappingComponentDefinition.Options.InputType,
            OptionValueKind.Text,
            displayName: "Input Type",
            helperText: "Optional semantic type name for the JSON input.",
            defaultValue: MapperOptions.ObjectTypeName,
            section: "Type Metadata",
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(
            MappingComponentDefinition.Options.OutputType,
            OptionValueKind.Text,
            displayName: "Output Type",
            helperText: "Optional semantic type name for the mapped JSON value.",
            defaultValue: MapperOptions.ObjectTypeName,
            section: "Type Metadata",
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<int>(
            MappingComponentDefinition.Options.BoundedCapacity,
            OptionValueKind.Number,
            displayName: "Bounded Capacity",
            helperText: "Capacity used for bounded processing and reliable normal-data output.",
            defaultValue: 128,
            min: 1,
            section: "Runtime",
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Number);

        component.AddResource<IFlowExpressionEngine>(
            MappingComponentDefinition.Resources.Engine,
            displayName: "Engine",
            order: 0,
            summary: "Keyed expression engine service used to evaluate mapper expressions.",
            isRequired: true,
            designValueType: nameof(IFlowExpressionEngine),
            ownership: ResourceDesignMetadataAttributeValues.HostOwned,
            pickerKind: ResourceDesignMetadataAttributeValues.ExpressionEngine,
            keyPattern: "Resources.{name}");
        component.AddResource<IMappingContextFactory>(
            MappingComponentDefinition.Resources.ContextFactory,
            displayName: "Context Factory",
            order: 1,
            summary: "Optional keyed mapping context factory for custom expression variables.",
            designValueType: nameof(IMappingContextFactory),
            ownership: ResourceDesignMetadataAttributeValues.HostOwned,
            pickerKind: ResourceDesignMetadataAttributeValues.ContextFactory,
            keyPattern: "Resources.{name}");
        component.AddResource<TimeProvider>(
            MappingComponentDefinition.Resources.Clock,
            displayName: "Clock",
            order: 2,
            summary: "Optional keyed clock for deterministic mapper diagnostics.",
            designValueType: nameof(TimeProvider),
            ownership: ResourceDesignMetadataAttributeValues.HostOwned,
            pickerKind: ResourceDesignMetadataAttributeValues.Clock,
            keyPattern: "Resources.{name}");
    }

    private static JsonMapperNode CreateJsonMapperNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<MapperOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            MappingComponentDefinition.Resources.Engine);
        var contextFactory = context.GetResource<IMappingContextFactory>(
            MappingComponentDefinition.Resources.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            MappingComponentDefinition.Resources.Clock);
        return new JsonMapperNode(
            options,
            expressionEngine,
            contextFactory,
            clock);
    }

}
