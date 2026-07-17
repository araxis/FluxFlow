using System.Collections.Immutable;

namespace FluxFlow.Data;

public sealed class FlowContentCodecCatalog
{
    private readonly ImmutableDictionary<string, IFlowContentCodec> _exact;
    private readonly ImmutableDictionary<string, IFlowContentCodec> _suffixes;
    private readonly ImmutableDictionary<string, IFlowContentCodec> _families;
    private readonly IFlowContentCodec _fallback;

    public FlowContentCodecCatalog(
        IEnumerable<FlowContentCodecRegistration> registrations,
        IFlowContentCodec fallback)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));

        var exact = ImmutableDictionary.CreateBuilder<string, IFlowContentCodec>(StringComparer.OrdinalIgnoreCase);
        var suffixes = ImmutableDictionary.CreateBuilder<string, IFlowContentCodec>(StringComparer.OrdinalIgnoreCase);
        var families = ImmutableDictionary.CreateBuilder<string, IFlowContentCodec>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            var target = registration.Match switch
            {
                FlowContentCodecMatch.ExactMediaType => exact,
                FlowContentCodecMatch.StructuredSuffix => suffixes,
                FlowContentCodecMatch.MediaFamily => families,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(registrations), registration.Match, "Unknown content codec match kind.")
            };

            var key = NormalizeKey(registration.Match, registration.Key);
            if (!target.TryAdd(key, registration.Codec))
            {
                throw new ArgumentException(
                    $"A content codec is already registered for {registration.Match} key '{key}'.",
                    nameof(registrations));
            }
        }

        _exact = exact.ToImmutable();
        _suffixes = suffixes.ToImmutable();
        _families = families.ToImmutable();
    }

    public static FlowContentCodecCatalog CreateDefault()
    {
        var json = new JsonFlowContentCodec();
        return new FlowContentCodecCatalog(
        [
            new(FlowContentCodecMatch.ExactMediaType, "application/json", json),
            new(FlowContentCodecMatch.StructuredSuffix, "json", json),
            new(FlowContentCodecMatch.MediaFamily, "text", new TextFlowContentCodec())
        ],
        new BinaryFlowContentCodec());
    }

    public IFlowContentCodec Resolve(string? contentType)
    {
        var parsed = FlowMediaType.Parse(contentType);
        if (parsed.MediaType is not null && _exact.TryGetValue(parsed.MediaType, out var exact))
            return exact;
        if (parsed.StructuredSuffix is not null &&
            _suffixes.TryGetValue(parsed.StructuredSuffix, out var suffix))
        {
            return suffix;
        }
        if (parsed.Family is not null && _families.TryGetValue(parsed.Family, out var family))
            return family;

        return _fallback;
    }

    internal FlowValue Decode(
        ImmutableArray<byte> content,
        string? contentType,
        string? declaredEncoding)
    {
        var parsed = FlowMediaType.Parse(contentType);
        return Resolve(contentType).Decode(content, NormalizeOptional(declaredEncoding) ?? parsed.Charset);
    }

    private static string NormalizeKey(FlowContentCodecMatch match, string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (match == FlowContentCodecMatch.StructuredSuffix)
            normalized = normalized.TrimStart('+');

        if (normalized.Length == 0 || normalized.Contains(';'))
            throw new ArgumentException($"Content codec key '{value}' is invalid.", nameof(value));

        if (match == FlowContentCodecMatch.ExactMediaType &&
            (normalized.Count(character => character == '/') != 1 || normalized.StartsWith('/') || normalized.EndsWith('/')))
        {
            throw new ArgumentException(
                $"Exact content codec key '{value}' must be a media type.",
                nameof(value));
        }

        if (match != FlowContentCodecMatch.ExactMediaType && normalized.Contains('/'))
            throw new ArgumentException($"Content codec key '{value}' must be one segment.", nameof(value));

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
}

internal readonly record struct FlowMediaType(
    string? MediaType,
    string? Family,
    string? StructuredSuffix,
    string? Charset)
{
    public static FlowMediaType Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return default;

        var segments = value.Split(';');
        var mediaType = segments[0].Trim().ToLowerInvariant();
        var slash = mediaType.IndexOf('/');
        if (slash <= 0 || slash == mediaType.Length - 1)
            return new FlowMediaType(null, null, null, ReadCharset(segments));

        var family = mediaType[..slash];
        var subtype = mediaType[(slash + 1)..];
        var suffixIndex = subtype.LastIndexOf('+');
        var suffix = suffixIndex >= 0 && suffixIndex < subtype.Length - 1
            ? subtype[(suffixIndex + 1)..]
            : null;

        return new FlowMediaType(mediaType, family, suffix, ReadCharset(segments));
    }

    private static string? ReadCharset(IReadOnlyList<string> segments)
    {
        for (var index = 1; index < segments.Count; index++)
        {
            var separator = segments[index].IndexOf('=');
            if (separator <= 0)
                continue;

            var name = segments[index][..separator].Trim();
            if (!string.Equals(name, "charset", StringComparison.OrdinalIgnoreCase))
                continue;

            var charset = segments[index][(separator + 1)..].Trim().Trim('"');
            return charset.Length == 0 ? null : charset;
        }

        return null;
    }
}
