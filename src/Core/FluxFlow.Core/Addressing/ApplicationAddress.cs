using System.Collections.Immutable;

namespace FluxFlow.Core.Addressing;

public sealed class ApplicationAddress : IEquatable<ApplicationAddress>
{
    private static readonly ApplicationAddress EventsAddress = new(
        ApplicationAddressKind.SystemPort,
        ["System", "Events", "Output"]);
    private static readonly ApplicationAddress DiagnosticsAddress = new(
        ApplicationAddressKind.SystemPort,
        ["System", "Diagnostics", "Output"]);

    private readonly ImmutableArray<string> _segments;

    private ApplicationAddress(
        ApplicationAddressKind kind,
        ImmutableArray<string> segments)
    {
        Kind = kind;
        _segments = segments;
        Value = string.Join('.', segments);
    }

    public ApplicationAddressKind Kind { get; }

    public string Value { get; }

    public IReadOnlyList<string> Segments => _segments;

    public static ApplicationAddress SystemEvents => EventsAddress;

    public static ApplicationAddress SystemDiagnostics => DiagnosticsAddress;

    public static ApplicationAddress Parse(string value)
    {
        var segments = ParseSegments(value, "Application address");
        if (string.Equals(segments[0], "Resources", StringComparison.Ordinal))
        {
            if (segments.Length < 2)
                throw new FormatException("Resource addresses require at least one path segment after 'Resources'.");

            return new ApplicationAddress(ApplicationAddressKind.Resource, segments);
        }

        if (string.Equals(segments[0], "System", StringComparison.Ordinal))
        {
            if (segments.SequenceEqual(EventsAddress._segments))
                return EventsAddress;
            if (segments.SequenceEqual(DiagnosticsAddress._segments))
                return DiagnosticsAddress;

            throw new FormatException(
                "System addresses are limited to 'System.Events.Output' and 'System.Diagnostics.Output'.");
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
        builder.Add("Resources");
        foreach (var segment in path)
            builder.Add(RequireConstructedSegment(segment, nameof(path), "Resource path segment"));

        return new ApplicationAddress(ApplicationAddressKind.Resource, builder.ToImmutable());
    }

    public static ApplicationAddress WorkflowPort(
        string workflow,
        string component,
        string port)
    {
        var componentAddress = WorkflowComponent(workflow, component);

        return new ApplicationAddress(
            ApplicationAddressKind.WorkflowPort,
            [
                componentAddress._segments[0],
                componentAddress._segments[1],
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
            if (string.Equals(segments[0], "Resources", StringComparison.Ordinal) ||
                string.Equals(segments[0], "System", StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"Address '{reference}' is not a local workflow port reference.");
            }

            return WorkflowPort(currentWorkflow, segments[0], segments[1]);
        }
        if (segments.Length != 3)
        {
            throw new FormatException(
                "Port references must use 'Component.Port' or 'Workflow.Component.Port'.");
        }

        var address = Parse(reference);
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

        var segments = value.Split('.');
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
        if (name is "Resources" or "System")
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
        if (value.Contains('.'))
            throw new ArgumentException($"{role} cannot contain '.'.", parameterName);

        return value;
    }
}
