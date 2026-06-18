namespace FluxFlow.Components.Http.Contracts;

public enum HttpErrorKind
{
    InvalidUrl,
    Timeout,
    Canceled,
    Network,
    SendFailed,
    NonSuccessStatus
}
