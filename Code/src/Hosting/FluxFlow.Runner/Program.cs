using FluxFlow.Execution;
using FluxFlow.Environments.Testcontainers;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrEmpty(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://127.0.0.1:5295");
builder.Services.AddLocalFlowRunner(FluxFlow.Execution.Plugins.InstalledPlugins.Load()).AddFlowContainers();
var app = builder.Build();
app.MapLocalFlowRunner();
await app.RunAsync();
