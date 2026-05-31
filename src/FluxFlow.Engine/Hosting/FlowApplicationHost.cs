using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using FluxFlow.Engine.Scenarios;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine;

/// <summary>
/// Protocol-neutral host that owns the lifecycle of one <see cref="ApplicationRuntime"/>.
/// Load a definition from <see cref="IConfiguration"/> or supply one directly, then
/// <see cref="StartAsync"/> to build and start the graph.
///
/// The host is intentionally free of any protocol-specific dependencies.
/// Applications add their own node factories by passing a pre-populated
/// <see cref="RuntimeNodeFactoryRegistry"/> to <see cref="Create(IConfiguration,RuntimeNodeFactoryRegistry)"/>.
/// </summary>
public sealed class FlowApplicationHost(
    IConfiguration? configuration,
    ApplicationRuntimeBuilder runtimeBuilder,
    FlowApplicationConfigurationLoader? configurationLoader = null,
    string sectionName = FlowApplicationConfigurationLoader.DefaultSectionName,
    ScenarioRunner? scenarioRunner = null,
    ApplicationDefinition? applicationDefinition = null)
    : IAsyncDisposable, IDisposable
{
    private readonly IConfiguration? _configuration = configuration;
    private readonly ApplicationRuntimeBuilder _runtimeBuilder = runtimeBuilder ?? throw new ArgumentNullException(nameof(runtimeBuilder));
    private readonly FlowApplicationConfigurationLoader _configurationLoader = configurationLoader ?? new FlowApplicationConfigurationLoader();
    private readonly ScenarioRunner _scenarioRunner = scenarioRunner ?? CreateDefaultScenarioRunner();
    private readonly ApplicationDefinition? _applicationDefinition = applicationDefinition;
    private readonly FlowFanoutSource<RuntimeFlowDiagnostic> _diagnostics = new();
    private ApplicationDefinition? _definition;
    private ApplicationRuntime? _runtime;
    private IDisposable? _runtimeDiagnosticsLink;
    private ITargetBlock<RuntimeFlowDiagnostic>? _runtimeDiagnosticsTarget;
    private bool _disposed;

    public FlowApplicationHostState State { get; private set; } = FlowApplicationHostState.Empty;
    public ApplicationDefinition? Definition => _definition;
    public ApplicationRuntime? Runtime => _runtime;
    public ISourceBlock<RuntimeFlowDiagnostic> Diagnostics => _diagnostics;
    public FlowApplicationHostBuildResult? LastBuildResult { get; private set; }
    public Exception? LastException { get; private set; }

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a host backed by <paramref name="configuration"/>.
    /// The caller provides a fully-configured <paramref name="registry"/> with all
    /// application-specific node factories registered.
    /// </summary>
    public static FlowApplicationHost Create(
        IConfiguration configuration,
        RuntimeNodeFactoryRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(registry);

        return new FlowApplicationHost(
            configuration,
            new ApplicationRuntimeBuilder(registry));
    }

    /// <summary>
    /// Creates a host from a pre-built <paramref name="definition"/>.
    /// The caller provides a fully-configured <paramref name="registry"/> with all
    /// application-specific node factories registered.
    /// </summary>
    public static FlowApplicationHost Create(
        ApplicationDefinition definition,
        RuntimeNodeFactoryRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(registry);

        return new FlowApplicationHost(
            null,
            new ApplicationRuntimeBuilder(registry),
            applicationDefinition: definition);
    }

    /// <summary>Returns a <see cref="ScenarioRunner"/> with the built-in generic step runners.</summary>
    public static ScenarioRunner CreateDefaultScenarioRunner()
        => new(CreateDefaultScenarioStepRunnerRegistry());

    /// <summary>
    /// Returns a registry containing the generic built-in step runners.
    /// Applications extend this by calling <see cref="ScenarioStepRunnerRegistry.Register"/> with
    /// their own protocol-specific step runners.
    /// </summary>
    public static ScenarioStepRunnerRegistry CreateDefaultScenarioStepRunnerRegistry()
        => new ScenarioStepRunnerRegistry()
            .Register(new ExpectEventScenarioStepRunner());

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public FlowApplicationHostBuildResult Build()
    {
        ThrowIfDisposed();
        DisposeRuntime();

        try
        {
            LastException = null;
            var definition = _applicationDefinition ?? LoadDefinitionFromConfiguration();
            _definition = definition;
            var runtimeBuild = _runtimeBuilder.Build(definition);

            if (runtimeBuild.IsSuccess)
            {
                _runtime = runtimeBuild.Runtime;
                AttachRuntimeDiagnostics(_runtime!);
                State = FlowApplicationHostState.Built;
            }
            else
            {
                State = FlowApplicationHostState.Empty;
            }

            LastBuildResult = FlowApplicationHostBuildResult.FromRuntime(runtimeBuild);
            return LastBuildResult;
        }
        catch (FlowApplicationConfigurationException exception)
        {
            State = FlowApplicationHostState.Empty;
            _definition = null;
            LastException = exception;
            LastBuildResult = FlowApplicationHostBuildResult.FromHostError(
                new FlowApplicationHostBuildError(
                    FlowApplicationHostBuildErrorCode.InvalidConfiguration,
                    exception.Message,
                    exception));

            return LastBuildResult;
        }
    }

    public FlowApplicationHostBuildResult Start()
        => StartAsync().GetAwaiter().GetResult();

    public async Task<FlowApplicationHostBuildResult> StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var result = Build();
        if (!result.IsSuccess) return result;

        return await StartBuiltAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<FlowApplicationHostBuildResult> StartBuiltAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_runtime is null || LastBuildResult is null || !LastBuildResult.IsSuccess)
        {
            var buildResult = Build();
            if (!buildResult.IsSuccess || _runtime is null)
                return buildResult;

            return await StartBuiltAsync(cancellationToken).ConfigureAwait(false);
        }

        var result = LastBuildResult;

        try
        {
            await _runtime!.StartAsync(cancellationToken).ConfigureAwait(false);
            State = FlowApplicationHostState.Running;
        }
        catch (OperationCanceledException)
        {
            State = FlowApplicationHostState.Stopped;
            LastException = null;
            await DisposeRuntimeAfterFailedStartAsync().ConfigureAwait(false);
            throw;
        }
        catch (ApplicationRuntimeNodeStartException exception)
        {
            State = FlowApplicationHostState.Faulted;
            LastException = exception.InnerException ?? exception;
            LastBuildResult = FlowApplicationHostBuildResult.FromHostError(
                new FlowApplicationHostBuildError(
                    FlowApplicationHostBuildErrorCode.StartFailed,
                    exception.Message,
                    exception,
                    exception.NodeAddress.Scope == WellKnownScopes.Resources ? null : exception.NodeAddress.Scope,
                    exception.NodeAddress.Node.Value));

            await DisposeRuntimeAfterFailedStartAsync().ConfigureAwait(false);
            return LastBuildResult;
        }
        catch (Exception exception)
        {
            State = FlowApplicationHostState.Faulted;
            LastException = exception;
            LastBuildResult = FlowApplicationHostBuildResult.FromHostError(
                new FlowApplicationHostBuildError(
                    FlowApplicationHostBuildErrorCode.StartFailed,
                    $"Flow application start failed: {exception.Message}",
                    exception));

            await DisposeRuntimeAfterFailedStartAsync().ConfigureAwait(false);
            return LastBuildResult;
        }

        return result;
    }

    // ── Scenarios ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a named scenario against the currently running runtime.
    /// Scenario step services are populated via <paramref name="servicesFactory"/>
    /// (defaults to <see cref="ScenarioStepServices.Empty"/> when not supplied).
    /// </summary>
    public async Task<ScenarioRunResult> RunScenarioAsync(
        string scenarioName,
        Func<ApplicationRuntime, ScenarioStepServices>? servicesFactory = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(scenarioName))
            throw new ArgumentException("Scenario name cannot be empty.", nameof(scenarioName));

        var definition = _definition ?? _applicationDefinition ?? LoadDefinitionFromConfiguration();
        if (!definition.Tests.TryGetValue(scenarioName, out _))
            throw new InvalidOperationException($"Scenario '{scenarioName}' does not exist.");

        if (_runtime is null || State != FlowApplicationHostState.Running)
            throw new InvalidOperationException(
                $"Scenario '{scenarioName}' cannot run because the app runtime is not running. " +
                $"Call StartAsync before running scenarios.");

        var services = servicesFactory?.Invoke(_runtime) ?? ScenarioStepServices.Empty;
        var scenario = definition.Tests[scenarioName];
        return await _scenarioRunner
            .RunAsync(scenarioName, scenario, _runtime.Events, services, cancellationToken)
            .ConfigureAwait(false);
    }

    // ── Stop / dispose ────────────────────────────────────────────────────────

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_runtime is null)
        {
            State = FlowApplicationHostState.Stopped;
            return;
        }

        try
        {
            _runtime.Complete();
            await _runtime.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
            State = FlowApplicationHostState.Stopped;
            LastException = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            State = FlowApplicationHostState.Faulted;
            LastException = exception;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            DisposeRuntime();
        }
        finally
        {
            _diagnostics.Complete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await DisposeRuntimeAsync().ConfigureAwait(false);
        }
        finally
        {
            _diagnostics.Complete();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void DisposeRuntime()
    {
        var runtime = _runtime;
        _runtime = null;
        try
        {
            runtime?.Dispose();
        }
        finally
        {
            DetachRuntimeDiagnostics();
        }
    }

    private async ValueTask DisposeRuntimeAsync()
    {
        var runtime = _runtime;
        _runtime = null;
        try
        {
            if (runtime is not null)
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            DetachRuntimeDiagnostics();
        }
    }

    private void AttachRuntimeDiagnostics(ApplicationRuntime runtime)
    {
        DetachRuntimeDiagnostics();
        _runtimeDiagnosticsTarget = new ActionBlock<RuntimeFlowDiagnostic>(
            diagnostic => _diagnostics.Post(diagnostic));
        _runtimeDiagnosticsLink = runtime.Diagnostics.LinkTo(
            _runtimeDiagnosticsTarget,
            new DataflowLinkOptions { PropagateCompletion = true });
    }

    private void DetachRuntimeDiagnostics()
    {
        _runtimeDiagnosticsLink?.Dispose();
        _runtimeDiagnosticsTarget?.Complete();
        _runtimeDiagnosticsLink = null;
        _runtimeDiagnosticsTarget = null;
    }

    private async ValueTask DisposeRuntimeAfterFailedStartAsync()
    {
        try
        {
            await DisposeRuntimeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppendLastBuildError(new FlowApplicationHostBuildError(
                FlowApplicationHostBuildErrorCode.StartFailed,
                $"Flow application start cleanup failed: {exception.Message}",
                exception));
        }
    }

    private void AppendLastBuildError(FlowApplicationHostBuildError error)
    {
        LastBuildResult = LastBuildResult is null
            ? FlowApplicationHostBuildResult.FromHostError(error)
            : LastBuildResult with { Errors = LastBuildResult.Errors.Concat([error]).ToArray() };
    }

    private ApplicationDefinition LoadDefinitionFromConfiguration()
    {
        if (_configuration is null)
            throw new InvalidOperationException("A flow application configuration was not provided.");

        return _configurationLoader.Load(_configuration, sectionName);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
