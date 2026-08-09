# Code-first prerelease publication

Date: 2026-08-09

## Outcome

The 31-package code-first simplification prerelease was published and accepted.
Pull request `#77` merged as immutable commit
`d6c245df82fb2958a77cff04985811fb49f12b04`. Every prerelease tag, workflow,
package, symbol package, and repository prerelease record uses that commit.

Main CI run `31295479215` passed before publication. Trusted short-lived
publication used the `release` environment and the feed-side identity bound to
the repository and `publish-nuget.yml`. The previous long-lived repository
secret was not consumed or deleted.

## Publication waves

| Wave | Package or group | Workflow run | Result |
| ---: | --- | ---: | --- |
| 1 | Composition | `31295661216` | success |
| 2 | Components Designer | `31296727157` | success |
| 2 | Engine | `31297632987` | success |
| 3 | Assertions Composition | `31298520989` | success |
| 3 | Expectations Composition | `31298552317` | success |
| 3 | FileSystem Composition | `31298579212` | success |
| 3 | HTTP Composition | `31298605706` | success |
| 3 | Mapping Composition | `31298619596` | success |
| 3 | Metrics Composition | `31298635889` | success |
| 3 | MQTT Composition | `31298658078` | success |
| 3 | Observability Composition | `31298670955` | success |
| 3 | Payloads Composition | `31298685999` | success |
| 3 | Projections Composition | `31298703364` | success |
| 3 | Resilience Composition | `31298718957` | success |
| 3 | Routing Composition | `31298733725` | success |
| 3 | Serialization Composition | `31298753102` | success |
| 3 | Sessions Composition | `31298773025` | success |
| 3 | Sources Composition | `31298798357` | success |
| 3 | State Composition | `31298830622` | success |
| 3 | Storage Composition | `31298874360` | success |
| 3 | Timers Composition | `31298915434` | success |
| 3 | Validation Composition | `31298931307` | success |
| 3 | Durable Input | `31298978516` | success |
| 3 | Durable Output | `31299004392` | success |
| 3 | Engine Health Checks | `31299063502` | success |
| 3 | Fluent | `31299088007` | success |
| 4 | Durable Input SQL-file | `31300333236` | success |
| 4 | Durable Input T-SQL | `31300365710` | success |
| 4 | Durable Output SQL-file | `31300390550` | success |
| 4 | Durable Output T-SQL | `31300421055` | success |
| 4 | Fluent Hosting | `31300441097` | success |

Each workflow passed restore, build, solution tests, durable-provider gates,
binary compatibility and pack, archive inspection, package-only smoke,
exact-version absence, trusted login, upload, public-feed verification, and
prerelease creation. Dependent waves did not start until the preceding wave was
publicly indexed and independently verified.

Independent checks confirmed all 31 exact public versions, prerelease status,
two expected assets (`.nupkg` and `.snupkg`), and the immutable target commit.
Composition also passed a separate public-only restore/build smoke with zero
warnings and errors before Wave 2 began.

## External package-only acceptance

The standalone `C:\Projects\FluxFlow.Pilot` repository was migrated from a
local candidate source to the public package feed only and committed as
`9e5699b`.

Its exact restored FluxFlow closure was:

- Nodes `4.0.0`;
- Mapping `1.0.3`;
- Composition `7.0.0-rc.1`;
- Engine `8.0.0-rc.1`;
- Engine Health Checks `1.0.0-rc.1`;
- Durable Input and Durable Input SQL-file `2.0.0-rc.1`; and
- Durable Output and Durable Output SQL-file `4.0.0-rc.1`.

The runner used one pilot-owned package cache and verified each resolved
package's public-source metadata. It had no FluxFlow repository argument,
project reference, pack command, candidate directory, archive hash comparison,
feed credential, or hidden source dependency.

Verification evidence:

- build: 0 warnings, 0 errors;
- tests: 6 passed, 0 failed, 0 skipped;
- code-first typed execution and readiness: passed;
- portable JSON startup, unchanged apply, rejected invalid candidate, retained
  active route, and post-rejection routing: passed;
- separate-process SQL-file durable seed/recovery: passed;
- final marker `PILOT_VERIFICATION_OK=True`: present exactly once; and
- default cleanup removed the pilot-owned package cache and restart artifacts.

No production defect was found. One initial test assertion incorrectly treated
the SDK-owned `library-packs` directory as a package feed; the assertion was
narrowed to allow that SDK path while retaining the exact public URI and
package-source metadata requirements.

## Decision

The prerelease acceptance goal is complete. Stable promotion is deliberately
not part of this result. Stable package versions must be new immutable versions
after the agreed observation period and must repeat the same dependency-wave,
public-verification, and external-pilot gates.
