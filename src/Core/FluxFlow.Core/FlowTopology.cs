using System.Runtime.CompilerServices;
using FluxFlow.Core.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Core;

internal sealed class FlowTopology
{
    private static readonly ConditionalWeakTable<IFlowNode, object> Owners = new();
    private readonly Dictionary<string, FlowWorkflow> _workflows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FlowComponent> _components = new(StringComparer.Ordinal);
    private readonly List<FlowLink> _links = [];
    private readonly HashSet<FlowOutputEndpoint> _discardUnmatched = [];
    private readonly Dictionary<FlowOutputEndpoint, Predicate<FlowMessage>?> _replies = [];
    private bool _frozen;
    internal object Gate { get; } = new();

    internal static bool HasOwner(IFlowNode component)
    {
        lock (Owners) return Owners.TryGetValue(component, out _);
    }

    public void Validate()
    {
        lock (Gate) BuildDependencyLayers();
    }

    public FlowWorkflow AddWorkflow(string name)
    {
        lock (Gate)
        {
            EnsureMutable();
            ApplicationAddress.RequireWorkflowName(name, nameof(name));
            var workflow = new FlowWorkflow(this, name);
            _workflows.Add(name, workflow);
            return workflow;
        }
    }

    public FlowComponent AddComponent(string workflow, string name, IFlowComponent instance, ComponentOwnership ownership,
        IFlowNode? node = null)
    {
        lock (Gate)
        {
            EnsureMutable();
            ApplicationAddress.RequireConstructedSegment(name, nameof(name), "Component name");
            ArgumentNullException.ThrowIfNull(instance);
            if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));
            var address = $"{workflow}.{name}";
            if (_components.ContainsKey(address)) throw new ArgumentException($"Component '{address}' already exists.", nameof(name));
            node ??= instance;
            var component = new FlowComponent(this, address, instance, ownership, node);
            lock (Owners)
            {
                if (Owners.TryGetValue(node, out _) || Owners.TryGetValue(instance, out _))
                    throw new ArgumentException("The same component instance cannot be added twice or shared by graphs.", nameof(instance));
                Owners.Add(node, new object());
                if (!ReferenceEquals(instance, node)) Owners.Add(instance, new object());
            }
            _components.Add(address, component);
            return component;
        }
    }

    public FlowLink Link(FlowOutputEndpoint output, FlowInputEndpoint input, Predicate<FlowMessage>? condition,
        FlowDeliveryPolicy delivery, bool fallback, Action<FlowMessage, Exception>? onRejected)
    {
        lock (Gate)
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(input);
            if (output.Topology != this || input.Topology != this)
                throw new ArgumentException("Both ports must belong to this application.");
            if (!Enum.IsDefined(delivery)) throw new ArgumentOutOfRangeException(nameof(delivery));
            if (delivery == FlowDeliveryPolicy.IsolateRejection)
                ArgumentNullException.ThrowIfNull(onRejected);
            else if (onRejected is not null)
                throw new ArgumentException("A rejection handler requires IsolateRejection delivery.", nameof(onRejected));
            if (fallback && _links.Any(link => link.Output == output && link.IsFallback))
                throw new InvalidOperationException($"Output '{output.Address}' already has a fallback.");
            var link = new FlowLink(output, input, condition, delivery, fallback, onRejected);
            _links.Add(link);
            return link;
        }
    }

    public void DiscardUnmatched(FlowOutputEndpoint output)
    {
        lock (Gate)
        {
            EnsureMutable();
            _discardUnmatched.Add(output);
        }
    }

    public FlowInputEndpoint GetInput(string address)
    {
        lock (Gate) return _components.Values.SelectMany(c => c.Inputs.Values)
            .FirstOrDefault(p => p.Address == address) ?? throw new KeyNotFoundException($"Input '{address}' was not found.");
    }

    public void RepliesFrom(FlowOutputEndpoint output, Predicate<FlowMessage>? condition)
    {
        lock (Gate)
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(output);
            if (output.Topology != this) throw new ArgumentException("Reply output belongs to another application.", nameof(output));
            _replies.Add(output, condition);
        }
    }

    public void ValidateRequestPorts(FlowInputEndpoint input, FlowOutputEndpoint output)
    {
        lock (Gate)
        {
            if (input.Topology != this || output.Topology != this || !_replies.ContainsKey(output))
                throw new ArgumentException("Request ports must belong to this application and the reply output must be configured with RepliesFrom.");
        }
    }

    public FlowOutputEndpoint GetOutput(string address)
    {
        lock (Gate) return _components.Values.SelectMany(c => c.Outputs.Values)
            .FirstOrDefault(p => p.Address == address) ?? throw new KeyNotFoundException($"Output '{address}' was not found.");
    }

    public FlowGraphSnapshot Freeze()
    {
        lock (Gate)
        {
            EnsureMutable();
            var layers = BuildDependencyLayers();
            _frozen = true;
            return new(_components.Values.ToArray(), _links.ToArray(), new(_discardUnmatched), new(_replies), layers);
        }
    }

    public FlowComponent[] Close()
    {
        lock (Gate)
        {
            _frozen = true;
            return _components.Values.ToArray();
        }
    }

    private FlowComponent[][] BuildDependencyLayers()
        => FlowDependencyOrder.TryCreate(_components.Values.ToArray(),
            _links.Where(link => !link.Input.IsControl).Select(link => (link.Output.Owner, link.Input.Owner)))
            ?? throw new InvalidOperationException("The application contains a cycle. Feedback execution is not supported.");

    private void EnsureMutable()
    {
        if (_frozen) throw new InvalidOperationException("Application topology is frozen.");
    }

}

internal sealed record FlowGraphSnapshot(FlowComponent[] Components, FlowLink[] Links,
    HashSet<FlowOutputEndpoint> DiscardUnmatched,
    Dictionary<FlowOutputEndpoint, Predicate<FlowMessage>?> Replies, FlowComponent[][] DependencyLayers);
