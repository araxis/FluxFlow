using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Core.Execution;
using FluxFlow.Core.Application;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Addressing;

namespace FluxFlow.Configuration.Links;

internal static class LinkRegistration
{
    internal static void RegisterPort(IServiceCollection services, IConfigurationSection value,
        ApplicationAddress declared, bool isInput,
        Func<IServiceProvider, ApplicationConditionCompiler> compiler)
    {
        var workflow = declared.WorkflowName!;
        var parsed = OneOrManyLinks.Read(value);
        foreach (var declaration in parsed)
        {
            var reference = ApplicationAddress.ResolvePort(declaration.Port, workflow);
            var source = isInput ? reference : declared;
            var target = isInput ? declared : reference;
            if (source.Kind is not (ApplicationAddressKind.WorkflowPort or ApplicationAddressKind.SystemPort)
                || target.Kind != ApplicationAddressKind.WorkflowPort)
                throw new JsonException($"Link '{source}' to '{target}' requires an output source and a workflow input target.");
            var signature = JsonSerializer.Serialize(declaration.Parameters?.OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, JsonElement>());
            var identity = new LinkKey(source.Value, target.Value, declaration.Condition, signature, declaration.Engine);
            var key = (Identity: identity, Delivery: declaration.Delivery, Fallback: declaration.Fallback);
            var existing = services.FirstOrDefault(service => service.IsKeyedService
                && service.ServiceType == typeof(LinkFactory)
                && service.ServiceKey is ValueTuple<LinkKey, FlowDeliveryPolicy, bool> registered
                && registered.Item1 == identity);
            if (existing is not null)
            {
                if (!Equals(existing.ServiceKey, key))
                    throw new JsonException($"Link '{source}' to '{target}' declares conflicting delivery or fallback policies.");
                continue;
            }
            LinkFactory factory = ports =>
            {
                if (declaration.Fallback)
                    ports.LinkFallback(source.Value, target.Value, declaration.Delivery);
                else
                {
                    var condition = declaration.Condition is null ? null : compiler(ports)
                        .Compile(declaration.Engine, declaration.Condition, typeof(FlowValue), declaration.Parameters);
                    ports.Link(source.Value, target.Value,
                        condition is null ? null : message => condition(FlowMapContext.ForMessage(message)),
                        declaration.Delivery);
                }
            };
            // The key also retains policy for conflict checks. Only the unkeyed alias executes.
            services.AddKeyedSingleton<LinkFactory>(key, factory);
            services.AddSingleton<LinkFactory>(provider => provider.GetRequiredKeyedService<LinkFactory>(key));
        }
    }

    private sealed record LinkKey(string Source, string Target, string? Condition, string Parameters, string? Engine);

}
