using System.Text;
using System.Runtime.ExceptionServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Environments.Testcontainers;

public abstract class ContainerProvider : IEnvironmentProvider
{
    public abstract string Name { get; }
    public abstract string ResourceType { get; }
    protected abstract string DefaultImage { get; }
    protected abstract int Port { get; }
    protected virtual string Scheme => "http";

    public virtual void Validate(ResourceBinding binding)
    {
        binding.ValidateSnapshot();
        if (binding.Image is not null && string.IsNullOrWhiteSpace(binding.Image)) throw new ArgumentException("An image name is required.");
        if (binding.Files is null || binding.Variables is null || binding.BrokerAccess is null || binding.BrokerAccess.Values.Any(item => item is null))
            throw new ArgumentException("Files, Variables and BrokerAccess must be objects with non-null entries.");
        foreach (var (path, content) in binding.Files)
            if (!path.StartsWith('/') || path.EndsWith('/') || path.Contains('\\') || path.Contains("//", StringComparison.Ordinal) || path.Split('/').Any(part => part is "." or "..") || content is null)
                throw new ArgumentException($"Startup file '{path}' must use an absolute, normalized container file path.");
        foreach (var (key, value) in binding.Variables)
            if (string.IsNullOrWhiteSpace(key) || key.Contains('=') || value is null) throw new ArgumentException("Invalid environment variable.");
    }

    protected virtual ContainerBuilder Configure(ContainerBuilder builder, ResourceBinding binding) => builder;
    protected virtual Task InitializeAsync(Uri endpoint, ResourceBinding binding, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<IEnvironmentService> StartAsync(ResourceBinding binding, CancellationToken cancellationToken)
    {
        Validate(binding);
        var builder = new ContainerBuilder(binding.Image ?? DefaultImage)
            .WithPortBinding(Port, true)
            .WithCreateParameterModifier(parameters =>
            {
                foreach (var bindings in parameters.HostConfig!.PortBindings.Values)
                    foreach (var port in bindings) port.HostIP = "127.0.0.1";
            })
            .WithLabel("fluxflow.environment", Name)
            .WithImagePullPolicy(binding.PullIfMissing ? PullPolicy.Missing : PullPolicy.Never)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(Port));
        builder = Configure(builder, binding);
        foreach (var (path, content) in binding.Files) builder = builder.WithResourceMapping(Encoding.UTF8.GetBytes(content), path);
        foreach (var (key, value) in binding.Variables) builder = builder.WithEnvironment(key, value);
        var container = builder.Build();
        try
        {
            await container.StartAsync(cancellationToken).ConfigureAwait(false);
            var endpoint = new UriBuilder(Scheme, container.Hostname, container.GetMappedPublicPort(Port)).Uri;
            await InitializeAsync(endpoint, binding, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new Service(container, endpoint);
        }
        catch (Exception original)
        {
            Exception failure = original;
            // A process can exit before Testcontainers finishes mapping its ports.
            // That initial wait reports a timeout instead of its usual exit diagnostics.
            if (original is TimeoutException && container.State == TestcontainersStates.Exited && !cancellationToken.IsCancellationRequested)
            {
                using var diagnostics = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var (stdout, stderr) = await container.GetLogsAsync(ct: diagnostics.Token).ConfigureAwait(false);
                    var exitCode = await container.GetExitCodeAsync(diagnostics.Token).ConfigureAwait(false);
                    failure = new ContainerNotRunningException(container.Id, stdout, stderr, exitCode, original);
                }
                catch (Exception diagnosticFailure)
                {
                    failure = new AggregateException("Container startup and diagnostic retrieval failed.", original, diagnosticFailure);
                }
            }
            try { await container.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Container startup and cleanup failed.", failure, cleanup); }
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    private sealed class Service(IContainer container, Uri endpoint) : IEnvironmentService
    {
        public Uri Endpoint => endpoint;
        public string ContainerId => container.Id;
        public ValueTask DisposeAsync() => container.DisposeAsync();
    }
}

public static class ContainerRegistration
{
    public static IServiceCollection AddFlowContainers(this IServiceCollection services) => services
        .AddSingleton<Func<ContainerServiceDefinition, IEnvironmentProvider>>(d => new ConfiguredContainerProvider(d))
        .AddSingleton<IContainerRuntimeCheck, DockerRuntimeCheck>();
}
