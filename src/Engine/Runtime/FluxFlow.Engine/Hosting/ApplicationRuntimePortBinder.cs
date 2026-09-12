using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine.Internal.Snapshots;
using FluxFlow.Json.Model;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Hosting;

internal static class ApplicationRuntimePortBinder
{
    internal static void AddWorkflowSnapshots(
        ApplicationDefinition definition,
        string revisionId,
        IReadOnlyDictionary<ApplicationRuntimeComponentKey, ApplicationRuntimeBuiltComponent> components,
        ICollection<CompositionServiceProviderSnapshot> snapshots)
    {
        foreach (var (workflowName, workflow) in definition.Workflows)
        {
            var services = new ServiceCollection();
            foreach (var componentName in workflow.Components.Keys)
            {
                var key = new ApplicationRuntimeComponentKey(workflowName, componentName);
                RegisterWorkflowViews(services, key, components[key]);
            }

            snapshots.Add(new CompositionServiceProviderSnapshotBuilder()
                .AddServices(services)
                .Build(
                    CompositionProviderBoundary.WorkflowRevision,
                    $"workflow:{workflowName}:{revisionId}"));
        }
    }

    internal static void ConfigureRevision(
        ApplicationPortRevisionBuilder builder,
        IReadOnlyDictionary<ApplicationRuntimeComponentKey, ApplicationRuntimeBuiltComponent> components)
    {
        foreach (var (key, component) in components)
            ConfigureComponent(builder, key, component);
    }

    private static void ConfigureComponent(
        ApplicationPortRevisionBuilder builder,
        ApplicationRuntimeComponentKey key,
        ApplicationRuntimeBuiltComponent component)
    {
        foreach (var metadata in component.Descriptor.Inputs.Values)
        {
            var address = ApplicationAddress.WorkflowPort(
                key.WorkflowName,
                key.ComponentName,
                metadata.Name);
            var input = component.Instance.Inputs[metadata.Name];
            if (metadata.Kind == ComponentPortKind.Signal)
                builder.ReplaceSignalInput(address, RequireSignalInput(address, input));
            else
                builder.ReplaceInput(address, RequireMessageInput(address, input));
        }

        foreach (var metadata in component.Descriptor.Outputs.Values)
        {
            var address = ApplicationAddress.WorkflowPort(
                key.WorkflowName,
                key.ComponentName,
                metadata.Name);
            builder.AttachOutput(address,
                RequireMessageOutput(metadata, address, component.Instance.Outputs[metadata.Name]));
        }
    }

    private static void RegisterWorkflowViews(
        IServiceCollection services,
        ApplicationRuntimeComponentKey key,
        ApplicationRuntimeBuiltComponent component)
    {
        var componentAddress = ApplicationAddress.WorkflowComponent(
            key.WorkflowName,
            key.ComponentName);
        services.AddKeyedSingleton<IFlowNode>(
            componentAddress.Value,
            new NonOwningFlowNodeView(component.Instance.Node));
        services.AddKeyedSingleton(componentAddress.Value, component.Instance);

        foreach (var metadata in component.Descriptor.Inputs.Values)
        {
            var address = ApplicationAddress.WorkflowPort(
                key.WorkflowName,
                key.ComponentName,
                metadata.Name);
            var input = component.Instance.Inputs[metadata.Name];
            if (metadata.Kind == ComponentPortKind.Signal)
                services.AddExternalFluxFlowSignalTarget(address, RequireSignalInput(address, input));
            else
                services.AddExternalFluxFlowInputPort(address, RequireMessageInput(address, input));
        }

        foreach (var metadata in component.Descriptor.Outputs.Values)
        {
            var address = ApplicationAddress.WorkflowPort(
                key.WorkflowName,
                key.ComponentName,
                metadata.Name);
            services.AddExternalFluxFlowOutputPort(address,
                RequireMessageOutput(metadata, address, component.Instance.Outputs[metadata.Name]));
        }
    }

    private static ITargetBlock<FlowMessage> RequireMessageInput(
        ApplicationAddress address, ComponentInputPort input)
        => input.Kind == ComponentPortKind.Message
            ? input.Target
            : throw new ApplicationRuntimeAssemblerException($"Input descriptor '{address}' has the wrong kind.");

    private static ITargetBlock<FlowMessage> RequireSignalInput(ApplicationAddress address, ComponentInputPort input)
        => input.Kind == ComponentPortKind.Signal
            ? input.Target
            : throw new ApplicationRuntimeAssemblerException($"Signal descriptor '{address}' has the wrong kind.");

    private static ISourceBlock<FlowMessage> RequireMessageOutput(
        ComponentPortMetadata metadata, ApplicationAddress address, ComponentOutputPort output)
    {
        if (metadata.Kind == ComponentPortKind.Signal)
            throw new ApplicationRuntimeAssemblerException($"Signal output '{address}' is unsupported.");
        return metadata.MessageType == typeof(global::FluxFlow.Data.FlowValue)
            ? output.Source
            : throw new ApplicationRuntimeAssemblerException($"Output descriptor '{address}' must use canonical workflow messages.");
    }

    private sealed class NonOwningFlowNodeView(IFlowNode node) : IFlowNode
    {
        public Task Completion => node.Completion;

        public void Complete() => node.Complete();

        public void Fault(Exception exception) => node.Fault(exception);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
