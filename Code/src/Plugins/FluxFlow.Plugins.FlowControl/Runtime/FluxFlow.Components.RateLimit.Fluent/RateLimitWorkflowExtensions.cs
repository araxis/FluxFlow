using System.Threading.RateLimiting;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Components.RateLimiting;
using FluxFlow.Configuration.Components.RequestReply;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.RateLimit;

public static class RateLimitWorkflowExtensions
{
    public static WorkflowBuilder RateLimit(this WorkflowBuilder workflow, string name, RateLimitConfiguration config)
    {
        workflow.Services.AddRateLimitNode(workflow.Address(name), config);
        return workflow;
    }

    public static IServiceCollection AddRateLimitNode(this IServiceCollection services, ComponentAddress address,
        RateLimitConfiguration config)
    {
        config.Validate();
        return services.AddComponentFactory(address, provider => new RateLimit(new RateLimitOptions
        {
            Limiter = provider.GetRequiredFlowResource<RateLimiter>(config.Limiter, address.WorkflowName),
            ResponseTimeout = config.ResponseTimeout,
            OutputCapacity = config.OutputCapacity
        }))
            .AddInputPort<RateLimit>(address, "Input", node => node.Input)
            .AddInputPort<RateLimit>(address, "Reply", node => node.Reply)
            .AddOutputPort<RateLimit>(address, "Output", node => node.Output)
            .AddOutputPort<RateLimit>(address, "Request", node => node.Request)
            .AddEventPort<RateLimit>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder RequestReply(this WorkflowBuilder workflow, string name, RequestReplyConfiguration config)
    {
        workflow.Services.AddRequestReplyNode(workflow.Address(name), config);
        return workflow;
    }

    public static IServiceCollection AddRequestReplyNode(this IServiceCollection services, ComponentAddress address,
        RequestReplyConfiguration config)
    {
        config.Validate();
        var capacity = config.MaxInFlight + config.BufferCapacity;
        return services.AddComponentFactory(address, _ => new RateLimit(new RateLimitOptions
        {
            Limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = config.MaxInFlight, QueueLimit = 0, QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }),
            OwnsLimiter = true,
            EmitRequestReplyResults = true,
            PendingCapacity = config.MaxInFlight,
            ResponseTimeout = config.ReplyTimeout,
            StopDispatchOnTimeout = config.TimeoutBehavior == RequestReplyTimeoutBehavior.StopDispatch,
            OutputCapacity = capacity,
            Execution = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = config.MaxInFlight,
                BoundedCapacity = capacity,
                EnsureOrdered = false
            }
        }))
            .AddInputPort<RateLimit>(address, "Input", node => node.Input)
            .AddInputPort<RateLimit>(address, "ACK", node => node.Reply)
            .AddInputPort<RateLimit>(address, "NACK", node => node.Nack)
            .AddOutputPort<RateLimit>(address, "Request", node => node.Request)
            .AddOutputPort<RateLimit>(address, "Output", node => node.Output)
            .AddEventPort<RateLimit>(address, "Events", node => node.Events);
    }

    public static IServiceCollection AddTokenBucket(this IServiceCollection services, string address, TokenBucketConfiguration config)
    {
        config.Validate();
        return services.AddFluxFlowResource<RateLimiter>(ApplicationAddress.Parse(address), _ => new TokenBucketRateLimiter(new()
        {
            TokenLimit = config.TokenLimit,
            TokensPerPeriod = config.TokensPerPeriod,
            ReplenishmentPeriod = config.ReplenishmentPeriod,
            QueueLimit = config.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        }));
    }
}
