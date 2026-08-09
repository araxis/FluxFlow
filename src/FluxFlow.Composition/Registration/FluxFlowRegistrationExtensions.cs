using FluxFlow.Composition.Authoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Composition;

public static class FluxFlowRegistrationExtensions
{
    public static FluxFlowRegistrationBuilder AddFluxFlowComponents(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddComponentCatalog(services);
        return new FluxFlowRegistrationBuilder(services);
    }

    public static FluxFlowRegistrationBuilder AddComponent(
        this FluxFlowRegistrationBuilder builder,
        ComponentContract component)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(component);
        RegisterDescriptor(
            builder.Services,
            component.Descriptor,
            requireReferenceMatch: true);
        return builder;
    }

    internal static ComponentDescriptor RegisterDescriptor(
        IServiceCollection services,
        ComponentDescriptor descriptor,
        bool requireReferenceMatch = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(descriptor);

        var existing = services
            .Where(static registration => registration.ServiceType == typeof(ComponentDescriptor))
            .Select(static registration => registration.ImplementationInstance)
            .OfType<ComponentDescriptor>()
            .SingleOrDefault(candidate => string.Equals(
                candidate.Type,
                descriptor.Type,
                StringComparison.Ordinal));

        if (existing is not null)
        {
            if (ReferenceEquals(existing, descriptor) ||
                (!requireReferenceMatch && ComponentDescriptorsMatch(existing, descriptor)))
            {
                return existing;
            }

            throw new InvalidOperationException(
                $"Component type '{descriptor.Type}' has a conflicting descriptor registration.");
        }

        services.AddSingleton(descriptor);
        AddComponentCatalog(services);
        return descriptor;
    }

    private static void AddComponentCatalog(IServiceCollection services)
        => services.TryAddSingleton(static provider => new ComponentCatalog(
            provider.GetServices<ComponentDescriptor>()));

    private static bool ComponentDescriptorsMatch(
        ComponentDescriptor left,
        ComponentDescriptor right)
        => string.Equals(left.Type, right.Type, StringComparison.Ordinal) &&
           Equals(left.RegistrationFactory, right.RegistrationFactory) &&
           left.RegistrationFactoryMode == right.RegistrationFactoryMode &&
           BindingsMatch(left.RegistrationBindings, right.RegistrationBindings) &&
           left.ProcessingCapabilities == right.ProcessingCapabilities &&
           DictionariesMatch(left.Inputs, right.Inputs, PortsMatch) &&
           DictionariesMatch(left.Outputs, right.Outputs, PortsMatch) &&
           DictionariesMatch(left.Options, right.Options, OptionsMatch) &&
           DictionariesMatch(left.Resources, right.Resources, ResourcesMatch);

    private static bool BindingsMatch(
        IReadOnlyList<ComponentBindingIdentity> left,
        IReadOnlyList<ComponentBindingIdentity> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            var leftBinding = left[index];
            var rightBinding = right[index];
            if (leftBinding.Role != rightBinding.Role ||
                !PortsMatch(leftBinding.Metadata, rightBinding.Metadata) ||
                !Equals(leftBinding.Selector, rightBinding.Selector))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DictionariesMatch<T>(
        IReadOnlyDictionary<string, T> left,
        IReadOnlyDictionary<string, T> right,
        Func<T, T, bool> valuesMatch)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var item in left)
        {
            if (!right.TryGetValue(item.Key, out var candidate) ||
                !valuesMatch(item.Value, candidate))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PortsMatch(ComponentPortMetadata left, ComponentPortMetadata right)
        => string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
           left.MessageType == right.MessageType &&
           left.LinkCardinality == right.LinkCardinality &&
           left.Kind == right.Kind;

    private static bool OptionsMatch(ComponentOptionMetadata left, ComponentOptionMetadata right)
        => string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
           left.ValueType == right.ValueType &&
           left.IsRequired == right.IsRequired;

    private static bool ResourcesMatch(ComponentResourceMetadata left, ComponentResourceMetadata right)
        => string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
           left.ServiceType == right.ServiceType &&
           left.IsRequired == right.IsRequired &&
           string.Equals(left.ValueTypeHint, right.ValueTypeHint, StringComparison.Ordinal);
}
