# Typed links and expression sources

The approved scope is readable reusable values in per-component configuration DTOs,
not a generic union-type system or another application document model.

## Result

- Added specialized `OneOrManyLinks : IReadOnlyList<LinkConfiguration>`. The strict
  parser previously embedded in internal LinkConfiguration moved into this type;
  there is still one parser implementation. Strings, objects, arrays and mixed
  arrays keep their conditions, engine, parameter, delivery and fallback rules.
- Renamed the remaining internal DI implementation to LinkRegistration. Its keyed
  identity, duplicate/conflict validation and one execution alias remain unchanged.
- All six bundled component DTOs expose their fixed port properties as OneOrManyLinks.
  The narrow ReadConfiguration<T> extension reads the whole DTO, using the specialized
  reader for top-level link properties and the native binder for other properties.
  Public setters/init, ConfigurationKeyName and property defaults are supported.
  Native strict conversion prevents malformed dictionary entries disappearing.
- ExpressionSource is now an immutable scalar-bindable value with Value and
  Read(baseDirectory), replacing the static utility. Mapper.Expression and Router
  route values use it. Existing inline/file rules remain. An explicit `base64:`
  prefix adds strict UTF-8 decoding; bare strings are not guessed to be encoded.
  Decoded text is not recursively interpreted as a file. Source resolution and
  compilation stay at Build; no per-message conversion was introduced.

Components, Core, configuration syntax, factories, ownership and runtime routing
are unchanged. No package dependency or registry/context was added. File count grows
to expose the requested meaningful types; this is a usage improvement, not a claim
of fewer implementation lines.

## Boundaries and tradeoffs

Standard ConfigurationBinder does not handle the link string/object/array union
automatically. Use ReadConfiguration<T> for DTOs with top-level link properties.
It is deliberately not a general recursive custom binder and does not support
constructor-only DTOs. ExpressionSource alone works with the standard Get<T>() binder.
Resources with ordinary settings continue to use Get<T>().

Typed binding reads link values for the DTO; automatic native-port registration reads
declared port sections through the same parser. Thus bundled fixed-port links are
parsed twice during Add. This preserves registration independence from a particular
DTO, custom registrar compatibility and dynamic Router port discovery without adding
a cache, DTO registry or changing the registration delegate. There is no duplicate
parser implementation, runtime lookup or per-message work. No setup performance
improvement is claimed.

Link conditions remain inline strings. ExpressionSource's file/Base64 behavior applies
to Mapper and Router expressions; applying it to links is outside this change.
The old static ExpressionSource.Read(text, directory) call is replaced with
new ExpressionSource(text).Read(directory). Implicit conversion supports string
assignment in C#. Router dictionary values change from string to ExpressionSource.

## Verification

- Configuration tests: 176 passed per target on .NET 8 and .NET 10.
- Full Release suite: 1,850 executions passed, zero failures.
- Isolated Configuration/engine package consumer passed on .NET 8 and .NET 10,
  exercising typed DTO binding and a Base64 C# Mapper in the running workflow.
- Public API baseline reviewed and updated for the new value types, binding method
  and configuration property types.
- Coverage includes JSON and in-memory binding, both link directions in existing
  integration tests, shorthand/mixed arrays, strict malformed links, parameter
  snapshots, defaults/key aliases, standard expression binding, explicit Base64,
  Unicode and invalid UTF-8, invalid route entries, and real Mapper evaluations.
- Existing file timing/compile reuse, duplicate/conflict, atomic registration,
  engine selection and component lifecycle coverage remains passing.

Logs: artifacts/cleanup-review-20260920/config-value-types-{tests,api,full-tests,package}.log.
No commit or publication.
