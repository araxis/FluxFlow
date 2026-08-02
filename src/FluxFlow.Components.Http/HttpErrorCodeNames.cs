namespace FluxFlow.Components.Http;

public static class HttpErrorCodeNames
{
    public const string InvalidUrl = "http.invalid_url";

    public const string InvalidMethod = "http.invalid_method";

    public const string InvalidHeader = "http.invalid_header";

    public const string InvalidContent = "http.invalid_content";

    public const string InvalidTimeout = "http.invalid_timeout";

    public const string Timeout = "http.timeout";

    public const string Canceled = "http.canceled";

    public const string Network = "http.network";

    public const string SendFailed = "http.send_failed";

    public const string ResponseReadFailed = "http.response_read_failed";

    public const string NonSuccessStatus = "http.non_success_status";
}
