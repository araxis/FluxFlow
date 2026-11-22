using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.DataGeneration;

public static class DataGeneratorWorkflowExtensions
{
    public static WorkflowBuilder GenerateData(this WorkflowBuilder workflow, string name, string expression,
        string? locale = null, int? outputCapacity = null)
    {
        workflow.Services.AddDataGeneratorNode(workflow.Address(name), () => expression, locale ?? "en", outputCapacity ?? 128);
        return workflow;
    }

    public static IServiceCollection AddDataGeneratorNode(this IServiceCollection services, ComponentAddress address,
        Func<string> readExpression, string locale, int outputCapacity)
        => services.AddComponentFactory(address, _ =>
        {
            try
            {
                return new DataGenerator(new DataGeneratorOptions
                {
                    Expression = readExpression(), Locale = locale, OutputCapacity = outputCapacity
                });
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Data generator '{address}' could not be built: {error.Message}", error);
            }
        }).AddInputPort<DataGenerator>(address, "Input", node => node.Input)
          .AddOutputPort<DataGenerator>(address, "Output", node => node.Output)
          .AddEventPort<DataGenerator>(address, node => node.Events);
}
