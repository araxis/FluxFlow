namespace FluxFlow.Components.Storage.Diagnostics;

public static class StorageDiagnosticNames
{
    public const string StoreOpened = "storage.store.opened";
    public const string StoreOpenFailed = "storage.store.open.failed";
    public const string PutStored = "storage.put.stored";
    public const string PutFailed = "storage.put.failed";
    public const string GetFound = "storage.get.found";
    public const string GetNotFound = "storage.get.not_found";
    public const string GetFailed = "storage.get.failed";
    public const string DeleteDeleted = "storage.delete.deleted";
    public const string DeleteMissing = "storage.delete.missing";
    public const string DeleteFailed = "storage.delete.failed";
}
