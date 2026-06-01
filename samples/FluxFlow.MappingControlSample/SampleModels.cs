namespace FluxFlow.MappingControlSample;

internal sealed record IncomingOrder(
    string Id,
    string Customer,
    decimal Total,
    bool Active);

internal sealed record ReviewedOrder(
    string Id,
    string Customer,
    decimal Total,
    bool Active,
    bool Priority);

internal sealed record StoredOrder(
    string Category,
    ReviewedOrder Order);

internal sealed class SampleStore
{
    private readonly object _gate = new();
    private readonly List<StoredOrder> _orders = [];
    private readonly List<SampleAssertion> _assertions = [];

    public void AddOrder(string category, ReviewedOrder order)
    {
        lock (_gate)
        {
            _orders.Add(new StoredOrder(category, order));
        }
    }

    public void AddAssertion(SampleAssertion assertion)
    {
        lock (_gate)
        {
            _assertions.Add(assertion);
        }
    }

    public IReadOnlyList<StoredOrder> GetOrders()
    {
        lock (_gate)
        {
            return _orders.ToArray();
        }
    }

    public IReadOnlyList<SampleAssertion> GetAssertions()
    {
        lock (_gate)
        {
            return _assertions.ToArray();
        }
    }
}

internal sealed record SampleAssertion(
    string Name,
    bool Passed,
    string? Message);
