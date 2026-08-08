using System.Text.Json.Serialization.Metadata;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Engine.DurableOutput;

public sealed class DurableOutputRegistrationBuilder
{
    private readonly Dictionary<ApplicationAddress, DurableOutputCaptureDefinition> _byAddress = [];
    private readonly Dictionary<string, Type> _contractTypes = new(StringComparer.Ordinal);

    public DurableOutputRegistrationBuilder Capture<T>(
        string output,
        string contractName,
        JsonTypeInfo<T> jsonTypeInfo)
        => Capture(ApplicationAddress.Parse(output), contractName, jsonTypeInfo);

    public DurableOutputRegistrationBuilder Capture<T>(
        OutputPortHandle<T> output,
        string contractName,
        JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Capture(output.Address, contractName, jsonTypeInfo);
    }

    public DurableOutputRegistrationBuilder Capture<T>(
        ApplicationAddress output,
        string contractName,
        JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (output.Kind != ApplicationAddressKind.WorkflowPort)
            throw new ArgumentException("Durable output capture requires a workflow port address.", nameof(output));
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        if (!string.Equals(contractName, contractName.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Contract name cannot have surrounding whitespace.", nameof(contractName));
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        var definition = new DurableOutputCaptureDefinition(
            output,
            contractName,
            typeof(T),
            jsonTypeInfo);
        if (_byAddress.TryGetValue(output, out var current))
        {
            if (current.IsEquivalentTo(definition))
                return this;

            throw new InvalidOperationException(
                $"Durable output '{output}' is already captured with a different contract or payload type.");
        }

        if (_contractTypes.TryGetValue(contractName, out var payloadType) && payloadType != typeof(T))
        {
            throw new InvalidOperationException(
                $"Durable output contract '{contractName}' is already used for payload type '{payloadType}'.");
        }

        _byAddress.Add(output, definition);
        _contractTypes.TryAdd(contractName, typeof(T));
        return this;
    }

    internal DurableOutputConfiguration Build()
    {
        if (_byAddress.Count == 0)
            throw new InvalidOperationException("At least one durable output capture must be configured.");

        return new DurableOutputConfiguration(
            new Dictionary<ApplicationAddress, DurableOutputCaptureDefinition>(_byAddress));
    }
}

internal sealed record DurableOutputCaptureDefinition(
    ApplicationAddress Address,
    string ContractName,
    Type PayloadType,
    JsonTypeInfo JsonTypeInfo)
{
    internal bool IsEquivalentTo(DurableOutputCaptureDefinition other)
        => Address == other.Address &&
           string.Equals(ContractName, other.ContractName, StringComparison.Ordinal) &&
           PayloadType == other.PayloadType &&
           ReferenceEquals(JsonTypeInfo, other.JsonTypeInfo);
}

internal sealed class DurableOutputConfiguration(
    IReadOnlyDictionary<ApplicationAddress, DurableOutputCaptureDefinition> captures)
{
    internal IReadOnlyDictionary<ApplicationAddress, DurableOutputCaptureDefinition> Captures { get; } = captures;

    internal bool IsEquivalentTo(DurableOutputConfiguration other)
        => Captures.Count == other.Captures.Count &&
           Captures.All(pair =>
               other.Captures.TryGetValue(pair.Key, out var candidate) &&
               pair.Value.IsEquivalentTo(candidate));
}
