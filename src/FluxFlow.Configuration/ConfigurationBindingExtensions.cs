using System.Reflection;
using FluxFlow.Configuration.Links;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Configuration;

/// <summary>Reads a component DTO with optional OneOrManyLinks port properties.</summary>
public static class ConfigurationBindingExtensions
{
    /// <summary>
    /// Reads public writable top-level properties. Port links use their dedicated reader;
    /// all other properties use the standard .NET configuration binder.
    /// </summary>
    public static T? ReadConfiguration<T>(this IConfigurationSection section) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(section);
        var children = section.GetChildren().ToArray();
        if (children.Length == 0 && section.Value is null) return null;

        var result = new T();
        foreach (var property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.SetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0) continue;
            var name = property.GetCustomAttribute<ConfigurationKeyNameAttribute>()?.Name ?? property.Name;
            var value = children.FirstOrDefault(child => child.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (value is null) continue;
            var bound = property.PropertyType == typeof(OneOrManyLinks)
                ? OneOrManyLinks.Read(value)
                : value.Get(property.PropertyType, options => options.ErrorOnUnknownConfiguration = true);
            if (bound is not null) property.SetValue(result, bound);
        }
        return result;
    }
}
