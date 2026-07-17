namespace FluxFlow.Data;

public sealed record FlowContentCodecRegistration
{
    public FlowContentCodecRegistration(
        FlowContentCodecMatch match,
        string key,
        IFlowContentCodec codec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Match = match;
        Key = key.Trim();
        Codec = codec ?? throw new ArgumentNullException(nameof(codec));
    }

    public FlowContentCodecMatch Match { get; }

    public string Key { get; }

    public IFlowContentCodec Codec { get; }
}
