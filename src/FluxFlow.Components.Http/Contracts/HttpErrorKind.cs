namespace FluxFlow.Components.Http.Contracts;

public enum HttpErrorKind
{
    InvalidRequest = 0,
    InvalidUrl = 1,
    Timeout = 2,
    Canceled = 3,
    Network = 4,
    ResponseTooLarge = 5,
    SendFailed = 6,
    NonSuccessStatus = 7,
    UrlNotAllowed = 8,
    NotConnected = 9
}
