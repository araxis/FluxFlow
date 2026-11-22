using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.DataGeneration;
using FluxFlow.Configuration.Components.Scheduling;
using FluxFlow.Core.Nodes;
using FluxFlow.Hosting.Sample;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));
builder.Services.AddScheduler();
builder.Services.AddDataGenerator(builder.Environment.ContentRootPath);
builder.Services.AddKeyedSingleton<ComponentRegistration>("readingReporter", RegisterReporter);
// Select only the engine section; ordinary host settings use the same configuration system.
builder.Services.AddFluxFlowApplication(builder.Configuration.GetRequiredSection("FluxFlow"));
builder.Services.AddFluxFlow();

var host = builder.Build();
await using var hostOwner = (IAsyncDisposable)host;
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
var application = host.Services.GetRequiredService<FlowApplication>();
var verified = false;

// Discovery reads DI registrations, before components have been constructed.
foreach (var address in host.Services.GetComponentAddresses()) Console.WriteLine($"Component: {address}");
foreach (var address in host.Services.GetInputPortAddresses()) Console.WriteLine($"Input: {address}");
foreach (var address in host.Services.GetOutputPortAddresses()) Console.WriteLine($"Output: {address}");

try
{
    await host.StartAsync();
    using var observation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
    observation.CancelAfter(TimeSpan.FromSeconds(15));
    var main = host.Services.GetRequiredKeyedService<FlowWorkflow>("main");
    var background = host.Services.GetRequiredKeyedService<FlowWorkflow>("background");
    var originalMain = main.Services.GetRequiredKeyedService<IFlowNode>("main.report");
    var originalBackground = background.Services.GetRequiredKeyedService<IFlowNode>("background.report");
    await ObserveReadingsAsync("main", "Initial main output and events", observation.Token);
    await ObserveReadingsAsync("background", "Initial background output and events", observation.Token);

    Console.WriteLine("Stopping main. Background must continue processing.");
    await main.StopAsync(observation.Token);
    if (!main.Completion.IsCompletedSuccessfully || background.Completion.IsCompleted)
        throw new InvalidOperationException("Stopping main did not leave background running.");
    // Ignore a retained broadcast value: require readings produced after main stopped.
    await ObserveReadingsAsync("background", "Fresh output while main is stopped", observation.Token, DateTimeOffset.UtcNow);

    Console.WriteLine("Restarting main with unchanged configuration and attaching new probes.");
    await main.StartAsync(observation.Token);
    if (ReferenceEquals(originalMain, main.Services.GetRequiredKeyedService<IFlowNode>("main.report"))
        || !ReferenceEquals(originalBackground, background.Services.GetRequiredKeyedService<IFlowNode>("background.report")))
        throw new InvalidOperationException("Restart must replace main's components and preserve background's components.");
    await ObserveReadingsAsync("main", "Reattached to the new main run", observation.Token, DateTimeOffset.UtcNow);
    verified = true;
}
catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
{
    Console.WriteLine("Host shutdown requested.");
}
finally
{
    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await host.StopAsync(shutdown.Token);
}
if (verified)
{
    if (application.Workflows.Any(workflow => !workflow.Completion.IsCompletedSuccessfully))
        throw new InvalidOperationException("Host shutdown did not complete both workflows successfully.");
    Console.WriteLine("HOSTING_SAMPLE_VERIFIED: continued output, restart, probe reattachment and shutdown.");
}
Console.WriteLine("Host stopped. Workflow scopes are disposed; host disposal releases shared resources.");

async Task ObserveReadingsAsync(string workflow, string phase, CancellationToken cancellationToken, DateTimeOffset? after = null)
{
    var component = ComponentAddress.Create(workflow, "report");
    using var output = host.Services.ObserveOutput(component.Port("Output"), capacity: 8);
    using var events = host.Services.ObserveOutput(component.Port("Events"), capacity: 8);
    Console.WriteLine(phase);
    for (var index = 0; index < 3; index++)
    {
        // Read and serialize on the observer side, never on the business delivery path.
        var reading = await output.Messages.ReceiveAsync(cancellationToken);
        while (after is { } readingCutoff && reading.Timestamp <= readingCutoff)
            reading = await output.Messages.ReceiveAsync(cancellationToken);
        var receivedEvent = await events.Messages.ReceiveAsync(cancellationToken);
        while (after is { } eventCutoff && receivedEvent.Timestamp <= eventCutoff)
            receivedEvent = await events.Messages.ReceiveAsync(cancellationToken);
        Console.WriteLine($"{output.Address}: {JsonSerializer.Serialize(reading)}");
        Console.WriteLine($"{events.Address}: {JsonSerializer.Serialize(receivedEvent)}");
    }
    Console.WriteLine($"Probe drops: Output={output.DroppedCount}, Events={events.DroppedCount}");
}

static void RegisterReporter(IServiceCollection services, IConfigurationSection section)
{
    var address = section.GetComponentAddress();
    var configuration = section.ReadRequiredConfiguration<ReporterConfiguration>();
    services.AddComponentFactory(address, _ => new ReadingReporter())
        .AddInputPort<ReadingReporter>(address, "Input", node => node.Input)
        .AddOutputPort<ReadingReporter>(address, "Output", node => node.Output)
        .AddEventPort<ReadingReporter>(address, node => node.Events);
    services.AddLinks(address, configuration);
}

internal sealed record ReporterConfiguration : ComponentConfiguration;
