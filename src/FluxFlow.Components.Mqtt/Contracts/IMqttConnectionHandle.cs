using System.Diagnostics.CodeAnalysis;
using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.Contracts;

public interface IMqttConnectionHandle
{
    string ConnectionName { get; }
    MqttConnectionProfile Profile { get; }
    MqttReconnectPolicy? Reconnect { get; }

    /// <summary>
    /// Current connection state. Reads lock-free; borrowers consult this before
    /// <see cref="TryGetAdapter"/> to decide whether a client is available.
    /// </summary>
    MqttClientHealthState State { get; }

    /// <summary>
    /// Identity of the current lease. Incremented on every successful new lease so
    /// borrowers (subscribe) can dedupe a within-lease Reconnecting -&gt; Connected
    /// transition from a genuine reconnect that produced a fresh lease.
    /// </summary>
    int ConnectionEpoch { get; }

    /// <summary>
    /// Establishes the client. Owner/host-driven: there is no auto-connect or lazy
    /// connect. Idempotent (a no-op when already connected) and single-flight (a
    /// concurrent call awaits the in-flight connect rather than starting a second).
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Tears down the client. Idempotent; cancels an in-flight connect.
    /// </summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Borrows the established adapter without taking ownership. Returns true only
    /// while the connection is <see cref="MqttClientHealthState.Connected"/>; the
    /// borrower must never connect or dispose the adapter.
    /// </summary>
    bool TryGetAdapter([NotNullWhen(true)] out IMqttClientAdapter? adapter);

    /// <summary>
    /// Completes on the next connect/disconnect transition. Lets borrowers await
    /// state/epoch changes instead of polling.
    /// </summary>
    Task WaitForChangeAsync(CancellationToken ct);
}
