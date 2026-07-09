namespace FluxFlow.Components.FileSystem.Nodes;

internal static class BoundedFileReader
{
    private const int BufferSize = 81_920;

    internal static async Task<BoundedFileReadResult> ReadAsync(
        Stream stream,
        long? maxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        var initialCapacity = maxBytes.HasValue
            ? (int)Math.Min(maxBytes.Value, BufferSize)
            : BufferSize;
        using var output = new MemoryStream(initialCapacity);
        var buffer = new byte[BufferSize];

        while (true)
        {
            if (maxBytes.HasValue && output.Length == maxBytes.Value)
            {
                var extra = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken)
                    .ConfigureAwait(false);
                if (extra > 0)
                {
                    var observed = maxBytes.Value == long.MaxValue
                        ? long.MaxValue
                        : maxBytes.Value + 1;
                    return new BoundedFileReadResult([], observed, LimitExceeded: true);
                }

                break;
            }

            var count = buffer.Length;
            if (maxBytes.HasValue)
            {
                count = (int)Math.Min(count, maxBytes.Value - output.Length);
            }

            var read = await stream.ReadAsync(buffer.AsMemory(0, count), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        return new BoundedFileReadResult(output.ToArray(), output.Length, LimitExceeded: false);
    }
}

internal readonly record struct BoundedFileReadResult(
    byte[] Bytes,
    long BytesRead,
    bool LimitExceeded);
