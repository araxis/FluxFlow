using FluxFlow.Authoring.Evaluation;
using FluxFlow.Conversations;
using FluxFlow.Environments.Testcontainers;
using FluxFlow.Execution;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Studio.Desktop.Services;
using FluxFlow.Studio.UI;
using FluxFlow.Studio.UI.Features.Designer.Projects;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Studio.Desktop;

public static class DesktopStudioServices
{
    public static IServiceCollection AddDesktopStudio(this IServiceCollection services)
        => AddDesktopStudio(services, FluxFlow.Execution.Plugins.InstalledPlugins.Load());

    internal static IServiceCollection AddDesktopStudio(this IServiceCollection services, FluxFlow.Execution.Plugins.InstalledPlugins plugins)
    {
        services.AddWpfBlazorWebView();
        services.AddLocalFlowRunner(plugins).AddFlowContainers();
        services.AddStudioUI(plugins.Descriptions);
        foreach (var plugin in plugins.Descriptions.Where(p => p.StudioEntry is not null || p.Design is not null))
        {
            foreach (var name in plugin.ClientAssemblies)
            {
                using var dependency = File.OpenRead(plugins.StudioAssembly(plugin.Id, dependency: name)!);
                FluxFlow.Studio.UI.Features.Plugins.StudioPluginLoader.LoadClientAssembly(services, plugin, name, dependency, inProcess: true);
            }
            if (plugin.StudioEntry is null) continue;
            using var stream = File.OpenRead(plugins.StudioAssembly(plugin.Id)!);
            FluxFlow.Studio.UI.Features.Plugins.StudioPluginLoader.Configure(services, plugin, stream, companionInProcess: true);
        }
        services.AddJsonataExpressionEngine(useAsDefault: true, functions: plugins.Functions).AddCSharpExpressionEngine();
        services.AddScoped<IExpressionEvaluator, ExpressionEvaluator>();
        services.AddLocalConversations();
        services.AddSingleton<IDesktopFileDialogs, DesktopFileDialogs>();
        services.AddScoped<IStudioFileAccess, NativeStudioFileAccess>();
        services.AddSingleton<IStudioSettingsStorage>(_ => new NativeStudioSettingsStorage(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxFlow", "Studio", "settings.json")));
        return services;
    }
}
