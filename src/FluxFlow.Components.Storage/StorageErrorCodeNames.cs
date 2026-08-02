namespace FluxFlow.Components.Storage;

public static class StorageErrorCodeNames
{
    public const string InvalidRequest = "storage.invalid_request";
    public const string ContentMissing = "storage.content_missing";
    public const string ContentUnavailable = "storage.content_unavailable";
    public const string StoredContentInvalid = "storage.stored_content_invalid";
    public const string PutFailed = "storage.put_failed";
    public const string GetFailed = "storage.get_failed";
    public const string QueryFailed = "storage.query_failed";
    public const string DeleteFailed = "storage.delete_failed";
}
