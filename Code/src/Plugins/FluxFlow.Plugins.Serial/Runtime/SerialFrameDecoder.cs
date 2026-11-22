namespace FluxFlow.Components.Serial;

// Keeps incomplete frames between OS reads. Never treats a driver read as a frame unless Raw is explicit.
internal sealed class SerialFrameDecoder
{
    private readonly SerialFrameOptions _options;
    private readonly byte[] _delimiter;
    private readonly byte[] _pending;
    private int _length;
    internal SerialFrameDecoder(SerialFrameOptions options)
    {
        options.Validate(); _options = options;
        _delimiter = options.Framing == SerialFraming.Delimiter ? Convert.FromBase64String(options.DelimiterBase64) : [];
        _pending = new byte[options.MaxFrameBytes + _delimiter.Length];
    }
    internal IEnumerable<byte[]> Push(byte[] bytes, int count)
    {
        if (_options.Framing == SerialFraming.Raw)
        { if (count > 0) yield return bytes.AsSpan(0, count).ToArray(); yield break; }
        for (var index = 0; index < count; index++)
        {
            if (_length == _pending.Length) throw new InvalidDataException("Serial frame exceeds MaxFrameBytes.");
            _pending[_length++] = bytes[index];
            if (_options.Framing == SerialFraming.FixedLength)
            {
                if (_length != _options.FrameLength) continue;
                var frame = _pending.AsSpan(0, _length).ToArray(); _length = 0; yield return frame;
            }
            else if (_length >= _delimiter.Length && _pending.AsSpan(_length - _delimiter.Length, _delimiter.Length).SequenceEqual(_delimiter))
            {
                var frame = _pending.AsSpan(0, _length - _delimiter.Length).ToArray(); _length = 0; yield return frame;
            }
        }
    }
}
