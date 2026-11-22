# .NET 11 Studio release check

From the repository root, on Windows x64 with PowerShell 7, Python 3 on PATH,
the .NET 11 SDK and a .NET 10 x64 runtime installed alongside it:

```powershell
pwsh -NoProfile -File Code/eng/check-studio-release.ps1
```

The existing multi-target projects also need their .NET 8/10 targeting packs
(restored normally). No broker, Docker or serial device is needed. The command
does not open Studio, edit user settings or use the user's installed plugin store.

Each invocation creates a unique directory beneath the system temporary folder's
`fluxflow-release-checks` directory. It prints that directory at the beginning and
end. Use `-ArtifactsRoot <directory>` to choose another parent. Existing output
directories are never reused or deleted.

The check stops on failure and returns a nonzero exit code. It retains a
`summary.json`, individual step logs, TRX test results, published applications,
the isolated plugin store, and stopped-run snapshots. A green automated result
does **not** mark the desktop UI checklist below as completed.

Terminating failures include complete exception details in the step log and
summary. Cleanup and reporting failures are reported alongside the original
failure, rather than replacing it. A summary-write failure also fails the check;
the working directory is restored even when reporting fails.

## Automated coverage

- Controlled script tests cover failed startup and Stop, continuing cleanup after
  a failed Stop, a process exiting during termination, and failed summary writes.
- Existing Studio tests cover editing, save/reopen, configuration preservation,
  Undo/Redo, portable editors and retained observations.
- Existing desktop boundary tests cover native file identity, Save As, cancelled
  pickers, failed writes, edit history and host disposal.
- Selected existing environment integration tests cover installed/missing plugin
  save/reopen and reinstall, broker plugin declarations, companion lifecycle,
  HTTP cancellation, original log transport and run isolation. These are the
  hardware-free classes listed in the script, not the broker/Docker suites.
- The existing `publish-studio.ps1` produces the .NET 11 companion, browser and
  five basic plugin packages. The check also publishes Windows desktop and packs
  Serial using its existing .NET 8/10 targets.
- The existing installer and `test-plugin-sources.ps1` check folder, ZIP and
  local NuGet installation, removal, cancellation and reinstall in private stores.
- `compiled-inventory` checks the packed component and resource mappings against
  the installed catalog, coverage rows and acceptance fixtures in a disposable store.
- `compiled-orders` publishes an unsaved Scheduler → Source → Mapper → Logger
  workflow with inline and file-based expressions. It compares two Studio runs
  with two launches of each .NET 10 and 11 EXE after removing the input files.
  The .NET 10 launches use an isolated runtime. Both targets must reject an
  unknown expression engine. Each check has its own release log and summary entry.
- The published companion starts on an automatically assigned loopback port.
  Its catalog must match the installed packages; served .NET 10 design assemblies
  and browser/canvas assets must match their files exactly. A local
  Scheduler → Gate → Mapper → Logger workflow runs twice, verifying output,
  original logger messages, confirmed Stop, retained records and disjoint
  message IDs between runs.

Only the companion process started by this invocation is terminated in cleanup,
after attempting Stop for every tracked workflow, even if an earlier Stop fails.
The previous plugin-directory environment variable is restored. This process
termination is not evidence of graceful native-window shutdown. Publications and evidence remain available for
the manual check and can be deleted afterward.

## Existing package compatibility

By default the command uses freshly packed plugins with the existing .NET 8/10
contract. To additionally verify binaries retained from a previous release:

```powershell
pwsh -NoProfile -File Code/eng/check-studio-release.ps1 -CompatibilityPackages C:\Releases\previous\plugins
```

Supply a folder containing one `.nupkg` per plugin ID, including Sources, Logging,
Routing, Transformation, FlowControl and Serial. Additional plugin packages are
also loaded and their design assets checked. The folder is read only; installation
and removal use isolated copies. Package names and SHA-256 hashes are recorded in
the summary. Do not interpret the default fresh-package run as proof for every
historical third-party binary. Real broker and hardware behavior remains covered
by its separate acceptance trials.

## Manual Windows CI workflow

In GitHub Actions, select **Studio .NET 11 release check**, then **Run workflow**
and the branch to check. The workflow has no push, pull-request or scheduled trigger.
Leave the compatibility run ID empty to test freshly packed plugins.

To test retained binaries, enter a previous run ID from this repository and the
artifact name (default: `studio-plugin-packages`). The artifact must contain the
complete package set described above, with the `.nupkg` files directly at its
root. Missing or expired artifacts fail the check; they do not fall back to fresh
packages. Use trusted package artifacts because plugins execute in the test host.

The workflow retains `studio-release-evidence` for 30 days, including available
failure logs, summaries, test results, published apps and the temporary project.
Downloaded compatibility packages remain in its `compatibility-packages` folder
even if installation or testing fails. The isolated plugin store includes hidden
metadata and assets so it can be used unchanged for manual desktop checks.
Evidence upload runs even when an earlier step fails. GitHub's step logs also
record setup or download failures before the release script starts. A terminated
runner cannot guarantee artifact upload. Successful runs also retain the exact
tested packages as `studio-plugin-packages` for reuse by another run. Download
them before expiry if a longer-lived compatibility baseline is needed.

The job summary explicitly leaves desktop UI acceptance unverified. Download and
extract the evidence artifact, open its generated run directory, and follow the
manual checklist below (also included as `studio-release-check.md` when the check
starts). The published desktop requires the .NET 11 Windows Desktop Runtime on
the test computer. This workflow neither publishes a release nor modifies
saved projects or user settings.

## Manual desktop UI checklist

Use the output directory printed by the automated command. In a separate
PowerShell window, set `FLUXFLOW_PLUGIN_DIRECTORY` to its `plugin-store` child and
launch `desktop/FluxFlow.Studio.Desktop.exe`. This environment variable is local to
that PowerShell process; do not change a persistent system setting. Avoid changing
Studio settings during the trial.

1. Open `desktop-trial.fluxflow` from the output directory using the native picker.
   Confirm four nodes and their wiring. Cancel another Open and confirm no change.
2. Toggle Gate's **Initially open** property. Undo, Redo, then Undo again to restore
   the original. Confirm each value in the Inspector.
3. Cancel Save As once. Then Save As to a new file in the same trial directory.
   Use normal Save; it should update that file without another destination prompt.
4. Run, observe **Release check** logs, and Stop. Confirm Stopped and retained
   observations. Rerun and confirm fresh observations.
5. Close the native window during execution. Confirm cleanup completes without an
   error dialog or leftover desktop process. Relaunch, reopen the saved trial,
   confirm wiring and settings, then run and stop again.
6. Close the test desktop. Record the result with the release evidence. Leave user
   projects, settings, installed plugins and other open apps untouched.

The check adds no runtime behavior, configuration migration or plugin contract
changes. Core and WFENG are outside its change scope.
