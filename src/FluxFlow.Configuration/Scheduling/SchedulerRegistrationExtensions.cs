using FluxFlow.Components.Scheduler;
using FluxFlow.Core.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Scheduling;

public static class SchedulerRegistrationExtensions
{
    /// <summary>Registers how to read a scheduler section, without creating a scheduler.</summary>
    public static IServiceCollection AddScheduler(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ComponentRegistration>("scheduler", Configure);
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadConfiguration<SchedulerConfiguration>()
            ?? throw new ArgumentException($"Missing scheduler configuration at '{section.Path}'.");
        var count = (config.Interval.HasValue ? 1 : 0)
            + (config.Cron is not null ? 1 : 0) + (config.At.HasValue ? 1 : 0);
        if (count != 1)
            throw new ArgumentException("Specify exactly one of Interval, Cron or At.");
        if (config.Cron is null && (config.TimeZone is not null || config.IncludeSeconds))
            throw new ArgumentException("TimeZone and IncludeSeconds apply only to Cron.");
        if (config.OutputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(config.OutputCapacity));

        Func<DateTimeOffset, DateTimeOffset?> next;
        if (config.Interval is { } interval)
            next = Schedules.Every(interval);
        else if (config.At is { } instant)
            next = Schedules.At(instant);
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(config.Cron);
            var zone = config.TimeZone is null ? TimeZoneInfo.Utc
                : TimeZoneInfo.FindSystemTimeZoneById(config.TimeZone);
            next = Schedules.Cron(config.Cron, zone, config.IncludeSeconds);
        }

        services.AddComponentFactory(address, provider =>
        {
            return new Scheduler(new SchedulerOptions
            {
                NextOccurrence = next,
                TimeProvider = provider.GetService<TimeProvider>() ?? TimeProvider.System,
                OutputCapacity = config.OutputCapacity
            });
        });
        services.AddOutputPort<Scheduler>(address, "Output", node => node.Output);
        services.AddEventPort<Scheduler>(address, node => node.Events);
    }
}
