using System.Collections.Immutable;

namespace FluxFlow.Application.Addressing;

public sealed class ApplicationAddress : IEquatable<ApplicationAddress>
{
    private const string SystemSegment = "System";
    private const string EventsSegment = "Events";
    private const string DiagnosticsSegment = "Diagnostics";
    private const string OutputSegment = "Output";
    private const string ResourcesSegment = "Resources";
    private const string ConnectionsSegment = "Connections";
    private const char SegmentSeparator = '.';

    private static readonly ApplicationAddress EventsAddress = new(
        ApplicationAddressKind.SystemPort,
        [SystemSegment, EventsSegment, OutputSegment]);
    private static readonly ApplicationAddress DiagnosticsAddress = new(
        ApplicationAddressKind.SystemPort,
        [SystemSegment, DiagnosticsSegment, OutputSegment]);

    private readonly ImmutableArray<string> _segments;

    private ApplicationAddress(
        ApplicationAddressKind kind,
        ImmutableArray<string> segments)
    {
        Kind = kind;
        _segments = segments;
        Value = string.Join(SegmentSeparator, segments);
    }

    public ApplicationAddressKind Kind { get; }

    public string Value { get; }

    public IReadOnlyList<string> Segments => _segments;

    /// <summary>The containing workflow, or null for application resources and system ports.</summary>
    public string? WorkflowName => Kind is ApplicationAddressKind.WorkflowComponent or ApplicationAddressKind.WorkflowPort
        || (Kind == ApplicationAddressKind.Resource && _segments[0] != ResourcesSegment) ? _segments[0] : null;

    /// <summary>The component name for a component or workflow port; otherwise null.</summary>
    public string? ComponentName => Kind is ApplicationAddressKind.WorkflowComponent or ApplicationAddressKind.WorkflowPort
        ? _segments[1] : null;

    /// <summary>The port name for a workflow or system port; otherwise null.</summary>
    public string? PortName => Kind is ApplicationAddressKind.WorkflowPort or ApplicationAddressKind.SystemPort
        ? _segments[2] : null;

    /// <summary>This component's address, or the containing component of a workflow port; otherwise null.</summary>
    public ApplicationAddress? ComponentAddress => Kind switch
    {
        ApplicationAddressKind.WorkflowComponent => this,
        ApplicationAddressKind.WorkflowPort => new(ApplicationAddressKind.WorkflowComponent, [_segments[0], _segments[1]]),
        _ => null
    };

    public static ApplicationAddress SystemEvents => EventsAddress;

    public static ApplicationAddress SystemDiagnostics => DiagnosticsAddress;

    public static ApplicationAddress Parse(string value)
    {
        return FromSegments(ParseSegments(value, "Application address"));
    }

    private static ApplicationAddress FromSegments(ImmutableArray<string> segments)
    {
        if (string.Equals(segments[0], ResourcesSegment, StringComparison.Ordinal))
        {
            if (segments.Length < 2)
                throw new FormatException($"Resource addresses require at least one path segment after '{ResourcesSegment}'.");

            return new ApplicationAddress(ApplicationAddressKind.Resource, segments);
        }

        if (segments.Length >= 3 && string.Equals(segments[1], ResourcesSegment, StringComparison.Ordinal))
            return WorkflowResource(segments[0], segments.Skip(2).ToArray());

        if (string.Equals(segments[0], SystemSegment, StringComparison.Ordinal))
        {
            if (segments.SequenceEqual(EventsAddress._segments))
                return EventsAddress;
            if (segments.SequenceEqual(DiagnosticsAddress._segments))
                return DiagnosticsAddress;

            throw new FormatException(
                $"System addresses are limited to '{EventsAddress.Value}' and '{DiagnosticsAddress.Value}'.");
        }

        if (segments.Length == 2)
        {
            return new ApplicationAddress(
                ApplicationAddressKind.WorkflowComponent,
                segments);
        }

        if (segments.Length != 3)
        {
            throw new FormatException(
                "Workflow component and port addresses must use 'Workflow.Component' or 'Workflow.Component.Port'.");
        }

        return new ApplicationAddress(ApplicationAddressKind.WorkflowPort, segments);
    }

    public static bool TryParse(string? value, out ApplicationAddress? address)
    {
        try
        {
            address = Parse(value!);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            address = null;
            return false;
        }
    }

    public static ApplicationAddress Resource(params string[] path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0)
            throw new ArgumentException("Resource addresses require at least one path segment.", nameof(path));

        var builder = ImmutableArray.CreateBuilder<string>(path.Length + 1);
        builder.Add(ResourcesSegment);
        foreach (var segment in path)
            builder.Add(RequireConstructedSegment(segment, nameof(path), "Resource path segment"));

        return new ApplicationAddress(ApplicationAddressKind.Resource, builder.ToImmutable());
    }

    /// <summary>Resolves a root-relative resource path, with an optional Resources prefix.</summary>
    public static ApplicationAddress ResolveResource(string reference)
        => ResolveResourcePaths(reference)[0];

    public static bool TryResolveResource(string? reference, out ApplicationAddress? address)
    {
        try { address = ResolveResource(reference!); return true; }
        catch (Exception error) when (error is ArgumentException or FormatException)
        { address = null; return false; }
    }

    /// <summary>Constructs a resource address within a workflow, without restricting access.</summary>
    public static ApplicationAddress WorkflowResource(string workflow, params string[] path)
    {
        workflow = RequireWorkflowName(workflow, nameof(workflow));
        var resource = Resource(path);
        return new ApplicationAddress(ApplicationAddressKind.Resource, [workflow, .. resource._segments]);
    }

    /// <summary>Resolves full or relative JSON resource paths. A context can be a workflow
    /// name or the full address of a resource object. Explicit paths never use fallback.</summary>
    public static IReadOnlyList<ApplicationAddress> ResolveResourcePaths(string reference, string? currentScope = null)
    {
        var parts = ParseSegments(reference, "Resource reference").ToArray();
        if (parts[0].Equals(ResourcesSegment, StringComparison.OrdinalIgnoreCase))
            return [Create(null, parts.Skip(1).ToArray())];
        if (parts.Length > 1 && parts[1].Equals(ResourcesSegment, StringComparison.OrdinalIgnoreCase))
            return [Create(parts[0], parts.Skip(2).ToArray())];
        var application = Create(null, parts);
        if (currentScope is null) return [application];
        var scope = ParseSegments(currentScope, "Current scope").ToArray();
        if (scope.Length == 1 && !scope[0].Equals(ResourcesSegment, StringComparison.OrdinalIgnoreCase))
            return [Create(scope[0], parts), application];
        if (scope[0].Equals(ResourcesSegment, StringComparison.OrdinalIgnoreCase))
            return [Create(null, [.. scope.Skip(1), .. parts])];
        if (scope.Length > 1 && scope[1].Equals(ResourcesSegment, StringComparison.OrdinalIgnoreCase))
            return [Create(scope[0], [.. scope.Skip(2), .. parts])];
        throw new FormatException($"Scope '{currentScope}' must be a workflow or a full resource object address.");

        static ApplicationAddress Create(string? workflow, string[] path)
        {
            if (path.Length > 0 && path[0].Equals(ConnectionsSegment, StringComparison.OrdinalIgnoreCase))
                path[0] = ConnectionsSegment;
            return workflow is null ? Resource(path) : WorkflowResource(workflow, path);
        }
    }

    /// <summary>Creates a port address beneath this component.</summary>
    public ApplicationAddress Port(string name)
    {
        if (Kind != ApplicationAddressKind.WorkflowComponent)
            throw new ArgumentException("Use a workflow.component address.");
        return WorkflowPort(_segments[0], _segments[1], name);
    }

    public static ApplicationAddress WorkflowPort(
        string workflow,
        string component,
        string port)
    {
        return new ApplicationAddress(
            ApplicationAddressKind.WorkflowPort,
            [
                RequireWorkflowName(workflow, nameof(workflow)),
                RequireConstructedSegment(component, nameof(component), "Component name"),
                RequireConstructedSegment(port, nameof(port), "Port name")
            ]);
    }

    public static ApplicationAddress WorkflowComponent(
        string workflow,
        string component)
    {
        workflow = RequireWorkflowName(workflow, nameof(workflow));

        return new ApplicationAddress(
            ApplicationAddressKind.WorkflowComponent,
            [
                workflow,
                RequireConstructedSegment(component, nameof(component), "Component name")
            ]);
    }

    public static ApplicationAddress ResolvePort(string reference, string currentWorkflow)
    {
        currentWorkflow = RequireWorkflowName(
            currentWorkflow,
            nameof(currentWorkflow),
            "Current workflow name");

        var segments = ParseSegments(reference, "Port reference");
        if (segments.Length == 2)
        {
            if (string.Equals(segments[0], ResourcesSegment, StringComparison.Ordinal) ||
                string.Equals(segments[0], SystemSegment, StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"Address '{reference}' is not a local workflow port reference.");
            }

            return new ApplicationAddress(ApplicationAddressKind.WorkflowPort,
                [currentWorkflow, segments[0], segments[1]]);
        }
        if (segments.Length != 3)
        {
            throw new FormatException(
                "Port references must use 'Component.Port' or 'Workflow.Component.Port'.");
        }

        var address = FromSegments(segments);
        if (address.Kind == ApplicationAddressKind.Resource)
            throw new FormatException($"Resource address '{reference}' is not a port reference.");

        return address;
    }

    public static bool TryResolvePort(
        string? reference,
        string? currentWorkflow,
        out ApplicationAddress? address)
    {
        try
        {
            address = ResolvePort(reference!, currentWorkflow!);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            address = null;
            return false;
        }
    }

    public bool Equals(ApplicationAddress? other)
        => other is not null &&
           Kind == other.Kind &&
           string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is ApplicationAddress other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode(Value));

    public override string ToString() => Value;

    public static bool operator ==(ApplicationAddress? left, ApplicationAddress? right)
        => Equals(left, right);

    public static bool operator !=(ApplicationAddress? left, ApplicationAddress? right)
        => !Equals(left, right);

    private static ImmutableArray<string> ParseSegments(string value, string subject)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException($"{subject} cannot be empty.");
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new FormatException($"{subject} cannot have surrounding whitespace.");

        var segments = value.Split(SegmentSeparator);
        if (segments.Any(string.IsNullOrWhiteSpace))
            throw new FormatException($"{subject} cannot contain empty segments.");
        if (segments.Any(segment => !string.Equals(segment, segment.Trim(), StringComparison.Ordinal)))
            throw new FormatException($"{subject} segments cannot have surrounding whitespace.");

        return ImmutableArray.CreateRange(segments);
    }

    internal static string RequireWorkflowName(
        string? value,
        string parameterName,
        string role = "Workflow name")
    {
        var name = RequireConstructedSegment(value, parameterName, role);
        if (name is ResourcesSegment or SystemSegment)
            throw new ArgumentException($"{role} '{name}' is reserved by the application address space.", parameterName);
        return name;
    }

    internal static string RequireConstructedSegment(
        string? value,
        string parameterName,
        string role)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{role} cannot be empty.", parameterName);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException($"{role} cannot have surrounding whitespace.", parameterName);
        if (value.Contains(SegmentSeparator))
            throw new ArgumentException($"{role} cannot contain '{SegmentSeparator}'.", parameterName);

        return value;
    }
}
