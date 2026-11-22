using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Expressions;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FluxFlow.Application;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Application.Addressing;

namespace FluxFlow.Configuration.Application.Links;

/// <summary>Registers deferred links from a component's bound configuration.</summary>
public static class LinkRegistrationExtensions
{
    /// <summary>Registers standard links, including Input when present in the configuration contract.</summary>
    public static IServiceCollection AddLinks(this IServiceCollection services,
        ComponentAddress address, OutputComponentConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration is ComponentConfiguration component)
            services.AddInputLinks(address.Port("Input"), component.Input);
        services.AddOutputLinks(address.Port("Output"), configuration.Output);
        services.AddOutputLinks(address.Port("Events"), configuration.Events);
        services.AddErrorLinks(address.Port("Output"), configuration.Error);
        return services;
    }

    /// <summary>Registers one deferred connection from an output to an input, without a collection.</summary>
    public static IServiceCollection AddLink(this IServiceCollection services, PortAddress source, PortAddress target)
        => RegisterLink(services, source, target, condition: null, engine: null, errorsOnly: false);

    /// <summary>Connects one configured source to this component's input.</summary>
    public static IServiceCollection AddInputLink(this IServiceCollection services,
        PortAddress input, PortReference link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return RegisterLink(services, source: link.Address, target: input,
            link.Condition, link.Engine, errorsOnly: false);
    }

    /// <summary>Connects this component's output to one configured destination.</summary>
    public static IServiceCollection AddOutputLink(this IServiceCollection services,
        PortAddress output, PortReference link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return RegisterLink(services, source: output, target: link.Address,
            link.Condition, link.Engine, errorsOnly: false);
    }

    /// <summary>Connects error messages from this output to one configured destination.</summary>
    public static IServiceCollection AddErrorLink(this IServiceCollection services,
        PortAddress output, PortReference link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return RegisterLink(services, source: output, target: link.Address,
            link.Condition, link.Engine, errorsOnly: true);
    }

    /// <summary>Connects configured sources to this component's named input.</summary>
    public static IServiceCollection AddInputLinks(this IServiceCollection services,
        PortAddress input, OneOrManyPortReferences links)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(input);
        foreach (var link in links) services.AddInputLink(input, link);
        return services;
    }

    /// <summary>Connects this component's named output to configured destinations.</summary>
    public static IServiceCollection AddOutputLinks(this IServiceCollection services,
        PortAddress output, OneOrManyPortReferences links)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(output);
        foreach (var link in links) services.AddOutputLink(output, link);
        return services;
    }

    /// <summary>Connects only error messages from the supplied output; creates no Error port.</summary>
    public static IServiceCollection AddErrorLinks(this IServiceCollection services,
        PortAddress output, OneOrManyPortReferences links)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(output);
        foreach (var link in links) services.AddErrorLink(output, link);
        return services;
    }

    private static IServiceCollection RegisterLink(IServiceCollection services, PortAddress source, PortAddress target,
        string? condition, string? engine, bool errorsOnly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        // System ports are output-only; native DI checks other port directions at Build.
        if (target.IsSystem)
            throw new JsonException($"System output '{target}' cannot be a link destination.");
        var key = new LinkKey(source, target, condition, engine, errorsOnly);
        if (services.Any(service => service.IsKeyedService
            && service.ServiceType == typeof(LinkFactory)
            && Equals(service.ServiceKey, key)))
            return services;
        if (condition is not null)
            // DI owns reuse per provider; Lazy also retains compilation failures.
            services.TryAddKeyedSingleton<Lazy<Func<FlowMapContext, bool>>>((engine, condition),
                (provider, _) => new(() => CompileCondition(provider, engine, condition)));
        LinkFactory factory = ports =>
        {
            var predicate = condition is null ? null : ports
                .GetRequiredKeyedService<Lazy<Func<FlowMapContext, bool>>>((engine, condition)).Value;
            ports.Link(source,target,
                predicate is null && !errorsOnly ? null : message =>
                    (!errorsOnly || message.IsError) && (predicate is null || predicate(FlowMapContext.ForMessage(message))));
        };
        // Only the unkeyed alias executes; the keyed registration identifies duplicates.
        services.AddKeyedSingleton<LinkFactory>(key, factory);
        services.AddSingleton<LinkFactory>(provider => provider.GetRequiredKeyedService<LinkFactory>(key));
        return services;
    }

    private static Func<FlowMapContext, bool> CompileCondition(IServiceProvider services,
        string? engineName, string expression)
    {
        return services.GetExpressionEngine(engineName).CompileExpression<bool>(expression, new Dictionary<string, Type>
        {
            ["input"] = typeof(FlowValue), ["payload"] = typeof(FlowValue), ["message"] = typeof(FlowMessage)
        });
    }

    private sealed record LinkKey(PortAddress Source, PortAddress Target, string? Condition, string? Engine, bool ErrorsOnly);
}
