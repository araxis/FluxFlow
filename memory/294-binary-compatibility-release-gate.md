# Binary Compatibility Release Gate

Date: 2026-08-03

## Outcome

The normal package release path now uses the existing .NET SDK binary package
validation helper as its only package-creation operation. A release with a
published predecessor cannot reach archive inspection or publication until the
candidate assembly is compatible with the exact baseline reviewed in
`eng/packages.json`.

Each of the 59 maintained package entries declares
`binaryCompatibilityBaseline`. All current entries point to their already
published current project version. For a later release, the project version is
advanced while the manifest retains the prior published comparison version.
Explicit JSON `null` is reserved for a genuine first release; missing, empty,
non-string, or invalid values fail resolution.

## Small Release Boundary

- `eng/resolve-package-release.ps1` validates the manifest policy and exports
  the exact baseline plus an explicit initial-release flag.
- `eng/package-binary-compat-preflight.ps1` consumes that policy, restores a
  baseline when one exists, and creates the candidate package once. Baseline
  restore uses a fresh temporary package directory, disables cache reuse, and
  passes the exact restored archive path to SDK validation.
- An optional output path lets the release workflow write its candidate to
  `artifacts/packages` while local checks retain their existing
  `artifacts/binary-compat` default.
- A deliberate local `-BaselineVersion` remains available without weakening
  the requirement for an explicit valid manifest policy.
- A declared initial release skips only the impossible prior-package
  comparison, prints that decision, and still creates the package through the
  same helper.
- `.github/workflows/publish-nuget.yml` runs the compatibility-aware package
  gate after the controlled Release build, solution tests, and real-provider
  suites and before archive inspection, package-only smoke testing, collision
  detection, publication, public-feed verification, or repository release.

No package lookup heuristic, reflection, compatibility framework, dependency,
or second workflow pack path was added.

## Stale-Cache Finding

The first real same-version validation correctly failed because the preflight
read `FluxFlow.Nodes` 4.0.0 from the machine-wide package cache. That archive
predated the canonical publication and exposed seven older members. The public
baseline was not the package being compared even though the temporary restore
named the public source.

The preflight now prevents that substitution with `--no-cache`, an isolated
temporary package root, and explicit `PackageValidationBaselinePath`. The stale
global package was left untouched; it is no longer part of release evidence.

## Verification

- Every one of the 59 manifest entries completed resolver and compatibility
  preparation successfully.
- A real `FluxFlow.Nodes` 4.0.0 comparison restored the public baseline with
  cache reuse disabled and a fresh isolated package root, packed the candidate
  once, and reported `BINARY_COMPAT_OK=FluxFlow.Nodes`.
- The focused release suite passed 151/151 tests with zero warnings; its
  assertion and mutation audit found no surviving high-risk requirement gap.
- The complete controlled Release build passed for 134 projects with zero
  warnings and zero errors.
- The complete solution Release test run passed 2,519 tests across 66 test
  projects with zero warnings.
- Complete solution formatting verification and `git diff --check` passed.
- The transitive vulnerable-package audit reported no vulnerable packages for
  any solution project.
- The generated candidate package and symbol package were removed after the
  compatibility proof. No package was published and no public state changed.

Pull-request, remote-check, merge, and synchronized-main evidence is recorded
in the goal closeout after normal review completes.

## Boundaries

This round changes release metadata, release scripts/workflow, focused release
tests, documentation, goal records, and memory only. It does not change runtime
source, public APIs, API baselines, schemas, project/package versions,
changelog entries, package bytes, tags, repository releases, or public package
state.
