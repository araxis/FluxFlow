using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.Storage.Materialization;

public sealed class StorageContentPutRequestMaterializer()
    : JsonFlowValueMaterializer<StorageContentPutRequest>(
        "storage.put.invalid_shape",
        "Storage",
        FlowValueShape.Object("A storage put request object with key, content, and optional collection, attributes, version, expiry, and mode.", "key", "content"));

public sealed class StorageGetRequestMaterializer()
    : JsonFlowValueMaterializer<StorageGetRequest>(
        "storage.get.invalid_shape",
        "Storage",
        FlowValueShape.Object("A storage get request object with key and optional collection, expiry, and correlation settings.", "key"));

public sealed class StorageQueryRequestMaterializer()
    : JsonFlowValueMaterializer<StorageQueryRequest>(
        "storage.query.invalid_shape",
        "Storage",
        FlowValueShape.Object("A storage query object with optional collection, key prefix, attributes, time range, paging, and correlation settings."));

public sealed class StorageDeleteRequestMaterializer()
    : JsonFlowValueMaterializer<StorageDeleteRequest>(
        "storage.delete.invalid_shape",
        "Storage",
        FlowValueShape.Object("A storage delete request object with key and optional collection and correlation settings.", "key"));
