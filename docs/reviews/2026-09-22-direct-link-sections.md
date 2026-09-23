# Direct link section parsing

## Result

ApplicationConfiguration passes each declared port's IConfigurationSection directly
to LinkConfiguration. The recursive ReadLinkValue conversion and temporary JSON
link document are removed. Link parsing still creates deferred LinkFactory delegates;
no new public model, registry, builder or context is introduced.

Address resolution, duplicate identity, condition compilation and runtime routing are
unchanged. Both declaration directions, arrays, named ports, Engine, Parameters,
Delivery and Fallback remain supported. Malformed declarations still reject the
staged registration before caller services change.

Only expression parameters retain ConfigurationJsonReader conversion because their
contract is JSON data. Existing scalar conversion, nested values, JSON object text,
reserved parameter validation and Add-time snapshots are preserved. IConfiguration
still cannot distinguish original JSON numbers from equivalent scalar strings;
this cleanup does not change that contract.

## Verification

- Full solution Release suite: 1,796 passing executions across .NET 8 and .NET 10.
- Configuration: 131 tests per target, including six added cases for nested/JSON-text
  parameter snapshots and malformed nested link fields/arrays.
- Existing coverage verifies cross-workflow links, both directions, duplicate policy
  conflicts, engine selection, compile reuse, fallback and transactional failure.
- Public API baseline passes unchanged; whitespace diff check passes.
- Log: artifacts/cleanup-review-20260920/direct-link-final-tests.log.

No package consumer rerun for this internal parser change. No commit/publication.
Next cleanup is current documentation consistency, including stale resource document
API guidance; historical reports should remain historical.
