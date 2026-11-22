namespace FluxFlow.Components.Serial;

public sealed record SerialTextOptions
{
    public string NewLine { get; init; } = "\n";
    public int MaxLineBytes { get; init; } = 65536;
    public int InputCapacity { get; init; } = 128;
    public int OutputCapacity { get; init; } = 128;

    public void Validate()
    {
        if (NewLine is not ("\n" or "\r\n"))
            throw new ArgumentException("NewLine must be LF or CRLF.", nameof(NewLine));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxLineBytes);
        ArgumentOutOfRangeException.ThrowIfEqual(MaxLineBytes, int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(InputCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
    }
}
