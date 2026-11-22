# Optional JSON source prefixes

The user corrected the explicit-file-only source contract: both text and file paths
must work without requiring prefixes. JsonSource now attempts JSON first. If that
fails, text starting with an object, array or quote keeps its JSON parse error;
other non-JSON text is interpreted as a file path. No extension or existence heuristic
is needed. File contents are parsed once, without recursive source interpretation.

inline: and file: remain optional overrides. Valid JSON wins over an identically
named file, preserving scalar types independently of filesystem contents. A file
named 123 can be addressed as file:123 or ./123. A filename starting with a JSON
structural character can similarly use an explicit file: or ./ path.

This is a small change to the existing JsonSource type. It adds no class, dependency,
registry or cache and leaves Source's Add-time snapshots, deferred construction and
atomic registration unchanged. ExpressionSource already accepts plain text and
recognized expression-file extensions; it is unchanged in this step.

Validation: Configuration tests pass 199 cases per .NET 8 and .NET 10 (398 executions).
Coverage includes plain/explicit/relative/absolute file paths, extensionless and numeric
filenames, spaces, JSON scalar precedence, explicit overrides, malformed JSON/file
contents, missing files, snapshot isolation and native binding. Public signatures
are unchanged. Log: artifacts/cleanup-review-20260920/json-source-paths-tests.log.

Other configuration-readability suggestions remain deferred. No commit/publication.
