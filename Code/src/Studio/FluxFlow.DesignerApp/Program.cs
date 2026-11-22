using FluxFlow.DesignerApp;
using FluxFlow.DesignerApp.Services;
using FluxFlow.Studio.UI;
using FluxFlow.Studio.UI.Features.Designer.Projects;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using FluxFlow.Authoring.Evaluation;
using FluxFlow.Conversations;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

using var pluginClient = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress), Timeout = TimeSpan.FromSeconds(5) };
pluginClient.DefaultRequestHeaders.Add("X-FluxFlow-Run", "1");
FluxFlow.Plugins.PluginDescription[] plugins = [];
try
{
    plugins = await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync(pluginClient, "api/runs/plugins",
        FluxFlow.Plugins.PluginJsonContext.Default.PluginDescriptionArray) ?? [];
}
catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
{
    Console.Error.WriteLine($"Companion plugins unavailable: {error.Message}");
}
builder.Services.AddStudioUI(plugins);
foreach (var plugin in plugins.Where(p => p.StudioEntry is not null || p.Design is not null))
{
    var path = $"api/runs/plugins/{Uri.EscapeDataString(plugin.Id)}/studio";
    foreach (var name in plugin.ClientAssemblies)
    {
        using var dependency = new MemoryStream(await pluginClient.GetByteArrayAsync($"{path}?dependency={Uri.EscapeDataString(name)}"), writable: false);
        FluxFlow.Studio.UI.Features.Plugins.StudioPluginLoader.LoadClientAssembly(builder.Services, plugin, name, dependency, inProcess: false);
    }
    if (plugin.StudioEntry is null) continue;
    var bytes = await pluginClient.GetByteArrayAsync(path);
    using var stream = new MemoryStream(bytes, writable: false);
    FluxFlow.Studio.UI.Features.Plugins.StudioPluginLoader.Configure(builder.Services, plugin, stream);
}
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress), Timeout = Timeout.InfiniteTimeSpan });
builder.Services.AddScoped<FluxFlow.Plugins.Management.IPluginManager, HttpPluginManager>();
builder.Services.AddScoped<IExpressionEvaluator, HttpExpressionEvaluator>();
builder.Services.AddScoped<IProposalGenerator, HttpProposalGenerator>();
builder.Services.AddScoped<IProposalConnectionService, HttpProposalConnectionService>();
builder.Services.AddScoped<FluxFlow.Environments.IEnvironmentRunner, HttpEnvironmentRunner>();
builder.Services.AddScoped<FluxFlow.Environments.IRegistryAccess, HttpRegistryAccess>();

builder.Services.AddScoped<IStudioFileAccess, BrowserStudioFileAccess>();
builder.Services.AddScoped<IStudioSettingsStorage, BrowserStudioSettingsStorage>();
builder.Services.AddScoped<FluxFlow.Studio.UI.Features.Chat.Dictation.IDictationService, BrowserDictationService>();

await builder.Build().RunAsync();
