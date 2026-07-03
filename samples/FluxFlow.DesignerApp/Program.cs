using FluxFlow.DesignerApp;
using FluxFlow.DesignerApp.Features.Designer;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

// The design-time metadata catalog is built once from the package-owned metadata
// providers. The renderer only reads it — it never owns resources or runtime.
builder.Services.AddSingleton<DesignerCatalog>();

// Canvas/editor state (the BlazorDiagram and current selection) is per-user UI state.
builder.Services.AddScoped<DesignerGraphState>();

await builder.Build().RunAsync();
