namespace FluxFlow.Components.Routing.Nodes;

/// <summary>
/// Validates the configured fan-out / fan-in port names a routing node exposes. Mirrors
/// the rules the old engine options-reader enforced (non-empty, no duplicates, not a
/// built-in port, a well-formed identifier) so port misconfiguration still fails fast at
/// construction — but without depending on the engine's PortName type.
/// </summary>
internal static class RoutingPortNames
{
    public static void Validate(
        string nodeType,
        string optionName,
        IReadOnlyCollection<string> portNames,
        IReadOnlyCollection<string> reservedPorts)
    {
        if (portNames.Count == 0)
        {
            throw new ArgumentException(
                $"{nodeType} option '{optionName}' must contain at least one value.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reserved = reservedPorts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var portName in portNames)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new ArgumentException(
                    $"{nodeType} option '{optionName}' cannot contain empty values.");
            }

            var normalized = portName.Trim();
            if (!seen.Add(normalized))
            {
                throw new ArgumentException(
                    $"{nodeType} option '{optionName}' contains duplicate port '{normalized}'.");
            }

            if (reserved.Contains(normalized))
            {
                throw new ArgumentException(
                    $"{nodeType} option '{optionName}' cannot use built-in port '{normalized}'.");
            }

            if (!IsValidPort(normalized))
            {
                throw new ArgumentException(
                    $"{nodeType} option '{optionName}' contains invalid port '{normalized}'.");
            }
        }
    }

    // A port name is a simple identifier: it must start with a letter or underscore and
    // contain only letters, digits, or underscores. This rejects dotted/spaced names such
    // as "Bad.Port", matching the old PortName parsing rule.
    private static bool IsValidPort(string value)
    {
        if (!(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(char.IsLetterOrDigit(character) || character == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
