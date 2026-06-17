namespace FluxFlow.Components.Http.Contracts;

/// <summary>
/// Lifecycle state of an http.client handle. Borrowers (http.request) consult
/// this before <see cref="IHttpClientHandle.TryGetSender"/> to decide whether a
/// shared sender is available.
/// </summary>
public enum HttpClientConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Disconnecting = 3,
    Faulted = 4
}
