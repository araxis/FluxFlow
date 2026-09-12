using FluxFlow.Core;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition.Authoring;

/// <summary>Builds the Core graph using existing optional component and resource contracts.</summary>
public static class ContractApplicationExtensions
{
    public static Task<FlowApplication> BuildApplicationAsync(this ApplicationDefinitionBuilder builder,
        IServiceProvider services, Action<string, string, FlowMessage, Exception>? onRejected = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Build().BuildApplicationAsync(services, onRejected, cancellationToken);
    }

    public static Task<FlowApplication> BuildApplicationAsync(this ApplicationDefinition definition,
        IServiceProvider services, Action<string, string, FlowMessage, Exception>? onRejected = null,
        CancellationToken cancellationToken = default)
        => new ApplicationSetup(definition).BuildApplicationAsync(services, onRejected, cancellationToken);

    public static async Task<FlowApplication> BuildApplicationAsync(this ApplicationSetup setup,
        IServiceProvider services, Action<string, string, FlowMessage, Exception>? onRejected = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(services);
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = (services.GetService<ComponentCatalog>() ?? new ComponentCatalog())
            .Merge(setup.ComponentDescriptors);
        var compilation = new ApplicationLinkCompiler(catalog, services.GetService<IFlowExpressionEngine>(),
            resolveConditionEngine: name => services.GetKeyedService<IFlowExpressionEngine>(name)).Compile(setup);
        if (!compilation.IsValid)
            throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics));
        if (onRejected is null && compilation.Links.Any(link => link.Delivery == FlowDeliveryPolicy.IsolateRejection))
            throw new ArgumentException("Isolated links require a rejection handler.", nameof(onRejected));
        ComponentConfigurationPreflight.Validate(setup.Definition, catalog);

        var resources = new ServiceCollection();
        setup.RegisterResources(new ApplicationResourceRegistrationContext(setup.Definition,
            1, "application", services, resources), cancellationToken: cancellationToken);
        var scope = new ApplicationResourceScope(resources, services);
        var application = new FlowApplication(scope);
        try
        {
            foreach (var (workflowName, definition) in setup.Definition.Workflows)
            {
                var workflow = application.AddWorkflow(workflowName);
                foreach (var (name, component) in definition.Components)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var instance = await ComponentActivation.CreateAsync(workflowName, name, component,
                        catalog.Components[component.Type], scope).ConfigureAwait(false);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        workflow.Add(name, ComponentGraphView.Create(instance), instance.Node);
                    }
                    catch (Exception error)
                    {
                        if (!FlowTopology.HasOwner(instance.Node))
                        {
                            try { await instance.DisposeAsync().ConfigureAwait(false); }
                            catch (Exception cleanup) { throw new AggregateException("Component addition and cleanup failed.", error, cleanup); }
                        }
                        throw;
                    }
                }
            }
            foreach (var link in compilation.Links)
            {
                if (link.IsFallback)
                {
                    application.GetOutput(link.Source.Value).OtherwiseTo(application.GetInput(link.Target.Value),
                        link.Delivery, link.Delivery == FlowDeliveryPolicy.IsolateRejection
                            ? (message, error) => onRejected!(link.Source.Value, link.Target.Value, message, error) : null);
                    continue;
                }
                application.GetOutput(link.Source.Value).LinkTo(application.GetInput(link.Target.Value),
                    link.IsConditional ? message => link.IsMatch(new FlowMapContext
                    {
                        Variables = new Dictionary<string, object?>
                        {
                            ["input"] = message.Value,
                            ["payload"] = message.Value,
                            ["message"] = message
                        }
                    }) : null, link.Delivery, link.Delivery == FlowDeliveryPolicy.IsolateRejection
                        ? (message, error) => onRejected!(link.Source.Value, link.Target.Value, message, error) : null);
            }
            foreach (var output in compilation.DiscardedOutputs)
                application.GetOutput(output.Value).DiscardUnmatched();
            foreach (var output in compilation.ReplyOutputs)
                application.RepliesFrom(application.GetOutput(output.Value));
            application.Validate();
            return application;
        }
        catch (Exception error)
        {
            try { await application.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Application construction and cleanup failed.", error, cleanup); }
            throw;
        }
    }
}
