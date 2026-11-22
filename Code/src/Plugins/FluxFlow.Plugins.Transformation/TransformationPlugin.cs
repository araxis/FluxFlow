using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.Batch;
using FluxFlow.Authoring.Definitions.JsonataMapper;
using FluxFlow.Authoring.Definitions.Reducer;
using FluxFlow.Configuration.Components.Batching;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Reducing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.Transformation;

public sealed class TransformationPlugin : IFluxFlowPlugin
{
    public PluginDescription Description { get; } = new("FluxFlow.Plugins.Transformation",
        [Describe(new JsonataMapperDescription()) with { Preset = JsonataMapperDescription.Preset },
         Describe(new ReducerDescription()), Describe(new BatchDescription())], [])
    { Design = new(typeof(TransformationDesign).Assembly.GetName().Name!, typeof(TransformationDesign).FullName!, []) };

    public void ConfigureWorkflow(IServiceCollection services) => services.AddMapper().AddReducer().AddBatch();

    public void ConfigureHost(IServiceCollection services)
    {
        services.AddKeyedSingleton<PluginExpressionPreview>(ReducerDescription.ReducePreviewKey,
            (provider, _) => (expression, settings) => ReducerPreview.Compile(provider, false, expression, settings));
        services.AddKeyedSingleton<PluginExpressionPreview>(ReducerDescription.EqualityPreviewKey,
            (provider, _) => (expression, settings) => ReducerPreview.Compile(provider, true, expression, settings));
    }

    private static PluginComponent Describe(IComponentDescription description) =>
        new(description.Palette, description.Inspector, description.Documentation);
}
