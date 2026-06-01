# Directory Enumerate Component

Date: 2026-06-01

## Decision

Add `directory.enumerate` to `FluxFlow.Components.FileSystem` as a finite
source node. The node belongs in the existing FileSystem category package
because it shares the same path policy, base directory rules, capacity settings,
diagnostic style, and package registration path as `file.write`, `file.read`,
and `file.watch`.

## Shape

```text
Node type: directory.enumerate
Input:     none
Output:    Output
Errors:    node error stream
Options:   directory, filter, includeSubdirectories, includeFiles,
           includeDirectories, maxEntries, boundedCapacity, baseDirectory,
           allowAbsolutePaths
```

Output contract:

```text
DirectoryEnumerateEntry
  EnumeratedAt
  Path
  Directory
  Name
  EntryType
  Length
  CreatedAt
  LastModifiedAt
  Attributes
```

## Behavior

- `StartAsync` validates and resolves the configured directory.
- Missing, invalid, or denied paths fail startup clearly and report `FlowError`.
- Enumeration runs as a background source operation and uses bounded output
  backpressure.
- The node completes its output when enumeration finishes or is stopped.
- File entries include byte length; directory entries leave length empty.
- Diagnostics are emitted for start, entry emission, completion, and failure.

## Verification

Focused tests cover:

- matching files under a base directory
- recursive enumeration
- directory entries when enabled
- `maxEntries`
- diagnostics
- missing directory startup failure
- absolute path rejection
- invalid required options and limits

## Release

Planned package version: `0.4.0-alpha.1`

Planned tag: `components-filesystem-v0.4.0-alpha.1`
