namespace FluxFlow.SampleApp;

internal sealed record SampleOrder(string Id, string Customer, decimal Total);

internal sealed record ReviewedOrder(string Id, string Customer, decimal Total, bool Priority);

internal sealed record StoredOrder(string Category, ReviewedOrder Order);

internal sealed class InMemoryOrderStore
{
    private readonly object _gate = new();
    private readonly List<StoredOrder> _orders = [];

    public void Add(string category, ReviewedOrder order)
    {
        lock (_gate)
        {
            _orders.Add(new StoredOrder(category, order));
        }
    }

    public IReadOnlyList<StoredOrder> GetSnapshot()
    {
        lock (_gate)
        {
            return _orders.ToArray();
        }
    }
}
