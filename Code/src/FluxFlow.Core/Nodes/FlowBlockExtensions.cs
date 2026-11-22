using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Core.Nodes;

// Shared component mechanics over native blocks; no custom delivery protocol.
public static class FlowBlockExtensions
{
    /// <summary>Processes a synchronous transform with the same tracing and error conversion as asynchronous callbacks.</summary>
    public static Task<IEnumerable<FlowMessage>> ProcessMessageAsync(this FlowMessage message, object component,
        Func<FlowMessage, IEnumerable<FlowMessage>> transform, ITargetBlock<FlowMessage> output,
        CancellationToken stopping = default, bool handlesErrors = false)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return ProcessAsync(message, component, value => ValueTask.FromResult(transform(value)),
            output, stopping, handlesErrors, emitErrors: false);
    }

    /// <summary>Processes a synchronous action. Forwarded errors still await output admission.</summary>
    public static Task<IEnumerable<FlowMessage>> ProcessMessageAsync(this FlowMessage message, object component,
        Action<FlowMessage> process, ITargetBlock<FlowMessage> output,
        CancellationToken stopping = default, bool handlesErrors = false)
    {
        ArgumentNullException.ThrowIfNull(process);
        return ProcessAsync(message, component, value =>
        {
            process(value);
            return ValueTask.FromResult<IEnumerable<FlowMessage>>([]);
        }, output, stopping, handlesErrors, emitErrors: true);
    }

    /// <summary>Applies tracing and error conversion to a transform callback without constructing or linking a block.</summary>
    public static Task<IEnumerable<FlowMessage>> ProcessMessageAsync(this FlowMessage message, object component,
        Func<FlowMessage, Task<IEnumerable<FlowMessage>>> transform, ITargetBlock<FlowMessage> output,
        CancellationToken stopping = default, bool handlesErrors = false)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return ProcessAsync(message, component, value => new ValueTask<IEnumerable<FlowMessage>>(transform(value)),
            output, stopping, handlesErrors, emitErrors: false);
    }

    /// <summary>Applies tracing and error conversion to an explicit-emission callback. Errors use the same output path.</summary>
    public static Task<IEnumerable<FlowMessage>> ProcessMessageAsync(this FlowMessage message, object component,
        Func<FlowMessage, Task> process, ITargetBlock<FlowMessage> output,
        CancellationToken stopping = default, bool handlesErrors = false)
    {
        ArgumentNullException.ThrowIfNull(process);
        return ProcessAsync(message, component, async value =>
        {
            await process(value).ConfigureAwait(false);
            return [];
        }, output, stopping, handlesErrors, emitErrors: true);
    }

    /// <summary>Produces flush results synchronously, then awaits their publication without blocking a thread.</summary>
    public static ValueTask FlushMessagesAsync(this ITargetBlock<FlowMessage> output,
        Func<IEnumerable<FlowMessage>> flush, CancellationToken stopping = default)
    {
        ArgumentNullException.ThrowIfNull(flush);
        return output.FlushMessagesAsync(() => ValueTask.FromResult(flush()), stopping);
    }

    /// <summary>Publishes a finite flush result after materializing it, so enumeration errors cannot partially publish it.</summary>
    public static async ValueTask FlushMessagesAsync(this ITargetBlock<FlowMessage> output,
        Func<ValueTask<IEnumerable<FlowMessage>>> flush, CancellationToken stopping = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(flush);
        foreach (var message in Materialize(await flush().ConfigureAwait(false)))
            await output.EmitAsync(message, stopping, "The node data output is no longer accepting messages.").ConfigureAwait(false);
    }

    private static async Task<IEnumerable<FlowMessage>> ProcessAsync(FlowMessage message, object component,
        Func<FlowMessage, ValueTask<IEnumerable<FlowMessage>>> transform, ITargetBlock<FlowMessage> output,
        CancellationToken stopping, bool handlesErrors, bool emitErrors)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(output);
        using var activity = FlowTracing.StartProcessing(message, component);
        var forwardError = message.IsError && !handlesErrors;
        try
        {
            if (forwardError) return await ForwardErrorAsync(message).ConfigureAwait(false);
            return Materialize(await transform(message).ConfigureAwait(false));
        }
        catch (Exception error)
        {
            if (error is OperationCanceledException && stopping.IsCancellationRequested)
            {
                activity.Canceled();
                throw;
            }
            activity.Failed(error);
            FlowDiagnostics.ProcessingException(message, component, error);
            if (forwardError) throw;
            if (output.Completion.IsFaulted) await output.Completion.ConfigureAwait(false);
            return await ForwardErrorAsync(message.WithValue(FlowProcessingError.FromException(error))).ConfigureAwait(false);
        }

        async Task<IEnumerable<FlowMessage>> ForwardErrorAsync(FlowMessage error)
        {
            if (!emitErrors) return [error];
            await output.EmitAsync(error, stopping, "The node data output is no longer accepting messages.").ConfigureAwait(false);
            return [];
        }
    }

    private static FlowMessage[] Materialize(IEnumerable<FlowMessage> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var messages = results.ToArray();
        foreach (var message in messages) ArgumentNullException.ThrowIfNull(message);
        return messages;
    }

    internal static async ValueTask EmitAsync<T>(this ITargetBlock<T> output, T value,
        CancellationToken cancellationToken, string closedMessage)
    {
        if (await output.SendAsync(value, cancellationToken).ConfigureAwait(false)) return;
        await output.Completion.ConfigureAwait(false);
        throw new InvalidOperationException(closedMessage);
    }

    /// <summary>Traces a synchronous control action and faults its owner on failure.</summary>
    public static Task ProcessControlAsync(FlowMessage message, object component, string name,
        Action<FlowMessage, CancellationToken> action,
        CancellationToken stopping, Action<Exception> fault)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ProcessControlAsync(message, component, name, (value, token) =>
        {
            action(value, token);
            return Task.CompletedTask;
        }, stopping, fault);
    }

    /// <summary>Traces a control action and faults its owner on failure. The caller creates the native input block.</summary>
    public static async Task ProcessControlAsync(FlowMessage message, object component, string name,
        Func<FlowMessage, CancellationToken, Task> action,
        CancellationToken stopping, Action<Exception> fault)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(fault);
        using var activity = FlowTracing.StartProcessing(message, component, name);
        try { await action(message, stopping).ConfigureAwait(false); }
        catch (Exception error)
        {
            if (error is OperationCanceledException && stopping.IsCancellationRequested) activity.Canceled();
            else activity.Failed(error);
            fault(error);
            throw;
        }
    }
}
