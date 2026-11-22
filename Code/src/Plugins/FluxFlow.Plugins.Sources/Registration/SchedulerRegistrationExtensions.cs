using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Components.Scheduler;
using FluxFlow.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Scheduling;

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
        var config = section.ReadRequiredConfiguration<SchedulerConfiguration>();
        if (section.GetSection("Input").Exists())
            throw new ArgumentException($"Scheduler '{address}' has no Input port.");
        config.Validate();

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

        services.AddSchedulerNode(address, next, config.OutputCapacity);

        services.AddLinks(address, config);
    }
}
