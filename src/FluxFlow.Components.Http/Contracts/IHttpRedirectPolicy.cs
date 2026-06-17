namespace FluxFlow.Components.Http.Contracts;

/// <summary>
/// Optional capability a request sender may expose so the configured redirect
/// policy is observable. Security-relevant: a sender built behind an allow-list
/// or origin guard must report <see cref="AllowAutoRedirect"/> as false so a
/// server cannot 3xx-redirect past the per-request host validation.
/// </summary>
public interface IHttpRedirectPolicy
{
    bool AllowAutoRedirect { get; }
}
