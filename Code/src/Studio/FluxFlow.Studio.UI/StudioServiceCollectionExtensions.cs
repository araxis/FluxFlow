using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Authoring.Definitions;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor.Services;

namespace FluxFlow.Studio.UI;

public static class StudioServiceCollectionExtensions
{
    public static IServiceCollection AddStudioUI(this IServiceCollection services, FluxFlow.Plugins.PluginDescription[]? plugins = null)
    {
        services.AddMudServices();
        plugins ??= [];
        services.AddCatalogComponents(plugins);
        services.TryAddScoped<DesignerGraphState>();
        services.TryAddScoped<Features.Designer.Components.Workspace.CanvasViewState>();
        services.TryAddScoped<Features.Observations.Validation.EditorValidationState>();
        services.TryAddScoped<StudioPreferences>();
        foreach (var connection in plugins.Where(p => p.StudioEntry is null && p.Design is null).SelectMany(p => p.Connections))
            services.AddSingleton(new Features.Connections.ConnectionDescription(connection.Type, connection.Title, connection.EndpointProperty,
                    () => System.Text.Json.Nodes.JsonNode.Parse(connection.Defaults.GetRawText())!.AsObject())
                    { Options = connection.Options, EndpointIsUri = connection.EndpointIsUri, RequiresWorkflowScope = connection.WorkflowOnly,
                        WorkflowResourcesAreLocal = connection.WorkflowResourcesAreLocal,
                        PropertyGroups = connection.Sections.Count == 0 ? null : _ => connection.Sections.Select(s => new Features.Connections.ConnectionPropertyGroup(s.Name, null, s.Options)).ToArray() });
        services.TryAddScoped<FluxFlow.Environments.IEnvironmentRunner, Features.Environments.UnavailableEnvironmentRunner>();
        services.TryAddScoped<FluxFlow.Environments.IRegistryAccess, Features.Environments.UnavailableRegistryAccess>();
        services.TryAddScoped<Features.Environments.StudioRunSession>();
        services.TryAddScoped<Features.Chat.Dictation.IDictationService, Features.Chat.Dictation.UnavailableDictationService>();
        return services;

    }
}
