using System.Runtime.Loader;
using System.Threading.RateLimiting;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using FluxFlow.Core.Messaging;
using FluxFlow.Execution.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 1 || !Directory.Exists(args[0]))
    throw new ArgumentException("Supply the temporary plugin store containing the installed FlowControl package.");

using var plugins = InstalledPlugins.Load(args[0]);
var module = plugins.Modules.Single(plugin => plugin.Description.Id == "FluxFlow.Plugins.FlowControl");
if (AssemblyLoadContext.GetLoadContext(module.GetType().Assembly) == AssemblyLoadContext.Default)
    throw new InvalidOperationException("The trial requires the installed package, not a project reference.");

foreach (var workflowScoped in new[] { false, true })
foreach (var configured in new[] { false, true })
{
    var resourcePath = workflowScoped ? "Workflows:main:Resources:limits" : "Resources:limits";
    using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Workflows:main:node:Type"] = "rateLimit",
        ["Workflows:main:node:Limiter"] = "limits",
        ["Workflows:main:node:ResponseTimeout"] = "00:00:05",
        [resourcePath + ":Type"] = configured ? "rateLimiter.tokenBucket" : "host.limiter",
        [resourcePath + ":TokenLimit"] = "1",
        [resourcePath + ":TokensPerPeriod"] = "1",
        [resourcePath + ":ReplenishmentPeriod"] = "01:00:00",
        [resourcePath + ":QueueLimit"] = "0"
    }).Build();
    var services = new ServiceCollection();
    module.ConfigureWorkflow(services);
    services.AddKeyedSingleton<ResourceRegistration>("host.limiter", (registrations, address, _) =>
        registrations.AddFluxFlowResource<RateLimiter>(address, _ => new ConcurrencyLimiter(new() { PermitLimit = 1, QueueLimit = 2 })));
    services.AddFluxFlowApplication(configuration);
    await using var root = services.BuildServiceProvider();

    RateLimiter limiter;
    await using (var application = await root.BuildApplicationAsync())
    {
        var workflow = root.GetWorkflowServices("main");
        limiter = workflow.GetRequiredFlowResource<RateLimiter>("limits", "main");
        var requests = new BufferBlock<FlowMessage>();
        var replies = new BufferBlock<FlowMessage>();
        using var requestLink = workflow.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Request").LinkTo(requests);
        using var replyLink = workflow.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Output").LinkTo(replies);
        await application.StartAsync();
        var deadline = TimeSpan.FromSeconds(5);
        await workflow.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Input").SendAsync(FlowMessage.Create((FlowValue)"sample"));
        var request = await requests.ReceiveAsync(deadline);
        if (limiter.GetStatistics()!.CurrentAvailablePermits != 0)
            throw new InvalidOperationException("The request did not acquire the host's limiter.");
        await workflow.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Reply").SendAsync(request.WithValue((FlowValue)"done"));
        if ((await replies.ReceiveAsync(deadline)).Value.Json.GetString() != "done")
            throw new InvalidOperationException("The correlated reply did not reach Output.");
        if (configured)
        {
            await workflow.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Input").SendAsync(FlowMessage.Create((FlowValue)"excess"));
            if ((await replies.ReceiveAsync(deadline)).Error?.Code != "rate_limit_rejected" || requests.Count != 0)
                throw new InvalidOperationException("The configured token budget did not reject excess work.");
        }
        await application.StopAsync().WaitAsync(deadline);
    }

    try
    {
        using var lease = limiter.AttemptAcquire();
        if (workflowScoped || lease.IsAcquired == configured)
            throw new InvalidOperationException("The limiter has the wrong lifetime after workflow disposal.");
    }
    catch (ObjectDisposedException) when (workflowScoped) { }
    Console.WriteLine($"{(configured ? "Configured token bucket" : "Existing host limiter")}: {(workflowScoped ? "disposed with workflow scope" : "retained by application owner")}.");
}
