namespace FluxFlow.Components.Storage;

public static class StorageResultKinds
{
    public const string PutStored = "StoragePut";
    public const string PutFailed = "StoragePutFailed";
    public const string GetFound = "StorageGetFound";
    public const string GetNotFound = "StorageGetNotFound";
    public const string GetFailed = "StorageGetFailed";
    public const string QueryCompleted = "StorageQuery";
    public const string QueryFailed = "StorageQueryFailed";
    public const string DeleteDeleted = "StorageDelete";
    public const string DeleteNotFound = "StorageDeleteNotFound";
    public const string DeleteFailed = "StorageDeleteFailed";
}
