namespace FluxFlow.Components.Storage.Composition;

public static partial class StorageComponentDefinition
{
    public static class Options
    {
        public const string Collection = "collection";
        public const string Mode = "mode";
        public const string EmitStoredRecord = "emitStoredRecord";
        public const string BoundedCapacity = "boundedCapacity";
        public const string IncludeExpired = "includeExpired";
        public const string Offset = "offset";
        public const string Limit = "limit";
        public const string EmitRecordsInResult = "emitRecordsInResult";
    }

    public static class Types { public const string Put = "storage.put"; public const string Get = "storage.get"; public const string Query = "storage.query"; public const string Delete = "storage.delete"; }
    public static class Ports { public const string Input = "Input"; public const string Output = "Output"; public const string Events = "Events"; }
    public static class Resources { public const string Store = "store"; public const string Clock = "clock"; }
}
