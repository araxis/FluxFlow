namespace FluxFlow.Composition.Authoring;

internal sealed class ApplicationResourceContractCollection
{
    private readonly Dictionary<string, ApplicationResourceContract> _contracts =
        new(StringComparer.Ordinal);

    internal void EnsureCanAdd(ApplicationResourceContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (_contracts.TryGetValue(contract.Type, out var existing) &&
            !ReferenceEquals(existing, contract))
        {
            throw new InvalidOperationException(
                $"Application resource type '{contract.Type}' has conflicting contracts in the application definition.");
        }
    }

    internal void Add(ApplicationResourceContract contract)
    {
        EnsureCanAdd(contract);
        _contracts.TryAdd(contract.Type, contract);
    }

    internal IReadOnlyList<ApplicationResourceContract> Build()
        => _contracts.Values
            .OrderBy(static contract => contract.Type, StringComparer.Ordinal)
            .ToArray();
}
