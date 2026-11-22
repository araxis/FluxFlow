using FluxFlow.Configuration.Application;
using System.Reflection;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Application.Addressing;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Configuration.Components;

/// <summary>Reads a component DTO with single or multiple port-link properties.</summary>
public static class ConfigurationBindingExtensions
{
    /// <summary>Reads a required component DTO, reporting the section path when it is missing.</summary>
    public static T ReadRequiredConfiguration<T>(this IConfigurationSection section) where T : class, new()
        => section.ReadConfiguration<T>()
            ?? throw new ArgumentException($"Missing {typeof(T).Name} at '{section.Path}'.", nameof(section));

    /// <summary>
    /// Reads public writable top-level properties. Port links use their dedicated reader;
    /// all other properties use the standard .NET configuration binder.
    /// </summary>
    public static T? ReadConfiguration<T>(this IConfigurationSection section) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.Value is not null)
            throw new ArgumentException($"'{section.Path}' requires child sections, not a scalar value.", nameof(section));
        var children = section.GetChildren().ToArray();
        if (children.Length == 0) return null;

        var result = new T();
        ComponentAddress? component = null;
        foreach (var property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.SetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0) continue;
            var name = property.GetCustomAttribute<ConfigurationKeyNameAttribute>()?.Name ?? property.Name;
            var value = children.FirstOrDefault(child => child.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (value is null) continue;
            object? bound;
            if (property.PropertyType == typeof(OneOrManyPortReferences))
                bound = OneOrManyPortReferences.Read(value, component ??= section.GetComponentAddress());
            else if (property.PropertyType == typeof(PortReference))
                bound = PortReference.Read(value, component ??= section.GetComponentAddress());
            else
                bound = value.Get(property.PropertyType, options => options.ErrorOnUnknownConfiguration = true);
            if (bound is not null) property.SetValue(result, bound);
        }
        return result;
    }
}
