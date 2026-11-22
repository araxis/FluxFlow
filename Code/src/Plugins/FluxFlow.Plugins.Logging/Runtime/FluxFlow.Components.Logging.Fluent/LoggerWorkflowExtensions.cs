using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Components.Logging;

public static class LoggerWorkflowExtensions
{
    private const int DefaultOutputCapacity = 128;

    public static WorkflowBuilder Logger(this WorkflowBuilder workflow, string name, string? category = null)
        => workflow.Logger(name, category, null);

    public static WorkflowBuilder Logger(this WorkflowBuilder workflow, string name, string? category, int? outputCapacity)
        => workflow.Logger(name, category, outputCapacity, null);

    public static WorkflowBuilder Logger(this WorkflowBuilder workflow, string name, string? category, int? outputCapacity,
        string? logger)
    {
        workflow.Services.AddLoggerNode(workflow.Address(name), category, outputCapacity ?? DefaultOutputCapacity, logger);
        return workflow;
    }

    public static IServiceCollection AddLoggerNode(this IServiceCollection services, ComponentAddress address,
        string? category, int outputCapacity, string? logger)
    {
        if (category is not null) ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (logger is not null) ArgumentException.ThrowIfNullOrWhiteSpace(logger);
        return services.AddLoggerNode(address, provider =>
        {
            var factory = logger is null ? provider.GetRequiredService<ILoggerFactory>()
                : provider.GetRequiredFlowResource<ILoggerFactory>(logger, address.WorkflowName);
            return factory.CreateLogger(category ?? address.Value);
        }, outputCapacity);
    }

    public static IServiceCollection AddLoggerNode(this IServiceCollection services, ComponentAddress address,
        Func<IServiceProvider, ILogger> resolveLogger, int outputCapacity = DefaultOutputCapacity)
        => services.AddComponentFactory(address, provider => new Logger(new LoggerOptions
        {
            Logger = resolveLogger(provider), OutputCapacity = outputCapacity
        })).AddInputPort<Logger>(address, "Input", node => node.Input)
           .AddOutputPort<Logger>(address, "Output", node => node.Output)
           .AddEventPort<Logger>(address, node => node.Events);
}
