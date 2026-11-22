using System.Text;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Environments;
using FluxFlow.Expressions.Jsonata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Execution;

public sealed class WorkflowExecution(EnvironmentPreparation preparation, ILoggerFactory loggerFactory, ExecutionTraceCollector? traces = null, IEnumerable<Plugins.IWorkflowHostSupport>? hostSupport = null, Plugins.InstalledPlugins? plugins = null)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<WorkflowExecution>();
    public async Task<WorkflowRun> StartAsync(EnvironmentRunRequest request, CancellationToken cancellationToken, RunObservations? observations = null)
    {
        preparation.ValidateBindings(request);
        using var traceScope = observations is null ? null : traces?.Capture(observations.Traces);
        using var diagnostics = observations is null ? null : _logger.BeginScope(observations.Diagnostics);
        _logger.LogInformation("Validating application configuration.");
        // Registration validates the supplied application without activating its resources.
        using (var validation = ReadConfiguration(request.Configuration)) Register(new ServiceCollection(), validation, plugins);
        _logger.LogInformation("Preparing execution environment.");
        PreparedEnvironment environment;
        using (observations is null ? null : _logger.BeginScope(observations.Logs))
            environment = await preparation.PrepareAsync(request.Configuration, preparation.PreparationProfile(request), cancellationToken).ConfigureAwait(false);
        var instances = new List<WorkflowInstanceRun>();
        var run = new WorkflowRun(instances, environment, observations, traces, _logger);
        var definitions = request.Instances.Count == 0 ? [new ApplicationInstance("default", "Application")] : request.Instances;
        try
        {
            _logger.LogInformation("Execution environment is ready.");
            var configuration = environment.Configuration.ToJsonString();
            var identityProperties = plugins?.Descriptions.SelectMany(p => p.Connections)
                .Where(c => c.InstanceIdentityProperty != null).ToDictionary(c => c.Type, c => c.InstanceIdentityProperty!);
            var identities = preparation.Inspect(configuration).Where(r => identityProperties?.ContainsKey(r.Type) == true)
                .Select(r => (r.Path, Key: $"{r.Path}:{identityProperties![r.Type]}")).ToArray();
            foreach (var definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var identity = _logger.BeginScope(InstanceAttributes(definition));
                using var instanceTrace = observations is null ? null : traces?.Capture(observations.Traces, definition);
                var builder = new HostBuilder();
                // Own application disposal explicitly: its execution fault must not abort
                // disposal of the host's remaining resources.
                var application = new FlowApplication();
                var instanceConfiguration = ReadConfiguration(preparation.BindConfiguration(environment, definition));
                IHost? host = null;
                try
                {
                    // Isolate connection identities when several instances or aliases use one service.
                    foreach (var resource in identities.Where(r => definitions.Count > 1 || definition.Bindings.Any(binding =>
                        binding.Key.Equals(r.Path, StringComparison.OrdinalIgnoreCase) && !binding.Key.Equals(binding.Value, StringComparison.OrdinalIgnoreCase))))
                        instanceConfiguration[resource.Key] = $"{instanceConfiguration[resource.Key] ?? "flow"}-{Guid.NewGuid():N}";
                    builder.ConfigureAppConfiguration(config => config.AddConfiguration(instanceConfiguration, shouldDisposeConfiguration: true));
                    // Register listeners first so workflow shutdown can drain before the listener closes.
                    foreach (var module in plugins?.Modules ?? []) module.ConfigureWorkflowHost(builder, instanceConfiguration);
                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton(application);
                        // ASP.NET Core adds host keys such as applicationName and urls;
                        // only the validated workflow document belongs to application registration.
                        Register(services, instanceConfiguration, plugins);
                        foreach (var support in hostSupport ?? []) support.ConfigureInstance(services);
                        services.AddSingleton(loggerFactory);
                        if (observations is not null)
                            services.AddSingleton<IStartupFilter>(new RequestCapture(observations, definition, _logger, traces));
                        // Do not let an embedded workflow stop the companion process.
                        services.AddSingleton<IHostLifetime, EmbeddedLifetime>();
                    });
                    _logger.LogInformation("Starting workflow execution.");
                    using (observations is null ? null : _logger.BeginScope(observations.Logs))
                    {
                        host = builder.Build();
                        var instance = new WorkflowInstanceRun(host, application, definition, observations, traces, _logger);
                        instances.Add(instance);
                        if (observations is not null)
                        {
                            await host.Services.BuildApplicationAsync(cancellationToken).ConfigureAwait(false);
                            instance.OutputCapture = new(observations, _logger, definition.Id);
                            instance.OutputCapture.Attach(host.Services);
                        }
                        await host.StartAsync(cancellationToken).ConfigureAwait(false);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogInformation("Workflow execution started.");
                }
                finally
                {
                    // A built host owns the chained configuration through native disposal.
                    if (host is null) instanceConfiguration.Dispose();
                }
            }
            run.ObserveCompletion();
            return run;
        }
        catch (Exception original)
        {
            _logger.LogInformation("Workflow startup did not complete; cleaning up prepared resources.");
            try { await run.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Workflow startup and cleanup failed.", original, cleanup); }
            throw;
        }
    }

    internal static Dictionary<string, object?> InstanceAttributes(ApplicationInstance instance) => new()
    {
        ["flow.application.instance.id"] = instance.Id,
        ["flow.application.instance.name"] = instance.Name
    };

    internal static ConfigurationRoot ReadConfiguration(string json) => (ConfigurationRoot)new ConfigurationBuilder()
        .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();

    internal static void Register(IServiceCollection services, IConfiguration configuration, Plugins.InstalledPlugins? plugins = null) => RegisterComponents(services, plugins)
        .AddFluxFlowApplication(configuration).AddFluxFlow();

    internal static IServiceCollection RegisterComponents(IServiceCollection services, Plugins.InstalledPlugins? plugins = null)
    {
        foreach (var module in plugins?.Modules ?? []) module.ConfigureWorkflow(services);
        return services.AddJsonataExpressionEngine(functions: plugins?.Functions);
    }

    // Requests enter through Kestrel independently of the workflow startup scope.
    private sealed class RequestCapture(RunObservations observations, ApplicationInstance instance, ILogger logger,
        ExecutionTraceCollector? traces) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, invoke) =>
            {
                using var identity = logger.BeginScope(InstanceAttributes(instance));
                using var logs = logger.BeginScope(observations.Logs);
                using var trace = traces?.Capture(observations.Traces, instance);
                await invoke(context).ConfigureAwait(false);
            });
            next(app);
        };
    }

    private sealed class EmbeddedLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
