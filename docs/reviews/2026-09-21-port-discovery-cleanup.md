# Configuration port discovery cleanup

Ordinary section registration now reads the existing native DI port-address
registrations, matching runtime composition and explicit document linking.
Previously it scanned every keyed ITargetBlock/ISourceBlock service and assumed
every string key was a FluxFlow port address. This duplicated discovery and made
unrelated host services affect configuration parsing.

No new registry, metadata model, provider, public API or construction path was
introduced. DocumentValidation is unchanged, as explicitly requested. Components
and native blocks remain deferred until Build; port declaration remains the job
of AddInputPort/AddOutputPort/AddEventPort.

Two regression cases demonstrate the problem: an unrelated `host-block` key and
an unrelated key shaped like `main.source.ExtraSetting`. Both failed before the
change. Afterward configuration registers the intended link, Build does not
resolve either host block, and a message reaches the configured sink unchanged.

Validation: all 112 Configuration tests passed on each of .NET 8 and .NET 10
(224 executions). Logs are in artifacts/cleanup-review-20260920/port-discovery-before.log
and port-discovery-tests.log. No full-suite or package-consumer rerun was made for
this local discovery correction; the prior broader checkpoint remains separate.
No commit or publication.
