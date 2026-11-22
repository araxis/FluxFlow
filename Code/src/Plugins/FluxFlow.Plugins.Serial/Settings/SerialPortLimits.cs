namespace FluxFlow.Configuration.Components.Serial;

internal static class SerialPortLimits
{
    internal static void Validate(string? portName, int baudRate, int dataBits, TimeSpan readTimeout, TimeSpan writeTimeout,
        bool rtsEnable, bool hardwareRts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baudRate);
        if (dataBits is < 5 or > 8) throw new ArgumentOutOfRangeException(nameof(dataBits));
        if (readTimeout.TotalMilliseconds is < 1 or > 60000) throw new ArgumentOutOfRangeException(nameof(readTimeout));
        if (writeTimeout.TotalMilliseconds is < 1 or > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(writeTimeout));
        if (rtsEnable && hardwareRts) throw new ArgumentException("RtsEnable cannot be set when hardware handshaking controls RTS.");
    }
}
