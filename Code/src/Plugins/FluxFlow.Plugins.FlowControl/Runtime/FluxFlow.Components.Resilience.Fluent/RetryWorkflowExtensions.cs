using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Components.Resilience;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.DependencyInjection;
using Polly.Retry;

namespace FluxFlow.Components.Resilience;

public static class RetryWorkflowExtensions
{
    public static WorkflowBuilder Retry(this WorkflowBuilder workflow, string name, RetryConfiguration config)
    {
        workflow.Services.AddRetryNode(workflow.Address(name), config, provider =>
        {
            var engine = provider.GetKeyedService<IFlowExpressionEngine>(config.Engine)
                ?? (config.Engine == "jsonata" ? JsonataExpressions.Default
                    : throw new InvalidOperationException($"Expression engine '{config.Engine}' is not registered."));
            return engine.Compile<bool>(config.ShouldRetry!.Value);
        });
        return workflow;
    }

    public static IServiceCollection AddRetryNode(this IServiceCollection services, ComponentAddress address,
        RetryConfiguration config, Func<IServiceProvider, Func<FlowMapContext, bool>> compile)
    {
        var policy = config.CreatePolicy();
        return services.AddComponentFactory(address, provider =>
        {
            // Each provider gets its own policy. Compilation does not mutate the registration snapshot.
            var runtimePolicy = new RetryStrategyOptions<FlowMessage>
            {
                MaxRetryAttempts = policy.MaxRetryAttempts,
                Delay = policy.Delay,
                BackoffType = policy.BackoffType,
                UseJitter = policy.UseJitter,
                MaxDelay = policy.MaxDelay,
                ShouldHandle = policy.ShouldHandle
            };
            if (config.ShouldRetry is not null)
            {
                var evaluate = compile(provider);
                runtimePolicy.ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Result is { } reply && evaluate(FlowMapContext.ForMessage(reply)));
            }
            return new Retry(new RetryOptions
            {
                AttemptTimeout = config.AttemptTimeout,
                RetryPolicy = runtimePolicy,
                OutputCapacity = config.OutputCapacity
            });
        })
            .AddInputPort<Retry>(address, "Input", node => node.Input)
            .AddInputPort<Retry>(address, "Reply", node => node.Reply)
            .AddOutputPort<Retry>(address, "Output", node => node.Output)
            .AddOutputPort<Retry>(address, "Request", node => node.Request)
            .AddEventPort<Retry>(address, "Events", node => node.Events);
    }
}
