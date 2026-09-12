using FluxFlow.Core;
using FluxFlow.Json;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Composition.Authoring;

/// <summary>Code-authored documents and native predicates build the same direct Core graph as JSON.</summary>
public static class PortableApplicationExtensions
{
    public static Task<FlowApplication> BuildApplicationAsync(this ApplicationDefinitionBuilder builder,
        ComponentBindings bindings, IFlowExpressionEngine? expressions = null,
        Func<string, IFlowExpressionEngine?>? resolveExpressions = null,
        Action<string, string, FlowMessage, Exception>? onRejected = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Build().BuildApplicationAsync(bindings, expressions, resolveExpressions, onRejected);
    }

    public static async Task<FlowApplication> BuildApplicationAsync(this ApplicationSetup setup,
        ComponentBindings bindings, IFlowExpressionEngine? expressions = null,
        Func<string, IFlowExpressionEngine?>? resolveExpressions = null,
        Action<string, string, FlowMessage, Exception>? onRejected = null)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (setup.ResourceContracts.Count != 0 || setup.ComponentDescriptors.Count != 0)
            throw new NotSupportedException("Embedded contracts require the BuildApplicationAsync overload that accepts application services.");

        var document = ApplicationSetupExportExtensions.Project(setup.Definition,
            setup.Links.Where(link => link.CodeCondition is null));
        var application = await FlowJson.BuildAsync(document, bindings, expressions, resolveExpressions, onRejected).ConfigureAwait(false);
        try
        {
            foreach (var link in setup.Links)
            {
                if (link.CodeCondition is not { } predicate) continue;
                application.GetOutput(link.Source.Value).LinkTo(application.GetInput(link.Target.Value),
                    message => predicate(new FlowMapContext
                    {
                        Variables = new Dictionary<string, object?>
                        {
                            ["input"] = message.Value,
                            ["payload"] = message.Value,
                            ["message"] = message
                        }
                    }), link.Delivery, link.Delivery != FlowDeliveryPolicy.IsolateRejection || onRejected is null ? null :
                        (message, error) => onRejected(link.Source.Value, link.Target.Value, message, error));
            }
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
