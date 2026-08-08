using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Designer;

public sealed class ComponentRegistrationBuilder : RuntimeComponentRegistrationBuilder
{
    private readonly List<OptionDesignMetadata> options = [];
    private readonly List<ResourceDesignMetadata> resources = [];
    private readonly List<PortDesignMetadata> ports = [];
    private readonly Dictionary<ComponentAttributeName, ComponentAttributeValue> attributes = [];
    private ComponentMetadataText? displayName;
    private ComponentCategory? category;
    private ComponentMetadataText? summary;
    private ComponentIconKey? iconKey;
    private ComponentPreferredNodeName? preferredNodeName;
    private int? suggestedEditorWidth;

    internal ComponentRegistrationBuilder(string type)
        : base(type)
    {
    }

    public new DesignedComponentBindingBuilder<TNode> UseFactory<TNode>(
        Func<ComponentActivationContext, TNode> value)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(value);
        var runtime = UseTypedFactory(
            value,
            context => ValueTask.FromResult(CreateNodeActivation(value(context))));
        return new DesignedComponentBindingBuilder<TNode>(this, runtime);
    }

    public new DesignedComponentBindingBuilder<TNode> UseFactory<TNode>(
        Func<ComponentActivationContext, ValueTask<TNode>> value)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(value);
        var runtime = UseTypedFactory(value, async context =>
            CreateNodeActivation(await value(context).ConfigureAwait(false)));
        return new DesignedComponentBindingBuilder<TNode>(this, runtime);
    }

    public new DesignedComponentBindingBuilder<TNode> UseFactory<TNode>(
        Func<ComponentActivationContext, ComponentNodeActivation<TNode>> value)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(value);
        var runtime = UseTypedFactory(value, context => ValueTask.FromResult(value(context)));
        return new DesignedComponentBindingBuilder<TNode>(this, runtime);
    }

    public new DesignedComponentBindingBuilder<TNode> UseFactory<TNode>(
        Func<ComponentActivationContext, ValueTask<ComponentNodeActivation<TNode>>> value)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(value);
        return new DesignedComponentBindingBuilder<TNode>(this, UseTypedFactory(value, value));
    }

    public new DesignedComponentInstanceBindingBuilder UseInstanceFactory(ComponentFactory value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new DesignedComponentInstanceBindingBuilder(this, UseAdvancedFactory(value));
    }

    private ComponentNodeActivation<TNode> CreateNodeActivation<TNode>(TNode node)
        where TNode : IFlowNode
    {
        if (node is null)
        {
            throw new InvalidOperationException(
                $"Factory for component type '{Type}' returned a null node.");
        }

        return new ComponentNodeActivation<TNode>(node);
    }

    public void WithDisplay(
        string? displayName = null,
        string? category = null,
        string? summary = null,
        string? iconKey = null,
        string? preferredNodeName = null,
        int? suggestedEditorWidth = null)
    {
        this.displayName = ToText(displayName);
        this.category = category is null ? null : new ComponentCategory(category);
        this.summary = ToText(summary);
        this.iconKey = iconKey is null ? null : new ComponentIconKey(iconKey);
        this.preferredNodeName = preferredNodeName is null
            ? null
            : new ComponentPreferredNodeName(preferredNodeName);
        this.suggestedEditorWidth = suggestedEditorWidth;
    }

    public void AddOption<TValue>(
        string name,
        OptionValueKind kind,
        string? displayName = null,
        string? helperText = null,
        bool isRequired = false,
        object? defaultValue = null,
        double? min = null,
        double? max = null,
        string? section = null,
        string? importance = null,
        string? editor = null,
        string? syntax = null,
        string? relatedResource = null)
    {
        base.AddOption<TValue>(name, isRequired);
        ReplaceOption(name, option => option with
        {
            Kind = kind,
            DisplayName = ToText(displayName),
            HelperText = ToText(helperText),
            IsRequired = isRequired,
            DefaultValue = defaultValue,
            Min = min,
            Max = max,
            Attributes = OptionDesignMetadataAttributes.CreateMap(
                section,
                importance,
                editor,
                syntax,
                relatedResource)
        });
    }

    public void AddOptionChoice(
        string optionName,
        string value,
        string? displayName = null,
        string? helperText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ReplaceOption(optionName, option =>
        {
            var normalized = value.Trim();
            if (option.Choices.Any(choice => string.Equals(
                    choice.Value.Value,
                    normalized,
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Option '{optionName}' choice '{normalized}' is already registered.");
            }

            return option with
            {
                Choices =
                [
                    .. option.Choices,
                    new OptionChoiceMetadata
                    {
                        Value = new ComponentOptionChoiceValue(normalized),
                        DisplayName = ToText(displayName),
                        HelperText = ToText(helperText)
                    }
                ]
            };
        });
    }

    public void SetOptionAttribute(string optionName, string name, string value)
        => ReplaceOption(optionName, option => option with
        {
            Attributes = SetAttribute(option.Attributes, name, value)
        });

    public void AddResource<TService>(
        string name,
        string? displayName,
        int order = 0,
        string? summary = null,
        bool isRequired = false,
        string? designValueType = null,
        string? runtimeValueTypeHint = null,
        string? ownership = null,
        string? pickerKind = null,
        string? keyPattern = null,
        string? option = null,
        string? requiredWhenAnyOption = null)
    {
        base.AddResource<TService>(name, isRequired, runtimeValueTypeHint);
        ReplaceResource(name, resource => resource with
        {
            DisplayName = ToText(displayName),
            Order = order,
            Summary = ToText(summary),
            ValueType = designValueType is null ? resource.ValueType : new ComponentValueTypeHint(designValueType),
            IsRequired = isRequired,
            Attributes = CreateResourceAttributes(
                ownership,
                pickerKind,
                keyPattern,
                option,
                requiredWhenAnyOption)
        });
    }

    public void SetResourceAttribute(string resourceName, string name, string value)
        => ReplaceResource(resourceName, resource => resource with
        {
            Attributes = SetAttribute(resource.Attributes, name, value)
        });

    public void SetPortAttribute(
        string portName,
        PortDirection direction,
        string name,
        string value)
        => ReplacePort(portName, direction, port => port with
        {
            Attributes = SetAttribute(port.Attributes, name, value)
        });

    public void AddAttribute(string name, string value)
    {
        var key = new ComponentAttributeName(name);
        if (!attributes.TryAdd(key, new ComponentAttributeValue(value)))
            throw new InvalidOperationException($"Component attribute '{key}' is already registered.");
    }

    internal ComponentDesignMetadata CreateMetadata()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType(Type),
            DisplayName = displayName,
            Category = category,
            Summary = summary,
            IconKey = iconKey,
            PreferredNodeName = preferredNodeName,
            SuggestedEditorWidth = suggestedEditorWidth,
            ProcessingCapabilities = ProcessingCapabilities,
            Options = options,
            Resources = resources,
            Ports = ports,
            Attributes = attributes
        };

        return ComponentDesignMetadataFinalizer.Finalize(metadata);
    }

    internal ComponentDesignDeclaration CreateDeclaration()
        => new(CreateDescriptor(), CreateMetadata());

    internal void CopyRuntimeTo(RuntimeComponentRegistrationBuilder target)
        => CopyRuntimeConfigurationTo(target);

    protected override void OnInputAdded(ComponentPortMetadata port)
        => ports.Add(CreatePort(port, PortDirection.Input));

    protected override void OnOutputAdded(ComponentPortMetadata port)
        => ports.Add(CreatePort(port, PortDirection.Output));

    protected override void OnOptionAdded(ComponentOptionMetadata option)
        => options.Add(new OptionDesignMetadata
        {
            Name = new ComponentOptionName(option.Name),
            Kind = InferOptionKind(option.ValueType),
            IsRequired = option.IsRequired
        });

    protected override void OnResourceAdded(ComponentResourceMetadata resource)
        => resources.Add(new ResourceDesignMetadata
        {
            Name = new ComponentResourceName(resource.Name),
            Order = resources.Count,
            ValueType = new ComponentValueTypeHint(
                resource.ValueTypeHint ?? ToValueTypeHint(resource.ServiceType)),
            IsRequired = resource.IsRequired
        });

    private static OptionValueKind InferOptionKind(Type valueType)
    {
        var type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        if (type == typeof(bool))
            return OptionValueKind.Boolean;
        if (type.IsEnum)
            return OptionValueKind.Enum;
        if (type == typeof(TimeSpan))
            return OptionValueKind.Duration;
        if (type == typeof(byte) || type == typeof(short) || type == typeof(int) ||
            type == typeof(long) || type == typeof(float) || type == typeof(double) ||
            type == typeof(decimal))
        {
            return OptionValueKind.Number;
        }

        return type.Namespace?.StartsWith("System.Text.Json", StringComparison.Ordinal) == true
            ? OptionValueKind.Json
            : OptionValueKind.Text;
    }

    private PortDesignMetadata CreatePort(ComponentPortMetadata port, PortDirection direction)
        => new()
        {
            Name = new ComponentPortName(port.Name),
            Direction = direction,
            DisplayName = new ComponentMetadataText(port.Name),
            Order = ports.Count(candidate => candidate.Direction == direction),
            ValueType = new ComponentValueTypeHint(ToValueTypeHint(port.MessageType)),
            MessageType = port.MessageType,
            Kind = port.Kind,
            LinkCardinality = port.LinkCardinality,
            Attributes = port.Kind == ComponentPortKind.Signal
                ? PortDesignMetadataAttributes.CreateSignalMap()
                : new Dictionary<ComponentAttributeName, ComponentAttributeValue>()
        };

    internal void DescribePort(
        string name,
        PortDirection direction,
        string? displayName,
        string? group,
        int? order,
        string? summary,
        bool? isPrimary)
        => ReplacePort(name, direction, port => port with
        {
            DisplayName = displayName is null ? port.DisplayName : ToText(displayName),
            Group = group is null ? port.Group : new ComponentPortGroup(group),
            Order = order ?? port.Order,
            Summary = summary is null ? port.Summary : ToText(summary),
            IsPrimary = isPrimary ?? port.IsPrimary
        });

    private void ReplaceOption(string name, Func<OptionDesignMetadata, OptionDesignMetadata> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var index = options.FindIndex(option => string.Equals(
            option.Name.Value,
            name.Trim(),
            StringComparison.Ordinal));
        if (index < 0)
            throw new InvalidOperationException($"Component option '{name.Trim()}' is not registered.");

        options[index] = update(options[index]);
    }

    private void ReplaceResource(string name, Func<ResourceDesignMetadata, ResourceDesignMetadata> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var index = resources.FindIndex(resource => string.Equals(
            resource.Name.Value,
            name.Trim(),
            StringComparison.Ordinal));
        if (index < 0)
            throw new InvalidOperationException($"Component resource '{name.Trim()}' is not registered.");

        resources[index] = update(resources[index]);
    }

    private void ReplacePort(
        string name,
        PortDirection direction,
        Func<PortDesignMetadata, PortDesignMetadata> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var index = ports.FindIndex(port =>
            port.Direction == direction &&
            string.Equals(port.Name.Value, name.Trim(), StringComparison.Ordinal));
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Component {direction.ToString().ToLowerInvariant()} port '{name.Trim()}' is not registered.");
        }

        ports[index] = update(ports[index]);
    }

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue>
        CreateResourceAttributes(
            string? ownership,
            string? pickerKind,
            string? keyPattern,
            string? option,
            string? requiredWhenAnyOption)
    {
        var result = new Dictionary<ComponentAttributeName, ComponentAttributeValue>();
        AddIfPresent(result, ResourceDesignMetadataAttributeNames.Ownership, ownership);
        AddIfPresent(result, ResourceDesignMetadataAttributeNames.PickerKind, pickerKind);
        AddIfPresent(result, ResourceDesignMetadataAttributeNames.KeyPattern, keyPattern);
        AddIfPresent(result, ResourceDesignMetadataAttributeNames.Option, option);
        AddIfPresent(
            result,
            ResourceDesignMetadataAttributeNames.RequiredWhenAnyOption,
            requiredWhenAnyOption);
        return result;
    }

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> SetAttribute(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> current,
        string name,
        string value)
    {
        var result = new Dictionary<ComponentAttributeName, ComponentAttributeValue>(current)
        {
            [new ComponentAttributeName(name)] = new ComponentAttributeValue(value)
        };
        return result;
    }

    private static void AddIfPresent(
        IDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name,
        string? value)
    {
        if (value is not null)
            attributes.Add(new ComponentAttributeName(name), new ComponentAttributeValue(value));
    }

    private static ComponentMetadataText? ToText(string? value)
        => value is null ? null : new ComponentMetadataText(value);

    private static string ToValueTypeHint(Type type)
    {
        if (type.IsArray)
            return $"{ToValueTypeHint(type.GetElementType()!)}[]";
        if (!type.IsGenericType)
            return type.Name;

        var tick = type.Name.IndexOf('`', StringComparison.Ordinal);
        var name = tick < 0 ? type.Name : type.Name[..tick];
        return $"{name}<{string.Join(",", type.GetGenericArguments().Select(ToValueTypeHint))}>";
    }
}
