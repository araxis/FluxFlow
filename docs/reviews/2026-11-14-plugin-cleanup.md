# Plugin cleanup review

This pass reviews the current plugin split before a separate framework upgrade.
Target frameworks, language settings, runtime behavior, Core and WFENG were not changed.

## Corrections

- Removed the unused Studio `ResourceRegistrationValidation` class. Connection
  validation belongs to the portable design rules; no caller used the old helper.
- Fixed data-only connection registration to retain `EndpointIsUri`. A rendered
  regression reproduced a valid `COM7` to `COM8` edit being rejected as an invalid
  URI. Tests now cover non-URI endpoints, valid and invalid URI edits, unknown saved
  fields, credentials, save/reopen and Undo/Redo.
- Completed package metadata and readmes for MQTT, NATS and RabbitMQ settings and
  the NATS and RabbitMQ runtime libraries already listed in the release manifest.
- Made HTTP settings packable and included it in the release manifest. The HTTP
  runtime already depended on this library, but standalone package delivery could
  not provide that dependency. A release check now rejects non-packable manifest entries.
- Replaced the obsolete hard-coded release-wave inventory with dependency-order
  checks and a small independent-package/dependent-package fixture. The layout check
  accepts the existing integration `.Tests` names while still requiring matching
  project folders under the correct test-kind directory. README root paths accept
  both valid slash forms.
- Reconciled package documentation and the source-declaration baseline with the
  existing plugin migrations. Core and Application baseline entries are unchanged.
  This does not certify binary compatibility with earlier preview packages.
- Isolated desktop boundary tests from the user's installed plugins through an
  internal composition overload. The public entry point still loads installed plugins.
  Native save/history tests no longer assume HTTP is built in. Stop/rerun tests use
  a fresh run ID and check that the old stopped run remains separate.
- Removed two empty Studio source directories and an empty project item group.

## Audit results

- Examined 745 production C#, Razor and CSS files for exact duplicates, excluding
  build output. No exact duplicate files over 160 bytes were found. This is not a
  claim that every implementation is free of similar logic.
- No broken production project references or linked C#/Razor source files were found.
  The runner intentionally copies the shared plugin-management script as content.
- Base Studio and companion dependency checks pass without concrete plugin drivers.
- Retained the documented legacy `StudioEntry` compatibility path and type forwards.
  Their lack of in-tree callers is not evidence that external plugins no longer use them.
- The 31 Core source/project/document files match their pre-review hashes.

## Verification

| Check | .NET 8 | .NET 10 |
| --- | --- | --- |
| Studio suite | 1,941 passed | 1,941 passed |
| Installed plugin lifecycle and dependency boundaries | 7 passed | 7 passed |
| Desktop native file, history and lifecycle boundaries | 11 passed | 11 passed |
| Fresh package consumer, HTTP/MQTT/NATS/RabbitMQ | Passed | Passed |

The release suite targets .NET 10 and passed all 112 tests. Local packages contain
both target-framework libraries and their readmes. The temporary consumer restored
from the local packages into an isolated package cache, then loaded all four runtime
assemblies and inspected their exported types and members. Nothing was published.

The three new endpoint regressions also passed separately after the final test-code
cleanup. No user project, plugin source, credential store or browser tab was edited.

## Verification limits

- This was a source, packaging and automated boundary review, not a new live broker,
  hardware or browser acceptance run.
- Existing MudBlazor analyzer warnings in test code remain; the test results do not
  mean the repository is warning-free.
- The repository pins SDK 10.0.203, which is unavailable on this machine. Commands
  ran outside the pinned directory using installed SDK 11.0.100, while compiling
  and executing the existing .NET 8 and .NET 10 targets. No SDK pin, framework target
  or language-version setting was upgraded.
