using System.Diagnostics.CodeAnalysis;

namespace FluxFlow.Components.Http.Contracts;

public interface IHttpClientHandle
{
    string ClientName { get; }
    string? BaseUrl { get; }
    IReadOnlyList<string> AllowedHosts { get; }
    bool RestrictToBaseUrlOrigin { get; }
    bool FollowRedirects { get; }
    int DefaultTimeoutMilliseconds { get; }
    int? PooledConnectionLifetimeSeconds { get; }
    int? MaxConnectionsPerServer { get; }
    IReadOnlyDictionary<string, string> DefaultHeaders { get; }

    /// <summary>
    /// Current connection state. Reads lock-free; borrowers consult this before
    /// <see cref="TryGetSender"/> to decide whether a shared sender is available.
    /// </summary>
    HttpClientConnectionState State { get; }

    /// <summary>
    /// Establishes the shared request sender. Owner/host-driven: there is no
    /// auto-connect or lazy connect. Idempotent (a no-op when already connected)
    /// and single-flight (a concurrent call awaits the in-flight connect rather
    /// than building a second sender).
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Tears down the shared request sender. Idempotent; cancels an in-flight
    /// connect.
    /// </summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Borrows the established sender without taking ownership. Returns true only
    /// while the connection is <see cref="HttpClientConnectionState.Connected"/>;
    /// the borrower must never connect or dispose the sender.
    /// </summary>
    bool TryGetSender([NotNullWhen(true)] out IHttpRequestSender? sender);
}
