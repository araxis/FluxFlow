namespace FluxFlow.Components.Nats;

public sealed record NatsComponentOptions
{
    public int Capacity { get; init; } = 128;
    public int MaxMessageBytes { get; init; } = 1048576;
    public int TimeoutSeconds { get; init; } = 30;
    public void Validate()
    {
        if (Capacity is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(Capacity));
        if (MaxMessageBytes is < 1 or > 67108864) throw new ArgumentOutOfRangeException(nameof(MaxMessageBytes));
        if (TimeoutSeconds is < 1 or > 600) throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds));
    }
    public static void ValidateSubject(string subject, bool subscription = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (subject.Any(char.IsWhiteSpace) || subject.Any(char.IsControl)) throw new ArgumentException("NATS subjects cannot contain whitespace or control characters.");
        var parts = subject.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0 || (part.Contains('*') || part.Contains('>')) &&
                (!subscription || !(part == "*" || part == ">" && i == parts.Length - 1)))
                throw new ArgumentException("Invalid NATS subject or wildcard position.");
        }
    }
}
