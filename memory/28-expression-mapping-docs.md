# Expression Mapping Docs

Date: 2026-05-31

## Summary

Added `docs/10-expression-mapping.md` as a focused reference for link condition
evaluation, expression engine extension, expression predicates, mapper
contracts, context variables, and app usage patterns.

## Decisions

- Explain link conditions as routing predicates.
- Explain mapper contracts as component/node helpers, not automatic graph-link
  transformations.
- Keep expression implementation details behind `IFlowExpressionEngine`.
- Document that validation checks expression shape, while expression semantics
  are evaluated at runtime.
- Recommend simple, stable context variables for persisted app expressions.

## Verification Target

The page should stay aligned with:

- `IFlowExpressionEngine`
- `FlowMapContext`
- `ExpressionFlowPredicate<TInput>`
- `IFlowMapContextFactory<TInput>`
- `IFlowMapper<TInput,TOutput>`
- `DelegateFlowMapper<TInput,TOutput>`
- `DelegateFlowPredicate<TInput>`
- `ApplicationRuntimeBuilder`

## Next Step

After the docs set settles, decide whether package versioning guidance belongs
in public docs or only in memory/release notes.
