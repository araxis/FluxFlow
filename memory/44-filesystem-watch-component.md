# FileSystem Watch Component

Date: 2026-06-01

## Decision

Add `file.watch` to `FluxFlow.Components.FileSystem` as the first source node
in the package.

The node is intentionally package-owned and host-neutral. It emits file system
events and leaves product-specific file ingestion, parsing, routing, and UI
behavior to consumers.

## Implemented Node

```text
Node type: file.watch
Input:     none
Output:    Output
Errors:    node error stream
Options:   directory, filter, includeSubdirectories, notifyFilters,
           boundedCapacity, baseDirectory, allowAbsolutePaths
```

Output contract:

```text
FileWatchEvent
  Timestamp
  Path
  Directory
  Name
  ChangeType
  OldPath
  OldName
```

Change type contract:

```text
FileWatchChangeType
  Created
  Changed
  Deleted
  Renamed
```

## Lifecycle

- `StartAsync` resolves and validates the configured directory.
- Missing directories fail startup clearly.
- Absolute directories require `allowAbsolutePaths`.
- Relative directories are resolved under `baseDirectory` when configured.
- `Complete` stops and disposes the watcher, then completes `Output`.
- `DisposeAsync` releases the watcher without requiring host-specific cleanup.

## Failure Behavior

- Startup configuration and directory failures throw from `StartAsync` and emit
  `FlowError`.
- Runtime watcher errors emit `FlowError` and diagnostics.
- If the bounded output queue is full, the event is dropped and a stable
  `FlowError` is emitted.

## Observation

The node emits:

- diagnostics for start, stop, observed changes, failures, and dropped events
- workflow events for observed changes with path/change metadata

## Verification

The focused tests cover:

- create/change events
- rename events
- diagnostics and workflow events
- completion and output completion
- missing directory startup failure
- denied absolute directory startup failure
- missing directory option validation
- invalid bounded capacity
- unsupported notify filter validation

## Next Steps

1. Tag and publish `components-filesystem-v0.3.0-alpha.1` after this commit.
2. Run a clean install check from the public package feed after publishing.
3. Consider `directory.enumerate` next because it pairs naturally with read and
   watch without adding long-running lifecycle complexity.
