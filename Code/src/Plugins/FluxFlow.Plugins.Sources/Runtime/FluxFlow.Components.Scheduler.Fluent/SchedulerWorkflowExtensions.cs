using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Scheduler;

public static class SchedulerWorkflowExtensions
{
    private const int DefaultOutputCapacity = 128;

    public static WorkflowBuilder Every(this WorkflowBuilder workflow, string name, TimeSpan interval)
        => workflow.Every(name, interval, null);

    public static WorkflowBuilder Every(this WorkflowBuilder workflow, string name, TimeSpan interval, int? outputCapacity)
    {
        workflow.Services.AddSchedulerNode(workflow.Address(name), Schedules.Every(interval), outputCapacity ?? DefaultOutputCapacity);
        return workflow;
    }

    public static WorkflowBuilder At(this WorkflowBuilder workflow, string name, DateTimeOffset instant, int? outputCapacity = null)
    {
        workflow.Services.AddSchedulerNode(workflow.Address(name), Schedules.At(instant), outputCapacity ?? DefaultOutputCapacity);
        return workflow;
    }

    public static WorkflowBuilder Cron(this WorkflowBuilder workflow, string name, string expression, string? timeZone = null,
        bool? includeSeconds = null, int? outputCapacity = null)
    {
        var zone = timeZone is null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        workflow.Services.AddSchedulerNode(workflow.Address(name), Schedules.Cron(expression, zone, includeSeconds ?? false),
            outputCapacity ?? DefaultOutputCapacity);
        return workflow;
    }

    public static IServiceCollection AddSchedulerNode(this IServiceCollection services, ComponentAddress address,
        Func<DateTimeOffset, DateTimeOffset?> schedule, int outputCapacity = DefaultOutputCapacity)
        => services.AddComponentFactory(address, provider => new Scheduler(new SchedulerOptions
        {
            NextOccurrence = schedule,
            TimeProvider = provider.GetService<TimeProvider>() ?? TimeProvider.System,
            OutputCapacity = outputCapacity
        })).AddOutputPort<Scheduler>(address, "Output", node => node.Output)
           .AddEventPort<Scheduler>(address, node => node.Events);
}
