using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Model;
using FluxFlow.Composition.Revisions;
using FluxFlow.Data;
using Microsoft.Extensions.Options;

namespace FluxFlow.Composition.Hosting;

public sealed class ApplicationRevisionHost : IApplicationRevisionHost, IAsyncDisposable
{
    private readonly IApplicationDefinitionSource _definitionSource;
    private readonly IApplicationRevisionCandidateFactory _candidateFactory;
    private readonly IApplicationRevisionEventSink? _eventSink;
    private readonly IOptions<ApplicationRevisionHostingOptions> _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile ApplicationRevisionCoordinator? _coordinator;
    private volatile ApplicationRevisionLoadResult? _lastLoad;
    private volatile ApplicationRevisionUpdateResult? _lastUpdate;
    private volatile ApplicationRevisionHostState _state = ApplicationRevisionHostState.Empty;
    private bool _hasActiveApplication;
    private bool _stopped;
    private int _disposed;

    public ApplicationRevisionHost(
        IApplicationDefinitionSource definitionSource,
        IApplicationRevisionCandidateFactory candidateFactory,
        IOptions<ApplicationRevisionHostingOptions> options,
        IApplicationRevisionEventSink? eventSink = null)
    {
        _definitionSource = definitionSource ?? throw new ArgumentNullException(nameof(definitionSource));
        _candidateFactory = candidateFactory ?? throw new ArgumentNullException(nameof(candidateFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _eventSink = eventSink;
    }

    public ApplicationRevisionHostState State => _state;

    public ApplicationDefinition? CurrentDefinition => _coordinator?.CurrentDefinition;

    public ApplicationRevisionSnapshot? Current => _coordinator?.Current;

    public ApplicationRevisionLoadResult? LastLoad => _lastLoad;

    public ApplicationRevisionUpdateResult? LastUpdate => _lastUpdate;

    public async ValueTask<ApplicationRevisionLoadResult> StartApplicationAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            if (_state == ApplicationRevisionHostState.Running && _lastUpdate is not null)
                return _lastLoad = ApplicationRevisionLoadResult.FromUpdate(_lastUpdate);

            var revisionId = ValidateRevisionId(
                _options.Value.InitialRevisionId,
                nameof(ApplicationRevisionHostingOptions.InitialRevisionId));
            _state = ApplicationRevisionHostState.Starting;
            return await LoadAndApplyCoreAsync(revisionId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<ApplicationRevisionLoadResult> ReloadAsync(
        string revisionId,
        CancellationToken cancellationToken = default)
    {
        revisionId = ValidateRevisionId(revisionId, nameof(revisionId));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            return await LoadAndApplyCoreAsync(revisionId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<ApplicationRevisionUpdateResult> ApplyAsync(
        string revisionId,
        ApplicationDefinition definition,
        CancellationToken cancellationToken = default)
    {
        revisionId = ValidateRevisionId(revisionId, nameof(revisionId));
        ArgumentNullException.ThrowIfNull(definition);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            var result = await GetOrCreateCoordinator()
                .ApplyAsync(revisionId, definition, cancellationToken)
                .ConfigureAwait(false);
            RecordUpdate(result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask StopApplicationAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_stopped)
                return;

            cancellationToken.ThrowIfCancellationRequested();
            _stopped = true;
            _hasActiveApplication = false;
            _state = ApplicationRevisionHostState.Stopped;
            var coordinator = Interlocked.Exchange(ref _coordinator, null);
            if (coordinator is not null)
                await coordinator.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _stopped = true;
            _hasActiveApplication = false;
            _state = ApplicationRevisionHostState.Disposed;
            var coordinator = Interlocked.Exchange(ref _coordinator, null);
            if (coordinator is not null)
                await coordinator.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<ApplicationRevisionLoadResult> LoadAndApplyCoreAsync(
        string revisionId,
        CancellationToken cancellationToken)
    {
        ApplicationDefinition definition;
        try
        {
            definition = await _definitionSource.LoadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The application definition source returned null.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var result = ApplicationRevisionLoadResult.FromError(SourceFailure(exception));
            _lastLoad = result;
            _state = _hasActiveApplication
                ? ApplicationRevisionHostState.Running
                : ApplicationRevisionHostState.Degraded;
            return result;
        }

        var update = await GetOrCreateCoordinator()
            .ApplyAsync(revisionId, definition, cancellationToken)
            .ConfigureAwait(false);
        var load = ApplicationRevisionLoadResult.FromUpdate(update);
        _lastLoad = load;
        RecordUpdate(update);
        return load;
    }

    private void RecordUpdate(ApplicationRevisionUpdateResult update)
    {
        _lastUpdate = update;
        if (update.Status != ApplicationRevisionUpdateStatus.Rejected)
            _hasActiveApplication = true;
        _state = _hasActiveApplication
            ? ApplicationRevisionHostState.Running
            : ApplicationRevisionHostState.Degraded;
    }

    private ApplicationRevisionCoordinator GetOrCreateCoordinator()
        => _coordinator ??= new ApplicationRevisionCoordinator(
            new ApplicationDefinition(),
            _candidateFactory,
            _eventSink);

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_stopped)
            throw new InvalidOperationException("The application revision host has stopped.");
    }

    private static FlowError SourceFailure(Exception exception)
        => new(
            "revision.source.load_failed",
            "The application definition source could not be loaded.",
            "Hosting",
            false,
            FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
            {
                ["exceptionMessage"] = FlowValue.From(exception.Message),
                ["exceptionType"] = FlowValue.From(
                    exception.GetType().FullName ?? exception.GetType().Name)
            }));

    private static string ValidateRevisionId(string revisionId, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId, parameterName);
        if (!string.Equals(revisionId, revisionId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Revision id cannot have surrounding whitespace.", parameterName);
        return revisionId;
    }
}
