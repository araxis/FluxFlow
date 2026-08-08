using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Model;
using FluxFlow.Engine;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Fluent;

/// <summary>
/// A fluent facade over the canonical immutable definition and <see cref="FluxFlowApplication"/>
/// host. The fluent DSL does not own a second workflow runtime.
/// </summary>
public sealed class FlowGraph : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly BroadcastBlock<FlowEvent> _events =
        new(static flowEvent => flowEvent);
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Task _sourceCompletion;
    private readonly object _lifecycleGate = new();
    private Task? _stopTask;
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private int _disposed;

    internal FlowGraph(
        ApplicationDefinition definition,
        IReadOnlyList<ISourceBlock<FlowEvent>> eventSources,
        IReadOnlyList<Task> sourceCompletions)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ArgumentNullException.ThrowIfNull(eventSources);
        ArgumentNullException.ThrowIfNull(sourceCompletions);
        _sourceCompletion = Task.WhenAll(sourceCompletions);

        var services = new ServiceCollection();
        services.AddFluxFlow(
            definition,
            options => options.StartWithHost = false);
        _services = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        Application = _services.GetRequiredService<FluxFlowApplication>();

        foreach (var source in eventSources)
            _subscriptions.Add(source.LinkTo(_events));
        _ = CompleteEventsAsync(eventSources);
    }

    public ApplicationDefinition Definition { get; }

    public FluxFlowApplication Application { get; }

    public Task Completion => _completion.Task;

    public ISourceBlock<FlowEvent> Events => _events;

    public IDisposable OnEvent(Action<FlowEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Subscribe(_events, handler);
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        var result = await Application.StartAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsRejected)
        {
            var message = string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Error.Message));
            _completion.TrySetException(new InvalidOperationException(message));
            throw new InvalidOperationException(message);
        }

        _ = StopWhenSourcesCompleteAsync();
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _started) == 0)
            return;

        await EnsureStoppedAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        _completion.TrySetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (Volatile.Read(ref _started) != 0)
                await EnsureStoppedAsync().ConfigureAwait(false);
            await Application.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (Volatile.Read(ref _started) == 0)
                _completion.TrySetResult();

            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _events.Complete();
            if (_services is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (_services is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private async Task StopWhenSourcesCompleteAsync()
    {
        try
        {
            await _sourceCompletion.ConfigureAwait(false);
            await EnsureStoppedAsync().ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            try
            {
                await EnsureStoppedAsync().ConfigureAwait(false);
            }
            catch
            {
                // The source failure remains the authoritative graph failure.
            }
            _completion.TrySetException(exception);
        }
    }

    private Task EnsureStoppedAsync()
    {
        lock (_lifecycleGate)
            return _stopTask ??= Application.StopAsync(CancellationToken.None).AsTask();
    }

    private async Task CompleteEventsAsync(IReadOnlyList<ISourceBlock<FlowEvent>> sources)
    {
        try
        {
            await Task.WhenAll(sources.Select(static source => source.Completion))
                .ConfigureAwait(false);
            _events.Complete();
        }
        catch (Exception exception)
        {
            ((IDataflowBlock)_events).Fault(exception);
        }
    }

    private IDisposable Subscribe<T>(ISourceBlock<T> source, Action<T> handler)
    {
        var sink = new ActionBlock<T>(item =>
        {
            try
            {
                handler(item);
            }
            catch
            {
                // Diagnostic observers cannot break the workflow.
            }
        });

        var link = source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        _subscriptions.Add(link);
        return link;
    }
}
