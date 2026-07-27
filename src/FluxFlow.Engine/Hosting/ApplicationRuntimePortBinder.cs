using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine.Internal.Snapshots;
using FluxFlow.Composition.Model;
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
            metadata.Accept(new RevisionInputVisitor(
                builder,
                address,
                component.Instance.Inputs[metadata.Name]));
        }

        foreach (var metadata in component.Descriptor.Outputs.Values)
        {
            var address = ApplicationAddress.WorkflowPort(
                key.WorkflowName,
                key.ComponentName,
                metadata.Name);
            metadata.Accept(new RevisionOutputVisitor(
                builder,
                address,
                component.Instance.Outputs[metadata.Name]));
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
            metadata.Accept(new WorkflowInputViewVisitor(
                services,
                address,
                component.Instance.Inputs[metadata.Name]));
        }

        foreach (var metadata in component.Descriptor.Outputs.Values)
        {
            var address = ApplicationAddress.WorkflowPort(
                key.WorkflowName,
                key.ComponentName,
                metadata.Name);
            metadata.Accept(new WorkflowOutputViewVisitor(
                services,
                address,
                component.Instance.Outputs[metadata.Name]));
        }
    }

    private sealed class RevisionInputVisitor(
        ApplicationPortRevisionBuilder builder,
        ApplicationAddress address,
        ComponentInputPort input) : IComponentPortTypeVisitor
    {
        public void Visit<TMessage>(ComponentPortMetadata metadata)
        {
            if (input is not ComponentInputPort<TMessage> typed)
                throw new ApplicationRuntimeAssemblerException($"Input descriptor '{address}' has the wrong type.");
            builder.ReplaceInput(address, typed.Target);
        }

        public void VisitSignal(ComponentPortMetadata metadata)
        {
            if (input is not ComponentSignalInputPort signal)
                throw new ApplicationRuntimeAssemblerException($"Signal descriptor '{address}' has the wrong kind.");
            builder.ReplaceSignalInput(address, signal.Target);
        }
    }

    private sealed class RevisionOutputVisitor(
        ApplicationPortRevisionBuilder builder,
        ApplicationAddress address,
        ComponentOutputPort output) : IComponentPortTypeVisitor
    {
        public void Visit<TMessage>(ComponentPortMetadata metadata)
        {
            if (output is not ComponentOutputPort<TMessage> typed)
                throw new ApplicationRuntimeAssemblerException($"Output descriptor '{address}' has the wrong type.");
            builder.AttachOutput(address, typed.Source);
        }

        public void VisitSignal(ComponentPortMetadata metadata)
            => throw new ApplicationRuntimeAssemblerException($"Signal output '{address}' is unsupported.");
    }

    private sealed class WorkflowInputViewVisitor(
        IServiceCollection services,
        ApplicationAddress address,
        ComponentInputPort input) : IComponentPortTypeVisitor
    {
        public void Visit<TMessage>(ComponentPortMetadata metadata)
        {
            if (input is not ComponentInputPort<TMessage> typed)
                throw new ApplicationRuntimeAssemblerException($"Input descriptor '{address}' has the wrong type.");
            services.AddExternalFluxFlowInputPort(address, typed.Target);
        }

        public void VisitSignal(ComponentPortMetadata metadata)
        {
            if (input is not ComponentSignalInputPort signal)
                throw new ApplicationRuntimeAssemblerException($"Signal descriptor '{address}' has the wrong kind.");
            services.AddExternalFluxFlowSignalTarget(address, signal.Target);
        }
    }

    private sealed class WorkflowOutputViewVisitor(
        IServiceCollection services,
        ApplicationAddress address,
        ComponentOutputPort output) : IComponentPortTypeVisitor
    {
        public void Visit<TMessage>(ComponentPortMetadata metadata)
        {
            if (output is not ComponentOutputPort<TMessage> typed)
                throw new ApplicationRuntimeAssemblerException($"Output descriptor '{address}' has the wrong type.");
            services.AddExternalFluxFlowOutputPort(address, typed.Source);
        }

        public void VisitSignal(ComponentPortMetadata metadata)
            => throw new ApplicationRuntimeAssemblerException($"Signal output '{address}' is unsupported.");
    }

    private sealed class NonOwningFlowNodeView(IFlowNode node) : IFlowNode
    {
        public Task Completion => node.Completion;

        public void Complete() => node.Complete();

        public void Fault(Exception exception) => node.Fault(exception);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
