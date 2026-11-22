using System.Collections.Concurrent;

namespace FluxFlow.Components.Serial;

// A host process must not open the same device for two workflow runs.
internal sealed class SerialPortLease : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> Owners = new(StringComparer.Ordinal);
    private string? _name;
    private SerialPortLease(string name) => _name = name;
    internal static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        if (!OperatingSystem.IsWindows()) return name;
        if (name.StartsWith(@"\\.\", StringComparison.Ordinal)) name = name[4..];
        return name.ToUpperInvariant();
    }
    internal static SerialPortLease Acquire(string name)
    {
        name = Normalize(name);
        if (!Owners.TryAdd(name, 0)) throw new IOException($"Serial port '{name}' is already owned by another session in this host.");
        return new(name);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _name, null) is { } name) Owners.TryRemove(name, out _);
    }
}
