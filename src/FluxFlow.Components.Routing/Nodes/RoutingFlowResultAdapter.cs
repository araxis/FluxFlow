using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;
using NodeFlowError = FluxFlow.Nodes.FlowError;

namespace FluxFlow.Components.Routing.Nodes;

internal static class RoutingFlowResultAdapter
{
    internal static ActionBlock<FlowMessage<TSource>> LinkSuccess<TSource, TResult>(
        ISourceBlock<FlowMessage<TSource>> source,
        BroadcastBlock<FlowMessage<FlowResult<TResult>>> output,
        Func<TSource, TResult> map,
        Func<TSource, string> kind,
        Func<TSource, DateTimeOffset> timestamp)
    {
        var target = new ActionBlock<FlowMessage<TSource>>(message =>
            output.Post(message.With(FlowResult<TResult>.Success(
                kind(message.Payload),
                map(message.Payload),
                timestamp(message.Payload)))));
        source.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true });
        return target;
    }

    internal static ActionBlock<NodeFlowError> LinkErrors<TResult>(
        ISourceBlock<NodeFlowError> source,
        BroadcastBlock<FlowMessage<FlowResult<TResult>>> output)
    {
        var target = new ActionBlock<NodeFlowError>(error =>
        {
            var result = FlowResult<TResult>.Failure(
                RoutingResultKinds.OperationFailed,
                new DataFlowError(
                    RoutingErrorCodeNames.OperationFailed,
                    error.Message,
                    category: "Routing",
                    isTransient: false,
                    details: CreateDetails(error)),
                error.Timestamp);
            var message = error.CorrelationId is { } correlationId
                ? new FlowMessage<FlowResult<TResult>>(correlationId, result)
                : FlowMessage.Create(result);
            output.Post(message);
        });
        source.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true });
        return target;
    }

    internal static async Task MonitorAsync<TResult>(
        IFlowNode inner,
        IReadOnlyCollection<IDataflowBlock> adapters,
        BroadcastBlock<FlowMessage<FlowResult<TResult>>> output,
        TaskCompletionSource completion)
    {
        try
        {
            await inner.Completion.ConfigureAwait(false);
            await Task.WhenAll(adapters.Select(adapter => adapter.Completion)).ConfigureAwait(false);
            output.Complete();
            await output.Completion.ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            foreach (var adapter in adapters)
            {
                try
                {
                    adapter.Fault(exception);
                }
                catch
                {
                    // Continue faulting sibling adapters and the public output.
                }
            }

            try
            {
                ((IDataflowBlock)output).Fault(exception);
            }
            catch
            {
                // The output may already be terminal; Completion remains authoritative.
            }
            completion.TrySetException(exception);
        }
    }

    private static FlowValue CreateDetails(NodeFlowError error)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["legacyCode"] = FlowValue.From(error.Code)
        };
        if (!string.IsNullOrWhiteSpace(error.Context))
            details["context"] = FlowValue.From(error.Context);
        if (error.Exception is not null)
        {
            details["exceptionType"] = FlowValue.From(
                error.Exception.GetType().FullName ?? error.Exception.GetType().Name);
        }

        return FlowValue.FromObject(details);
    }
}
