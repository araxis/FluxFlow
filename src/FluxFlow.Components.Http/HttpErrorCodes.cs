namespace FluxFlow.Components.Http;

public static class HttpErrorCodes
{
    public const int InvalidRequest = 9000;
    public const int InvalidUrl = 9001;
    public const int Timeout = 9002;
    public const int Canceled = 9003;
    public const int Network = 9004;
    public const int ResponseTooLarge = 9005;
    public const int SendFailed = 9006;
    public const int NonSuccessStatus = 9007;
    public const int UrlNotAllowed = 9008;
    public const int RequestNotConnected = 9009;
}
