namespace FluxFlow.Studio.UI.Features.Connections;

internal static class ConnectionEndpointDisplay
{
    internal static string Format(string address, bool preserveSpelling = false)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return "Invalid endpoint";
        if (uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0)
            return preserveSpelling ? address : uri.AbsoluteUri;
        return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri +
            (uri.Query.Length == 0 ? "" : " (query hidden)");
    }
}
