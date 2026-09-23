# Remove parallel block registration

The user approved starting with the unused legacy registration path identified in
the cleanup review. Searches across production, tests, examples and benchmark code
found no callers of the old component/port/signal helpers outside the helpers themselves.

Removed 13 public method overloads for raw-block components, input/output ports,
signal targets and non-owning views from FluxFlowServiceCollectionExtensions.
Deleted NonOwningDataflowViews and its DataflowBlockView, TargetBlockView and
SourceBlockView classes. The old output-address validator is also removed.

The two resource registration methods keep their public signatures and behavior.
Their private validator now checks Resource directly rather than accepting a kind
parameter that no longer varies. Factories remain provider-owned keyed singletons;
external instances remain externally owned under standard DI semantics.

The existing AddComponentFactory/AddInputPort/AddOutputPort/AddEventPort path is
unchanged. No wrappers, aliases or replacement public construction path were added.
This is an intentional breaking removal of unused preview APIs, not a runtime feature
change. No new tests mirror the deleted helpers; existing native identity, ownership,
delivery and configuration/resource tests exercise the supported path.

API baseline updated. All 1,784 full-solution Release test executions passed on the
supported target frameworks. Diff whitespace validation passed and no references to
the removed wrappers remain in production/tests/consumer/benchmark C# code.
Package consumers were not rerun for this unused-API removal.
Full Release test evidence:
artifacts/cleanup-review-20260920/native-registration-tests.log.
Historical reports remain historical. No commit/publication.
