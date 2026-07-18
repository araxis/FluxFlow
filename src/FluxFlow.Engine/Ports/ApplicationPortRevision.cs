using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

public sealed class ApplicationPortRevisionBuilder : IAsyncDisposable
{
    private readonly ApplicationPortRuntime _runtime;
    private readonly Dictionary<ApplicationAddress, object?> _inputReplacements = [];
    private readonly List<IPreparedApplicationOutput> _preparedOutputs = [];
    private ApplicationRevisionRouting.Snapshot? _routing;
    private bool _routingConfigured;
    private int _built;
    private int _disposed;

    internal ApplicationPortRevisionBuilder(
        ApplicationPortRuntime runtime,
        string revisionId)
    {
        _runtime = runtime;
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        if (!string.Equals(revisionId, revisionId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Revision id cannot have surrounding whitespace.", nameof(revisionId));
        RevisionId = revisionId;
    }

    public string RevisionId { get; }

    public ApplicationPortRevisionBuilder ReplaceInput<T>(
        ApplicationAddress address,
        ITargetBlock<FlowMessage<T>> target)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(target);
        _runtime.ValidateRevisionInput<T>(address);
        AddInputReplacement(address, target);
        return this;
    }

    public ApplicationPortRevisionBuilder RemoveInput<T>(ApplicationAddress address)
    {
        ThrowIfUnavailable();
        _runtime.ValidateRevisionInput<T>(address);
        AddInputReplacement(address, target: null);
        return this;
    }

    public ApplicationPortRevisionBuilder AttachOutput<T>(
        ApplicationAddress address,
        ISourceBlock<FlowMessage<T>> source)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(source);
        _preparedOutputs.Add(_runtime.PrepareRevisionOutput(address, source));
        return this;
    }

    public ApplicationPortRevisionBuilder SetLinks(
        IEnumerable<CompiledApplicationLink> links)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(links);
        if (_routingConfigured)
            throw new InvalidOperationException("Revision links were already configured.");
        _routing = _runtime.PrepareRevisionRouting(links);
        _routingConfigured = true;
        return this;
    }

    public ApplicationPortRevision Build()
    {
        ThrowIfUnavailable();
        if (Interlocked.Exchange(ref _built, 1) != 0)
            throw new InvalidOperationException("The port revision was already built.");

        return new ApplicationPortRevision(
            _runtime,
            RevisionId,
            _inputReplacements,
            _preparedOutputs,
            _routing,
            _routingConfigured);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 &&
            Volatile.Read(ref _built) == 0)
        {
            DisposePreparedOutputs(_preparedOutputs);
        }

        return ValueTask.CompletedTask;
    }

    private void AddInputReplacement(ApplicationAddress address, object? target)
    {
        if (!_inputReplacements.TryAdd(address, target))
            throw new InvalidOperationException($"Input port '{address}' already has a revision replacement.");
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _built) != 0)
            throw new InvalidOperationException("The port revision was already built.");
    }

    internal static void DisposePreparedOutputs(IEnumerable<IPreparedApplicationOutput> outputs)
    {
        List<Exception>? failures = null;
        foreach (var output in outputs)
        {
            try
            {
                output.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
            throw new AggregateException("Prepared revision output cleanup failed.", failures);
    }
}

public sealed class ApplicationPortRevision : IAsyncDisposable
{
    private readonly ApplicationPortRuntime _runtime;
    private readonly IReadOnlyDictionary<ApplicationAddress, object?> _inputReplacements;
    private IReadOnlyList<IPreparedApplicationOutput> _preparedOutputs;
    private readonly ApplicationRevisionRouting.Snapshot? _routing;
    private readonly bool _routingConfigured;
    private int _state;

    internal ApplicationPortRevision(
        ApplicationPortRuntime runtime,
        string revisionId,
        IReadOnlyDictionary<ApplicationAddress, object?> inputReplacements,
        IReadOnlyList<IPreparedApplicationOutput> preparedOutputs,
        ApplicationRevisionRouting.Snapshot? routing,
        bool routingConfigured)
    {
        _runtime = runtime;
        RevisionId = revisionId;
        _inputReplacements = new Dictionary<ApplicationAddress, object?>(inputReplacements);
        _preparedOutputs = preparedOutputs.ToArray();
        _routing = routing;
        _routingConfigured = routingConfigured;
    }

    public string RevisionId { get; }

    public ValueTask<ApplicationPortRevisionLease> ActivateAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("The port revision is no longer prepared.");
        return ActivateCoreAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _state, 3, 0) == 0)
        {
            var outputs = Interlocked.Exchange(
                ref _preparedOutputs,
                Array.Empty<IPreparedApplicationOutput>());
            ApplicationPortRevisionBuilder.DisposePreparedOutputs(outputs);
        }

        return ValueTask.CompletedTask;
    }

    internal IReadOnlyDictionary<ApplicationAddress, object?> InputReplacements => _inputReplacements;

    internal IReadOnlyList<IPreparedApplicationOutput> PreparedOutputs => _preparedOutputs;

    internal ApplicationRevisionRouting.Snapshot? Routing => _routing;

    internal bool RoutingConfigured => _routingConfigured;

    internal IReadOnlyList<IPreparedApplicationOutput> TransferPreparedOutputs()
    {
        var outputs = Interlocked.Exchange(
            ref _preparedOutputs,
            Array.Empty<IPreparedApplicationOutput>());
        Volatile.Write(ref _state, 2);
        return outputs;
    }

    private async ValueTask<ApplicationPortRevisionLease> ActivateCoreAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _runtime.ActivateRevisionAsync(this, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception activationFailure)
        {
            Volatile.Write(ref _state, 3);
            var outputs = Interlocked.Exchange(
                ref _preparedOutputs,
                Array.Empty<IPreparedApplicationOutput>());
            try
            {
                ApplicationPortRevisionBuilder.DisposePreparedOutputs(outputs);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Port revision activation and rollback cleanup failed.",
                    activationFailure,
                    cleanupFailure);
            }

            throw;
        }
    }
}

public sealed class ApplicationPortRevisionLease : IAsyncDisposable
{
    private IReadOnlyList<IAsyncDisposable> _inputAttachments;
    private IReadOnlyList<IPreparedApplicationOutput> _outputAttachments;
    private int _disposed;

    internal ApplicationPortRevisionLease(
        ApplicationPortRevisionInfo info,
        IReadOnlyList<IAsyncDisposable> inputAttachments,
        IReadOnlyList<IPreparedApplicationOutput> outputAttachments)
    {
        Info = info;
        _inputAttachments = inputAttachments;
        _outputAttachments = outputAttachments;
    }

    public ApplicationPortRevisionInfo Info { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var inputs = Interlocked.Exchange(
            ref _inputAttachments,
            Array.Empty<IAsyncDisposable>());
        var outputs = Interlocked.Exchange(
            ref _outputAttachments,
            Array.Empty<IPreparedApplicationOutput>());
        List<Exception>? failures = null;

        foreach (var input in inputs)
        {
            try
            {
                await input.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        foreach (var output in outputs)
        {
            try
            {
                output.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
            throw new AggregateException("Port revision cleanup failed.", failures);
    }
}
