# Known expression extension checks

The approved convention is .jsonata for JSONata and .cs for C#, retaining .csx.
Engine remains explicitly configured. ExpressionSource.Read now accepts an optional
engine name; only recognized mismatched pairs are rejected, before reading a file.
Mapper and Router pass their configured names and retain their existing Build-time
exception context (component address and Router route name).

The check does not infer an engine from a suffix, constrain custom engines, reject
unknown extensions or extensionless paths, or inspect inline/Base64 text as filenames.
Comparisons are case-insensitive. No new production type, enum, registry or dependency.
Single-argument Read calls remain source-compatible; its signature changes require
rebuilding already compiled consumers. Public API baseline updated.

Configuration tests: 228 passed per target on .NET 8/10 (456 executions), including
known mismatches, matching file types, custom engines, unknown/no extensions, text
overrides and actual Mapper/Router Build failures with located errors.
Log: artifacts/cleanup-review-20260920/expression-extension-tests.log.
No commit/publication.
