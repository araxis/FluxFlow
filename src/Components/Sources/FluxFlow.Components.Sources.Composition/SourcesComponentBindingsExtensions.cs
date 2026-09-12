using System.Text.Json;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Json;
using FluxFlow.Json.Model;

namespace FluxFlow.Components.Sources.Composition;

/// <summary>Optional document bindings over the independently constructible source components.</summary>
public static class SourcesComponentBindingsExtensions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Registers direct source constructors. Clocks are borrowed. Configured clock references
    /// require an explicit resolver; otherwise the supplied default clock (or system clock) is used.
    /// Processing-profile references are not interpreted by this direct binding.
    /// </summary>
    public static ComponentBindings AddSources(this ComponentBindings bindings, TimeProvider? clock = null,
        Func<string, TimeProvider?>? resolveClock = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        bindings.Add(SourcesComponentDefinition.Types.Generated, definition =>
        {
            var sourceClock = ResolveClock(definition, clock, resolveClock);
            definition.Properties.TryGetValue(SourcesComponentDefinition.Options.Items, out var items);
            return GeneratedSourceNode.Create(Bind<GeneratedSourceOptions>(definition), items, sourceClock);
        });
        bindings.Add(SourcesComponentDefinition.Types.Sequence, definition =>
            new SequenceSourceNode(Bind<SequenceSourceOptions>(definition), ResolveClock(definition, clock, resolveClock)));
        return bindings;
    }

    private static T Bind<T>(ComponentDefinition definition)
        => JsonSerializer.SerializeToElement(definition.Properties).Deserialize<T>(Options)
            ?? throw new JsonException($"Options for '{definition.Type}' cannot be null.");

    private static TimeProvider? ResolveClock(ComponentDefinition definition, TimeProvider? clock,
        Func<string, TimeProvider?>? resolveClock)
    {
        if (definition.Properties.Keys.Any(key => string.Equals(key, "processing", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Direct source bindings require explicit source options, not a processing-profile reference.");
        if (!definition.Properties.TryGetValue(SourcesComponentDefinition.Resources.Clock, out var reference))
            return clock;
        if (reference.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(reference.GetString()))
            throw new JsonException("A source clock reference must be a non-empty string.");
        var key = reference.GetString()!.Trim();
        return resolveClock?.Invoke(key)
            ?? throw new InvalidOperationException($"Source clock '{key}' was not resolved.");
    }
}
