using System.Text;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Serial;

// Keeps incomplete UTF-8 bytes across reads; only complete lines are decoded.
internal sealed class SerialLineReader(int maxLineBytes)
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly byte[] _line = new byte[checked(maxLineBytes + 1)];
    private int _length;
    private bool _overflow;

    internal FlowValue? Push(byte value)
    {
        if (value != '\n')
        {
            if (_length < _line.Length) _line[_length++] = value;
            else _overflow = true;
            return null;
        }

        var length = _length;
        if (length > 0 && _line[length - 1] == '\r') length--;
        var overflow = _overflow || length > maxLineBytes;
        _length = 0;
        _overflow = false;
        if (overflow) return new FlowError("serial.line_too_long", "Received line exceeds MaxLineBytes.", "serial");
        try { return (FlowValue)Utf8.GetString(_line, 0, length); }
        catch (DecoderFallbackException error)
        {
            return new FlowError("serial.invalid_utf8", error.Message, "serial");
        }
    }
}
