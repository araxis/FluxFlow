using FluxFlow.Components.RateLimit;
using FluxFlow.Configuration.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.RateLimiting;

public static class TokenBucketRegistrationExtensions
{
    public static IServiceCollection AddTokenBucketResource(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ResourceRegistration>(TokenBucketConfiguration.ResourceType, (registrations, address, section) =>
        {
            var config = section.Get<TokenBucketConfiguration>() ?? throw new ArgumentException($"Missing limiter settings at '{section.Path}'.");
            registrations.AddTokenBucket(address.Value, config);
        });
        return services;
    }
}
