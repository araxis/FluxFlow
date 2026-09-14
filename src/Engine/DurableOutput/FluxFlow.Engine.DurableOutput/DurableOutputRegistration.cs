using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Definition;

namespace FluxFlow.Engine.DurableOutput;

public sealed class DurableOutputRegistrationBuilder
{
    private readonly Dictionary<ApplicationAddress, DurableOutputCaptureDefinition> _byAddress = [];

    public DurableOutputRegistrationBuilder Capture(
        string output,
        string contractName)
        => Capture(ApplicationAddress.Parse(output), contractName);

    public DurableOutputRegistrationBuilder Capture<T>(
        OutputPortHandle<T> output,
        string contractName)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Capture(output.Address, contractName);
    }

    public DurableOutputRegistrationBuilder Capture(
        ApplicationAddress output,
        string contractName)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (output.Kind != ApplicationAddressKind.WorkflowPort)
            throw new ArgumentException("Durable output capture requires a workflow port address.", nameof(output));
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        if (!string.Equals(contractName, contractName.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Contract name cannot have surrounding whitespace.", nameof(contractName));
        var definition = new DurableOutputCaptureDefinition(output, contractName);
        if (_byAddress.TryGetValue(output, out var current))
        {
            if (current.IsEquivalentTo(definition))
                return this;

            throw new InvalidOperationException(
                $"Durable output '{output}' is already captured with a different contract.");
        }

        _byAddress.Add(output, definition);
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
    string ContractName)
{
    internal bool IsEquivalentTo(DurableOutputCaptureDefinition other)
        => Address == other.Address &&
           string.Equals(ContractName, other.ContractName, StringComparison.Ordinal);
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
