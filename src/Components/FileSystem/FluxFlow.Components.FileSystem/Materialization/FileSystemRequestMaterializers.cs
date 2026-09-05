using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.FileSystem.Materialization;

public sealed class FileReadRequestMaterializer()
    : JsonFlowValueMaterializer<FileReadRequest>(
        "filesystem.read.invalid_shape",
        "FileSystem",
        FlowValueShape.Object("A file read request object with a path and optional content settings.", "path"));

public sealed class FileContentWriteRequestMaterializer()
    : JsonFlowValueMaterializer<FileContentWriteRequest>(
        "filesystem.write.invalid_shape",
        "FileSystem",
        FlowValueShape.Object("A file write request object with path, content, mode, and directory settings.", "path", "content"));
