namespace FluxFlow.Nodes;

/// <summary>
/// Capacity settings for a reliable in-process workflow output.
/// </summary>
public sealed record FlowOutputOptions
{
    private int _capacity = 128;

    /// <summary>
    /// Maximum number of messages waiting behind the message currently being delivered.
    /// </summary>
    public int Capacity
    {
        get => _capacity;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Capacity),
                    "Capacity must be greater than zero.");
            }

            _capacity = value;
        }
    }
}
