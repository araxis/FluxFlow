# 140 - Local graph maintenance

Date: 2026-06-20

## Rule

Keep the generated knowledge-graph output current during local work, but do not
stage or publish it.

## Local setup

- `graphify-out/` is excluded through `.git/info/exclude`.
- Local hooks are installed in `.git/hooks/post-commit` and
  `.git/hooks/post-checkout`.
- The hooks are local machine state, not repository content.

## Operating notes

- Use `graphify hook status` to confirm hooks are installed.
- Use `graphify update .` after code changes when an immediate refresh is
  needed.
- The hook path maintains code-derived graph output after local commits and
  checkouts.
- For documentation or memory-only edits, run an explicit graph update because
  the hook is optimized for code-file changes.

## Current verification

- Local `main` matches `origin/main`.
- `dotnet test FluxFlow.sln --configuration Release` passed on 2026-06-20.
- No-build Release TRX aggregation: 742 passed, 0 failed, 0 skipped.
- On `work/mqtt-connection-pilot`, `graphify update . --force` was run after
  adding the MQTTnet adapter package and updating memory. Code graph output
  refreshed to 7783 nodes, 11712 edges, and 740 communities. `graph.html` was
  intentionally not regenerated because the graph exceeds the local HTML
  visualization limit.
