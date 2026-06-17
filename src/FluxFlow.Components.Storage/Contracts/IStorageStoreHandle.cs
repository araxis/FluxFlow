using System.Diagnostics.CodeAnalysis;

namespace FluxFlow.Components.Storage.Contracts;

public interface IStorageStoreHandle
{
    string StoreName { get; }

    /// <summary>
    /// Current store connection state. Reads lock-free; borrowers consult this
    /// before <see cref="TryGetStore"/> to decide whether a store is available.
    /// </summary>
    StorageStoreConnectionState State { get; }

    /// <summary>
    /// Opens the store. Owner/host-driven: there is no auto-open or lazy open.
    /// Idempotent (a no-op when already connected) and single-flight (a concurrent
    /// call awaits the in-flight open rather than starting a second). Named
    /// ConnectAsync for cross-protocol consistency even though storage 'opens' a store.
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Closes the store. Idempotent; cancels an in-flight open. A shared/host-owned
    /// store is not disposed.
    /// </summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Borrows the opened store without taking ownership. Returns true only while
    /// the connection is <see cref="StorageStoreConnectionState.Connected"/>; the
    /// borrower must never open or dispose the store.
    /// </summary>
    bool TryGetStore([NotNullWhen(true)] out IStorageStore? store);
}
