using System.Collections;
using System.Text.Json;
using FluxFlow.Application.Addressing;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Configuration.Application.Links;

/// <summary>One port reference or a list of references, each written as an address or an object.</summary>
public sealed class OneOrManyPortReferences : IReadOnlyList<PortReference>
{
    private readonly IReadOnlyList<PortReference> _links;

    private OneOrManyPortReferences(IReadOnlyList<PortReference> links) => _links = links;

    public static OneOrManyPortReferences Empty { get; } = new(Array.Empty<PortReference>());
    public int Count => _links.Count;
    public PortReference this[int index] => _links[index];
    public IEnumerator<PortReference> GetEnumerator() => _links.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static OneOrManyPortReferences Read(IConfigurationSection value, ComponentAddress component)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(component);
        var children = value.GetChildren().ToArray();
        if (value.Value is not null && children.Length != 0)
            throw new JsonException($"{value.Path}: port references cannot contain both a scalar value and child fields");
        var isArray = children.Length != 0 && children.Select((child, index) =>
            child.Key == index.ToString(System.Globalization.CultureInfo.InvariantCulture)).All(match => match);
        if (!isArray) return new OneOrManyPortReferences(new[] { PortReference.Read(value, component) });

        var declarations = new List<PortReference>();
        var errors = new List<string>();
        for (var index = 0; index < children.Length; index++)
        {
            try { declarations.Add(PortReference.Read(children[index], component, $"{value.Path}[{index}]")); }
            catch (JsonException error) { errors.Add(error.Message); }
        }
        if (errors.Count != 0) throw new JsonException(string.Join("; ", errors));
        return new OneOrManyPortReferences(declarations.AsReadOnly());
    }
}
