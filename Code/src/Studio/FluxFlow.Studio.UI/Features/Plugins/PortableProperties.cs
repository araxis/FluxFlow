using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Plugins;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Studio.UI.Features.Plugins;

public static class PortableProperties
{
    public static void Configure(IServiceCollection services, PluginDescription plugin, IPluginDesign design, bool inProcess)
    {
        if (!inProcess) design.ConfigureRemoteServices(services);
        foreach (var component in plugin.Components)
        {
            var type = component.Palette.ComponentType;
            var rules = design.Component(component);
            if (rules.Validate is not null || rules.ValidationNote is not null) services.AddSingleton(new PluginSettingsValidator(type, rules));
            if (rules.Interactive is not null)
            {
                services.AddKeyedSingleton(type, rules);
                services.AddSingleton<IComponentConfigurator>(new ComponentConfigurator<InteractivePropertyConfigurator>(type,
                    RendersPropertyRows: true, ProvidesTools: true, ProvidesComponentJson: true));
                continue;
            }
            if (component.NamedOutputs is not null)
            {
                services.AddSingleton<IComponentConfigurator>(new ComponentConfigurator<NamedOutputs.NamedOutputConfigurator>(type, RendersPropertyRows: true));
                continue;
            }
            if (rules.Collection is { } collection)
            {
                services.AddKeyedSingleton(type, collection);
                services.AddSingleton<IComponentConfigurator>(new ComponentConfigurator<PortableCollectionConfigurator>(type, RendersPropertyRows: true));
                continue;
            }
            if (component.Editor != "properties") continue;
            services.AddKeyedSingleton(type, rules);
            services.AddSingleton<IComponentConfigurator>(new ComponentConfigurator<PortablePropertyConfigurator>(type,
                RendersPropertyRows: true, ProvidesComponentJson: true));
        }
        foreach (var schema in plugin.Connections)
        {
            var rules = design.Connection(schema);
            services.AddSingleton(new ConnectionDescription(schema.Type, schema.Title, schema.EndpointProperty,
                () => { var value = JsonNode.Parse(schema.Defaults.GetRawText())!.AsObject(); rules.Initialize?.Invoke(value); return value; })
            {
                Preview = rules.Preview, PluginId = plugin.Id, EndpointIsUri = schema.EndpointIsUri, RequiresWorkflowScope = schema.WorkflowOnly,
                WorkflowResourcesAreLocal = schema.WorkflowResourcesAreLocal,
                Options = schema.Options, Validate = value => Validate(rules, value),
                PropertyGroups = rules.Groups is { } groups ? settings => groups(settings).Select(g =>
                    new ConnectionPropertyGroup(g.Title, g.Path, g.Options) { Fields = g.Fields, StringMaps = g.StringMaps, AddBeforeEditing = g.AddBeforeEditing, ShowGroup = g.ShowGroup, IsVisible = g.IsVisible ?? rules.IsVisible, StringLists = g.StringLists
                        .Select(l => new ConnectionStringList(l.Name, l.Label, l.EntryLabel, l.Validate)).ToArray() }).ToArray()
                    : schema.Sections.Count == 0 ? null : _ => schema.Sections.Select(section =>
                    new ConnectionPropertyGroup(section.Name, null, section.Options) { IsVisible = rules.IsVisible }).ToArray()
            });
        }
        if (inProcess) return;
        foreach (var operation in plugin.Connections.SelectMany(c => c.Options.Concat(c.Sections.SelectMany(s => s.Options)))
            .Select(f => f.Suggestions?.Operation).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            var key = new SuggestionKey(plugin.Id, operation);
            services.AddKeyedScoped<Func<CancellationToken, Task<string[]>>>(key,
                (provider, _) => token => ListAsync(provider.GetRequiredService<HttpClient>(), key, token));
        }
    }

    internal static void Validate(PropertyRules rules, JsonObject value, bool component = false)
    {
        if (rules.Validate is null) return;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(component ? "{\"Workflows\":{\"main\":{\"component\":" + value.ToJsonString() + "}}}" : value.ToJsonString()));
        using var settings = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
        rules.Validate(component ? settings.GetSection("Workflows:main:component") : settings);
    }

    private static async Task<string[]> ListAsync(HttpClient client, SuggestionKey key, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"api/runs/plugins/{Uri.EscapeDataString(key.Plugin)}/suggestions/{Uri.EscapeDataString(key.Operation)}");
        request.Headers.Add("X-FluxFlow-Run", "1");
        using var response = await client.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(PluginJsonContext.Default.StringArray, cancellation) ?? [];
    }
}
