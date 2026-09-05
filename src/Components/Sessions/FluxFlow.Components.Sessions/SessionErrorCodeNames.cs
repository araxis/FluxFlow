namespace FluxFlow.Components.Sessions;

public static class SessionErrorCodeNames
{
    public const string InvalidRequest = "session.invalid_request";
    public const string ContentMissing = "session.content_missing";
    public const string ContentUnavailable = "session.content_unavailable";
    public const string StoredContentInvalid = "session.stored_content_invalid";
    public const string StoreUnavailable = "session.store_unavailable";
    public const string SessionNotFound = "session.not_found";
    public const string RecordFailed = "session.record_failed";
    public const string ReplayFailed = "session.replay_failed";
    public const string QueryFailed = "session.query_failed";
    public const string CompleteFailed = "session.complete_failed";
}
