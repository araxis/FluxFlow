using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

/// <summary>
/// Resolves the optional capture operation configured for an application output.
/// </summary>
public interface IApplicationOutputCaptureResolver
{
    /// <summary>
    /// Returns the capture operation for <paramref name="address"/>, or <see langword="null"/>
    /// when the output keeps its ordinary in-memory behavior.
    /// </summary>
    IApplicationOutputCapture<T>? Resolve<T>(ApplicationAddress address);
}

/// <summary>
/// Accepts an output message before the Engine dispatches it to links or host taps.
/// </summary>
/// <remarks>
/// Returning successfully means the configured capture boundary accepted the message.
/// Implementations must surface capture failures rather than silently continuing.
/// </remarks>
public interface IApplicationOutputCapture<T>
{
    ValueTask CaptureAsync(
        FlowMessage<T> message,
        CancellationToken cancellationToken = default);
}
