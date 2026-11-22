using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.DataGenerator;
using FluxFlow.Authoring.Definitions.Scheduler;
using FluxFlow.Authoring.Definitions.Source;
using FluxFlow.Configuration.Components.DataGeneration;
using FluxFlow.Configuration.Components.Scheduling;
using FluxFlow.Configuration.Components.Sources;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Components.DataGeneration;
using FluxFlow.Core.Messaging;
using System.Text.Json;

namespace FluxFlow.Plugins.Sources;

public sealed class SourcesPlugin : IFluxFlowPlugin
{
    public PluginDescription Description { get; } = new("FluxFlow.Plugins.Sources",
        [Describe(new SchedulerDescription()), Describe(new SourceDescription()), Describe(new DataGeneratorDescription())], [])
    {
        Design = new(typeof(SourcesDesign).Assembly.GetName().Name!, typeof(SourcesDesign).FullName!, ["Bogus", "Cronos"])
    };

    public void ConfigureWorkflow(IServiceCollection services) => services.AddScheduler().AddSource().AddDataGenerator();

    public void ConfigureHost(IServiceCollection services) => services.AddKeyedSingleton<PluginExpressionPreview>(
        DataGeneratorDescription.PreviewKey, (expression, settings) =>
        {
            var locale = settings.FirstOrDefault(item => item.Key.Equals("Locale", StringComparison.OrdinalIgnoreCase)).Value;
            var evaluator = new GeneratorExpression(expression, locale.ValueKind == JsonValueKind.Undefined ? "en"
                : locale.ValueKind == JsonValueKind.String ? locale.GetString()! : throw new ArgumentException("Locale must be text."));
            return sample =>
            {
                var message = sample.Deserialize(FlowJsonContext.Default.FlowMessage)!;
                // Like normal processing, upstream errors bypass generation.
                return (message.IsError ? message.Value : evaluator.Evaluate(message)).Json.Clone();
            };
        });

    private static PluginComponent Describe(IComponentDescription component) =>
        new(component.Palette, component.Inspector, component.Documentation, "properties");
}
