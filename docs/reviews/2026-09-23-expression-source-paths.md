# Optional file prefixes across expression consumers

The source-consumer audit found two relevant production readers: JsonSource used by
Source, and ExpressionSource shared by Mapper and Router. ExpressionSource already
supported unprefixed .jsonata/.csx/.cs filenames. It now also accepts fully qualified
paths and ./ or ../ relative paths (including Windows backslashes) with arbitrary
extensions or no extension. Both component adapters get this behavior from the
existing shared reader; neither gains file-reading logic.

An expression cannot use JSON's parse-first rule: identifiers, property access and
division can themselves resemble paths. Recognized path syntax chooses files;
otherwise the value remains expression text. Bare request means an expression;
./request selects a file. No filesystem-existence heuristic or expression engine
dependency is added to resolve this ambiguity. inline:/file: remain overrides;
base64: behavior and nonrecursive content interpretation remain unchanged.

File reading stays at Build with the adapter's existing host-selected base directory.
Missing files fail, and repeated Build/message processing does not read or compile
again. Link conditions have never consumed a file-source type and remain inline-only;
this audit does not expand their contract. No new classes, packages or Core changes.

Validation: 212 Configuration cases pass per .NET 8 and .NET 10. Tests include relative,
parent and absolute paths, spaces, extensionless files, explicit overrides, missing
files, division/member/string expressions, Mapper compilation once at Build and Router
reading ./route.txt at Build with subsequent file changes ignored.
Log: artifacts/cleanup-review-20260920/expression-paths-tests.log.
Public signatures unchanged. No commit/publication.
